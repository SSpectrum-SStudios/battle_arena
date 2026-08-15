using BattleArena.Multiplayer.Connection;
using BattleArena.Multiplayer.OwnerPrediction;
using BattleArena.Multiplayer.Prediction;
using BattleArena.Multiplayer.Transport;
using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Tests.OwnerPrediction;

public sealed class PredictionMeshImpairmentDecoratorTests
{
    [Fact]
    public void MovementIsDelayedAndPayloadIsSnapshotted()
    {
        var inner = new FakeMeshTransport();
        using var decorator = Create(
            inner,
            new NetworkImpairmentPolicy(
                upstreamBaseDelay: TimeSpan.FromMilliseconds(40)));
        decorator.Apply(Start());
        var payload = new byte[] { 7 };

        Assert.True(decorator.TrySendMovement(Peer(2), 3, 4, payload));
        payload[0] = 99;
        decorator.AdvanceTo(TimeSpan.FromMilliseconds(39));
        Assert.Empty(inner.MovementSends);

        decorator.AdvanceTo(TimeSpan.FromMilliseconds(40));
        Assert.Equal(7, inner.MovementSends.Single().Payload.Span[0]);
        Assert.Equal(TransportDelivery.Unreliable, inner.MovementSends.Single().Delivery);
    }

    [Fact]
    public void DroppedMeshMovementIsAcceptedWithoutTouchingAuthorityPath()
    {
        var mesh = new FakeMeshTransport();
        using var decorator = Create(
            mesh,
            new NetworkImpairmentPolicy(independentLossProbability: 1d));
        var authority = new FakeAuthorityTransport();
        decorator.Apply(Start());

        Assert.True(decorator.TrySendMovement(Peer(2), 3, 4, new byte[] { 1 }));
        authority.Send(new OutboundTransportPacket(
            new TransportConnectionId(9),
            TransportChannel.Movement,
            TransportDelivery.Unreliable,
            new byte[] { 8 }));

        Assert.Empty(mesh.MovementSends);
        Assert.Single(authority.Sent);
        Assert.Equal(8, authority.Sent[0].Payload.Span[0]);
    }

    [Fact]
    public void DelayedMeshFailureFallsBackAndAReplacementRouteRecovers()
    {
        var mesh = new FakeMeshTransport { FailMovementSends = true };
        using var decorator = Create(
            mesh,
            new NetworkImpairmentPolicy(
                upstreamBaseDelay: TimeSpan.FromMilliseconds(20)));
        PredictionTransportRouteChanged? failure = null;
        decorator.RouteChanged += change => failure = change;
        decorator.Apply(Start());
        Assert.True(decorator.TrySendMovement(Peer(2), 3, 4, new byte[] { 1 }));

        var exception = Record.Exception(
            () => decorator.AdvanceTo(TimeSpan.FromMilliseconds(20)));

        Assert.Null(exception);
        Assert.NotNull(failure);
        Assert.Equal(PredictionTransportRouteState.Failed, failure!.State);
        Assert.Equal(4UL, failure.AttemptId);
        Assert.False(decorator.TrySendMovement(Peer(2), 3, 4, new byte[] { 2 }));
        Assert.Equal(0, decorator.PendingPacketCount);

        mesh.FailMovementSends = false;
        decorator.Apply(Start(attemptId: 5));
        Assert.True(decorator.TrySendMovement(Peer(2), 3, 5, new byte[] { 3 }));
        decorator.AdvanceTo(TimeSpan.FromMilliseconds(40));
        Assert.Equal(3, mesh.MovementSends.Single().Payload.Span[0]);
    }

    [Fact]
    public void ThrowingMeshApplyBecomesRouteFailureInsteadOfEscaping()
    {
        var mesh = new FakeMeshTransport { ThrowOnApply = true };
        using var decorator = Create(mesh, NetworkImpairmentPolicy.None);
        PredictionTransportRouteChanged? failure = null;
        decorator.RouteChanged += change => failure = change;

        var exception = Record.Exception(() => decorator.Apply(Start()));

        Assert.Null(exception);
        Assert.Equal(PredictionTransportRouteState.Failed, failure?.State);
        Assert.False(decorator.TrySendMovement(Peer(2), 3, 4, new byte[] { 1 }));
    }

    [Fact]
    public void ThrowingDelayedSendBecomesRouteFailureInsteadOfEscaping()
    {
        var mesh = new FakeMeshTransport { ThrowOnMovementSend = true };
        using var decorator = Create(mesh, NetworkImpairmentPolicy.None);
        PredictionTransportRouteChanged? failure = null;
        decorator.RouteChanged += change => failure = change;
        decorator.Apply(Start());

        var exception = Record.Exception(
            () => decorator.TrySendMovement(Peer(2), 3, 4, new byte[] { 1 }));

        Assert.Null(exception);
        Assert.Equal(PredictionTransportRouteState.Failed, failure?.State);
    }

    [Fact]
    public void ControlPassesThroughACompleteMovementLossSchedule()
    {
        var mesh = new FakeMeshTransport();
        using var decorator = Create(
            mesh,
            new NetworkImpairmentPolicy(independentLossProbability: 1d));
        decorator.Apply(Start());

        Assert.True(decorator.TrySendControl(Peer(2), 3, 4, new byte[] { 9 }));

        Assert.Equal(9, mesh.ControlSends.Single().Payload.Span[0]);
        Assert.Empty(mesh.MovementSends);
    }

    [Fact]
    public void ControlCanBeExplicitlyRejectedWithoutCallingInnerTransport()
    {
        var mesh = new FakeMeshTransport();
        using var decorator = Create(
            mesh,
            NetworkImpairmentPolicy.None,
            controlBehavior: ReliableImpairmentBehavior.Reject);
        decorator.Apply(Start());

        Assert.False(decorator.TrySendControl(Peer(2), 3, 4, new byte[] { 9 }));
        Assert.Empty(mesh.ControlSends);
    }

    [Fact]
    public void LaterControlCannotOvertakeDeferredReentrantControl()
    {
        var mesh = new FakeMeshTransport();
        using var decorator = Create(mesh, NetworkImpairmentPolicy.None);
        decorator.Apply(Start());
        var control = new byte[] { 6 };
        mesh.AfterMovementSend = _ =>
        {
            Assert.True(decorator.TrySendControl(Peer(2), 3, 4, control));
            control[0] = 99;
        };

        Assert.True(decorator.TrySendMovement(Peer(2), 3, 4, new byte[] { 1 }));
        Assert.Empty(mesh.ControlSends);

        Assert.True(decorator.TrySendControl(Peer(2), 3, 4, new byte[] { 7 }));
        Assert.Equal([6, 7], mesh.ControlSends.Select(sent => sent.Payload.Span[0]));
    }

    [Fact]
    public void RouteReplacementPurgesOldPacketsAndStartsANewOrdinalSequence()
    {
        var mesh = new FakeMeshTransport();
        var factoryKeys = new List<PredictionMeshRouteKey>();
        using var decorator = new PredictionMeshImpairmentDecorator(
            mesh,
            key =>
            {
                factoryKeys.Add(key);
                return new NetworkImpairmentSchedule(
                    1,
                    new NetworkImpairmentPolicy(
                        upstreamBaseDelay: TimeSpan.FromMilliseconds(10)));
            },
            NetworkImpairmentDirection.Upstream,
            ReliableImpairmentBehavior.PassThrough,
            8,
            ImpairmentQueueOverflowBehavior.RejectNewest);
        decorator.Apply(Start());
        Assert.True(decorator.TrySendMovement(Peer(2), 3, 4, new byte[] { 1 }));

        decorator.Apply(Start(routeGeneration: 4, attemptId: 5));
        Assert.Equal(0, decorator.PendingPacketCount);
        Assert.False(decorator.TrySendMovement(Peer(2), 3, 4, new byte[] { 2 }));
        Assert.True(decorator.TrySendMovement(Peer(2), 4, 5, new byte[] { 3 }));
        decorator.AdvanceTo(TimeSpan.FromMilliseconds(10));

        Assert.Equal(3, mesh.MovementSends.Single().Payload.Span[0]);
        Assert.Equal(
            [
                new PredictionMeshRouteKey(Peer(2), 3, 4),
                new PredictionMeshRouteKey(Peer(2), 4, 5),
            ],
            factoryKeys);
    }

    [Fact]
    public void RemovePurgesPendingPackets()
    {
        var mesh = new FakeMeshTransport();
        using var decorator = Create(
            mesh,
            new NetworkImpairmentPolicy(
                upstreamBaseDelay: TimeSpan.FromSeconds(1)));
        decorator.Apply(Start());
        Assert.True(decorator.TrySendMovement(Peer(2), 3, 4, new byte[] { 1 }));

        decorator.Apply(new PredictionMeshDirective(
            PredictionMeshDirectiveKind.Remove,
            Peer(2),
            null,
            4));
        decorator.AdvanceTo(TimeSpan.FromSeconds(1));

        Assert.Empty(mesh.MovementSends);
        Assert.Equal(0, decorator.PendingPacketCount);
    }

    [Fact]
    public void InnerDisconnectPurgesOnlyTheMatchingAttempt()
    {
        var mesh = new FakeMeshTransport();
        using var decorator = Create(
            mesh,
            new NetworkImpairmentPolicy(
                upstreamBaseDelay: TimeSpan.FromSeconds(1)));
        decorator.Apply(Start());
        Assert.True(decorator.TrySendMovement(Peer(2), 3, 4, new byte[] { 1 }));

        mesh.RaiseRouteChanged(new PredictionTransportRouteChanged(
            Peer(2),
            2,
            3,
            PredictionTransportRouteState.Disconnected));
        Assert.Equal(1, decorator.PendingPacketCount);
        mesh.RaiseRouteChanged(new PredictionTransportRouteChanged(
            Peer(2),
            3,
            4,
            PredictionTransportRouteState.Disconnected));

        Assert.Equal(0, decorator.PendingPacketCount);
    }

    [Theory]
    [InlineData(ImpairmentQueueOverflowBehavior.RejectNewest, false)]
    [InlineData(ImpairmentQueueOverflowBehavior.DropNewest, true)]
    public void DuplicateAdmissionIsAtomicAtTheQueueLimit(
        ImpairmentQueueOverflowBehavior behavior,
        bool expectedAccepted)
    {
        var mesh = new FakeMeshTransport();
        using var decorator = Create(
            mesh,
            new NetworkImpairmentPolicy(
                duplicateProbability: 1d,
                duplicateSpacing: TimeSpan.FromMilliseconds(1)),
            maximumPendingPackets: 1,
            overflowBehavior: behavior);
        decorator.Apply(Start());

        Assert.Equal(
            expectedAccepted,
            decorator.TrySendMovement(Peer(2), 3, 4, new byte[] { 1 }));
        Assert.Equal(0, decorator.PendingPacketCount);
        Assert.Empty(mesh.MovementSends);
    }

    [Fact]
    public void DuplicateMovementUsesTheSameRouteAndPayload()
    {
        var mesh = new FakeMeshTransport();
        using var decorator = Create(
            mesh,
            new NetworkImpairmentPolicy(
                duplicateProbability: 1d,
                duplicateSpacing: TimeSpan.FromMilliseconds(5)));
        decorator.Apply(Start());

        Assert.True(decorator.TrySendMovement(Peer(2), 3, 4, new byte[] { 4 }));
        Assert.Single(mesh.MovementSends);
        decorator.AdvanceTo(TimeSpan.FromMilliseconds(5));

        Assert.Equal(2, mesh.MovementSends.Count);
        Assert.All(mesh.MovementSends, sent =>
        {
            Assert.Equal(Peer(2), sent.Recipient);
            Assert.Equal(3U, sent.RouteGeneration);
            Assert.Equal(4UL, sent.AttemptId);
            Assert.Equal(4, sent.Payload.Span[0]);
        });
    }

    [Fact]
    public void PropertiesAndInboundEventsAreTransparent()
    {
        var mesh = new FakeMeshTransport();
        using var decorator = Create(mesh, NetworkImpairmentPolicy.None);
        InboundPredictionPacket? received = null;
        PredictionTransportRouteChanged? changed = null;
        decorator.PacketReceived += packet => received = packet;
        decorator.RouteChanged += change => changed = change;
        var inbound = new InboundPredictionPacket(
            Peer(2),
            3,
            4,
            TransportDelivery.Unreliable,
            new byte[] { 5 });
        var routeChange = new PredictionTransportRouteChanged(
            Peer(2),
            3,
            4,
            PredictionTransportRouteState.Connected);

        mesh.RaisePacket(inbound);
        mesh.RaiseRouteChanged(routeChange);

        Assert.Equal(TransportKind.Test, decorator.Kind);
        Assert.True(decorator.SupportsDirectRoutes);
        Assert.Same(mesh.LocalRouteDescriptor, decorator.LocalRouteDescriptor);
        Assert.Equal(inbound, received);
        Assert.Equal(routeChange, changed);
    }

    [Fact]
    public void StopIsIdempotentPurgesStateAndSuppressesFurtherMeshUse()
    {
        var mesh = new FakeMeshTransport { ThrowOnStop = true };
        using var decorator = Create(
            mesh,
            new NetworkImpairmentPolicy(
                upstreamBaseDelay: TimeSpan.FromSeconds(1)));
        decorator.Apply(Start());
        Assert.True(decorator.TrySendMovement(Peer(2), 3, 4, new byte[] { 1 }));

        var first = Record.Exception(decorator.Stop);
        var second = Record.Exception(decorator.Stop);

        Assert.Null(first);
        Assert.Null(second);
        Assert.Equal(1, mesh.StopCalls);
        Assert.Equal(0, decorator.PendingPacketCount);
        Assert.False(decorator.TrySendMovement(Peer(2), 3, 4, new byte[] { 2 }));
        Assert.Throws<InvalidOperationException>(() => decorator.Apply(Start()));

        mesh.RaisePacket(new InboundPredictionPacket(
            Peer(2),
            3,
            4,
            TransportDelivery.Unreliable,
            new byte[] { 3 }));
        mesh.RaiseRouteChanged(new PredictionTransportRouteChanged(
            Peer(2),
            3,
            4,
            PredictionTransportRouteState.Connected));
        Assert.Equal(0, mesh.PacketSubscriberCount);
        Assert.Equal(0, mesh.RouteSubscriberCount);
    }

    [Fact]
    public void DisposalUnsubscribesAndRejectsMutation()
    {
        var mesh = new FakeMeshTransport();
        var decorator = Create(mesh, NetworkImpairmentPolicy.None);
        var receivedCount = 0;
        decorator.PacketReceived += _ => receivedCount++;

        decorator.Dispose();
        decorator.Dispose();
        mesh.RaisePacket(new InboundPredictionPacket(
            Peer(2),
            3,
            4,
            TransportDelivery.Unreliable,
            new byte[] { 1 }));

        Assert.Equal(0, receivedCount);
        Assert.Throws<ObjectDisposedException>(
            () => decorator.TrySendMovement(Peer(2), 3, 4, new byte[] { 1 }));
        Assert.Throws<ObjectDisposedException>(decorator.Stop);
    }

    [Fact]
    public void InvalidConstructionAndDirectivesAreRejected()
    {
        Func<PredictionMeshRouteKey, NetworkImpairmentSchedule> factory = _ =>
            new NetworkImpairmentSchedule(1, NetworkImpairmentPolicy.None);
        Assert.Throws<ArgumentNullException>(() => new PredictionMeshImpairmentDecorator(
            null!,
            factory,
            NetworkImpairmentDirection.Upstream,
            ReliableImpairmentBehavior.PassThrough,
            1,
            ImpairmentQueueOverflowBehavior.RejectNewest));
        Assert.Throws<ArgumentNullException>(() => new PredictionMeshImpairmentDecorator(
            new FakeMeshTransport(),
            null!,
            NetworkImpairmentDirection.Upstream,
            ReliableImpairmentBehavior.PassThrough,
            1,
            ImpairmentQueueOverflowBehavior.RejectNewest));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(
            new FakeMeshTransport(),
            NetworkImpairmentPolicy.None,
            maximumPendingPackets: 0));

        using var decorator = Create(new FakeMeshTransport(), NetworkImpairmentPolicy.None);
        Assert.Throws<ArgumentException>(() => decorator.Apply(new PredictionMeshDirective(
            PredictionMeshDirectiveKind.Listen,
            Peer(2),
            null,
            4)));
        Assert.Throws<ArgumentException>(() => decorator.Apply(new PredictionMeshDirective(
            PredictionMeshDirectiveKind.Listen,
            Peer(3),
            Route(Peer(2), 3),
            4)));
        Assert.False(decorator.TrySendMovement(Peer(2), 3, 4, new byte[] { 1 }));
    }

    private static PredictionMeshImpairmentDecorator Create(
        FakeMeshTransport inner,
        NetworkImpairmentPolicy policy,
        ulong seed = 1,
        NetworkImpairmentDirection direction = NetworkImpairmentDirection.Upstream,
        ReliableImpairmentBehavior controlBehavior = ReliableImpairmentBehavior.PassThrough,
        int maximumPendingPackets = 1_024,
        ImpairmentQueueOverflowBehavior overflowBehavior =
            ImpairmentQueueOverflowBehavior.RejectNewest) =>
        new(
            inner,
            _ => new NetworkImpairmentSchedule(seed, policy),
            direction,
            controlBehavior,
            maximumPendingPackets,
            overflowBehavior);

    private static PredictionMeshDirective Start(
        uint routeGeneration = 3,
        ulong attemptId = 4) =>
        new(
            PredictionMeshDirectiveKind.Initiate,
            Peer(2),
            Route(Peer(2), routeGeneration),
            attemptId);

    private static AuthorizedPredictionRoute Route(
        SessionPeerId remote,
        uint routeGeneration) =>
        new(
            10,
            Peer(1),
            ConnectionGeneration.Initial,
            remote,
            ConnectionGeneration.Initial,
            routeGeneration,
            new PredictionRouteDescriptor(),
            new byte[] { 1 },
            100);

    private static SessionPeerId Peer(ulong value) => new(value);

    private sealed record SentPredictionPacket(
        SessionPeerId Recipient,
        uint RouteGeneration,
        ulong AttemptId,
        TransportDelivery Delivery,
        ReadOnlyMemory<byte> Payload);

    private sealed class FakeMeshTransport : IPredictionMeshTransport
    {
        private Action<PredictionTransportRouteChanged>? _routeChanged;
        private Action<InboundPredictionPacket>? _packetReceived;

        public event Action<PredictionTransportRouteChanged>? RouteChanged
        {
            add => _routeChanged += value;
            remove => _routeChanged -= value;
        }

        public event Action<InboundPredictionPacket>? PacketReceived
        {
            add => _packetReceived += value;
            remove => _packetReceived -= value;
        }

        public TransportKind Kind => TransportKind.Test;

        public bool SupportsDirectRoutes => true;

        public PredictionRouteDescriptor LocalRouteDescriptor { get; } = new();

        public List<PredictionMeshDirective> Applied { get; } = [];

        public List<SentPredictionPacket> ControlSends { get; } = [];

        public List<SentPredictionPacket> MovementSends { get; } = [];

        public bool FailControlSends { get; set; }

        public bool FailMovementSends { get; set; }

        public bool ThrowOnApply { get; set; }

        public bool ThrowOnMovementSend { get; set; }

        public bool ThrowOnStop { get; set; }

        public int StopCalls { get; private set; }

        public int RouteSubscriberCount => _routeChanged?.GetInvocationList().Length ?? 0;

        public int PacketSubscriberCount => _packetReceived?.GetInvocationList().Length ?? 0;

        public Action<SentPredictionPacket>? AfterMovementSend { get; set; }

        public void Apply(PredictionMeshDirective directive)
        {
            if (ThrowOnApply)
            {
                throw new InvalidOperationException("Injected apply failure.");
            }

            Applied.Add(directive);
        }

        public bool TrySendControl(
            SessionPeerId recipient,
            uint routeGeneration,
            ulong attemptId,
            ReadOnlyMemory<byte> payload)
        {
            if (FailControlSends)
            {
                return false;
            }

            ControlSends.Add(new SentPredictionPacket(
                recipient,
                routeGeneration,
                attemptId,
                TransportDelivery.ReliableOrdered,
                payload.ToArray()));
            return true;
        }

        public bool TrySendMovement(
            SessionPeerId recipient,
            uint routeGeneration,
            ulong attemptId,
            ReadOnlyMemory<byte> payload)
        {
            if (ThrowOnMovementSend)
            {
                throw new InvalidOperationException("Injected movement send failure.");
            }

            if (FailMovementSends)
            {
                return false;
            }

            var sent = new SentPredictionPacket(
                recipient,
                routeGeneration,
                attemptId,
                TransportDelivery.Unreliable,
                payload.ToArray());
            MovementSends.Add(sent);
            AfterMovementSend?.Invoke(sent);
            return true;
        }

        public void Stop()
        {
            StopCalls++;
            if (ThrowOnStop)
            {
                throw new InvalidOperationException("Injected stop failure.");
            }
        }

        public void RaiseRouteChanged(PredictionTransportRouteChanged change) =>
            _routeChanged?.Invoke(change);

        public void RaisePacket(InboundPredictionPacket packet) =>
            _packetReceived?.Invoke(packet);
    }

    private sealed class FakeAuthorityTransport : INetworkTransport
    {
        public event Action<InboundTransportPacket>? PacketReceived;

        public event Action<TransportConnectionId>? ConnectionOpened;

        public event Action<TransportConnectionId>? ConnectionClosed;

        public TransportKind Kind => TransportKind.Test;

        public List<OutboundTransportPacket> Sent { get; } = [];

        public void Send(OutboundTransportPacket packet) => Sent.Add(packet);

        public void KeepCompilerAwareOfEvents()
        {
            _ = PacketReceived;
            _ = ConnectionOpened;
            _ = ConnectionClosed;
        }
    }
}
