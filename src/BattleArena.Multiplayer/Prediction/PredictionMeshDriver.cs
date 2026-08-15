using BattleArena.Multiplayer.Connection;
using BattleArena.Multiplayer.Protocol;
using BattleArena.Multiplayer.Timing;
using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Prediction;

/// <summary>
/// Coordinates authority-approved route lifecycle, credential handshakes, and
/// fail-open use of the mandatory authority relay. It contains no Godot, ENet,
/// or Steam behavior.
/// </summary>
public sealed class PredictionMeshDriver : IPredictionMeshDriver
{
    private const ulong HandshakeTimeoutTicks = 180;
    private const int DirectProbeRatePerSecond = 4;
    private const int MaximumOutstandingProbes = 32;
    private readonly IClientPredictionMeshCoordinator _coordinator;
    private readonly IPredictionMeshTransport _transport;
    private readonly IPredictionHandshakeSessionFactory _handshakeFactory;
    private readonly IPredictionProtocolCodec _codec;
    private readonly PredictionInboundMessageValidator _validator;
    private readonly INetworkTimeSource _timeSource;
    private readonly uint _simulationTicksPerSecond;
    private readonly Dictionary<SessionPeerId, AttemptRuntime> _attempts = [];
    private bool _disposed;
    private ulong _clientTick;
    private ulong _authorityTick;

    public PredictionMeshDriver(
        IClientPredictionMeshCoordinator coordinator,
        IPredictionMeshTransport transport,
        IPredictionHandshakeSessionFactory handshakeFactory,
        IPredictionProtocolCodec codec,
        PredictionInboundMessageValidator validator,
        INetworkTimeSource timeSource,
        uint simulationTicksPerSecond = 60)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _handshakeFactory = handshakeFactory ?? throw new ArgumentNullException(nameof(handshakeFactory));
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _timeSource = timeSource ?? throw new ArgumentNullException(nameof(timeSource));
        if (simulationTicksPerSecond == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(simulationTicksPerSecond));
        }

        _simulationTicksPerSecond = simulationTicksPerSecond;
        _transport.RouteChanged += OnRouteChanged;
        _transport.PacketReceived += OnPacketReceived;
    }

    public event Action<AuthenticatedPredictionPacket>? AuthenticatedPacketReceived;
    public event Action<PredictionRouteStatus>? RouteStatusChanged;

    public IReadOnlyCollection<PredictionRouteStatus> RouteStatuses => _coordinator.RouteStatuses;

    public IReadOnlyCollection<PredictionPeerPathStatus> DirectPathStatuses =>
        _coordinator.RouteStatuses.Select(status =>
        {
            _attempts.TryGetValue(status.Route.RemotePeerId, out var runtime);
            return new PredictionPeerPathStatus(
                status.Route.RemotePeerId,
                status.Health,
                runtime?.Path.Current ?? default,
                runtime?.LastMovementArrivalTimestampMicroseconds ?? 0,
                status.ConsecutiveFailures);
        }).ToArray();

    public void ApplyAuthorization(PredictionRouteAuthorization authorization, ulong authorityTick)
    {
        ThrowIfDisposed();
        _authorityTick = Math.Max(_authorityTick, authorityTick);
        ApplyDirectives(_coordinator.ApplyAuthorization(authorization, authorityTick));
    }

    public void ApplyRevocation(PredictionRouteRevoked revocation)
    {
        ThrowIfDisposed();
        ApplyDirectives(_coordinator.ApplyRevocation(revocation));
    }

    public void ApplyRoster(PeerRosterUpdate roster)
    {
        ThrowIfDisposed();
        ApplyDirectives(_coordinator.RemoveMissingRosterPeers(roster));
    }

    public void Advance(ulong clientTick, ulong estimatedAuthorityTick)
    {
        ThrowIfDisposed();
        if (clientTick == 0 || estimatedAuthorityTick == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(clientTick));
        }

        _clientTick = clientTick;
        _authorityTick = estimatedAuthorityTick;
        foreach (var runtime in _attempts.Values.ToArray())
        {
            TryStartHandshake(runtime);
            TrySendClockProbe(runtime);
        }

        foreach (var runtime in _attempts.Values.ToArray())
        {
            if (!runtime.Handshake.IsAuthenticated &&
                estimatedAuthorityTick >= runtime.DeadlineAuthorityTick)
            {
                Fail(runtime.Route.RemotePeerId, runtime.Route.RouteGeneration, runtime.AttemptId);
            }
        }

        ApplyDirectives(_coordinator.AdvanceAuthorityTick(estimatedAuthorityTick));
    }

    public void PublishMovement(MovementPredictionBundle bundle)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(bundle);
        var validation = InboundMessageValidator.ValidateMovementPredictionBundle(bundle);
        if (!validation.IsValid)
        {
            throw new ArgumentException(validation.Violation!.Message, nameof(bundle));
        }

        foreach (var runtime in _attempts.Values.ToArray())
        {
            if (!runtime.Handshake.IsAuthenticated ||
                _authorityTick >= runtime.Route.ExpiresAuthorityTick)
            {
                continue;
            }

            var newest = bundle.Commands[^1];
            var envelope = CreateDataEnvelope(
                runtime,
                newest.ClientTick,
                newest.EstimatedAuthorityTick);
            envelope.MovementPredictionBundle = bundle.Clone();
            SendUnreliable(runtime, envelope);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _transport.RouteChanged -= OnRouteChanged;
        _transport.PacketReceived -= OnPacketReceived;
        _transport.Stop();
        _attempts.Clear();
        _disposed = true;
    }

    private void ApplyDirectives(IReadOnlyList<PredictionMeshDirective> directives)
    {
        foreach (var directive in directives)
        {
            if (directive.Kind == PredictionMeshDirectiveKind.Remove)
            {
                _attempts.Remove(directive.RemotePeerId);
                _transport.Apply(directive);
                PublishStatus(directive.RemotePeerId);
                continue;
            }

            var route = directive.Route ??
                throw new InvalidOperationException("A start directive requires an authorized route.");
            _attempts[directive.RemotePeerId] = new AttemptRuntime(
                route,
                directive.AttemptId,
                _handshakeFactory.Create(route),
                Math.Min(
                    route.ExpiresAuthorityTick,
                    checked(_authorityTick + HandshakeTimeoutTicks)),
                _simulationTicksPerSecond);
            _transport.Apply(directive);
            PublishStatus(directive.RemotePeerId);
        }
    }

    private void OnRouteChanged(PredictionTransportRouteChanged change)
    {
        if (!_attempts.TryGetValue(change.RemotePeerId, out var runtime) ||
            runtime.Route.RouteGeneration != change.RouteGeneration ||
            runtime.AttemptId != change.AttemptId)
        {
            return;
        }

        if (change.State is PredictionTransportRouteState.Failed or
            PredictionTransportRouteState.Disconnected)
        {
            Fail(change.RemotePeerId, change.RouteGeneration, change.AttemptId);
            return;
        }

        if (change.State == PredictionTransportRouteState.Connected)
        {
            runtime.TransportConnected = true;
            TryStartHandshake(runtime);
        }
    }

    private void OnPacketReceived(InboundPredictionPacket packet)
    {
        if (!_attempts.TryGetValue(packet.Sender, out var runtime) ||
            runtime.Route.RouteGeneration != packet.RouteGeneration ||
            runtime.AttemptId != packet.AttemptId)
        {
            return;
        }

        var decoded = _codec.Decode(packet.Payload.Span);
        if (!decoded.IsSuccess)
        {
            Fail(packet.Sender, packet.RouteGeneration, packet.AttemptId);
            return;
        }

        var envelope = decoded.Envelope!;
        var isHandshake = envelope.PayloadCase is
            PredictionPacketEnvelope.PayloadOneofCase.MeshHello or
            PredictionPacketEnvelope.PayloadOneofCase.MeshChallenge or
            PredictionPacketEnvelope.PayloadOneofCase.MeshProof or
            PredictionPacketEnvelope.PayloadOneofCase.MeshAccepted or
            PredictionPacketEnvelope.PayloadOneofCase.MeshRejected;
        if (runtime.Handshake.IsAuthenticated && !isHandshake)
        {
            var validation = _validator.Validate(
                envelope,
                runtime.ValidationContext(_authorityTick, authenticated: true));
            if (!validation.IsValid)
            {
                Fail(packet.Sender, packet.RouteGeneration, packet.AttemptId);
                return;
            }

            if (!runtime.DataSequences.TryAccept(envelope.PacketSequence))
            {
                return;
            }

            switch (envelope.PayloadCase)
            {
                case PredictionPacketEnvelope.PayloadOneofCase.MovementPredictionBundle:
                    runtime.Path.ObservePacket(
                        envelope.MovementPredictionBundle.BundleSequence,
                        _timeSource.GetTimestampMicroseconds());
                    runtime.LastMovementArrivalTimestampMicroseconds =
                        _timeSource.GetTimestampMicroseconds();
                    AuthenticatedPacketReceived?.Invoke(new AuthenticatedPredictionPacket(
                        packet.Sender,
                        packet.RouteGeneration,
                        packet.AttemptId,
                        packet.Delivery,
                        envelope));
                    break;
                case PredictionPacketEnvelope.PayloadOneofCase.DirectClockProbe:
                    ReplyToClockProbe(runtime, envelope.DirectClockProbe);
                    break;
                case PredictionPacketEnvelope.PayloadOneofCase.DirectClockReply:
                    ObserveClockReply(runtime, envelope.DirectClockReply);
                    break;
            }
            return;
        }

        PredictionHandshakeResult result = envelope.PayloadCase switch
        {
            PredictionPacketEnvelope.PayloadOneofCase.MeshHello =>
                runtime.Handshake.ReceiveHello(
                    envelope, runtime.NextSequence(), _clientTick, _authorityTick),
            PredictionPacketEnvelope.PayloadOneofCase.MeshChallenge =>
                runtime.Handshake.ReceiveChallenge(
                    envelope, runtime.NextSequence(), _clientTick, _authorityTick),
            PredictionPacketEnvelope.PayloadOneofCase.MeshProof =>
                runtime.Handshake.ReceiveProof(
                    envelope, runtime.NextSequence(), _clientTick, _authorityTick),
            PredictionPacketEnvelope.PayloadOneofCase.MeshAccepted =>
                runtime.Handshake.ReceiveAccepted(envelope, _authorityTick),
            _ => PredictionHandshakeResult.Failure(new ProtocolViolation(
                ProtocolViolationCode.InvalidSession,
                "Prediction data arrived before route authentication.")),
        };

        if (!result.Accepted)
        {
            Fail(packet.Sender, packet.RouteGeneration, packet.AttemptId);
            return;
        }

        if (result.Response is not null)
        {
            SendControl(runtime, result.Response);
        }

        if (runtime.Handshake.IsAuthenticated &&
            _coordinator.MarkAuthenticated(
                packet.Sender, packet.RouteGeneration, packet.AttemptId, _authorityTick))
        {
            PublishStatus(packet.Sender);
        }
    }

    private void SendControl(AttemptRuntime runtime, PredictionPacketEnvelope envelope)
    {
        if (!_transport.TrySendControl(
                runtime.Route.RemotePeerId,
                runtime.Route.RouteGeneration,
                runtime.AttemptId,
                _codec.Encode(envelope)))
        {
            Fail(runtime.Route.RemotePeerId, runtime.Route.RouteGeneration, runtime.AttemptId);
        }
    }

    private void TryStartHandshake(AttemptRuntime runtime)
    {
        if (!runtime.Route.LocalPeerInitiates || !runtime.TransportConnected ||
            runtime.Handshake.State != PredictionHandshakeState.ReadyToInitiate ||
            _clientTick == 0 || _authorityTick == 0 ||
            _authorityTick >= runtime.Route.ExpiresAuthorityTick)
        {
            return;
        }

        var hello = runtime.Handshake.CreateHello(
            runtime.NextSequence(), _clientTick, _authorityTick);
        SendControl(runtime, hello);
    }

    private void TrySendClockProbe(AttemptRuntime runtime)
    {
        if (!runtime.Handshake.IsAuthenticated ||
            _clientTick < runtime.NextProbeClientTick ||
            _authorityTick >= runtime.Route.ExpiresAuthorityTick)
        {
            return;
        }

        var probeSequence = runtime.NextProbeSequence++;
        var sentAt = _timeSource.GetTimestampMicroseconds();
        runtime.OutstandingProbes[probeSequence] = sentAt;
        while (runtime.OutstandingProbes.Count > MaximumOutstandingProbes)
        {
            runtime.OutstandingProbes.Remove(runtime.OutstandingProbes.Keys.Min());
        }

        var envelope = CreateDataEnvelope(runtime, _clientTick, _authorityTick);
        envelope.DirectClockProbe = new DirectClockProbe
        {
            ProbeSequence = probeSequence,
            SourceSendTimestampMicroseconds = sentAt,
        };
        runtime.NextProbeClientTick = checked(
            _clientTick + Math.Max(1U, _simulationTicksPerSecond / DirectProbeRatePerSecond));
        SendUnreliable(runtime, envelope);
    }

    private void ReplyToClockProbe(AttemptRuntime runtime, DirectClockProbe probe)
    {
        var receivedAt = _timeSource.GetTimestampMicroseconds();
        var envelope = CreateDataEnvelope(runtime, _clientTick, _authorityTick);
        envelope.DirectClockReply = new DirectClockReply
        {
            ProbeSequence = probe.ProbeSequence,
            SourceSendTimestampMicroseconds = probe.SourceSendTimestampMicroseconds,
            ReceiverReceiveTimestampMicroseconds = receivedAt,
            ReceiverSendTimestampMicroseconds = _timeSource.GetTimestampMicroseconds(),
            ReceiverEstimatedAuthorityTick = _authorityTick,
        };
        SendUnreliable(runtime, envelope);
    }

    private void ObserveClockReply(AttemptRuntime runtime, DirectClockReply reply)
    {
        if (!runtime.OutstandingProbes.Remove(reply.ProbeSequence, out var sentAt) ||
            sentAt != reply.SourceSendTimestampMicroseconds)
        {
            return;
        }

        var receivedAt = _timeSource.GetTimestampMicroseconds();
        if (receivedAt >= sentAt)
        {
            runtime.Path.ObserveRoundTrip((receivedAt - sentAt) / 1_000d);
        }
    }

    private PredictionPacketEnvelope CreateDataEnvelope(
        AttemptRuntime runtime,
        ulong clientTick,
        ulong estimatedAuthorityTick) => new()
    {
        ProtocolVersion = ProtocolConstants.CurrentVersion,
        SessionId = runtime.Route.SessionId,
        SourceSessionPeerId = runtime.Route.LocalPeerId.Value,
        DestinationSessionPeerId = runtime.Route.RemotePeerId.Value,
        SourcePeerSessionGeneration = runtime.Route.LocalPeerSessionGeneration.Value,
        DestinationPeerSessionGeneration = runtime.Route.RemotePeerSessionGeneration.Value,
        PredictionRouteGeneration = runtime.Route.RouteGeneration,
        PacketSequence = runtime.NextSequence(),
        ClientTick = clientTick,
        EstimatedAuthorityTick = estimatedAuthorityTick,
    };

    private void SendUnreliable(
        AttemptRuntime runtime,
        PredictionPacketEnvelope envelope)
    {
        if (!_transport.TrySendMovement(
                runtime.Route.RemotePeerId,
                runtime.Route.RouteGeneration,
                runtime.AttemptId,
                _codec.Encode(envelope)))
        {
            Fail(runtime.Route.RemotePeerId, runtime.Route.RouteGeneration, runtime.AttemptId);
        }
    }

    private void Fail(SessionPeerId peer, uint routeGeneration, ulong attemptId)
    {
        ApplyDirectives(_coordinator.ReportRouteFailure(
            peer, routeGeneration, attemptId, _authorityTick));
        PublishStatus(peer);
    }

    private void PublishStatus(SessionPeerId peer)
    {
        var status = _coordinator.RouteStatuses.SingleOrDefault(
            candidate => candidate.Route.RemotePeerId == peer);
        if (status is not null)
        {
            RouteStatusChanged?.Invoke(status);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class AttemptRuntime(
        AuthorizedPredictionRoute route,
        ulong attemptId,
        IPredictionHandshakeSession handshake,
        ulong deadlineAuthorityTick,
        uint expectedMovementPacketsPerSecond)
    {
        private ulong _nextPacketSequence = 1;
        public AuthorizedPredictionRoute Route { get; } = route;
        public ulong AttemptId { get; } = attemptId;
        public IPredictionHandshakeSession Handshake { get; } = handshake;
        public ulong DeadlineAuthorityTick { get; } = deadlineAuthorityTick;
        public bool TransportConnected { get; set; }
        public PredictionPacketSequenceWindow DataSequences { get; } = new();
        public NetworkPathEstimator Path { get; } = new(expectedMovementPacketsPerSecond);
        public Dictionary<ulong, ulong> OutstandingProbes { get; } = [];
        public ulong NextProbeSequence { get; set; } = 1;
        public ulong NextProbeClientTick { get; set; }
        public ulong LastMovementArrivalTimestampMicroseconds { get; set; }
        public ulong NextSequence() => _nextPacketSequence++;

        public PredictionProtocolValidationContext ValidationContext(
            ulong currentAuthorityTick,
            bool authenticated) => new(
            Route.SessionId,
            Route.LocalPeerId.Value,
            Route.RemotePeerId.Value,
            Route.LocalPeerSessionGeneration.Value,
            Route.RemotePeerSessionGeneration.Value,
            Route.RouteGeneration,
            Route.ExpiresAuthorityTick,
            currentAuthorityTick,
            authenticated);
    }
}
