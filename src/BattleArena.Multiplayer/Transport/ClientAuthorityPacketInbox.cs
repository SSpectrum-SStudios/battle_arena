namespace BattleArena.Multiplayer.Transport;

/// <summary>
/// Owns authenticated authority packet bytes and their transport receipt time
/// until application state consumes them at a fixed-physics boundary.
/// </summary>
public sealed class ClientAuthorityPacketInbox
{
    private readonly Queue<TimestampedInboundTransportPacket> _pending = [];
    private readonly int _capacity;

    public ClientAuthorityPacketInbox(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    public int Count => _pending.Count;

    public long DroppedPacketCount { get; private set; }

    public void Enqueue(InboundTransportPacket packet, ulong receivedAtMicroseconds)
    {
        if (receivedAtMicroseconds == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(receivedAtMicroseconds),
                "A queued packet requires its actual nonzero transport receipt time.");
        }

        if (_pending.Count == _capacity)
        {
            _pending.Dequeue();
            DroppedPacketCount++;
        }

        var ownedPacket = new InboundTransportPacket(
            packet.Sender,
            packet.Channel,
            packet.Payload.ToArray());
        _pending.Enqueue(new TimestampedInboundTransportPacket(
            ownedPacket,
            receivedAtMicroseconds));
    }

    public bool TryDequeue(out TimestampedInboundTransportPacket packet) =>
        _pending.TryDequeue(out packet);
}

public readonly record struct TimestampedInboundTransportPacket(
    InboundTransportPacket Packet,
    ulong ReceivedAtMicroseconds);
