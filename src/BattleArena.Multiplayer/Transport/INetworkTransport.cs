namespace BattleArena.Multiplayer.Transport;

public interface INetworkTransport
{
    event Action<InboundTransportPacket>? PacketReceived;

    event Action<NetworkPeerId>? PeerConnected;

    event Action<NetworkPeerId>? PeerDisconnected;

    void Send(OutboundTransportPacket packet);
}
