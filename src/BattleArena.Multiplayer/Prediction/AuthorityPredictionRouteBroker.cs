using BattleArena.Multiplayer.Connection;
using BattleArena.Multiplayer.Protocol;
using BattleArena.Protocol.V1;
using Google.Protobuf;

namespace BattleArena.Multiplayer.Prediction;

public sealed class AuthorityPredictionRouteBroker : IAuthorityPredictionRouteBroker
{
    private readonly IPredictionRouteCredentialGenerator _credentialGenerator;
    private readonly ulong _credentialLifetimeTicks;
    private readonly SessionPeerId _authorityPeerId;
    private readonly Dictionary<SessionPeerId, SessionPeer> _peers = [];
    private readonly Dictionary<SessionPeerId, PredictionRouteDescriptor> _advertisements = [];
    private readonly Dictionary<PredictionPairKey, ActiveRoute> _activeRoutes = [];
    private readonly Dictionary<PredictionPairKey, uint> _lastRouteGenerations = [];
    private ulong _rosterRevision;

    public AuthorityPredictionRouteBroker(
        IPredictionRouteCredentialGenerator credentialGenerator,
        SessionPeer authorityPeer,
        ulong credentialLifetimeTicks = 600)
    {
        _credentialGenerator = credentialGenerator ??
            throw new ArgumentNullException(nameof(credentialGenerator));
        ArgumentNullException.ThrowIfNull(authorityPeer);
        if (!authorityPeer.IsAuthority)
        {
            throw new ArgumentException("Prediction route broker requires the authority peer.", nameof(authorityPeer));
        }
        if (credentialLifetimeTicks == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(credentialLifetimeTicks));
        }

        _credentialLifetimeTicks = credentialLifetimeTicks;
        _authorityPeerId = authorityPeer.Id;
        _peers.Add(authorityPeer.Id, authorityPeer);
    }

    public IReadOnlyCollection<SessionPeer> Peers => _peers.Values;

    public PredictionRouteBrokerDelta AddOrUpdatePeer(SessionPeer peer)
    {
        ArgumentNullException.ThrowIfNull(peer);
        if (!_peers.ContainsKey(peer.Id) && _peers.Count >= ProtocolConstants.MaxSessionPeers)
        {
            return PredictionRouteBrokerDelta.Empty;
        }

        var revocations = new List<PredictionRouteRevoked>();
        if (_peers.TryGetValue(peer.Id, out var existing))
        {
            if (peer.PeerSessionGeneration.Value < existing.PeerSessionGeneration.Value)
            {
                return PredictionRouteBrokerDelta.Empty;
            }

            if (peer.PlayerId != existing.PlayerId ||
                peer.CombatantId != existing.CombatantId ||
                peer.IsAuthority != existing.IsAuthority)
            {
                return PredictionRouteBrokerDelta.Empty;
            }

            if (peer.PeerSessionGeneration == existing.PeerSessionGeneration &&
                peer.DisplayName == existing.DisplayName)
            {
                return PredictionRouteBrokerDelta.Empty;
            }

            if (peer.PeerSessionGeneration != existing.PeerSessionGeneration)
            {
                RevokeRoutesForPeer(
                    peer.Id,
                    PredictionRouteRevocationReason.PeerReconnected,
                    revocations);
                _advertisements.Remove(peer.Id);
            }
        }
        else if (peer.IsAuthority ||
                 _peers.Values.Any(value =>
                     value.PlayerId == peer.PlayerId || value.CombatantId == peer.CombatantId))
        {
            return PredictionRouteBrokerDelta.Empty;
        }

        _peers[peer.Id] = peer;
        return new PredictionRouteBrokerDelta(BuildRoster(), [], revocations);
    }

    public PredictionRouteBrokerDelta RemovePeer(
        SessionPeerId peerId,
        PredictionRouteRevocationReason reason)
    {
        if (!Enum.IsDefined(reason) || reason == PredictionRouteRevocationReason.Unspecified ||
            peerId == _authorityPeerId || !_peers.Remove(peerId))
        {
            return PredictionRouteBrokerDelta.Empty;
        }

        _advertisements.Remove(peerId);
        var revocations = new List<PredictionRouteRevoked>();
        RevokeRoutesForPeer(peerId, reason, revocations);
        return new PredictionRouteBrokerDelta(BuildRoster(), [], revocations);
    }

    public PredictionRouteBrokerDelta ApplyAdvertisement(
        SessionPeerId authenticatedPeerId,
        ConnectionGeneration authenticatedGeneration,
        PredictionRouteDescriptor descriptor,
        ulong currentAuthorityTick)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (!_peers.TryGetValue(authenticatedPeerId, out var peer) ||
            peer.IsAuthority ||
            peer.PeerSessionGeneration != authenticatedGeneration ||
            !InboundMessageValidator.ValidateRouteDescriptor(descriptor).IsValid)
        {
            return PredictionRouteBrokerDelta.Empty;
        }

        if (_advertisements.TryGetValue(authenticatedPeerId, out var existing) &&
            DescriptorEquals(existing, descriptor))
        {
            return PredictionRouteBrokerDelta.Empty;
        }

        _advertisements[authenticatedPeerId] = descriptor.Clone();
        var revocations = new List<PredictionRouteRevoked>();
        RevokeRoutesForPeer(
            authenticatedPeerId,
            PredictionRouteRevocationReason.RouteReplaced,
            revocations);
        var authorizations = CreateEligibleRoutes(authenticatedPeerId, currentAuthorityTick);
        return new PredictionRouteBrokerDelta(null, authorizations, revocations);
    }

    public PredictionRouteBrokerDelta AdvanceAuthorityTick(ulong currentAuthorityTick)
    {
        var expired = _activeRoutes
            .Where(pair => pair.Value.ExpiresAuthorityTick <= currentAuthorityTick)
            .Select(pair => pair.Key)
            .ToArray();
        if (expired.Length == 0)
        {
            return PredictionRouteBrokerDelta.Empty;
        }

        var revocations = new List<PredictionRouteRevoked>();
        var affectedPeers = new HashSet<SessionPeerId>();
        foreach (var key in expired)
        {
            var route = _activeRoutes[key];
            revocations.AddRange(CreateRevocations(
                route,
                PredictionRouteRevocationReason.CredentialExpired));
            affectedPeers.Add(key.Low);
            affectedPeers.Add(key.High);
            _activeRoutes.Remove(key);
        }

        var authorizations = new List<PredictionRouteAuthorization>();
        foreach (var peer in affectedPeers)
        {
            authorizations.AddRange(CreateEligibleRoutes(peer, currentAuthorityTick));
        }

        return new PredictionRouteBrokerDelta(null, authorizations, revocations);
    }

    public PredictionRouteBrokerDelta RevokePairForProtocolViolation(
        SessionPeerId first,
        SessionPeerId second)
    {
        if (first == second)
        {
            return PredictionRouteBrokerDelta.Empty;
        }

        var key = PredictionPairKey.Create(first, second);
        if (!_activeRoutes.Remove(key, out var route))
        {
            return PredictionRouteBrokerDelta.Empty;
        }

        return new PredictionRouteBrokerDelta(
            null,
            [],
            CreateRevocations(
                route,
                PredictionRouteRevocationReason.ProtocolViolation).ToArray());
    }

    private PeerRosterUpdate BuildRoster()
    {
        var roster = new PeerRosterUpdate { RosterRevision = ++_rosterRevision };
        foreach (var peer in _peers.Values.OrderBy(value => value.Id.Value))
        {
            roster.Peers.Add(new PredictionPeerRosterEntry
            {
                SessionPeerId = peer.Id.Value,
                PeerSessionGeneration = peer.PeerSessionGeneration.Value,
                PlayerId = peer.PlayerId,
                CombatantId = peer.CombatantId,
                DisplayName = peer.DisplayName,
                IsAuthority = peer.IsAuthority,
            });
        }

        return roster;
    }

    private IReadOnlyList<PredictionRouteAuthorization> CreateEligibleRoutes(
        SessionPeerId peerId,
        ulong currentAuthorityTick)
    {
        var result = new List<PredictionRouteAuthorization>();
        if (!_advertisements.TryGetValue(peerId, out var localDescriptor))
        {
            return result;
        }

        foreach (var remote in _advertisements)
        {
            if (remote.Key == peerId ||
                remote.Value.TransportKind != localDescriptor.TransportKind ||
                !_peers.TryGetValue(remote.Key, out var remotePeer) ||
                remotePeer.IsAuthority)
            {
                continue;
            }

            var key = PredictionPairKey.Create(peerId, remote.Key);
            if (_activeRoutes.ContainsKey(key))
            {
                continue;
            }

            var lowPeer = _peers[key.Low];
            var highPeer = _peers[key.High];
            var generation = checked(_lastRouteGenerations.GetValueOrDefault(key) + 1);
            _lastRouteGenerations[key] = generation;
            var credential = _credentialGenerator.CreateRouteCredential();
            if (credential.Length != ProtocolConstants.PredictionRouteCredentialBytes ||
                credential.All(value => value == 0))
            {
                throw new InvalidOperationException("Credential generator returned an invalid route credential.");
            }

            var route = new ActiveRoute(
                key,
                lowPeer.PeerSessionGeneration,
                highPeer.PeerSessionGeneration,
                generation,
                checked(currentAuthorityTick + _credentialLifetimeTicks),
                credential);
            _activeRoutes.Add(key, route);
            result.Add(CreateAuthorization(route, lowPeer, highPeer, _advertisements[highPeer.Id]));
            result.Add(CreateAuthorization(route, highPeer, lowPeer, _advertisements[lowPeer.Id]));
        }

        return result;
    }

    private void RevokeRoutesForPeer(
        SessionPeerId peerId,
        PredictionRouteRevocationReason reason,
        ICollection<PredictionRouteRevoked> destination)
    {
        foreach (var key in _activeRoutes.Keys.Where(key => key.Contains(peerId)).ToArray())
        {
            foreach (var revocation in CreateRevocations(_activeRoutes[key], reason))
            {
                destination.Add(revocation);
            }
            _activeRoutes.Remove(key);
        }
    }

    private static IEnumerable<PredictionRouteRevoked> CreateRevocations(
        ActiveRoute route,
        PredictionRouteRevocationReason reason)
    {
        yield return CreateRevocation(route, route.Key.Low, route.Key.High, reason);
        yield return CreateRevocation(route, route.Key.High, route.Key.Low, reason);
    }

    private static PredictionRouteRevoked CreateRevocation(
        ActiveRoute route,
        SessionPeerId local,
        SessionPeerId remote,
        PredictionRouteRevocationReason reason) => new()
    {
        LocalSessionPeerId = local.Value,
        LocalPeerSessionGeneration = route.GenerationFor(local).Value,
        RemoteSessionPeerId = remote.Value,
        RemotePeerSessionGeneration = route.GenerationFor(remote).Value,
        PredictionRouteGeneration = route.RouteGeneration,
        Reason = reason,
    };

    private static PredictionRouteAuthorization CreateAuthorization(
        ActiveRoute route,
        SessionPeer local,
        SessionPeer remote,
        PredictionRouteDescriptor remoteDescriptor) => new()
    {
        LocalSessionPeerId = local.Id.Value,
        LocalPeerSessionGeneration = local.PeerSessionGeneration.Value,
        RemoteSessionPeerId = remote.Id.Value,
        RemotePeerSessionGeneration = remote.PeerSessionGeneration.Value,
        PredictionRouteGeneration = route.RouteGeneration,
        RemoteDescriptor = remoteDescriptor.Clone(),
        RouteCredential = ByteString.CopyFrom(route.Credential),
        ExpiresAuthorityTick = route.ExpiresAuthorityTick,
    };

    private static bool DescriptorEquals(
        PredictionRouteDescriptor left,
        PredictionRouteDescriptor right) =>
        left.DescriptorVersion == right.DescriptorVersion &&
        left.TransportKind == right.TransportKind &&
        left.Payload.Span.SequenceEqual(right.Payload.Span);

    private readonly record struct PredictionPairKey(SessionPeerId Low, SessionPeerId High)
    {
        public static PredictionPairKey Create(SessionPeerId first, SessionPeerId second) =>
            first.Value < second.Value ? new(first, second) : new(second, first);

        public bool Contains(SessionPeerId peer) => Low == peer || High == peer;
    }

    private sealed record ActiveRoute(
        PredictionPairKey Key,
        ConnectionGeneration LowGeneration,
        ConnectionGeneration HighGeneration,
        uint RouteGeneration,
        ulong ExpiresAuthorityTick,
        byte[] Credential)
    {
        public ConnectionGeneration GenerationFor(SessionPeerId peer) =>
            peer == Key.Low ? LowGeneration : HighGeneration;
    }
}
