using BattleArena.Multiplayer.Connection;
using BattleArena.Multiplayer.Protocol;
using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Prediction;

public sealed class ClientPredictionMeshCoordinator : IClientPredictionMeshCoordinator
{
    private const int MaximumHandshakeRetries = 5;
    private const ulong InitialRetryDelayTicks = 30;
    private readonly ulong _sessionId;
    private readonly ISessionPeerDirectory _directory;
    private readonly Dictionary<SessionPeerId, PredictionRouteEpochGate> _epochGates = [];
    private readonly Dictionary<SessionPeerId, AuthorizedPredictionRoute> _routes = [];
    private readonly Dictionary<SessionPeerId, RouteRuntime> _runtime = [];
    private readonly Dictionary<SessionPeerId, ulong> _lastAttemptIds = [];
    private ulong _lastRosterRevision;

    public ClientPredictionMeshCoordinator(
        ulong sessionId,
        ISessionPeerDirectory directory)
    {
        if (sessionId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionId));
        }

        _sessionId = sessionId;
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _lastRosterRevision = directory.RosterRevision;
    }

    public IReadOnlyCollection<AuthorizedPredictionRoute> AuthorizedRoutes => _routes.Values;
    public IReadOnlyCollection<PredictionRouteStatus> RouteStatuses => _runtime
        .Select(pair => new PredictionRouteStatus(
            pair.Value.Route,
            pair.Value.Health,
            pair.Value.ConsecutiveFailures,
            pair.Value.NextRetryAuthorityTick,
            pair.Value.AttemptId))
        .ToArray();

    public IReadOnlyList<PredictionMeshDirective> ApplyAuthorization(
        PredictionRouteAuthorization authorization,
        ulong currentAuthorityTick)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        var localId = _directory.LocalPeerId;
        if (authorization.LocalSessionPeerId == 0 ||
            authorization.RemoteSessionPeerId == 0 ||
            authorization.LocalSessionPeerId == authorization.RemoteSessionPeerId ||
            authorization.LocalSessionPeerId != localId.Value ||
            authorization.LocalPeerSessionGeneration == 0 ||
            authorization.RemotePeerSessionGeneration == 0 ||
            authorization.PredictionRouteGeneration == 0 ||
            authorization.RouteCredential.Length != ProtocolConstants.PredictionRouteCredentialBytes ||
            authorization.RouteCredential.ToByteArray().All(value => value == 0) ||
            !InboundMessageValidator.ValidateRouteDescriptor(authorization.RemoteDescriptor).IsValid ||
            authorization.ExpiresAuthorityTick <= currentAuthorityTick)
        {
            return [];
        }

        var remoteId = new SessionPeerId(authorization.RemoteSessionPeerId);
        if (
            !_directory.TryGetPeer(localId, out var local) ||
            !_directory.TryGetPeer(remoteId, out var remote) ||
            local.PeerSessionGeneration.Value != authorization.LocalPeerSessionGeneration ||
            remote.PeerSessionGeneration.Value != authorization.RemotePeerSessionGeneration ||
            remote.IsAuthority)
        {
            return [];
        }

        if (!_epochGates.TryGetValue(remoteId, out var gate))
        {
            gate = new PredictionRouteEpochGate(localId.Value, remoteId.Value);
            _epochGates.Add(remoteId, gate);
        }

        if (!gate.TryApplyAuthorization(authorization, currentAuthorityTick))
        {
            return [];
        }

        var route = new AuthorizedPredictionRoute(
            _sessionId,
            localId,
            local.PeerSessionGeneration,
            remoteId,
            remote.PeerSessionGeneration,
            authorization.PredictionRouteGeneration,
            authorization.RemoteDescriptor.Clone(),
            authorization.RouteCredential.ToByteArray(),
            authorization.ExpiresAuthorityTick);
        _routes[remoteId] = route;
        var attemptId = checked(_lastAttemptIds.GetValueOrDefault(remoteId) + 1);
        _lastAttemptIds[remoteId] = attemptId;
        _runtime[remoteId] = new RouteRuntime(
            route,
            PredictionRouteHealth.AwaitingHandshake,
            0,
            0,
            attemptId);
        return
        [
            new PredictionMeshDirective(
                route.LocalPeerInitiates
                    ? PredictionMeshDirectiveKind.Initiate
                    : PredictionMeshDirectiveKind.Listen,
                remoteId,
                route,
                attemptId),
        ];
    }

    public IReadOnlyList<PredictionMeshDirective> ApplyRevocation(
        PredictionRouteRevoked revocation)
    {
        ArgumentNullException.ThrowIfNull(revocation);
        if (revocation.LocalSessionPeerId == 0 || revocation.RemoteSessionPeerId == 0 ||
            revocation.LocalSessionPeerId != _directory.LocalPeerId.Value ||
            revocation.LocalPeerSessionGeneration == 0 ||
            revocation.RemotePeerSessionGeneration == 0 ||
            revocation.PredictionRouteGeneration == 0 ||
            !Enum.IsDefined(revocation.Reason) ||
            revocation.Reason == PredictionRouteRevocationReason.Unspecified)
        {
            return [];
        }

        var remoteId = new SessionPeerId(revocation.RemoteSessionPeerId);
        if (!_epochGates.TryGetValue(remoteId, out var gate) ||
            !gate.TryApplyRevocation(revocation))
        {
            return [];
        }

        _routes.Remove(remoteId);
        _runtime.Remove(remoteId);
        return [new PredictionMeshDirective(
            PredictionMeshDirectiveKind.Remove,
            remoteId,
            null,
            0)];
    }

    public IReadOnlyList<PredictionMeshDirective> RemoveMissingRosterPeers(PeerRosterUpdate roster)
    {
        ArgumentNullException.ThrowIfNull(roster);
        if (roster.RosterRevision <= _lastRosterRevision)
        {
            return [];
        }

        var entries = new Dictionary<ulong, PredictionPeerRosterEntry>();
        foreach (var peer in roster.Peers)
        {
            if (peer.SessionPeerId == 0 || peer.PeerSessionGeneration == 0 ||
                !entries.TryAdd(peer.SessionPeerId, peer))
            {
                return [];
            }
        }
        _lastRosterRevision = roster.RosterRevision;
        entries.TryGetValue(_directory.LocalPeerId.Value, out var localEntry);
        var removed = _routes
            .Where(pair =>
                localEntry is null ||
                localEntry.PeerSessionGeneration != pair.Value.LocalPeerSessionGeneration.Value ||
                !entries.TryGetValue(pair.Key.Value, out var remoteEntry) ||
                remoteEntry.PeerSessionGeneration != pair.Value.RemotePeerSessionGeneration.Value)
            .Select(pair => pair.Key)
            .ToArray();
        var directives = new List<PredictionMeshDirective>(removed.Length);
        foreach (var peer in removed)
        {
            _routes.Remove(peer);
            _epochGates.Remove(peer);
            _runtime.Remove(peer);
            directives.Add(new PredictionMeshDirective(
                PredictionMeshDirectiveKind.Remove,
                peer,
                null,
                0));
        }

        return directives;
    }

    public IReadOnlyList<PredictionMeshDirective> AdvanceAuthorityTick(ulong currentAuthorityTick)
    {
        var directives = new List<PredictionMeshDirective>();
        foreach (var pair in _runtime.ToArray())
        {
            var runtime = pair.Value;
            if (runtime.Route.ExpiresAuthorityTick <= currentAuthorityTick)
            {
                _runtime.Remove(pair.Key);
                _routes.Remove(pair.Key);
                _epochGates.Remove(pair.Key);
                directives.Add(new PredictionMeshDirective(
                    PredictionMeshDirectiveKind.Remove,
                    pair.Key,
                    null,
                    runtime.AttemptId));
                continue;
            }

            if (runtime.Health == PredictionRouteHealth.AuthorityFallback &&
                runtime.ConsecutiveFailures <= MaximumHandshakeRetries &&
                runtime.NextRetryAuthorityTick <= currentAuthorityTick)
            {
                _runtime[pair.Key] = runtime with
                {
                    Health = PredictionRouteHealth.AwaitingHandshake,
                    AttemptId = checked(runtime.AttemptId + 1),
                };
                _lastAttemptIds[pair.Key] = checked(runtime.AttemptId + 1);
                directives.Add(CreateStartDirective(
                    runtime.Route,
                    checked(runtime.AttemptId + 1)));
            }
        }

        return directives;
    }

    public IReadOnlyList<PredictionMeshDirective> ReportRouteFailure(
        SessionPeerId remotePeerId,
        uint routeGeneration,
        ulong attemptId,
        ulong currentAuthorityTick)
    {
        if (!_runtime.TryGetValue(remotePeerId, out var runtime) ||
            runtime.Health == PredictionRouteHealth.AuthorityFallback ||
            runtime.Route.ExpiresAuthorityTick <= currentAuthorityTick ||
            routeGeneration == 0 || routeGeneration != runtime.Route.RouteGeneration ||
            attemptId == 0 || attemptId != runtime.AttemptId)
        {
            return [];
        }

        var failures = checked(runtime.ConsecutiveFailures + 1);
        var exponent = Math.Min(failures - 1, 6);
        var retryDelay = checked(InitialRetryDelayTicks << exponent);
        _runtime[remotePeerId] = runtime with
        {
            Health = PredictionRouteHealth.AuthorityFallback,
            ConsecutiveFailures = failures,
            NextRetryAuthorityTick = failures <= MaximumHandshakeRetries
                ? checked(currentAuthorityTick + retryDelay)
                : ulong.MaxValue,
        };
        return [new PredictionMeshDirective(
            PredictionMeshDirectiveKind.Remove,
            remotePeerId,
            null,
            attemptId)];
    }

    public bool MarkAuthenticated(
        SessionPeerId remotePeerId,
        uint routeGeneration,
        ulong attemptId,
        ulong currentAuthorityTick)
    {
        if (!_runtime.TryGetValue(remotePeerId, out var runtime) ||
            runtime.Route.RouteGeneration != routeGeneration ||
            runtime.Route.ExpiresAuthorityTick <= currentAuthorityTick ||
            attemptId == 0 || attemptId != runtime.AttemptId ||
            runtime.Health != PredictionRouteHealth.AwaitingHandshake)
        {
            return false;
        }

        _runtime[remotePeerId] = runtime with
        {
            Health = PredictionRouteHealth.Authenticated,
            ConsecutiveFailures = 0,
            NextRetryAuthorityTick = 0,
        };
        return true;
    }

    private static PredictionMeshDirective CreateStartDirective(
        AuthorizedPredictionRoute route,
        ulong attemptId) =>
        new(
            route.LocalPeerInitiates
                ? PredictionMeshDirectiveKind.Initiate
                : PredictionMeshDirectiveKind.Listen,
            route.RemotePeerId,
            route,
            attemptId);

    private sealed record RouteRuntime(
        AuthorizedPredictionRoute Route,
        PredictionRouteHealth Health,
        int ConsecutiveFailures,
        ulong NextRetryAuthorityTick,
        ulong AttemptId);
}
