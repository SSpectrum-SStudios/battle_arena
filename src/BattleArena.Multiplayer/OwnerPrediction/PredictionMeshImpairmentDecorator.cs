using BattleArena.Multiplayer.Connection;
using BattleArena.Multiplayer.Prediction;
using BattleArena.Multiplayer.Transport;
using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.OwnerPrediction;

/// <summary>
/// Stable identity for one authority-authorized direct-prediction attempt.
/// Packet ordinals restart when any part of this identity changes.
/// </summary>
public readonly record struct PredictionMeshRouteKey
{
    public PredictionMeshRouteKey(
        SessionPeerId remotePeerId,
        uint routeGeneration,
        ulong attemptId)
    {
        if (remotePeerId.Value == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(remotePeerId));
        }

        if (routeGeneration == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(routeGeneration));
        }

        if (attemptId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptId));
        }

        RemotePeerId = remotePeerId;
        RouteGeneration = routeGeneration;
        AttemptId = attemptId;
    }

    public SessionPeerId RemotePeerId { get; }

    public uint RouteGeneration { get; }

    public ulong AttemptId { get; }
}

/// <summary>
/// Applies deterministic impairment only to optional direct movement hints.
/// It never owns or decorates the mandatory authority transport. A delayed
/// mesh-send failure is reported as a route failure so the mesh driver can
/// continue in authority-only mode.
/// </summary>
public sealed class PredictionMeshImpairmentDecorator :
    IPredictionMeshTransport,
    IDisposable
{
    private readonly IPredictionMeshTransport _inner;
    private readonly Func<PredictionMeshRouteKey, NetworkImpairmentSchedule>
        _routeScheduleFactory;
    private readonly NetworkImpairmentDirection _outboundDirection;
    private readonly ReliableImpairmentBehavior _controlBehavior;
    private readonly int _maximumPendingPackets;
    private readonly ImpairmentQueueOverflowBehavior _overflowBehavior;
    private readonly PriorityQueue<ScheduledPacket, (long DueTicks, ulong Sequence)> _pending =
        new();
    private readonly Dictionary<PredictionMeshRouteKey, RouteState> _routes = [];
    private readonly Dictionary<SessionPeerId, PredictionMeshRouteKey> _activeRoutes = [];

    private ulong _nextInsertionSequence;
    private TimeSpan _currentTime;
    private bool _isFlushing;
    private bool _flushRequested;
    private bool _eventsAttached = true;
    private bool _stopped;
    private bool _disposed;

    public PredictionMeshImpairmentDecorator(
        IPredictionMeshTransport inner,
        Func<PredictionMeshRouteKey, NetworkImpairmentSchedule> routeScheduleFactory,
        NetworkImpairmentDirection outboundDirection,
        ReliableImpairmentBehavior controlBehavior,
        int maximumPendingPackets,
        ImpairmentQueueOverflowBehavior overflowBehavior)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _routeScheduleFactory = routeScheduleFactory ??
            throw new ArgumentNullException(nameof(routeScheduleFactory));
        if (!Enum.IsDefined(outboundDirection))
        {
            throw new ArgumentOutOfRangeException(nameof(outboundDirection));
        }

        if (!Enum.IsDefined(controlBehavior))
        {
            throw new ArgumentOutOfRangeException(nameof(controlBehavior));
        }

        if (maximumPendingPackets <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPendingPackets));
        }

        if (!Enum.IsDefined(overflowBehavior))
        {
            throw new ArgumentOutOfRangeException(nameof(overflowBehavior));
        }

        _outboundDirection = outboundDirection;
        _controlBehavior = controlBehavior;
        _maximumPendingPackets = maximumPendingPackets;
        _overflowBehavior = overflowBehavior;
        _inner.RouteChanged += OnRouteChanged;
        _inner.PacketReceived += OnPacketReceived;
    }

    public event Action<PredictionTransportRouteChanged>? RouteChanged;

    public event Action<InboundPredictionPacket>? PacketReceived;

    public TransportKind Kind => _inner.Kind;

    public bool SupportsDirectRoutes => _inner.SupportsDirectRoutes;

    public PredictionRouteDescriptor LocalRouteDescriptor =>
        _inner.LocalRouteDescriptor;

    public TimeSpan CurrentTime => _currentTime;

    public int PendingPacketCount => _pending.Count;

    public void Apply(PredictionMeshDirective directive)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(directive);
        if (!Enum.IsDefined(directive.Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(directive));
        }

        if (_stopped)
        {
            throw new InvalidOperationException("The prediction mesh transport is stopped.");
        }

        if (directive.Kind == PredictionMeshDirectiveKind.Remove)
        {
            PurgePeer(directive.RemotePeerId);
            TryApplyInner(directive, failureKey: null);
            return;
        }

        var route = directive.Route ??
            throw new ArgumentException(
                "A listen or initiate directive requires an authorized route.",
                nameof(directive));
        if (route.RemotePeerId != directive.RemotePeerId)
        {
            throw new ArgumentException(
                "The directive peer must match the authorized route peer.",
                nameof(directive));
        }

        var key = new PredictionMeshRouteKey(
            directive.RemotePeerId,
            route.RouteGeneration,
            directive.AttemptId);
        if (!_activeRoutes.TryGetValue(directive.RemotePeerId, out var active) ||
            active != key)
        {
            PurgePeer(directive.RemotePeerId);
            _activeRoutes.Add(directive.RemotePeerId, key);
        }

        TryApplyInner(directive, key);
    }

    public bool TrySendControl(
        SessionPeerId recipient,
        uint routeGeneration,
        ulong attemptId,
        ReadOnlyMemory<byte> payload)
    {
        ThrowIfDisposed();
        if (_stopped || !TryGetActiveKey(recipient, routeGeneration, attemptId, out var key))
        {
            return false;
        }

        switch (_controlBehavior)
        {
            case ReliableImpairmentBehavior.Reject:
                return false;
            case ReliableImpairmentBehavior.PassThrough:
                if (_isFlushing || HasPendingControl(key))
                {
                    var admitted = TryDeferControl(key, payload);
                    if (admitted && !_isFlushing)
                    {
                        FlushDue();
                    }

                    return admitted;
                }

                try
                {
                    return _inner.TrySendControl(
                        recipient,
                        routeGeneration,
                        attemptId,
                        payload);
                }
                catch
                {
                    return false;
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(_controlBehavior));
        }
    }

    public bool TrySendMovement(
        SessionPeerId recipient,
        uint routeGeneration,
        ulong attemptId,
        ReadOnlyMemory<byte> payload)
    {
        ThrowIfDisposed();
        if (_stopped || !TryGetActiveKey(recipient, routeGeneration, attemptId, out var key))
        {
            return false;
        }

        var route = GetOrCreateRoute(key);
        var ordinal = route.NextOrdinal;
        var decision = route.Schedule.Evaluate(_outboundDirection, ordinal);
        if (decision.IsDropped)
        {
            route.NextOrdinal = checked(ordinal + 1UL);
            return true;
        }

        var requiredEntries = decision.DuplicateDelay is null ? 1 : 2;
        if (_pending.Count > _maximumPendingPackets - requiredEntries)
        {
            switch (_overflowBehavior)
            {
                case ImpairmentQueueOverflowBehavior.RejectNewest:
                    return false;
                case ImpairmentQueueOverflowBehavior.DropNewest:
                    route.NextOrdinal = checked(ordinal + 1UL);
                    return true;
                default:
                    throw new ArgumentOutOfRangeException(nameof(_overflowBehavior));
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

        var snapshot = payload.ToArray();
        var firstSequence = _nextInsertionSequence;
        _nextInsertionSequence += (ulong)requiredEntries;
        route.NextOrdinal = checked(ordinal + 1UL);
        Enqueue(new ScheduledPacket(
            key,
            PredictionPacketKind.Movement,
            snapshot,
            primaryDueTicks,
            firstSequence));
        if (duplicateDueTicks is { } dueTicks)
        {
            Enqueue(new ScheduledPacket(
                key,
                PredictionPacketKind.Movement,
                snapshot,
                dueTicks,
                firstSequence + 1UL));
        }

        FlushDue();
        return true;
    }

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

    public void Stop()
    {
        ThrowIfDisposed();
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        ClearState();
        DetachInnerEvents();
        try
        {
            _inner.Stop();
        }
        catch
        {
            // The mesh is optional. Stopping it must not tear down authority play.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DetachInnerEvents();
        ClearState();
        RouteChanged = null;
        PacketReceived = null;
    }

    private void TryApplyInner(
        PredictionMeshDirective directive,
        PredictionMeshRouteKey? failureKey)
    {
        try
        {
            _inner.Apply(directive);
        }
        catch (Exception exception)
        {
            if (failureKey is { } key)
            {
                FailRoute(key, $"Prediction mesh apply failed: {exception.GetType().Name}.");
            }
        }
    }

    private bool TryDeferControl(
        PredictionMeshRouteKey key,
        ReadOnlyMemory<byte> payload)
    {
        if (_pending.Count >= _maximumPendingPackets ||
            _nextInsertionSequence == ulong.MaxValue)
        {
            return false;
        }

        var sequence = _nextInsertionSequence++;
        Enqueue(new ScheduledPacket(
            key,
            PredictionPacketKind.Control,
            payload.ToArray(),
            _currentTime.Ticks,
            sequence));
        _flushRequested = true;
        return true;
    }

    private bool HasPendingControl(PredictionMeshRouteKey key) =>
        _pending.UnorderedItems.Any(item =>
            item.Element.Key == key &&
            item.Element.Kind == PredictionPacketKind.Control);

    private RouteState GetOrCreateRoute(PredictionMeshRouteKey key)
    {
        if (_routes.TryGetValue(key, out var route))
        {
            return route;
        }

        var schedule = _routeScheduleFactory(key) ??
            throw new InvalidOperationException(
                "The mesh route impairment schedule factory returned null.");
        route = new RouteState(schedule);
        _routes.Add(key, route);
        return route;
    }

    private long CalculateDueTicks(TimeSpan delay) =>
        checked(_currentTime.Ticks + delay.Ticks);

    private void Enqueue(ScheduledPacket packet) =>
        _pending.Enqueue(packet, (packet.DueTicks, packet.Sequence));

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
                var packet = _pending.Dequeue();
                if (!IsActive(packet.Key))
                {
                    continue;
                }

                var sent = TrySendInner(packet);
                if (!sent)
                {
                    FailRoute(packet.Key, "Prediction mesh send failed.");
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

    private bool TrySendInner(ScheduledPacket packet)
    {
        try
        {
            return packet.Kind switch
            {
                PredictionPacketKind.Control => _inner.TrySendControl(
                    packet.Key.RemotePeerId,
                    packet.Key.RouteGeneration,
                    packet.Key.AttemptId,
                    packet.Payload),
                PredictionPacketKind.Movement => _inner.TrySendMovement(
                    packet.Key.RemotePeerId,
                    packet.Key.RouteGeneration,
                    packet.Key.AttemptId,
                    packet.Payload),
                _ => throw new ArgumentOutOfRangeException(nameof(packet)),
            };
        }
        catch
        {
            return false;
        }
    }

    private bool TryGetActiveKey(
        SessionPeerId recipient,
        uint routeGeneration,
        ulong attemptId,
        out PredictionMeshRouteKey key)
    {
        if (recipient.Value == 0 || routeGeneration == 0 || attemptId == 0)
        {
            key = default;
            return false;
        }

        key = new PredictionMeshRouteKey(recipient, routeGeneration, attemptId);
        return IsActive(key);
    }

    private bool IsActive(PredictionMeshRouteKey key) =>
        _activeRoutes.TryGetValue(key.RemotePeerId, out var active) && active == key;

    private void FailRoute(PredictionMeshRouteKey key, string detail)
    {
        if (!IsActive(key))
        {
            return;
        }

        PurgeKey(key);
        RouteChanged?.Invoke(new PredictionTransportRouteChanged(
            key.RemotePeerId,
            key.RouteGeneration,
            key.AttemptId,
            PredictionTransportRouteState.Failed,
            detail));
    }

    private void OnRouteChanged(PredictionTransportRouteChanged change)
    {
        if (_disposed || _stopped)
        {
            return;
        }

        if (change.RouteGeneration != 0 && change.AttemptId != 0)
        {
            var key = new PredictionMeshRouteKey(
                change.RemotePeerId,
                change.RouteGeneration,
                change.AttemptId);
            if (change.State is PredictionTransportRouteState.Failed or
                PredictionTransportRouteState.Disconnected)
            {
                PurgeKey(key);
            }
        }

        RouteChanged?.Invoke(change);
    }

    private void OnPacketReceived(InboundPredictionPacket packet)
    {
        if (!_disposed && !_stopped)
        {
            PacketReceived?.Invoke(packet);
        }
    }

    private void PurgePeer(SessionPeerId peer)
    {
        if (_activeRoutes.Remove(peer, out var active))
        {
            _routes.Remove(active);
        }

        PurgePending(packet => packet.Key.RemotePeerId == peer);
    }

    private void PurgeKey(PredictionMeshRouteKey key)
    {
        if (_activeRoutes.TryGetValue(key.RemotePeerId, out var active) && active == key)
        {
            _activeRoutes.Remove(key.RemotePeerId);
        }

        _routes.Remove(key);
        PurgePending(packet => packet.Key == key);
    }

    private void PurgePending(Predicate<ScheduledPacket> shouldRemove)
    {
        if (_pending.Count == 0)
        {
            return;
        }

        var retained = new List<ScheduledPacket>(_pending.Count);
        while (_pending.TryDequeue(out var packet, out _))
        {
            if (!shouldRemove(packet))
            {
                retained.Add(packet);
            }
        }

        foreach (var packet in retained)
        {
            Enqueue(packet);
        }
    }

    private void ClearState()
    {
        _pending.Clear();
        _routes.Clear();
        _activeRoutes.Clear();
    }

    private void DetachInnerEvents()
    {
        if (!_eventsAttached)
        {
            return;
        }

        _inner.RouteChanged -= OnRouteChanged;
        _inner.PacketReceived -= OnPacketReceived;
        _eventsAttached = false;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private enum PredictionPacketKind
    {
        Control,
        Movement,
    }

    private sealed class RouteState(NetworkImpairmentSchedule schedule)
    {
        public NetworkImpairmentSchedule Schedule { get; } = schedule;

        public ulong NextOrdinal { get; set; }
    }

    private readonly record struct ScheduledPacket(
        PredictionMeshRouteKey Key,
        PredictionPacketKind Kind,
        byte[] Payload,
        long DueTicks,
        ulong Sequence);
}
