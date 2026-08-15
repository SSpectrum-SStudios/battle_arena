using BattleArena.Multiplayer.Transport;

namespace BattleArena.Multiplayer.OwnerPrediction;

/// <summary>
/// Bounded configuration for a deterministic reliable-ordered lane sharing a
/// simulated datagram path with an independent unreliable lane.
/// </summary>
public sealed class DeterministicReliableChannelPolicy
{
    public DeterministicReliableChannelPolicy(
        TimeSpan retransmissionTimeout,
        uint maximumTransmissionAttempts = 8,
        int reliableSendWindow = 32,
        int maximumPendingReliableMessages = 1_024,
        int maximumPendingUnreliableDatagrams = 4_096,
        int maximumBufferedObservations = 4_096,
        int maximumScheduledEvents = 16_384)
    {
        if (retransmissionTimeout <= TimeSpan.Zero ||
            retransmissionTimeout > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(nameof(retransmissionTimeout));
        }

        if (maximumTransmissionAttempts == 0 || maximumTransmissionAttempts > 1_024)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumTransmissionAttempts));
        }

        if (reliableSendWindow <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(reliableSendWindow));
        }

        if (maximumPendingReliableMessages <= 0 ||
            reliableSendWindow > maximumPendingReliableMessages)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPendingReliableMessages));
        }

        if (maximumPendingUnreliableDatagrams <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumPendingUnreliableDatagrams));
        }

        if (maximumBufferedObservations <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBufferedObservations));
        }

        if (maximumScheduledEvents <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumScheduledEvents));
        }

        RetransmissionTimeout = retransmissionTimeout;
        MaximumTransmissionAttempts = maximumTransmissionAttempts;
        ReliableSendWindow = reliableSendWindow;
        MaximumPendingReliableMessages = maximumPendingReliableMessages;
        MaximumPendingUnreliableDatagrams = maximumPendingUnreliableDatagrams;
        MaximumBufferedObservations = maximumBufferedObservations;
        MaximumScheduledEvents = maximumScheduledEvents;
    }

    public TimeSpan RetransmissionTimeout { get; }

    public uint MaximumTransmissionAttempts { get; }

    public int ReliableSendWindow { get; }

    public int MaximumPendingReliableMessages { get; }

    public int MaximumPendingUnreliableDatagrams { get; }

    public int MaximumBufferedObservations { get; }

    public int MaximumScheduledEvents { get; }
}

public enum DeterministicChannelTransmissionKind
{
    ReliableData = 0,
    ReliableAcknowledgement = 1,
    UnreliableData = 2,
}

public readonly record struct DeterministicChannelTransmission(
    DeterministicChannelTransmissionKind Kind,
    NetworkImpairmentDirection Direction,
    ulong WireOrdinal,
    ulong MessageSequence,
    uint AttemptNumber,
    TimeSpan SentAt,
    NetworkImpairmentDecision Impairment);

public readonly record struct DeterministicChannelDelivery(
    TransportDelivery Delivery,
    ulong MessageSequence,
    TimeSpan DeliveredAt,
    ReadOnlyMemory<byte> Payload);

public enum DeterministicReliableFailureReason
{
    TransmissionAttemptsExhausted = 0,
    SimulationCapacityExhausted = 1,
    ClockDomainExhausted = 2,
}

public readonly record struct DeterministicReliableFailure(
    ulong MessageSequence,
    uint AttemptCount,
    TimeSpan FailedAt,
    DeterministicReliableFailureReason Reason);

/// <summary>
/// Deterministic lower-level transport simulator. Reliable data and reverse
/// acknowledgements traverse independently impaired directions. Reliable data
/// is retransmitted until acknowledged or exhausted and is released to the
/// application strictly in sequence. Unreliable data never waits behind the
/// reliable receive head.
/// </summary>
public sealed class DeterministicReliableChannelModel
{
    private readonly NetworkImpairmentSchedule _schedule;
    private readonly NetworkImpairmentDirection _forwardDirection;
    private readonly NetworkImpairmentDirection _reverseDirection;
    private readonly DeterministicReliableChannelPolicy _policy;
    private readonly PriorityQueue<
        ScheduledEvent,
        (long DueTicks, int Priority, ulong Order)> _events = new();
    private readonly Dictionary<ulong, ReliableMessage> _outstandingReliable = [];
    private readonly Queue<ulong> _waitingReliable = [];
    private readonly SortedDictionary<ulong, byte[]> _receivedReliable = [];
    private readonly Queue<DeterministicChannelTransmission> _transmissions = [];
    private readonly Queue<DeterministicChannelDelivery> _deliveries = [];
    private readonly Queue<DeterministicReliableFailure> _failures = [];

    private TimeSpan _currentTime;
    private ulong _nextReliableSequence = 1;
    private ulong _nextUnreliableSequence = 1;
    private ulong _nextExpectedReliableSequence = 1;
    private ulong _nextForwardWireOrdinal;
    private ulong _nextReverseWireOrdinal;
    private ulong _nextEventOrder;
    private int _pendingUnreliableDatagrams;
    private ulong _droppedTransmissionObservations;
    private ulong _droppedDeliveryObservations;
    private ulong _droppedFailureObservations;
    private ulong _suppressedAcknowledgements;
    private bool _reliableStreamFailed;

    public DeterministicReliableChannelModel(
        NetworkImpairmentSchedule schedule,
        NetworkImpairmentDirection forwardDirection,
        DeterministicReliableChannelPolicy policy)
    {
        _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
        if (!Enum.IsDefined(forwardDirection))
        {
            throw new ArgumentOutOfRangeException(nameof(forwardDirection));
        }

        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _forwardDirection = forwardDirection;
        _reverseDirection = forwardDirection == NetworkImpairmentDirection.Upstream
            ? NetworkImpairmentDirection.Downstream
            : NetworkImpairmentDirection.Upstream;
    }

    public TimeSpan CurrentTime => _currentTime;

    public bool ReliableStreamFailed => _reliableStreamFailed;

    public int PendingReliableMessageCount => _outstandingReliable.Count;

    public int PendingUnreliableDatagramCount => _pendingUnreliableDatagrams;

    public int BufferedOutOfOrderReliableMessageCount => _receivedReliable.Count;

    public int ScheduledEventCount => _events.Count;

    public ulong DroppedTransmissionObservationCount =>
        _droppedTransmissionObservations;

    public ulong DroppedDeliveryObservationCount => _droppedDeliveryObservations;

    public ulong DroppedFailureObservationCount => _droppedFailureObservations;

    public ulong SuppressedAcknowledgementCount => _suppressedAcknowledgements;

    public bool TrySendReliable(ReadOnlyMemory<byte> payload, out ulong sequence)
    {
        sequence = 0;
        if (_reliableStreamFailed ||
            _outstandingReliable.Count >= _policy.MaximumPendingReliableMessages)
        {
            return false;
        }

        EnsureSequenceAvailable(
            _nextReliableSequence,
            "The reliable message sequence was exhausted.");
        var canStart = ActiveReliableCount() < _policy.ReliableSendWindow;
        ReliableTransmissionPlan? plan;
        try
        {
            plan = canStart
                ? PrepareReliableTransmission(attemptNumber: 1)
                : null;
        }
        catch (ScheduledEventCapacityException)
        {
            return false;
        }
        var nextSequence = _nextReliableSequence;
        var nextCounter = AdvanceSequenceCounter(nextSequence);
        var message = new ReliableMessage(nextSequence, payload.ToArray());

        sequence = nextSequence;
        _nextReliableSequence = nextCounter;
        _outstandingReliable.Add(nextSequence, message);
        if (plan is { } prepared)
        {
            message.Started = true;
            CommitReliableTransmission(message, prepared);
        }
        else
        {
            _waitingReliable.Enqueue(nextSequence);
        }

        ProcessThrough(_currentTime);
        return true;
    }

    public bool TrySendUnreliable(ReadOnlyMemory<byte> payload, out ulong sequence)
    {
        sequence = 0;
        EnsureSequenceAvailable(
            _nextUnreliableSequence,
            "The unreliable message sequence was exhausted.");
        UnreliableTransmissionPlan plan;
        try
        {
            plan = PrepareUnreliableTransmission();
        }
        catch (ScheduledEventCapacityException)
        {
            return false;
        }
        if (_pendingUnreliableDatagrams >
            _policy.MaximumPendingUnreliableDatagrams - plan.ArrivalCount)
        {
            return false;
        }

        var nextSequence = _nextUnreliableSequence;
        var nextCounter = AdvanceSequenceCounter(nextSequence);
        var snapshot = payload.ToArray();

        sequence = nextSequence;
        _nextUnreliableSequence = nextCounter;
        _nextForwardWireOrdinal = plan.NextWireOrdinal;
        ObserveTransmission(new DeterministicChannelTransmission(
            DeterministicChannelTransmissionKind.UnreliableData,
            _forwardDirection,
            plan.WireOrdinal,
            sequence,
            1,
            _currentTime,
            plan.Impairment));
        foreach (var arrival in plan.ArrivalDueTicks)
        {
            Schedule(new ScheduledEvent(
                ScheduledEventKind.UnreliableDataArrival,
                sequence,
                1,
                snapshot,
                arrival,
                NextEventOrder()));
        }

        _pendingUnreliableDatagrams += plan.ArrivalCount;
        ProcessThrough(_currentTime);
        return true;
    }

    public void AdvanceTo(TimeSpan time)
    {
        if (time < _currentTime)
        {
            throw new ArgumentOutOfRangeException(
                nameof(time),
                time,
                "The reliable-channel clock cannot move backward.");
        }

        ProcessThrough(time);
    }

    public IReadOnlyList<DeterministicChannelTransmission> DrainTransmissions() =>
        Drain(_transmissions);

    public IReadOnlyList<DeterministicChannelDelivery> DrainDeliveries() =>
        Drain(_deliveries);

    public IReadOnlyList<DeterministicReliableFailure> DrainFailures() =>
        Drain(_failures);

    private void PumpReliableWindow()
    {
        if (_reliableStreamFailed)
        {
            return;
        }

        var activeCount = ActiveReliableCount();
        while (activeCount < _policy.ReliableSendWindow &&
               _waitingReliable.TryPeek(out var sequence))
        {
            if (!_outstandingReliable.TryGetValue(sequence, out var message))
            {
                _waitingReliable.Dequeue();
                continue;
            }

            var plan = PrepareReliableTransmission(attemptNumber: 1);
            _waitingReliable.Dequeue();
            message.Started = true;
            CommitReliableTransmission(message, plan);
            activeCount++;
        }
    }

    private ReliableTransmissionPlan PrepareReliableTransmission(uint attemptNumber)
    {
        var ordinal = _nextForwardWireOrdinal;
        var impairment = _schedule.Evaluate(_forwardDirection, ordinal);
        var timeoutDueTicks = AddTicks(
            _currentTime,
            _policy.RetransmissionTimeout);
        var arrivals = PrepareArrivals(impairment);
        EnsureScheduledEventCapacity(checked(arrivals.Length + 1));
        EnsureEventOrderCapacity(checked(arrivals.Length + 1));
        return new ReliableTransmissionPlan(
            ordinal,
            NextOrdinal(ordinal),
            attemptNumber,
            impairment,
            timeoutDueTicks,
            arrivals);
    }

    private void CommitReliableTransmission(
        ReliableMessage message,
        ReliableTransmissionPlan plan)
    {
        _nextForwardWireOrdinal = plan.NextWireOrdinal;
        message.AttemptCount = plan.AttemptNumber;
        ObserveTransmission(new DeterministicChannelTransmission(
            DeterministicChannelTransmissionKind.ReliableData,
            _forwardDirection,
            plan.WireOrdinal,
            message.Sequence,
            plan.AttemptNumber,
            _currentTime,
            plan.Impairment));
        foreach (var arrival in plan.ArrivalDueTicks)
        {
            Schedule(new ScheduledEvent(
                ScheduledEventKind.ReliableDataArrival,
                message.Sequence,
                plan.AttemptNumber,
                message.Payload,
                arrival,
                NextEventOrder()));
        }

        Schedule(new ScheduledEvent(
            ScheduledEventKind.RetransmissionTimeout,
            message.Sequence,
            plan.AttemptNumber,
            null,
            plan.TimeoutDueTicks,
            NextEventOrder()));
    }

    private UnreliableTransmissionPlan PrepareUnreliableTransmission()
    {
        var ordinal = _nextForwardWireOrdinal;
        var impairment = _schedule.Evaluate(_forwardDirection, ordinal);
        var arrivals = PrepareArrivals(impairment);
        EnsureScheduledEventCapacity(arrivals.Length);
        EnsureEventOrderCapacity(arrivals.Length);
        return new UnreliableTransmissionPlan(
            ordinal,
            NextOrdinal(ordinal),
            impairment,
            arrivals);
    }

    private AcknowledgementPlan PrepareAcknowledgement(
        ulong messageSequence,
        uint attemptNumber)
    {
        var ordinal = _nextReverseWireOrdinal;
        var impairment = _schedule.Evaluate(_reverseDirection, ordinal);
        var arrivals = PrepareArrivals(impairment);
        EnsureScheduledEventCapacity(arrivals.Length);
        EnsureEventOrderCapacity(arrivals.Length);
        return new AcknowledgementPlan(
            ordinal,
            NextOrdinal(ordinal),
            messageSequence,
            attemptNumber,
            impairment,
            arrivals);
    }

    private void CommitAcknowledgement(AcknowledgementPlan plan)
    {
        _nextReverseWireOrdinal = plan.NextWireOrdinal;
        ObserveTransmission(new DeterministicChannelTransmission(
            DeterministicChannelTransmissionKind.ReliableAcknowledgement,
            _reverseDirection,
            plan.WireOrdinal,
            plan.MessageSequence,
            plan.AttemptNumber,
            _currentTime,
            plan.Impairment));
        foreach (var arrival in plan.ArrivalDueTicks)
        {
            Schedule(new ScheduledEvent(
                ScheduledEventKind.ReliableAcknowledgementArrival,
                plan.MessageSequence,
                plan.AttemptNumber,
                null,
                arrival,
                NextEventOrder()));
        }
    }

    private long[] PrepareArrivals(NetworkImpairmentDecision impairment)
    {
        if (impairment.IsDropped)
        {
            return [];
        }

        if (impairment.DuplicateDelay is not { } duplicateDelay)
        {
            return [AddTicks(_currentTime, impairment.PrimaryDelay)];
        }

        return
        [
            AddTicks(_currentTime, impairment.PrimaryDelay),
            AddTicks(_currentTime, duplicateDelay),
        ];
    }

    private void ProcessThrough(TimeSpan target)
    {
        while (_events.TryPeek(out _, out var priority) &&
               priority.DueTicks <= target.Ticks)
        {
            var scheduled = _events.Dequeue();
            _currentTime = TimeSpan.FromTicks(scheduled.DueTicks);
            switch (scheduled.Kind)
            {
                case ScheduledEventKind.ReliableDataArrival:
                    ProcessReliableDataArrival(scheduled);
                    break;
                case ScheduledEventKind.ReliableAcknowledgementArrival:
                    ProcessAcknowledgementArrival(scheduled);
                    break;
                case ScheduledEventKind.UnreliableDataArrival:
                    ProcessUnreliableArrival(scheduled);
                    break;
                case ScheduledEventKind.RetransmissionTimeout:
                    ProcessRetransmissionTimeout(scheduled);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(scheduled));
            }
        }

        _currentTime = target;
    }

    private void ProcessReliableDataArrival(ScheduledEvent scheduled)
    {
        if (_reliableStreamFailed)
        {
            return;
        }

        var payload = scheduled.Payload ??
            throw new InvalidOperationException("Reliable data requires a payload.");
        var alreadyDelivered =
            scheduled.MessageSequence < _nextExpectedReliableSequence;
        var isExpectedHead =
            scheduled.MessageSequence == _nextExpectedReliableSequence;
        var alreadyBuffered = _receivedReliable.ContainsKey(scheduled.MessageSequence);
        var canAccept = alreadyDelivered || isExpectedHead || alreadyBuffered ||
            _receivedReliable.Count < _policy.MaximumPendingReliableMessages;
        if (!canAccept)
        {
            return;
        }

        AcknowledgementPlan? acknowledgement;
        try
        {
            acknowledgement = PrepareAcknowledgement(
                scheduled.MessageSequence,
                scheduled.AttemptNumber);
        }
        catch (ScheduledEventCapacityException)
        {
            acknowledgement = null;
            _suppressedAcknowledgements = checked(_suppressedAcknowledgements + 1UL);
        }
        catch (OverflowException)
        {
            FailReliableStream(
                scheduled.MessageSequence,
                scheduled.AttemptNumber,
                DeterministicReliableFailureReason.ClockDomainExhausted);
            return;
        }
        if (isExpectedHead)
        {
            DeliverReliable(scheduled.MessageSequence, payload);
            while (_receivedReliable.Remove(
                       _nextExpectedReliableSequence,
                       out var nextPayload))
            {
                DeliverReliable(_nextExpectedReliableSequence, nextPayload);
            }
        }
        else if (!alreadyDelivered && !alreadyBuffered)
        {
            _receivedReliable.Add(scheduled.MessageSequence, payload);
        }

        if (acknowledgement is { } prepared)
        {
            CommitAcknowledgement(prepared);
        }
    }

    private void ProcessAcknowledgementArrival(ScheduledEvent scheduled)
    {
        if (_reliableStreamFailed ||
            !_outstandingReliable.Remove(scheduled.MessageSequence))
        {
            return;
        }

        PurgeRetransmissionTimers(scheduled.MessageSequence);
        try
        {
            PumpReliableWindow();
        }
        catch (ScheduledEventCapacityException)
        {
            var failedSequence = _waitingReliable.TryPeek(out var waiting)
                ? waiting
                : scheduled.MessageSequence;
            FailReliableStream(
                failedSequence,
                0,
                DeterministicReliableFailureReason.SimulationCapacityExhausted);
        }
        catch (OverflowException)
        {
            var failedSequence = _waitingReliable.TryPeek(out var waiting)
                ? waiting
                : scheduled.MessageSequence;
            FailReliableStream(
                failedSequence,
                0,
                DeterministicReliableFailureReason.ClockDomainExhausted);
        }
    }

    private void ProcessUnreliableArrival(ScheduledEvent scheduled)
    {
        var payload = scheduled.Payload ??
            throw new InvalidOperationException("Unreliable data requires a payload.");
        _pendingUnreliableDatagrams--;
        ObserveDelivery(new DeterministicChannelDelivery(
            TransportDelivery.Unreliable,
            scheduled.MessageSequence,
            _currentTime,
            payload));
    }

    private void ProcessRetransmissionTimeout(ScheduledEvent scheduled)
    {
        if (_reliableStreamFailed ||
            !_outstandingReliable.TryGetValue(scheduled.MessageSequence, out var message) ||
            message.AttemptCount != scheduled.AttemptNumber)
        {
            return;
        }

        if (message.AttemptCount >= _policy.MaximumTransmissionAttempts)
        {
            FailReliableStream(
                message.Sequence,
                message.AttemptCount,
                DeterministicReliableFailureReason.TransmissionAttemptsExhausted);
            return;
        }

        var nextAttempt = checked(message.AttemptCount + 1U);
        try
        {
            var plan = PrepareReliableTransmission(nextAttempt);
            CommitReliableTransmission(message, plan);
        }
        catch (ScheduledEventCapacityException)
        {
            FailReliableStream(
                message.Sequence,
                message.AttemptCount,
                DeterministicReliableFailureReason.SimulationCapacityExhausted);
        }
        catch (OverflowException)
        {
            FailReliableStream(
                message.Sequence,
                message.AttemptCount,
                DeterministicReliableFailureReason.ClockDomainExhausted);
        }
    }

    private void DeliverReliable(ulong sequence, byte[] payload)
    {
        ObserveDelivery(new DeterministicChannelDelivery(
            TransportDelivery.ReliableOrdered,
            sequence,
            _currentTime,
            payload));
        _nextExpectedReliableSequence = NextSequence(
            _nextExpectedReliableSequence,
            "The reliable receive sequence was exhausted.");
    }

    private void FailReliableStream(
        ulong messageSequence,
        uint attemptCount,
        DeterministicReliableFailureReason reason)
    {
        if (_reliableStreamFailed)
        {
            return;
        }

        _reliableStreamFailed = true;
        ObserveFailure(new DeterministicReliableFailure(
            messageSequence,
            attemptCount,
            _currentTime,
            reason));
        _outstandingReliable.Clear();
        _waitingReliable.Clear();
        _receivedReliable.Clear();
        PurgeAllReliableEvents();
    }

    private void Schedule(ScheduledEvent scheduled) =>
        _events.Enqueue(
            scheduled,
            (scheduled.DueTicks, EventPriority(scheduled.Kind), scheduled.Order));

    private void PurgeRetransmissionTimers(ulong sequence) =>
        PurgeEvents(scheduled =>
            scheduled.Kind == ScheduledEventKind.RetransmissionTimeout &&
            scheduled.MessageSequence == sequence);

    private void PurgeAllReliableEvents() =>
        PurgeEvents(scheduled =>
            scheduled.Kind != ScheduledEventKind.UnreliableDataArrival);

    private void PurgeEvents(Predicate<ScheduledEvent> shouldRemove)
    {
        if (_events.Count == 0)
        {
            return;
        }

        var retained = new List<ScheduledEvent>(_events.Count);
        while (_events.TryDequeue(out var scheduled, out _))
        {
            if (!shouldRemove(scheduled))
            {
                retained.Add(scheduled);
            }
        }

        foreach (var scheduled in retained)
        {
            Schedule(scheduled);
        }
    }

    private int ActiveReliableCount() =>
        _outstandingReliable.Values.Count(message => message.Started);

    private void ObserveTransmission(DeterministicChannelTransmission value) =>
        EnqueueBounded(
            _transmissions,
            value,
            ref _droppedTransmissionObservations);

    private void ObserveDelivery(DeterministicChannelDelivery value) =>
        EnqueueBounded(_deliveries, value, ref _droppedDeliveryObservations);

    private void ObserveFailure(DeterministicReliableFailure value) =>
        EnqueueBounded(_failures, value, ref _droppedFailureObservations);

    private void EnqueueBounded<T>(Queue<T> queue, T value, ref ulong droppedCount)
    {
        if (queue.Count == _policy.MaximumBufferedObservations)
        {
            queue.Dequeue();
            droppedCount = checked(droppedCount + 1UL);
        }

        queue.Enqueue(value);
    }

    private ulong NextEventOrder()
    {
        var current = _nextEventOrder;
        if (current == ulong.MaxValue)
        {
            throw new OverflowException("The scheduled-event order was exhausted.");
        }

        _nextEventOrder++;
        return current;
    }

    private void EnsureEventOrderCapacity(int count)
    {
        if (count < 0 || (ulong)count > ulong.MaxValue - _nextEventOrder)
        {
            throw new OverflowException("The scheduled-event order was exhausted.");
        }
    }

    private void EnsureScheduledEventCapacity(int count)
    {
        if (count < 0 ||
            _events.Count > _policy.MaximumScheduledEvents - count)
        {
            throw new ScheduledEventCapacityException();
        }
    }

    private static int EventPriority(ScheduledEventKind kind) => kind switch
    {
        ScheduledEventKind.ReliableDataArrival or
        ScheduledEventKind.ReliableAcknowledgementArrival or
        ScheduledEventKind.UnreliableDataArrival => 0,
        ScheduledEventKind.RetransmissionTimeout => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static ulong NextOrdinal(ulong ordinal)
    {
        if (ordinal == ulong.MaxValue)
        {
            throw new OverflowException("The simulated wire ordinal was exhausted.");
        }

        return ordinal + 1;
    }

    private static void EnsureSequenceAvailable(ulong sequence, string message)
    {
        if (sequence == 0)
        {
            throw new OverflowException(message);
        }
    }

    private static ulong AdvanceSequenceCounter(ulong sequence) =>
        sequence == ulong.MaxValue ? 0 : sequence + 1;

    private static ulong NextSequence(ulong sequence, string message)
    {
        if (sequence == ulong.MaxValue)
        {
            throw new OverflowException(message);
        }

        return sequence + 1;
    }

    private static long AddTicks(TimeSpan start, TimeSpan delay) =>
        checked(start.Ticks + delay.Ticks);

    private static IReadOnlyList<T> Drain<T>(Queue<T> source)
    {
        if (source.Count == 0)
        {
            return Array.Empty<T>();
        }

        var drained = source.ToArray();
        source.Clear();
        return drained;
    }

    private enum ScheduledEventKind
    {
        ReliableDataArrival,
        ReliableAcknowledgementArrival,
        UnreliableDataArrival,
        RetransmissionTimeout,
    }

    private sealed class ReliableMessage(ulong sequence, byte[] payload)
    {
        public ulong Sequence { get; } = sequence;

        public byte[] Payload { get; } = payload;

        public bool Started { get; set; }

        public uint AttemptCount { get; set; }
    }

    private sealed class ScheduledEventCapacityException : Exception
    {
    }

    private readonly record struct ReliableTransmissionPlan(
        ulong WireOrdinal,
        ulong NextWireOrdinal,
        uint AttemptNumber,
        NetworkImpairmentDecision Impairment,
        long TimeoutDueTicks,
        long[] ArrivalDueTicks);

    private readonly record struct UnreliableTransmissionPlan(
        ulong WireOrdinal,
        ulong NextWireOrdinal,
        NetworkImpairmentDecision Impairment,
        long[] ArrivalDueTicks)
    {
        public int ArrivalCount => ArrivalDueTicks.Length;
    }

    private readonly record struct AcknowledgementPlan(
        ulong WireOrdinal,
        ulong NextWireOrdinal,
        ulong MessageSequence,
        uint AttemptNumber,
        NetworkImpairmentDecision Impairment,
        long[] ArrivalDueTicks);

    private readonly record struct ScheduledEvent(
        ScheduledEventKind Kind,
        ulong MessageSequence,
        uint AttemptNumber,
        byte[]? Payload,
        long DueTicks,
        ulong Order);
}
