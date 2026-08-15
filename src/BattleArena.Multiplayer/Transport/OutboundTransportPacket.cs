namespace BattleArena.Multiplayer.Transport;

public readonly record struct OutboundTransportPacket(
    TransportConnectionId Recipient,
    TransportChannel Channel,
    TransportDelivery Delivery,
    ReadOnlyMemory<byte> Payload);
