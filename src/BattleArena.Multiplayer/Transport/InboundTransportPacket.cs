namespace BattleArena.Multiplayer.Transport;

public readonly record struct InboundTransportPacket(
    NetworkPeerId Sender,
    TransportChannel Channel,
    ReadOnlyMemory<byte> Payload);
