using BattleArena.Multiplayer.OwnerPrediction;
using BattleArena.Multiplayer.Transport;

namespace BattleArena.Multiplayer.Tests.OwnerPrediction;

public sealed class NetworkImpairmentTransportDecoratorTests
{
    [Fact]
    public void ZeroDelayUnreliablePacketPassesImmediately()
    {
        var inner = new FakeTransport();
        var decorator = Create(inner, NetworkImpairmentPolicy.None);
        var packet = Packet(1);

        decorator.Send(packet);

        var sent = Assert.Single(inner.Sent);
        Assert.Equal(packet.Recipient, sent.Recipient);
        Assert.Equal(packet.Channel, sent.Channel);
        Assert.Equal(packet.Delivery, sent.Delivery);
        Assert.Equal(packet.Payload.ToArray(), sent.Payload.ToArray());
        Assert.Equal(0, decorator.PendingPacketCount);
    }

    [Fact]
    public void DroppedUnreliablePacketNeverReachesInnerTransport()
    {
        var inner = new FakeTransport();
        var decorator = Create(
            inner,
            new NetworkImpairmentPolicy(independentLossProbability: 1d));

        decorator.Send(Packet(1));
        decorator.AdvanceTo(TimeSpan.FromHours(1));

        Assert.Empty(inner.Sent);
        Assert.Equal(0, decorator.PendingPacketCount);
    }

    [Fact]
    public void DelayFlushesOnlyAtOrAfterDueTime()
    {
        var inner = new FakeTransport();
        var decorator = Create(
            inner,
            new NetworkImpairmentPolicy(
                upstreamBaseDelay: TimeSpan.FromMilliseconds(50)));

        decorator.Send(Packet(1));
        decorator.AdvanceTo(TimeSpan.FromMilliseconds(49));
        Assert.Empty(inner.Sent);
        Assert.Equal(1, decorator.PendingPacketCount);

        decorator.AdvanceTo(TimeSpan.FromMilliseconds(50));

        Assert.Single(inner.Sent);
        Assert.Equal(0, decorator.PendingPacketCount);
    }

    [Fact]
    public void EqualDueTimesPreserveSendOrder()
    {
        var inner = new FakeTransport();
        var decorator = Create(
            inner,
            new NetworkImpairmentPolicy(
                upstreamBaseDelay: TimeSpan.FromMilliseconds(10)));

        decorator.Send(Packet(1));
        decorator.Send(Packet(2));
        decorator.Send(Packet(3));
        decorator.AdvanceTo(TimeSpan.FromMilliseconds(10));

        Assert.Equal([1, 2, 3], inner.Sent.Select(ReadValue));
    }

    [Fact]
    public void SampledReorderHoldCanDeliverLaterPacketFirst()
    {
        var policy = new NetworkImpairmentPolicy(
            reorderProbability: 0.5d,
            reorderAdditionalDelay: TimeSpan.FromMilliseconds(20));
        var seed = Enumerable.Range(0, 10_000)
            .Select(value => (ulong)value)
            .First(candidate =>
            {
                var schedule = new NetworkImpairmentSchedule(candidate, policy);
                return schedule.Evaluate(NetworkImpairmentDirection.Upstream, 0).Reordered &&
                       !schedule.Evaluate(NetworkImpairmentDirection.Upstream, 1).Reordered;
            });
        var inner = new FakeTransport();
        var decorator = Create(inner, policy, seed);

        decorator.Send(Packet(1));
        decorator.Send(Packet(2));

        Assert.Equal([2], inner.Sent.Select(ReadValue));
        decorator.AdvanceTo(TimeSpan.FromMilliseconds(20));
        Assert.Equal([2, 1], inner.Sent.Select(ReadValue));
    }

    [Fact]
    public void DuplicateUsesIndependentScheduledSpacing()
    {
        var inner = new FakeTransport();
        var decorator = Create(
            inner,
            new NetworkImpairmentPolicy(
                duplicateProbability: 1d,
                duplicateSpacing: TimeSpan.FromMilliseconds(5)));

        decorator.Send(Packet(7));
        Assert.Equal([7], inner.Sent.Select(ReadValue));
        Assert.Equal(1, decorator.PendingPacketCount);

        decorator.AdvanceTo(TimeSpan.FromMilliseconds(5));
        Assert.Equal([7, 7], inner.Sent.Select(ReadValue));
    }

    [Fact]
    public void DelayedPayloadIsSnapshottedAtSendTime()
    {
        var source = new byte[] { 10, 20, 30 };
        var packet = Packet(source);
        var inner = new FakeTransport();
        var decorator = Create(
            inner,
            new NetworkImpairmentPolicy(
                upstreamBaseDelay: TimeSpan.FromMilliseconds(1)));

        decorator.Send(packet);
        source[0] = 99;
        decorator.AdvanceTo(TimeSpan.FromMilliseconds(1));

        Assert.Equal([10, 20, 30], inner.Sent[0].Payload.ToArray());
    }

    [Fact]
    public void ConfiguredDirectionChoosesAsymmetricDelay()
    {
        var inner = new FakeTransport();
        var policy = new NetworkImpairmentPolicy(
            upstreamBaseDelay: TimeSpan.FromMilliseconds(10),
            downstreamBaseDelay: TimeSpan.FromMilliseconds(90));
        var decorator = Create(
            inner,
            policy,
            direction: NetworkImpairmentDirection.Downstream);

        decorator.Send(Packet(1));
        decorator.AdvanceTo(TimeSpan.FromMilliseconds(10));
        Assert.Empty(inner.Sent);
        decorator.AdvanceTo(TimeSpan.FromMilliseconds(90));
        Assert.Single(inner.Sent);
    }

    [Fact]
    public void ReliablePassThroughBypassesLossAndQueue()
    {
        var inner = new FakeTransport();
        var decorator = Create(
            inner,
            new NetworkImpairmentPolicy(independentLossProbability: 1d),
            reliableBehavior: ReliableImpairmentBehavior.PassThrough);
        var packet = Packet(1, TransportDelivery.ReliableOrdered);

        decorator.Send(packet);

        Assert.Equal([packet], inner.Sent);
        Assert.Equal(0, decorator.PendingPacketCount);
    }

    [Fact]
    public void ReliableRejectPolicyFailsWithoutClaimingTransportEmulation()
    {
        var inner = new FakeTransport();
        var decorator = Create(
            inner,
            NetworkImpairmentPolicy.None,
            reliableBehavior: ReliableImpairmentBehavior.Reject);

        var exception = Assert.Throws<InvalidOperationException>(
            () => decorator.Send(Packet(1, TransportDelivery.ReliableOrdered)));

        Assert.Contains("cannot emulate reliable", exception.Message, StringComparison.Ordinal);
        Assert.Empty(inner.Sent);
        Assert.Equal(0, decorator.PendingPacketCount);
    }

    [Fact]
    public void FlushFailureRetainsPacketForExplicitRetry()
    {
        var inner = new FakeTransport { FailNextSend = true };
        var decorator = Create(
            inner,
            new NetworkImpairmentPolicy(
                upstreamBaseDelay: TimeSpan.FromMilliseconds(10)));
        decorator.Send(Packet(1));

        Assert.Throws<InvalidOperationException>(
            () => decorator.AdvanceTo(TimeSpan.FromMilliseconds(10)));
        Assert.Equal(1, decorator.PendingPacketCount);

        decorator.AdvanceTo(TimeSpan.FromMilliseconds(10));
        Assert.Single(inner.Sent);
        Assert.Equal(0, decorator.PendingPacketCount);
    }

    [Fact]
    public void ClockCannotMoveBackward()
    {
        var decorator = Create(new FakeTransport(), NetworkImpairmentPolicy.None);
        decorator.AdvanceTo(TimeSpan.FromMilliseconds(10));

        Assert.Equal(
            "time",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => decorator.AdvanceTo(TimeSpan.FromMilliseconds(9))).ParamName);
    }

    [Fact]
    public void ClosingRecipientPurgesItsQueueAndCannotBlockHealthyRoute()
    {
        var inner = new FakeTransport();
        var decorator = Create(
            inner,
            new NetworkImpairmentPolicy(
                upstreamBaseDelay: TimeSpan.FromMilliseconds(10)));
        decorator.Send(Packet(1, recipient: 2));
        decorator.Send(Packet(2, recipient: 3));

        inner.RaiseClosed(new TransportConnectionId(2));

        Assert.Equal(1, decorator.PendingPacketCount);
        Assert.Throws<InvalidOperationException>(
            () => decorator.Send(Packet(3, recipient: 2)));
        decorator.AdvanceTo(TimeSpan.FromMilliseconds(10));
        Assert.Equal([2], inner.Sent.Select(ReadValue));

        inner.RaiseOpened(new TransportConnectionId(2));
        decorator.Send(Packet(4, recipient: 2));
        decorator.AdvanceTo(TimeSpan.FromMilliseconds(20));
        Assert.Equal([2, 4], inner.Sent.Select(ReadValue));
    }

    [Fact]
    public void EachRecipientOwnsIndependentOrdinalAndSchedule()
    {
        var policy = new NetworkImpairmentPolicy(
            reorderProbability: 0.5d,
            reorderAdditionalDelay: TimeSpan.FromMilliseconds(20));
        var seed = FindFirstReorderedSecondNot(policy);
        var inner = new FakeTransport();
        var decorator = new NetworkImpairmentTransportDecorator(
            inner,
            _ => new NetworkImpairmentSchedule(seed, policy),
            NetworkImpairmentDirection.Upstream,
            ReliableImpairmentBehavior.PassThrough,
            maximumPendingPackets: 4,
            ImpairmentQueueOverflowBehavior.RejectNewest);

        decorator.Send(Packet(1, recipient: 2));
        decorator.Send(Packet(2, recipient: 2));
        decorator.Send(Packet(3, recipient: 3));

        Assert.Equal([2], inner.Sent.Select(ReadValue));
        Assert.Equal(2, decorator.PendingPacketCount);
        decorator.AdvanceTo(TimeSpan.FromMilliseconds(20));
        Assert.Equal([2, 1, 3], inner.Sent.Select(ReadValue));
    }

    [Fact]
    public void CloseAndOpenResetRecipientOrdinalToNewRouteStart()
    {
        var policy = new NetworkImpairmentPolicy(
            reorderProbability: 0.5d,
            reorderAdditionalDelay: TimeSpan.FromMilliseconds(20));
        var seed = FindFirstReorderedSecondNot(policy);
        var inner = new FakeTransport();
        var decorator = Create(inner, policy, seed);

        decorator.Send(Packet(1, recipient: 2));
        Assert.Equal(1, decorator.PendingPacketCount);
        inner.RaiseClosed(new TransportConnectionId(2));
        inner.RaiseOpened(new TransportConnectionId(2));

        decorator.Send(Packet(2, recipient: 2));

        Assert.Empty(inner.Sent);
        Assert.Equal(1, decorator.PendingPacketCount);
    }

    [Fact]
    public void ReentrantEnqueueAndLaterFailureNeverDuplicateDeliveredPacket()
    {
        var inner = new FakeTransport();
        var decorator = Create(inner, NetworkImpairmentPolicy.None);
        inner.AfterSuccessfulSend = packet =>
        {
            if (ReadValue(packet) != 1)
            {
                return;
            }

            inner.FailNextSend = true;
            decorator.Send(Packet(2));
        };

        decorator.Send(Packet(1));
        Assert.Equal([1], inner.Sent.Select(ReadValue));
        Assert.Equal(1, decorator.PendingPacketCount);
        inner.AfterSuccessfulSend = null;
        Assert.Throws<InvalidOperationException>(() => decorator.AdvanceTo(TimeSpan.Zero));
        Assert.Equal([1], inner.Sent.Select(ReadValue));
        Assert.Equal(1, decorator.PendingPacketCount);
        decorator.AdvanceTo(TimeSpan.Zero);
        Assert.Equal([1, 2], inner.Sent.Select(ReadValue));
    }

    [Fact]
    public void ReentrantReliableFailureNeverRequeuesDeliveredUnreliablePacket()
    {
        var inner = new FakeTransport();
        var decorator = Create(
            inner,
            NetworkImpairmentPolicy.None,
            reliableBehavior: ReliableImpairmentBehavior.PassThrough);
        inner.AfterSuccessfulSend = packet =>
        {
            if (ReadValue(packet) != 1)
            {
                return;
            }

            inner.FailNextSend = true;
            decorator.Send(Packet(2, TransportDelivery.ReliableOrdered));
        };

        decorator.Send(Packet(1));

        Assert.Equal([1], inner.Sent.Select(ReadValue));
        Assert.Equal(1, decorator.PendingPacketCount);
        inner.AfterSuccessfulSend = null;
        Assert.Throws<InvalidOperationException>(() => decorator.AdvanceTo(TimeSpan.Zero));
        Assert.Equal([1], inner.Sent.Select(ReadValue));
        Assert.Equal(1, decorator.PendingPacketCount);

        decorator.AdvanceTo(TimeSpan.Zero);
        Assert.Equal([1, 2], inner.Sent.Select(ReadValue));
        Assert.Equal(TransportDelivery.ReliableOrdered, inner.Sent[1].Delivery);
    }

    [Fact]
    public void DuplicateAdmissionIsAtomicAtHardQueueBoundary()
    {
        var policy = new NetworkImpairmentPolicy(
            upstreamBaseDelay: TimeSpan.FromMilliseconds(10),
            duplicateProbability: 1d,
            duplicateSpacing: TimeSpan.FromMilliseconds(1));
        var rejected = Create(
            new FakeTransport(),
            policy,
            maximumPendingPackets: 1);

        Assert.Throws<InvalidOperationException>(() => rejected.Send(Packet(1)));
        Assert.Equal(0, rejected.PendingPacketCount);

        var admitted = Create(
            new FakeTransport(),
            policy,
            maximumPendingPackets: 2);
        admitted.Send(Packet(1));
        Assert.Equal(2, admitted.PendingPacketCount);
    }

    [Fact]
    public void DropNewestOverflowConsumesPacketWithoutGrowingQueue()
    {
        var inner = new FakeTransport();
        var decorator = Create(
            inner,
            new NetworkImpairmentPolicy(
                upstreamBaseDelay: TimeSpan.FromMilliseconds(10)),
            maximumPendingPackets: 1,
            overflowBehavior: ImpairmentQueueOverflowBehavior.DropNewest);

        decorator.Send(Packet(1));
        decorator.Send(Packet(2));

        Assert.Equal(1, decorator.PendingPacketCount);
        decorator.AdvanceTo(TimeSpan.FromMilliseconds(10));
        Assert.Equal([1], inner.Sent.Select(ReadValue));
    }

    [Fact]
    public void InvalidPacketMetadataCannotCreateRouteOrPoisonQueue()
    {
        var factoryCalls = 0;
        var inner = new FakeTransport();
        var decorator = new NetworkImpairmentTransportDecorator(
            inner,
            _ =>
            {
                factoryCalls++;
                return new NetworkImpairmentSchedule(1UL, NetworkImpairmentPolicy.None);
            },
            NetworkImpairmentDirection.Upstream,
            ReliableImpairmentBehavior.PassThrough,
            maximumPendingPackets: 2,
            ImpairmentQueueOverflowBehavior.RejectNewest);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => decorator.Send(new OutboundTransportPacket(
                default,
                TransportChannel.Movement,
                TransportDelivery.Unreliable,
                new byte[] { 1 })));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => decorator.Send(new OutboundTransportPacket(
                new TransportConnectionId(2),
                (TransportChannel)99,
                TransportDelivery.Unreliable,
                new byte[] { 1 })));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => decorator.Send(Packet(1, (TransportDelivery)99)));

        Assert.Equal(0, factoryCalls);
        Assert.Equal(0, decorator.PendingPacketCount);
        decorator.Send(Packet(2));
        Assert.Equal(1, factoryCalls);
        Assert.Equal([2], inner.Sent.Select(ReadValue));
    }

    [Fact]
    public void DisposeUnsubscribesEventsAndClearsOwnedState()
    {
        var inner = new FakeTransport();
        var decorator = Create(
            inner,
            new NetworkImpairmentPolicy(
                upstreamBaseDelay: TimeSpan.FromMilliseconds(10)));
        var receivedEvents = 0;
        var lifecycleEvents = 0;
        decorator.PacketReceived += _ => receivedEvents++;
        decorator.ConnectionOpened += _ => lifecycleEvents++;
        decorator.ConnectionClosed += _ => lifecycleEvents++;
        decorator.Send(Packet(1));

        decorator.Dispose();
        decorator.Dispose();
        inner.Raise(new InboundTransportPacket(
            new TransportConnectionId(2),
            TransportChannel.Movement,
            new byte[] { 1 }));
        inner.RaiseOpened(new TransportConnectionId(2));
        inner.RaiseClosed(new TransportConnectionId(2));

        Assert.Equal(0, receivedEvents);
        Assert.Equal(0, lifecycleEvents);
        Assert.Equal(0, decorator.PendingPacketCount);
        Assert.Throws<ObjectDisposedException>(() => decorator.Send(Packet(2)));
        Assert.Throws<ObjectDisposedException>(() => decorator.AdvanceTo(TimeSpan.Zero));
    }

    [Fact]
    public void KindAndInboundConnectionEventsAreTransparent()
    {
        var inner = new FakeTransport();
        var decorator = Create(inner, NetworkImpairmentPolicy.None);
        InboundTransportPacket? received = null;
        TransportConnectionId? opened = null;
        TransportConnectionId? closed = null;
        decorator.PacketReceived += packet => received = packet;
        decorator.ConnectionOpened += connection => opened = connection;
        decorator.ConnectionClosed += connection => closed = connection;
        var inbound = new InboundTransportPacket(
            new TransportConnectionId(2),
            TransportChannel.Movement,
            new byte[] { 5 });

        inner.Raise(inbound);
        inner.RaiseOpened(new TransportConnectionId(2));
        inner.RaiseClosed(new TransportConnectionId(2));

        Assert.Equal(TransportKind.Test, decorator.Kind);
        Assert.Equal(inbound, received);
        Assert.Equal(new TransportConnectionId(2), opened);
        Assert.Equal(new TransportConnectionId(2), closed);
    }

    [Fact]
    public void InvalidConstructionAndDeliveryValuesAreRejected()
    {
        var schedule = new NetworkImpairmentSchedule(1UL, NetworkImpairmentPolicy.None);
        Func<TransportConnectionId, NetworkImpairmentSchedule> factory = _ => schedule;
        Assert.Throws<ArgumentNullException>(
            () => new NetworkImpairmentTransportDecorator(
                null!,
                factory,
                NetworkImpairmentDirection.Upstream,
                ReliableImpairmentBehavior.PassThrough,
                1,
                ImpairmentQueueOverflowBehavior.RejectNewest));
        Assert.Throws<ArgumentNullException>(
            () => new NetworkImpairmentTransportDecorator(
                new FakeTransport(),
                null!,
                NetworkImpairmentDirection.Upstream,
                ReliableImpairmentBehavior.PassThrough,
                1,
                ImpairmentQueueOverflowBehavior.RejectNewest));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new NetworkImpairmentTransportDecorator(
                new FakeTransport(),
                factory,
                (NetworkImpairmentDirection)99,
                ReliableImpairmentBehavior.PassThrough,
                1,
                ImpairmentQueueOverflowBehavior.RejectNewest));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new NetworkImpairmentTransportDecorator(
                new FakeTransport(),
                factory,
                NetworkImpairmentDirection.Upstream,
                ReliableImpairmentBehavior.PassThrough,
                0,
                ImpairmentQueueOverflowBehavior.RejectNewest));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new NetworkImpairmentTransportDecorator(
                new FakeTransport(),
                factory,
                NetworkImpairmentDirection.Upstream,
                ReliableImpairmentBehavior.PassThrough,
                1,
                (ImpairmentQueueOverflowBehavior)99));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new NetworkImpairmentTransportDecorator(
                new FakeTransport(),
                factory,
                NetworkImpairmentDirection.Upstream,
                (ReliableImpairmentBehavior)99,
                1,
                ImpairmentQueueOverflowBehavior.RejectNewest));

        var decorator = Create(new FakeTransport(), NetworkImpairmentPolicy.None);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => decorator.Send(Packet(1, (TransportDelivery)99)));
    }

    private static NetworkImpairmentTransportDecorator Create(
        FakeTransport inner,
        NetworkImpairmentPolicy policy,
        ulong seed = 1UL,
        NetworkImpairmentDirection direction = NetworkImpairmentDirection.Upstream,
        ReliableImpairmentBehavior reliableBehavior =
            ReliableImpairmentBehavior.PassThrough,
        int maximumPendingPackets = 1_024,
        ImpairmentQueueOverflowBehavior overflowBehavior =
            ImpairmentQueueOverflowBehavior.RejectNewest) =>
        new(
            inner,
            _ => new NetworkImpairmentSchedule(seed, policy),
            direction,
            reliableBehavior,
            maximumPendingPackets,
            overflowBehavior);

    private static OutboundTransportPacket Packet(
        byte value,
        TransportDelivery delivery = TransportDelivery.Unreliable,
        ulong recipient = 2) =>
        Packet([value], delivery, recipient);

    private static OutboundTransportPacket Packet(byte value, ulong recipient) =>
        Packet([value], TransportDelivery.Unreliable, recipient);

    private static OutboundTransportPacket Packet(
        byte[] payload,
        TransportDelivery delivery = TransportDelivery.Unreliable,
        ulong recipient = 2) =>
        new(
            new TransportConnectionId(recipient),
            TransportChannel.Movement,
            delivery,
            payload);

    private static int ReadValue(OutboundTransportPacket packet) =>
        packet.Payload.Span[0];

    private static ulong FindFirstReorderedSecondNot(NetworkImpairmentPolicy policy) =>
        Enumerable.Range(0, 10_000)
            .Select(value => (ulong)value)
            .First(candidate =>
            {
                var schedule = new NetworkImpairmentSchedule(candidate, policy);
                return schedule.Evaluate(NetworkImpairmentDirection.Upstream, 0).Reordered &&
                       !schedule.Evaluate(NetworkImpairmentDirection.Upstream, 1).Reordered;
            });

    private sealed class FakeTransport : INetworkTransport
    {
        public event Action<InboundTransportPacket>? PacketReceived;

        public event Action<TransportConnectionId>? ConnectionOpened;

        public event Action<TransportConnectionId>? ConnectionClosed;

        public TransportKind Kind => TransportKind.Test;

        public List<OutboundTransportPacket> Sent { get; } = [];

        public bool FailNextSend { get; set; }

        public Action<OutboundTransportPacket>? AfterSuccessfulSend { get; set; }

        public void Send(OutboundTransportPacket packet)
        {
            if (FailNextSend)
            {
                FailNextSend = false;
                throw new InvalidOperationException("Injected send failure.");
            }

            Sent.Add(packet);
            AfterSuccessfulSend?.Invoke(packet);
        }

        public void Raise(InboundTransportPacket packet) => PacketReceived?.Invoke(packet);

        public void RaiseOpened(TransportConnectionId connection) =>
            ConnectionOpened?.Invoke(connection);

        public void RaiseClosed(TransportConnectionId connection) =>
            ConnectionClosed?.Invoke(connection);
    }
}
