namespace BattleArena.Multiplayer.Transport;

public readonly record struct OutboundTransportPacket(
    NetworkPeerId Recipient,
    TransportChannel Channel,
    TransportDelivery Delivery,
    ReadOnlyMemory<byte> Payload);
