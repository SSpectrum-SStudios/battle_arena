using BattleArena.Multiplayer.Connection;
using BattleArena.Multiplayer.Prediction;
using BattleArena.Multiplayer.Protocol;
using BattleArena.Multiplayer.Transport;
using BattleArena.Multiplayer.Timing;
using BattleArena.Multiplayer.Tests.Protocol;
using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Tests.Prediction;

public sealed class PredictionMeshDriverTests
{
    [Fact]
    public void TwoAuthorizedPeersCompleteMutualProofBeforeBecomingUsable()
    {
        var setup = CreateSetup();
        setup.Left.Driver.Advance(100, 100);
        setup.Right.Driver.Advance(100, 100);

        setup.Right.Driver.ApplyAuthorization(setup.RightAuthorization, 100);
        setup.Left.Driver.ApplyAuthorization(setup.LeftAuthorization, 100);

        Assert.Equal(
            PredictionRouteHealth.Authenticated,
            Assert.Single(setup.Left.Driver.RouteStatuses).Health);
        Assert.Equal(
            PredictionRouteHealth.Authenticated,
            Assert.Single(setup.Right.Driver.RouteStatuses).Health);
        Assert.True(setup.Left.Transport.Connected);
        Assert.True(setup.Right.Transport.Connected);
    }

    [Fact]
    public void DirectFailureImmediatelyFallsBackWithoutStoppingAuthorityPlay()
    {
        var setup = CreateSetup();
        setup.Left.Driver.Advance(100, 100);
        setup.Left.Driver.ApplyAuthorization(setup.LeftAuthorization, 100);

        setup.Left.Transport.FailCurrentRoute();

        var status = Assert.Single(setup.Left.Driver.RouteStatuses);
        Assert.Equal(PredictionRouteHealth.AuthorityFallback, status.Health);
        Assert.Equal(1, status.ConsecutiveFailures);
        Assert.False(setup.Left.Transport.Stopped);
    }

    [Fact]
    public void HandshakeTimeoutFallsBackAndRetriesWithNewAttemptIdentity()
    {
        var setup = CreateSetup();
        setup.Left.Driver.Advance(100, 100);
        setup.Left.Driver.ApplyAuthorization(setup.LeftAuthorization, 100);

        setup.Left.Driver.Advance(280, 280);
        var fallback = Assert.Single(setup.Left.Driver.RouteStatuses);
        Assert.Equal(PredictionRouteHealth.AuthorityFallback, fallback.Health);
        var firstAttempt = fallback.AttemptId;

        setup.Left.Driver.Advance(310, 310);
        var retry = Assert.Single(setup.Left.Driver.RouteStatuses);
        Assert.Equal(PredictionRouteHealth.AwaitingHandshake, retry.Health);
        Assert.True(retry.AttemptId > firstAttempt);
    }

    [Fact]
    public void AuthenticatedDataIsValidatedAndReplayIsDiscarded()
    {
        var setup = CreateSetup();
        setup.Left.Driver.Advance(100, 100);
        setup.Right.Driver.Advance(100, 100);
        setup.Right.Driver.ApplyAuthorization(setup.RightAuthorization, 100);
        setup.Left.Driver.ApplyAuthorization(setup.LeftAuthorization, 100);
        var received = 0;
        setup.Left.Driver.AuthenticatedPacketReceived += _ => received++;
        var valid = DataEnvelope(setup.RightAuthorization, 100);

        setup.Right.Transport.SendEnvelope(valid);
        setup.Right.Transport.SendEnvelope(valid);

        Assert.Equal(1, received);
        Assert.Equal(
            PredictionRouteHealth.Authenticated,
            Assert.Single(setup.Left.Driver.RouteStatuses).Health);

        var forged = DataEnvelope(setup.RightAuthorization, 101);
        forged.SessionId++;
        setup.Right.Transport.SendEnvelope(forged);

        Assert.Equal(1, received);
        var fallback = Assert.Single(setup.Left.Driver.RouteStatuses);
        Assert.Equal(PredictionRouteHealth.AuthorityFallback, fallback.Health);
        Assert.Equal(1, fallback.ConsecutiveFailures);
    }

    [Fact]
    public void FailedControlSendFallsBackWithoutThrowing()
    {
        var setup = CreateSetup();
        setup.Left.Transport.FailControlSends = true;
        setup.Left.Driver.Advance(100, 100);
        setup.Right.Driver.Advance(100, 100);
        setup.Right.Driver.ApplyAuthorization(setup.RightAuthorization, 100);

        var exception = Record.Exception(() =>
            setup.Left.Driver.ApplyAuthorization(setup.LeftAuthorization, 100));

        Assert.Null(exception);
        var fallback = Assert.Single(setup.Left.Driver.RouteStatuses);
        Assert.Equal(PredictionRouteHealth.AuthorityFallback, fallback.Health);
        Assert.Equal(1, fallback.ConsecutiveFailures);
    }

    [Fact]
    public void PublishMovementSendsOneAuthenticatedUnreliableCopyPerRoute()
    {
        var setup = CreateSetup();
        setup.Left.Driver.Advance(100, 100);
        setup.Right.Driver.Advance(100, 100);
        setup.Right.Driver.ApplyAuthorization(setup.RightAuthorization, 100);
        setup.Left.Driver.ApplyAuthorization(setup.LeftAuthorization, 100);
        AuthenticatedPredictionPacket? received = null;
        setup.Right.Driver.AuthenticatedPacketReceived += packet => received = packet;
        var bundle = PredictionProtocolTestData.MovementBundle();
        bundle.SourceCombatantId = 2;

        setup.Left.Driver.PublishMovement(bundle);

        Assert.NotNull(received);
        Assert.Equal(TransportDelivery.Unreliable, received.Delivery);
        Assert.Equal(2UL, received.Envelope.SourceSessionPeerId);
        Assert.Equal(3UL, received.Envelope.DestinationSessionPeerId);
        Assert.Equal(bundle.BundleSequence,
            received.Envelope.MovementPredictionBundle.BundleSequence);
    }

    [Fact]
    public void AuthenticatedPeersMeasureDirectRttAndMovementCadence()
    {
        var setup = CreateSetup();
        setup.Left.Driver.Advance(100, 100);
        setup.Right.Driver.Advance(100, 100);
        setup.Right.Driver.ApplyAuthorization(setup.RightAuthorization, 100);
        setup.Left.Driver.ApplyAuthorization(setup.LeftAuthorization, 100);

        setup.Left.Driver.Advance(101, 101);
        var bundle = PredictionProtocolTestData.MovementBundle();
        bundle.SourceCombatantId = 2;
        setup.Left.Driver.PublishMovement(bundle);

        var leftPath = Assert.Single(setup.Left.Driver.DirectPathStatuses);
        var rightPath = Assert.Single(setup.Right.Driver.DirectPathStatuses);
        Assert.True(leftPath.Path.SmoothedRttMilliseconds > 0d);
        Assert.Equal(bundle.BundleSequence, rightPath.Path.LatestSequence);
        Assert.True(rightPath.LastMovementArrivalTimestampMicroseconds > 0);
    }

    [Fact]
    public void FailedClockProbeSendFallsBackWithoutMutatingActiveEnumeration()
    {
        var setup = CreateSetup();
        setup.Left.Driver.Advance(100, 100);
        setup.Right.Driver.Advance(100, 100);
        setup.Right.Driver.ApplyAuthorization(setup.RightAuthorization, 100);
        setup.Left.Driver.ApplyAuthorization(setup.LeftAuthorization, 100);
        setup.Left.Transport.FailMovementSends = true;

        var exception = Record.Exception(() => setup.Left.Driver.Advance(101, 101));

        Assert.Null(exception);
        Assert.Equal(
            PredictionRouteHealth.AuthorityFallback,
            Assert.Single(setup.Left.Driver.RouteStatuses).Health);
    }

    private static MeshSetup CreateSetup()
    {
        var broker = new AuthorityPredictionRouteBroker(
            new PredictionTestCredentialGenerator(),
            AuthorityPredictionRouteBrokerTests.Peer(1, authority: true),
            10_000);
        broker.AddOrUpdatePeer(AuthorityPredictionRouteBrokerTests.Peer(2));
        var roster = broker.AddOrUpdatePeer(AuthorityPredictionRouteBrokerTests.Peer(3)).Roster!;
        broker.ApplyAdvertisement(
            new SessionPeerId(2),
            ConnectionGeneration.Initial,
            EnetPredictionRouteDescriptorCodec.Encode(
                new EnetPredictionEndpoint("127.0.0.1", 7782)),
            100);
        var delta = broker.ApplyAdvertisement(
            new SessionPeerId(3),
            ConnectionGeneration.Initial,
            EnetPredictionRouteDescriptorCodec.Encode(
                new EnetPredictionEndpoint("127.0.0.1", 7783)),
            100);
        var hub = new FakePredictionHub();
        var left = CreateEndpoint(2, roster, hub);
        var right = CreateEndpoint(3, roster, hub);
        return new MeshSetup(
            left,
            right,
            delta.Authorizations.Single(value => value.LocalSessionPeerId == 2),
            delta.Authorizations.Single(value => value.LocalSessionPeerId == 3));
    }

    private static TestEndpoint CreateEndpoint(
        ulong id,
        PeerRosterUpdate roster,
        FakePredictionHub hub)
    {
        var peerId = new SessionPeerId(id);
        var directory = new SessionPeerDirectory(peerId);
        Assert.True(directory.TryApply(roster));
        var transport = new FakePredictionTransport(peerId, hub);
        var driver = new PredictionMeshDriver(
            new ClientPredictionMeshCoordinator(73, directory),
            transport,
            new PredictionHandshakeSessionFactory(
                new HmacPredictionHandshakeAuthenticator(),
                new CryptographicPredictionCredentialGenerator(),
                new PredictionInboundMessageValidator()),
            new ProtobufPredictionProtocolCodec(),
            new PredictionInboundMessageValidator(),
            new IncrementingNetworkTimeSource());
        return new TestEndpoint(driver, transport);
    }

    private static PredictionPacketEnvelope DataEnvelope(
        PredictionRouteAuthorization senderAuthorization,
        ulong packetSequence)
    {
        var bundle = PredictionProtocolTestData.MovementBundle();
        bundle.SourceCombatantId = senderAuthorization.LocalSessionPeerId;
        return new PredictionPacketEnvelope
        {
            ProtocolVersion = ProtocolConstants.CurrentVersion,
            SessionId = 73,
            SourceSessionPeerId = senderAuthorization.LocalSessionPeerId,
            DestinationSessionPeerId = senderAuthorization.RemoteSessionPeerId,
            SourcePeerSessionGeneration = senderAuthorization.LocalPeerSessionGeneration,
            DestinationPeerSessionGeneration = senderAuthorization.RemotePeerSessionGeneration,
            PredictionRouteGeneration = senderAuthorization.PredictionRouteGeneration,
            PacketSequence = packetSequence,
            ClientTick = 102,
            EstimatedAuthorityTick = 202,
            MovementPredictionBundle = bundle,
        };
    }

    private sealed record MeshSetup(
        TestEndpoint Left,
        TestEndpoint Right,
        PredictionRouteAuthorization LeftAuthorization,
        PredictionRouteAuthorization RightAuthorization);

    private sealed record TestEndpoint(
        PredictionMeshDriver Driver,
        FakePredictionTransport Transport);

    private sealed class IncrementingNetworkTimeSource : INetworkTimeSource
    {
        private ulong _now = 1_000_000;

        public ulong GetTimestampMicroseconds()
        {
            _now += 1_000;
            return _now;
        }
    }

    private sealed class FakePredictionHub
    {
        private readonly Dictionary<SessionPeerId, FakePredictionTransport> _transports = [];

        public void Add(FakePredictionTransport transport) =>
            _transports.Add(transport.LocalPeerId, transport);

        public void Route(
            FakePredictionTransport sender,
            ReadOnlyMemory<byte> payload,
            TransportDelivery delivery = TransportDelivery.ReliableOrdered)
        {
            var target = _transports[sender.RemotePeerId];
            target.Deliver(sender.LocalPeerId, payload, delivery);
        }

        public void TryConnect(FakePredictionTransport source)
        {
            if (!source.HasRoute || !_transports.TryGetValue(source.RemotePeerId, out var target) ||
                !target.HasRoute || target.RemotePeerId != source.LocalPeerId)
            {
                return;
            }

            target.Connect();
            source.Connect();
        }
    }

    private sealed class FakePredictionTransport : IPredictionMeshTransport
    {
        private readonly FakePredictionHub _hub;
        private PredictionMeshDirective? _directive;

        public FakePredictionTransport(SessionPeerId localPeerId, FakePredictionHub hub)
        {
            LocalPeerId = localPeerId;
            _hub = hub;
            hub.Add(this);
        }

        public event Action<PredictionTransportRouteChanged>? RouteChanged;
        public event Action<InboundPredictionPacket>? PacketReceived;
        public SessionPeerId LocalPeerId { get; }
        public SessionPeerId RemotePeerId => _directive!.RemotePeerId;
        public bool HasRoute => _directive?.Route is not null;
        public bool Connected { get; private set; }
        public bool Stopped { get; private set; }
        public bool FailControlSends { get; set; }
        public bool FailMovementSends { get; set; }
        public TransportKind Kind => TransportKind.Enet;
        public bool SupportsDirectRoutes => true;
        public PredictionRouteDescriptor LocalRouteDescriptor =>
            EnetPredictionRouteDescriptorCodec.Encode(
                new EnetPredictionEndpoint("127.0.0.1", checked((ushort)(7780 + LocalPeerId.Value))));

        public void Apply(PredictionMeshDirective directive)
        {
            if (directive.Kind == PredictionMeshDirectiveKind.Remove)
            {
                Connected = false;
                _directive = null;
                return;
            }

            _directive = directive;
            _hub.TryConnect(this);
        }

        public bool TrySendControl(
            SessionPeerId recipient,
            uint routeGeneration,
            ulong attemptId,
            ReadOnlyMemory<byte> payload)
        {
            if (!Connected || FailControlSends)
            {
                return false;
            }

            _hub.Route(this, payload);
            return true;
        }

        public bool TrySendMovement(
            SessionPeerId recipient,
            uint routeGeneration,
            ulong attemptId,
            ReadOnlyMemory<byte> payload)
        {
            if (!Connected || FailMovementSends)
            {
                return false;
            }

            _hub.Route(this, payload, TransportDelivery.Unreliable);
            return true;
        }

        public void Stop() => Stopped = true;

        public void Connect()
        {
            if (Connected)
            {
                return;
            }

            Connected = true;
            var directive = _directive!;
            RouteChanged?.Invoke(new PredictionTransportRouteChanged(
                directive.RemotePeerId,
                directive.Route!.RouteGeneration,
                directive.AttemptId,
                PredictionTransportRouteState.Connected));
        }

        public void Deliver(
            SessionPeerId sender,
            ReadOnlyMemory<byte> payload,
            TransportDelivery delivery = TransportDelivery.ReliableOrdered)
        {
            var directive = _directive!;
            PacketReceived?.Invoke(new InboundPredictionPacket(
                sender,
                directive.Route!.RouteGeneration,
                directive.AttemptId,
                delivery,
                payload));
        }

        public void FailCurrentRoute()
        {
            var directive = _directive!;
            RouteChanged?.Invoke(new PredictionTransportRouteChanged(
                directive.RemotePeerId,
                directive.Route!.RouteGeneration,
                directive.AttemptId,
                PredictionTransportRouteState.Failed));
        }


        public void SendEnvelope(PredictionPacketEnvelope envelope) =>
            _hub.Route(this, new ProtobufPredictionProtocolCodec().Encode(envelope));
    }
}
