namespace BattleArena.Multiplayer.Transport;

public interface INetworkTransport
{
    event Action<InboundTransportPacket>? PacketReceived;

    event Action<TransportConnectionId>? ConnectionOpened;

    event Action<TransportConnectionId>? ConnectionClosed;

    TransportKind Kind { get; }

    void Send(OutboundTransportPacket packet);
}
