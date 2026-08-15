namespace BattleArena.Multiplayer.Transport;

public readonly record struct InboundTransportPacket(
    TransportConnectionId Sender,
    TransportChannel Channel,
    ReadOnlyMemory<byte> Payload);
