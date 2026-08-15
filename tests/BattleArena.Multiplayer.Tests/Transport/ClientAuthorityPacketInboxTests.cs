using BattleArena.Multiplayer.Transport;

namespace BattleArena.Multiplayer.Tests.Transport;

public sealed class ClientAuthorityPacketInboxTests
{
    [Fact]
    public void OnePhysicsDrainPreservesDistinctTransportReceiptTimes()
    {
        var inbox = new ClientAuthorityPacketInbox(4);
        var source = new byte[] { 1, 2, 3 };
        var connection = new TransportConnectionId(7);

        inbox.Enqueue(
            new InboundTransportPacket(
                connection,
                TransportChannel.Movement,
                source),
            10_001);
        source[0] = 99;
        inbox.Enqueue(
            new InboundTransportPacket(
                connection,
                TransportChannel.Timing,
                new byte[] { 4 }),
            10_719);

        Assert.True(inbox.TryDequeue(out var first));
        Assert.True(inbox.TryDequeue(out var second));
        Assert.Equal(10_001UL, first.ReceivedAtMicroseconds);
        Assert.Equal(10_719UL, second.ReceivedAtMicroseconds);
        Assert.Equal(new byte[] { 1, 2, 3 }, first.Packet.Payload.ToArray());
        Assert.Equal(TransportChannel.Movement, first.Packet.Channel);
        Assert.Equal(TransportChannel.Timing, second.Packet.Channel);
        Assert.False(inbox.TryDequeue(out _));
    }

    [Fact]
    public void CapacityIsBoundedAndRejectsMissingReceiptTime()
    {
        var inbox = new ClientAuthorityPacketInbox(1);
        var packet = new InboundTransportPacket(
            new TransportConnectionId(1),
            TransportChannel.Snapshot,
            new byte[] { 1 });

        Assert.Throws<ArgumentOutOfRangeException>(() => inbox.Enqueue(packet, 0));
        inbox.Enqueue(packet, 1);
        inbox.Enqueue(packet with { Payload = new byte[] { 2 } }, 2);

        Assert.Equal(1, inbox.Count);
        Assert.Equal(1, inbox.DroppedPacketCount);
        Assert.True(inbox.TryDequeue(out var retained));
        Assert.Equal(2UL, retained.ReceivedAtMicroseconds);
        Assert.Equal(new byte[] { 2 }, retained.Packet.Payload.ToArray());
    }
}
