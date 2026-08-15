using BattleArena.Multiplayer.Transport;

namespace BattleArena.Multiplayer.OwnerPrediction;

public enum ReliableImpairmentBehavior
{
    PassThrough = 0,
    Reject = 1,
}

public enum ImpairmentQueueOverflowBehavior
{
    RejectNewest = 0,
    DropNewest = 1,
}

/// <summary>
/// Applies independently configured deterministic schedules to outbound
/// unreliable routes. Reliable packets are explicitly passed through or
/// rejected; retransmission and head-of-line behavior are not simulated here.
/// </summary>
public sealed class NetworkImpairmentTransportDecorator : INetworkTransport, IDisposable
{
    private readonly INetworkTransport _inner;
    private readonly Func<TransportConnectionId, NetworkImpairmentSchedule>
        _routeScheduleFactory;
    private readonly NetworkImpairmentDirection _outboundDirection;
    private readonly ReliableImpairmentBehavior _reliableBehavior;
    private readonly int _maximumPendingPackets;
    private readonly ImpairmentQueueOverflowBehavior _overflowBehavior;
    private readonly PriorityQueue<ScheduledPacket, (long DueTicks, ulong Sequence)> _pending =
        new();
    private readonly Dictionary<TransportConnectionId, RouteState> _routes = [];
    private readonly HashSet<TransportConnectionId> _closedRecipients = [];

    private ulong _nextInsertionSequence;
    private TimeSpan _currentTime;
    private bool _isFlushing;
    private bool _flushRequested;
    private bool _disposed;

    public NetworkImpairmentTransportDecorator(
        INetworkTransport inner,
        Func<TransportConnectionId, NetworkImpairmentSchedule> routeScheduleFactory,
        NetworkImpairmentDirection outboundDirection,
        ReliableImpairmentBehavior reliableBehavior,
        int maximumPendingPackets,
        ImpairmentQueueOverflowBehavior overflowBehavior)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _routeScheduleFactory = routeScheduleFactory ??
            throw new ArgumentNullException(nameof(routeScheduleFactory));
        if (!Enum.IsDefined(outboundDirection))
        {
            throw new ArgumentOutOfRangeException(
                nameof(outboundDirection),
                outboundDirection,
                null);
        }

        if (!Enum.IsDefined(reliableBehavior))
        {
            throw new ArgumentOutOfRangeException(
                nameof(reliableBehavior),
                reliableBehavior,
                null);
        }

        if (maximumPendingPackets <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumPendingPackets),
                maximumPendingPackets,
                "The impairment queue limit must be positive.");
        }

        if (!Enum.IsDefined(overflowBehavior))
        {
            throw new ArgumentOutOfRangeException(
                nameof(overflowBehavior),
                overflowBehavior,
                null);
        }

        _outboundDirection = outboundDirection;
        _reliableBehavior = reliableBehavior;
        _maximumPendingPackets = maximumPendingPackets;
        _overflowBehavior = overflowBehavior;
        _inner.PacketReceived += OnPacketReceived;
        _inner.ConnectionOpened += OnConnectionOpened;
        _inner.ConnectionClosed += OnConnectionClosed;
    }

    public event Action<InboundTransportPacket>? PacketReceived;

    public event Action<TransportConnectionId>? ConnectionOpened;

    public event Action<TransportConnectionId>? ConnectionClosed;

    public TransportKind Kind => _inner.Kind;

    public TimeSpan CurrentTime => _currentTime;

    public int PendingPacketCount => _pending.Count;

    public void Send(OutboundTransportPacket packet)
    {
        ThrowIfDisposed();
        ValidatePacket(packet);

        switch (packet.Delivery)
        {
            case TransportDelivery.ReliableOrdered:
                SendReliable(packet);
                return;
            case TransportDelivery.Unreliable:
                ScheduleUnreliable(packet);
                return;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(packet),
                    packet.Delivery,
                    "The packet delivery mode is not supported.");
        }
    }

    /// <summary>
    /// Advances the explicit impairment clock. Reentrant advances/enqueues are
    /// folded into the active flush instead of recursively pumping the queue.
    /// </summary>
    public void AdvanceTo(TimeSpan time)
    {
        ThrowIfDisposed();
        if (time < _currentTime)
        {
            throw new ArgumentOutOfRangeException(
                nameof(time),
                time,
                "The impairment clock cannot move backward.");
        }

        _currentTime = time;
        FlushDue();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _inner.PacketReceived -= OnPacketReceived;
        _inner.ConnectionOpened -= OnConnectionOpened;
        _inner.ConnectionClosed -= OnConnectionClosed;
        _pending.Clear();
        _routes.Clear();
        _closedRecipients.Clear();
        PacketReceived = null;
        ConnectionOpened = null;
        ConnectionClosed = null;
    }

    private void SendReliable(OutboundTransportPacket packet)
    {
        switch (_reliableBehavior)
        {
            case ReliableImpairmentBehavior.PassThrough:
                if (_isFlushing)
                {
                    DeferReliable(packet);
                }
                else
                {
                    _inner.Send(packet);
                }

                break;
            case ReliableImpairmentBehavior.Reject:
                throw new InvalidOperationException(
                    "Application impairment cannot emulate reliable retransmission or ordering.");
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(_reliableBehavior),
                    _reliableBehavior,
                    null);
        }
    }

    private void DeferReliable(OutboundTransportPacket packet)
    {
        if (_pending.Count >= _maximumPendingPackets)
        {
            throw new InvalidOperationException(
                "The deterministic impairment queue is full.");
        }

        if (_nextInsertionSequence == ulong.MaxValue)
        {
            throw new OverflowException("The impairment insertion sequence was exhausted.");
        }

        var snapshot = packet with { Payload = packet.Payload.ToArray() };
        var sequence = _nextInsertionSequence;
        _nextInsertionSequence++;
        Enqueue(snapshot, _currentTime.Ticks, sequence);
        _flushRequested = true;
    }

    private void ScheduleUnreliable(OutboundTransportPacket packet)
    {
        var route = GetOrCreateRoute(packet.Recipient);
        var ordinal = route.NextOrdinal;
        var decision = route.Schedule.Evaluate(_outboundDirection, ordinal);
        if (decision.IsDropped)
        {
            route.NextOrdinal = checked(ordinal + 1UL);
            return;
        }

        var requiredEntries = decision.DuplicateDelay is null ? 1 : 2;
        if (_pending.Count > _maximumPendingPackets - requiredEntries)
        {
            switch (_overflowBehavior)
            {
                case ImpairmentQueueOverflowBehavior.RejectNewest:
                    throw new InvalidOperationException(
                        "The deterministic impairment queue is full.");
                case ImpairmentQueueOverflowBehavior.DropNewest:
                    route.NextOrdinal = checked(ordinal + 1UL);
                    return;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(_overflowBehavior),
                        _overflowBehavior,
                        null);
            }
        }

        var primaryDueTicks = CalculateDueTicks(decision.PrimaryDelay);
        long? duplicateDueTicks = decision.DuplicateDelay is { } duplicateDelay
            ? CalculateDueTicks(duplicateDelay)
            : null;
        if (_nextInsertionSequence > ulong.MaxValue - (ulong)requiredEntries)
        {
            throw new OverflowException("The impairment insertion sequence was exhausted.");
        }

        var payloadSnapshot = packet.Payload.ToArray();
        var snapshot = packet with { Payload = payloadSnapshot };
        var firstSequence = _nextInsertionSequence;
        route.NextOrdinal = checked(ordinal + 1UL);
        _nextInsertionSequence += (ulong)requiredEntries;

        Enqueue(snapshot, primaryDueTicks, firstSequence);
        if (duplicateDueTicks is { } dueTicks)
        {
            Enqueue(snapshot, dueTicks, firstSequence + 1UL);
        }

        FlushDue();
    }

    private RouteState GetOrCreateRoute(TransportConnectionId recipient)
    {
        if (_routes.TryGetValue(recipient, out var route))
        {
            return route;
        }

        var schedule = _routeScheduleFactory(recipient) ??
            throw new InvalidOperationException(
                "The route impairment schedule factory returned null.");
        route = new RouteState(schedule);
        _routes.Add(recipient, route);
        return route;
    }

    private long CalculateDueTicks(TimeSpan delay) =>
        checked(_currentTime.Ticks + delay.Ticks);

    private void Enqueue(
        OutboundTransportPacket packet,
        long dueTicks,
        ulong sequence)
    {
        var scheduled = new ScheduledPacket(packet, dueTicks, sequence);
        _pending.Enqueue(scheduled, (dueTicks, sequence));
    }

    private void FlushDue()
    {
        if (_isFlushing)
        {
            _flushRequested = true;
            return;
        }

        _isFlushing = true;
        try
        {
            _flushRequested = false;
            while (_pending.TryPeek(out _, out var priority) &&
                   priority.DueTicks <= _currentTime.Ticks)
            {
                var scheduled = _pending.Dequeue();
                try
                {
                    _inner.Send(scheduled.Packet);
                }
                catch
                {
                    if (!_disposed &&
                        !_closedRecipients.Contains(scheduled.Packet.Recipient))
                    {
                        Enqueue(
                            scheduled.Packet,
                            scheduled.DueTicks,
                            scheduled.Sequence);
                        throw;
                    }
                }

                if (_flushRequested)
                {
                    break;
                }
            }
        }
        finally
        {
            _isFlushing = false;
            _flushRequested = false;
        }
    }

    private void ValidatePacket(OutboundTransportPacket packet)
    {
        if (packet.Recipient.Value == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(packet),
                "A packet recipient must be initialized.");
        }

        if (!TransportChannels.IsDefined(packet.Channel))
        {
            throw new ArgumentOutOfRangeException(
                nameof(packet),
                "The packet channel is not supported.");
        }

        if (!Enum.IsDefined(packet.Delivery))
        {
            throw new ArgumentOutOfRangeException(
                nameof(packet),
                "The packet delivery mode is not supported.");
        }

        if (_closedRecipients.Contains(packet.Recipient))
        {
            throw new InvalidOperationException(
                "Cannot schedule a packet for a closed transport connection.");
        }
    }

    private void OnPacketReceived(InboundTransportPacket packet) =>
        PacketReceived?.Invoke(packet);

    private void OnConnectionOpened(TransportConnectionId connection)
    {
        _closedRecipients.Remove(connection);
        PurgeRoute(connection);
        ConnectionOpened?.Invoke(connection);
    }

    private void OnConnectionClosed(TransportConnectionId connection)
    {
        _closedRecipients.Add(connection);
        PurgeRoute(connection);
        ConnectionClosed?.Invoke(connection);
    }

    private void PurgeRoute(TransportConnectionId connection)
    {
        _routes.Remove(connection);
        if (_pending.Count == 0)
        {
            return;
        }

        var retained = new List<ScheduledPacket>(_pending.Count);
        while (_pending.TryDequeue(out var packet, out _))
        {
            if (packet.Packet.Recipient != connection)
            {
                retained.Add(packet);
            }
        }

        foreach (var packet in retained)
        {
            Enqueue(packet.Packet, packet.DueTicks, packet.Sequence);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed class RouteState(NetworkImpairmentSchedule schedule)
    {
        public NetworkImpairmentSchedule Schedule { get; } = schedule;

        public ulong NextOrdinal { get; set; }
    }

    private readonly record struct ScheduledPacket(
        OutboundTransportPacket Packet,
        long DueTicks,
        ulong Sequence);
}
