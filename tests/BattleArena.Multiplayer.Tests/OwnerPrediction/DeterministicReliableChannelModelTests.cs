using BattleArena.Multiplayer.OwnerPrediction;
using BattleArena.Multiplayer.Transport;

namespace BattleArena.Multiplayer.Tests.OwnerPrediction;

public sealed class DeterministicReliableChannelModelTests
{
    [Fact]
    public void LostReliableHeadBlocksLaterReliableButNotUnreliable()
    {
        var impairment = new NetworkImpairmentPolicy(
            upstreamBaseDelay: TimeSpan.FromMilliseconds(10),
            burstIntervalPackets: 4,
            burstLengthPackets: 1);
        var model = Create(
            impairment,
            seed: FindSeed(impairment, droppedOrdinals: [0], clearOrdinals: [1, 2, 3]),
            timeout: TimeSpan.FromMilliseconds(50),
            sendWindow: 4);

        Assert.True(model.TrySendReliable(new byte[] { 1 }, out var first));
        Assert.True(model.TrySendReliable(new byte[] { 2 }, out var second));
        Assert.True(model.TrySendUnreliable(new byte[] { 9 }, out var unreliable));
        model.AdvanceTo(TimeSpan.FromMilliseconds(10));

        var early = model.DrainDeliveries();
        Assert.Single(early);
        Assert.Equal(TransportDelivery.Unreliable, early[0].Delivery);
        Assert.Equal(unreliable, early[0].MessageSequence);
        Assert.Equal(9, early[0].Payload.Span[0]);

        model.AdvanceTo(TimeSpan.FromMilliseconds(59));
        Assert.Empty(model.DrainDeliveries());
        model.AdvanceTo(TimeSpan.FromMilliseconds(60));

        var released = model.DrainDeliveries();
        Assert.Equal([first, second], released.Select(value => value.MessageSequence));
        Assert.All(released, value =>
            Assert.Equal(TransportDelivery.ReliableOrdered, value.Delivery));
        Assert.All(released, value =>
            Assert.Equal(TimeSpan.FromMilliseconds(60), value.DeliveredAt));
        Assert.Equal(
            [1U, 2U],
            model.DrainTransmissions()
                .Where(value =>
                    value.Kind == DeterministicChannelTransmissionKind.ReliableData &&
                    value.MessageSequence == first)
                .Select(value => value.AttemptNumber));
    }

    [Fact]
    public void ReliableMessagesRemainOrderedWhenLaterPacketArrivesFirst()
    {
        var impairment = new NetworkImpairmentPolicy(
            upstreamBaseDelay: TimeSpan.FromMilliseconds(5),
            burstIntervalPackets: 3,
            burstLengthPackets: 1);
        var model = Create(
            impairment,
            seed: FindSeed(impairment, droppedOrdinals: [0], clearOrdinals: [1, 2]),
            timeout: TimeSpan.FromMilliseconds(20));
        model.TrySendReliable(new byte[] { 10 }, out var first);
        model.TrySendReliable(new byte[] { 20 }, out var second);

        model.AdvanceTo(TimeSpan.FromMilliseconds(5));
        Assert.Empty(model.DrainDeliveries());
        model.AdvanceTo(TimeSpan.FromMilliseconds(25));

        Assert.Equal(
            [first, second],
            model.DrainDeliveries().Select(value => value.MessageSequence));
    }

    [Fact]
    public void ReliableArrivalAtTimeoutBoundarySuppressesRetransmission()
    {
        var model = Create(
            new NetworkImpairmentPolicy(
                upstreamBaseDelay: TimeSpan.FromMilliseconds(20)),
            timeout: TimeSpan.FromMilliseconds(20));

        model.TrySendReliable(new byte[] { 1 }, out _);
        model.AdvanceTo(TimeSpan.FromMilliseconds(20));

        Assert.Single(model.DrainDeliveries());
        var transmissions = model.DrainTransmissions();
        Assert.Single(transmissions, value =>
            value.Kind == DeterministicChannelTransmissionKind.ReliableData);
        Assert.Single(transmissions, value =>
            value.Kind == DeterministicChannelTransmissionKind.ReliableAcknowledgement);
        Assert.Empty(model.DrainFailures());
    }

    [Fact]
    public void SendWindowDoesNotTransmitQueuedMessageUntilAnAckFreesCapacity()
    {
        var model = Create(
            new NetworkImpairmentPolicy(
                upstreamBaseDelay: TimeSpan.FromMilliseconds(10)),
            timeout: TimeSpan.FromMilliseconds(50),
            sendWindow: 1);
        model.TrySendReliable(new byte[] { 1 }, out var first);
        model.TrySendReliable(new byte[] { 2 }, out var second);

        Assert.Equal(
            [first],
            model.DrainTransmissions().Select(value => value.MessageSequence));
        model.AdvanceTo(TimeSpan.FromMilliseconds(10));

        var afterAck = model.DrainTransmissions();
        var secondTransmission = Assert.Single(afterAck, value =>
            value.Kind == DeterministicChannelTransmissionKind.ReliableData);
        Assert.Equal(second, secondTransmission.MessageSequence);
        Assert.Equal(TimeSpan.FromMilliseconds(10), secondTransmission.SentAt);
        Assert.Single(afterAck, value =>
            value.Kind == DeterministicChannelTransmissionKind.ReliableAcknowledgement);
        model.AdvanceTo(TimeSpan.FromMilliseconds(20));
        Assert.Equal(
            [first, second],
            model.DrainDeliveries().Select(value => value.MessageSequence));
    }

    [Fact]
    public void ExhaustedReliableStreamDoesNotDisableUnreliableLane()
    {
        var impairment = new NetworkImpairmentPolicy(
            burstIntervalPackets: 3,
            burstLengthPackets: 2);
        var model = Create(
            impairment,
            seed: FindSeed(impairment, droppedOrdinals: [0, 1], clearOrdinals: [2]),
            timeout: TimeSpan.FromMilliseconds(10),
            maximumAttempts: 2);
        model.TrySendReliable(new byte[] { 1 }, out var reliable);

        model.AdvanceTo(TimeSpan.FromMilliseconds(20));

        Assert.True(model.ReliableStreamFailed);
        var failure = Assert.Single(model.DrainFailures());
        Assert.Equal(reliable, failure.MessageSequence);
        Assert.Equal(2U, failure.AttemptCount);
        Assert.Equal(
            DeterministicReliableFailureReason.TransmissionAttemptsExhausted,
            failure.Reason);
        Assert.False(model.TrySendReliable(new byte[] { 2 }, out _));
        Assert.True(model.TrySendUnreliable(new byte[] { 9 }, out var unreliable));
        var delivered = Assert.Single(model.DrainDeliveries());
        Assert.Equal(unreliable, delivered.MessageSequence);
        Assert.Equal(TransportDelivery.Unreliable, delivered.Delivery);
    }

    [Fact]
    public void ReliableDuplicateIsDeduplicatedWhileUnreliableDuplicateIsVisible()
    {
        var model = Create(
            new NetworkImpairmentPolicy(
                duplicateProbability: 1d,
                duplicateSpacing: TimeSpan.FromMilliseconds(5)),
            timeout: TimeSpan.FromMilliseconds(20));
        model.TrySendReliable(new byte[] { 1 }, out var reliable);
        model.TrySendUnreliable(new byte[] { 2 }, out var unreliable);

        var immediate = model.DrainDeliveries();
        Assert.Equal(2, immediate.Count);
        model.AdvanceTo(TimeSpan.FromMilliseconds(5));
        var duplicates = model.DrainDeliveries();

        Assert.Single(immediate, value =>
            value.Delivery == TransportDelivery.ReliableOrdered &&
            value.MessageSequence == reliable);
        Assert.Single(immediate, value =>
            value.Delivery == TransportDelivery.Unreliable &&
            value.MessageSequence == unreliable);
        var duplicate = Assert.Single(duplicates);
        Assert.Equal(TransportDelivery.Unreliable, duplicate.Delivery);
        Assert.Equal(unreliable, duplicate.MessageSequence);
    }

    [Fact]
    public void ReliablePayloadIsSnapshottedAndPendingQueueIsBounded()
    {
        var model = Create(
            new NetworkImpairmentPolicy(
                upstreamBaseDelay: TimeSpan.FromMilliseconds(10)),
            timeout: TimeSpan.FromMilliseconds(50),
            sendWindow: 1,
            maximumPendingReliable: 2);
        var payload = new byte[] { 4 };

        Assert.True(model.TrySendReliable(payload, out _));
        Assert.True(model.TrySendReliable(new byte[] { 5 }, out _));
        Assert.False(model.TrySendReliable(new byte[] { 6 }, out var rejectedSequence));
        payload[0] = 99;
        model.AdvanceTo(TimeSpan.FromMilliseconds(20));

        Assert.Equal(0UL, rejectedSequence);
        Assert.Equal(
            [4, 5],
            model.DrainDeliveries().Select(value => (int)value.Payload.Span[0]));
    }

    [Fact]
    public void SelectiveAckBufferAndObsoleteRetryEventsRemainBounded()
    {
        var impairment = new NetworkImpairmentPolicy(
            upstreamBaseDelay: TimeSpan.FromMilliseconds(10),
            burstIntervalPackets: 10,
            burstLengthPackets: 1);
        var model = Create(
            impairment,
            seed: FindSeed(
                impairment,
                droppedOrdinals: [0],
                clearOrdinals: [1, 2, 3, 4]),
            timeout: TimeSpan.FromMilliseconds(100),
            sendWindow: 2,
            maximumPendingReliable: 2);
        model.TrySendReliable(new byte[] { 1 }, out _);
        model.TrySendReliable(new byte[] { 2 }, out _);
        model.AdvanceTo(TimeSpan.FromMilliseconds(10));
        model.TrySendReliable(new byte[] { 3 }, out _);
        model.AdvanceTo(TimeSpan.FromMilliseconds(20));
        model.TrySendReliable(new byte[] { 4 }, out _);
        model.AdvanceTo(TimeSpan.FromMilliseconds(30));

        Assert.Equal(2, model.BufferedOutOfOrderReliableMessageCount);
        Assert.Equal(2, model.PendingReliableMessageCount);
        Assert.InRange(model.ScheduledEventCount, 1, 4);
        Assert.Empty(model.DrainDeliveries());

        model.AdvanceTo(TimeSpan.FromMilliseconds(110));
        Assert.Equal(
            [1, 2, 3],
            model.DrainDeliveries().Select(value => (int)value.Payload.Span[0]));
        Assert.Equal(0, model.BufferedOutOfOrderReliableMessageCount);
        model.AdvanceTo(TimeSpan.FromMilliseconds(130));
        Assert.Equal(4, Assert.Single(model.DrainDeliveries()).Payload.Span[0]);
    }

    [Fact]
    public void AcknowledgementCancelsOnlyTimeoutAndInflightDuplicateIsDeduplicated()
    {
        var model = Create(
            new NetworkImpairmentPolicy(
                duplicateProbability: 1d,
                duplicateSpacing: TimeSpan.FromMilliseconds(50)),
            timeout: TimeSpan.FromMilliseconds(100));

        model.TrySendReliable(new byte[] { 1 }, out _);

        Assert.Single(model.DrainDeliveries());
        Assert.Equal(2, model.ScheduledEventCount);
        model.AdvanceTo(TimeSpan.FromSeconds(1));
        Assert.Empty(model.DrainDeliveries());
        var transmissions = model.DrainTransmissions();
        Assert.Single(transmissions, value =>
            value.Kind == DeterministicChannelTransmissionKind.ReliableData);
        Assert.Equal(
            2,
            transmissions.Count(value =>
                value.Kind ==
                DeterministicChannelTransmissionKind.ReliableAcknowledgement));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnreliableDatagramBoundUsesAtomicDuplicateAdmission(bool dropped)
    {
        var impairment = dropped
            ? new NetworkImpairmentPolicy(independentLossProbability: 1d)
            : new NetworkImpairmentPolicy(
                duplicateProbability: 1d,
                duplicateSpacing: TimeSpan.FromMilliseconds(5));
        var model = Create(
            impairment,
            maximumPendingUnreliable: 1);

        var accepted = model.TrySendUnreliable(new byte[] { 1 }, out var sequence);

        Assert.Equal(dropped, accepted);
        Assert.Equal(dropped ? 1UL : 0UL, sequence);
        Assert.Equal(0, model.PendingUnreliableDatagramCount);
        Assert.Empty(model.DrainDeliveries());
    }

    [Fact]
    public void OutputsDrainWithoutChangingChannelState()
    {
        var model = Create(NetworkImpairmentPolicy.None);
        model.TrySendReliable(new byte[] { 1 }, out _);
        model.TrySendUnreliable(new byte[] { 2 }, out _);

        Assert.Equal(3, model.DrainTransmissions().Count);
        Assert.Empty(model.DrainTransmissions());
        Assert.Equal(2, model.DrainDeliveries().Count);
        Assert.Empty(model.DrainDeliveries());
        Assert.Equal(TimeSpan.Zero, model.CurrentTime);
        Assert.Equal(0, model.PendingReliableMessageCount);
    }

    [Fact]
    public void SendWindowWaitsForReversePathAcknowledgementDelay()
    {
        var model = Create(
            new NetworkImpairmentPolicy(
                upstreamBaseDelay: TimeSpan.FromMilliseconds(10),
                downstreamBaseDelay: TimeSpan.FromMilliseconds(15)),
            timeout: TimeSpan.FromMilliseconds(100),
            sendWindow: 1);
        model.TrySendReliable(new byte[] { 1 }, out _);
        model.TrySendReliable(new byte[] { 2 }, out var second);
        model.DrainTransmissions();

        model.AdvanceTo(TimeSpan.FromMilliseconds(24));
        Assert.DoesNotContain(
            model.DrainTransmissions(),
            value =>
                value.Kind == DeterministicChannelTransmissionKind.ReliableData &&
                value.MessageSequence == second);
        model.AdvanceTo(TimeSpan.FromMilliseconds(25));

        var secondData = Assert.Single(
            model.DrainTransmissions(),
            value =>
                value.Kind == DeterministicChannelTransmissionKind.ReliableData &&
                value.MessageSequence == second);
        Assert.Equal(TimeSpan.FromMilliseconds(25), secondData.SentAt);
    }

    [Fact]
    public void LostAcknowledgementForcesRetransmissionWithoutDuplicateDelivery()
    {
        var impairment = new NetworkImpairmentPolicy(
            burstIntervalPackets: 4,
            burstLengthPackets: 1);
        var seed = FindDirectionalSeed(
            impairment,
            dropped:
            [
                (NetworkImpairmentDirection.Downstream, 0UL),
            ],
            clear:
            [
                (NetworkImpairmentDirection.Upstream, 0UL),
                (NetworkImpairmentDirection.Upstream, 1UL),
                (NetworkImpairmentDirection.Downstream, 1UL),
            ]);
        var model = Create(
            impairment,
            seed,
            timeout: TimeSpan.FromMilliseconds(10));
        model.TrySendReliable(new byte[] { 1 }, out _);
        Assert.Single(model.DrainDeliveries());

        model.AdvanceTo(TimeSpan.FromMilliseconds(10));

        Assert.Empty(model.DrainDeliveries());
        var transmissions = model.DrainTransmissions();
        Assert.Equal(
            [1U, 2U],
            transmissions
                .Where(value =>
                    value.Kind == DeterministicChannelTransmissionKind.ReliableData)
                .Select(value => value.AttemptNumber));
        Assert.Equal(
            [true, false],
            transmissions
                .Where(value =>
                    value.Kind ==
                    DeterministicChannelTransmissionKind.ReliableAcknowledgement)
                .Select(value => value.Impairment.IsDropped));
        Assert.False(model.ReliableStreamFailed);
    }

    [Fact]
    public void ObservationBuffersAreBoundedAndReportDroppedOldestEntries()
    {
        var model = Create(
            NetworkImpairmentPolicy.None,
            maximumObservations: 2);
        model.TrySendUnreliable(new byte[] { 1 }, out _);
        model.TrySendUnreliable(new byte[] { 2 }, out _);
        model.TrySendUnreliable(new byte[] { 3 }, out _);

        Assert.Equal(1UL, model.DroppedTransmissionObservationCount);
        Assert.Equal(1UL, model.DroppedDeliveryObservationCount);
        Assert.Equal(
            [2UL, 3UL],
            model.DrainTransmissions().Select(value => value.MessageSequence));
        Assert.Equal(
            [2, 3],
            model.DrainDeliveries().Select(value => (int)value.Payload.Span[0]));
    }

    [Fact]
    public void ClockOverflowRejectsSendsWithoutPartialState()
    {
        var delay = TimeSpan.FromTicks(1);
        var reliable = Create(
            new NetworkImpairmentPolicy(upstreamBaseDelay: delay),
            timeout: delay);
        reliable.AdvanceTo(TimeSpan.MaxValue);
        ulong reliableSequence = 99;

        Assert.Throws<OverflowException>(() =>
            reliable.TrySendReliable(new byte[] { 1 }, out reliableSequence));
        Assert.Equal(0UL, reliableSequence);
        Assert.Equal(0, reliable.PendingReliableMessageCount);
        Assert.Empty(reliable.DrainTransmissions());

        var unreliable = Create(
            new NetworkImpairmentPolicy(upstreamBaseDelay: delay));
        unreliable.AdvanceTo(TimeSpan.MaxValue);
        ulong unreliableSequence = 99;
        Assert.Throws<OverflowException>(() =>
            unreliable.TrySendUnreliable(new byte[] { 1 }, out unreliableSequence));
        Assert.Equal(0UL, unreliableSequence);
        Assert.Equal(0, unreliable.PendingUnreliableDatagramCount);
        Assert.Empty(unreliable.DrainTransmissions());
    }

    [Fact]
    public void RetainedWireDuplicatesCannotExceedScheduledEventCapacity()
    {
        var model = Create(
            new NetworkImpairmentPolicy(
                duplicateProbability: 1d,
                duplicateSpacing: TimeSpan.FromSeconds(1)),
            timeout: TimeSpan.FromSeconds(2),
            maximumScheduledEvents: 4);

        Assert.True(model.TrySendReliable(new byte[] { 1 }, out _));
        Assert.Equal(2, model.ScheduledEventCount);
        for (var index = 0; index < 100; index++)
        {
            Assert.False(model.TrySendReliable(new byte[] { 2 }, out var sequence));
            Assert.Equal(0UL, sequence);
            Assert.Equal(2, model.ScheduledEventCount);
        }

        Assert.InRange(model.ScheduledEventCount, 0, 4);
        Assert.Equal(0, model.PendingReliableMessageCount);
    }

    [Fact]
    public void InternalAcknowledgementCapacityPressureIsCountedAndRecovers()
    {
        var model = Create(
            new NetworkImpairmentPolicy(
                duplicateProbability: 1d,
                duplicateSpacing: TimeSpan.FromMilliseconds(5)),
            timeout: TimeSpan.FromMilliseconds(20),
            maximumScheduledEvents: 3);

        Assert.True(model.TrySendReliable(new byte[] { 1 }, out _));
        Assert.Single(model.DrainDeliveries());
        Assert.Equal(1UL, model.SuppressedAcknowledgementCount);
        Assert.Equal(1, model.PendingReliableMessageCount);

        model.AdvanceTo(TimeSpan.FromMilliseconds(5));

        Assert.Empty(model.DrainDeliveries());
        Assert.Equal(0, model.PendingReliableMessageCount);
        Assert.False(model.ReliableStreamFailed);
        Assert.InRange(model.ScheduledEventCount, 0, 3);
    }

    [Fact]
    public void QueuedSendReleasedAtClockEndProducesExplicitTerminalFailure()
    {
        var tick = TimeSpan.FromTicks(1);
        var model = Create(
            new NetworkImpairmentPolicy(downstreamBaseDelay: tick),
            timeout: tick,
            sendWindow: 1);
        model.AdvanceTo(TimeSpan.MaxValue - tick);
        Assert.True(model.TrySendReliable(new byte[] { 1 }, out _));
        Assert.True(model.TrySendReliable(new byte[] { 2 }, out var queued));

        var exception = Record.Exception(() => model.AdvanceTo(TimeSpan.MaxValue));

        Assert.Null(exception);
        Assert.True(model.ReliableStreamFailed);
        var failure = Assert.Single(model.DrainFailures());
        Assert.Equal(queued, failure.MessageSequence);
        Assert.Equal(0U, failure.AttemptCount);
        Assert.Equal(
            DeterministicReliableFailureReason.ClockDomainExhausted,
            failure.Reason);
        Assert.Equal(0, model.PendingReliableMessageCount);
        Assert.Equal(0, model.ScheduledEventCount);
    }

    [Fact]
    public void ReverseAckBeyondClockEndProducesExplicitTerminalFailure()
    {
        var tick = TimeSpan.FromTicks(1);
        var model = Create(
            new NetworkImpairmentPolicy(
                downstreamBaseDelay: TimeSpan.FromTicks(2)),
            timeout: tick);
        model.AdvanceTo(TimeSpan.MaxValue - tick);

        var exception = Record.Exception(
            () => model.TrySendReliable(new byte[] { 1 }, out _));

        Assert.Null(exception);
        Assert.True(model.ReliableStreamFailed);
        Assert.Empty(model.DrainDeliveries());
        var failure = Assert.Single(model.DrainFailures());
        Assert.Equal(
            DeterministicReliableFailureReason.ClockDomainExhausted,
            failure.Reason);
        Assert.Equal(0, model.PendingReliableMessageCount);
        Assert.Equal(0, model.ScheduledEventCount);
    }

    [Fact]
    public void AdvanceIsMonotonic()
    {
        var model = Create(NetworkImpairmentPolicy.None);
        model.AdvanceTo(TimeSpan.FromSeconds(1));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => model.AdvanceTo(TimeSpan.FromMilliseconds(999)));
    }

    [Fact]
    public void InvalidConstructionIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DeterministicReliableChannelPolicy(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DeterministicReliableChannelPolicy(
                TimeSpan.FromMilliseconds(1),
                maximumTransmissionAttempts: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DeterministicReliableChannelPolicy(
                TimeSpan.FromMilliseconds(1),
                reliableSendWindow: 2,
                maximumPendingReliableMessages: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DeterministicReliableChannelPolicy(
                TimeSpan.FromMilliseconds(1),
                maximumBufferedObservations: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DeterministicReliableChannelPolicy(
                TimeSpan.FromMilliseconds(1),
                maximumScheduledEvents: 0));
        var schedule = new NetworkImpairmentSchedule(1, NetworkImpairmentPolicy.None);
        var policy = new DeterministicReliableChannelPolicy(TimeSpan.FromMilliseconds(1));
        Assert.Throws<ArgumentNullException>(() =>
            new DeterministicReliableChannelModel(
                null!,
                NetworkImpairmentDirection.Upstream,
                policy));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DeterministicReliableChannelModel(
                schedule,
                (NetworkImpairmentDirection)99,
                policy));
        Assert.Throws<ArgumentNullException>(() =>
            new DeterministicReliableChannelModel(
                schedule,
                NetworkImpairmentDirection.Upstream,
                null!));
    }

    private static DeterministicReliableChannelModel Create(
        NetworkImpairmentPolicy impairment,
        ulong seed = 1,
        TimeSpan? timeout = null,
        uint maximumAttempts = 8,
        int sendWindow = 32,
        int maximumPendingReliable = 1_024,
        int maximumPendingUnreliable = 4_096,
        int maximumObservations = 4_096,
        int maximumScheduledEvents = 16_384) =>
        new(
            new NetworkImpairmentSchedule(seed, impairment),
            NetworkImpairmentDirection.Upstream,
            new DeterministicReliableChannelPolicy(
                timeout ?? TimeSpan.FromMilliseconds(100),
                maximumAttempts,
                sendWindow,
                maximumPendingReliable,
                maximumPendingUnreliable,
                maximumObservations,
                maximumScheduledEvents));

    private static ulong FindSeed(
        NetworkImpairmentPolicy policy,
        IReadOnlyCollection<ulong> droppedOrdinals,
        IReadOnlyCollection<ulong> clearOrdinals) =>
        Enumerable.Range(0, 100_000)
            .Select(value => (ulong)value)
            .First(seed =>
            {
                var schedule = new NetworkImpairmentSchedule(seed, policy);
                return droppedOrdinals.All(ordinal =>
                           schedule.Evaluate(
                               NetworkImpairmentDirection.Upstream,
                               ordinal).IsDropped) &&
                       clearOrdinals.All(ordinal =>
                           !schedule.Evaluate(
                               NetworkImpairmentDirection.Upstream,
                               ordinal).IsDropped);
            });

    private static ulong FindDirectionalSeed(
        NetworkImpairmentPolicy policy,
        IReadOnlyCollection<(NetworkImpairmentDirection Direction, ulong Ordinal)> dropped,
        IReadOnlyCollection<(NetworkImpairmentDirection Direction, ulong Ordinal)> clear) =>
        Enumerable.Range(0, 100_000)
            .Select(value => (ulong)value)
            .First(seed =>
            {
                var schedule = new NetworkImpairmentSchedule(seed, policy);
                return dropped.All(value =>
                           schedule.Evaluate(value.Direction, value.Ordinal).IsDropped) &&
                       clear.All(value =>
                           !schedule.Evaluate(value.Direction, value.Ordinal).IsDropped);
            });
}
