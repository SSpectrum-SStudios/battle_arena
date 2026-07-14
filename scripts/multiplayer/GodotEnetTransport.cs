#nullable enable

using BattleArena.Multiplayer.Transport;
using Godot;

namespace BattleArena.GodotNetworking;

public partial class GodotEnetTransport : Node, INetworkTransport
{
    private const int ChannelCount = 5;
    private ENetMultiplayerPeer? _peer;
    private MultiplayerPeer.ConnectionStatus _lastConnectionStatus =
        MultiplayerPeer.ConnectionStatus.Disconnected;

    public event Action<InboundTransportPacket>? PacketReceived;

    public event Action<NetworkPeerId>? PeerConnected;

    public event Action<NetworkPeerId>? PeerDisconnected;

    public event Action<string>? StatusChanged;

    public bool IsRunning => _peer is not null;

    public bool IsAuthority { get; private set; }

    public Error Host(int port, int maximumClients)
    {
        Stop();
        var peer = new ENetMultiplayerPeer();
        var error = peer.CreateServer(port, maximumClients, ChannelCount);
        if (error != Error.Ok)
        {
            peer.Dispose();
            StatusChanged?.Invoke($"Unable to host on port {port}: {error}");
            return error;
        }

        Attach(peer, isAuthority: true);
        StatusChanged?.Invoke($"Hosting ENet on port {port}");
        return Error.Ok;
    }

    public Error Join(string address, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        Stop();
        var peer = new ENetMultiplayerPeer();
        var error = peer.CreateClient(address, port, ChannelCount);
        if (error != Error.Ok)
        {
            peer.Dispose();
            StatusChanged?.Invoke($"Unable to connect to {address}:{port}: {error}");
            return error;
        }

        Attach(peer, isAuthority: false);
        StatusChanged?.Invoke($"Connecting to {address}:{port}...");
        return Error.Ok;
    }

    public void Send(OutboundTransportPacket packet)
    {
        if (_peer is null ||
            _peer.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Connected)
        {
            throw new InvalidOperationException("ENet transport is not connected.");
        }

        if (packet.Channel is < TransportChannel.Input or > TransportChannel.Connection)
        {
            throw new ArgumentOutOfRangeException(nameof(packet), "Transport channel is not supported.");
        }

        _peer.SetTargetPeer(checked((int)packet.Recipient.Value));
        _peer.TransferChannel = (int)packet.Channel;
        _peer.TransferMode = packet.Delivery switch
        {
            TransportDelivery.UnreliableOrdered => MultiplayerPeer.TransferModeEnum.UnreliableOrdered,
            TransportDelivery.ReliableOrdered => MultiplayerPeer.TransferModeEnum.Reliable,
            _ => throw new ArgumentOutOfRangeException(nameof(packet), "Transport delivery mode is not supported."),
        };

        var error = _peer.PutPacket(packet.Payload.ToArray());
        if (error != Error.Ok)
        {
            throw new InvalidOperationException($"ENet failed to send packet: {error}");
        }
    }

    public override void _Process(double delta)
    {
        if (_peer is null)
        {
            return;
        }

        if (_peer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Disconnected)
        {
            return;
        }

        _peer.Poll();
        ReportConnectionStatusChange();
        if (_peer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Disconnected)
        {
            return;
        }

        while (_peer.GetAvailablePacketCount() > 0)
        {
            // ENet metadata describes the next queued packet and must be read before GetPacket pops it.
            var senderValue = checked((ulong)_peer.GetPacketPeer());
            var channelValue = _peer.GetPacketChannel();
            var payload = _peer.GetPacket();
            var packetError = _peer.GetPacketError();
            if (packetError != Error.Ok)
            {
                StatusChanged?.Invoke($"Discarded ENet packet: {packetError}");
                continue;
            }

            if (senderValue == 0 || channelValue is < 0 or >= ChannelCount)
            {
                StatusChanged?.Invoke("Discarded ENet packet with invalid peer or channel metadata.");
                continue;
            }

            PacketReceived?.Invoke(new InboundTransportPacket(
                new NetworkPeerId(senderValue),
                (TransportChannel)channelValue,
                payload));
        }
    }

    public override void _ExitTree() => Stop();

    public void Stop()
    {
        if (_peer is null)
        {
            return;
        }

        _peer.PeerConnected -= OnPeerConnected;
        _peer.PeerDisconnected -= OnPeerDisconnected;
        _peer.Close();
        _peer.Dispose();
        _peer = null;
        IsAuthority = false;
        _lastConnectionStatus = MultiplayerPeer.ConnectionStatus.Disconnected;
        StatusChanged?.Invoke("ENet transport stopped");
    }

    private void Attach(ENetMultiplayerPeer peer, bool isAuthority)
    {
        _peer = peer;
        IsAuthority = isAuthority;
        _lastConnectionStatus = peer.GetConnectionStatus();
        peer.PeerConnected += OnPeerConnected;
        peer.PeerDisconnected += OnPeerDisconnected;
    }

    private void OnPeerConnected(long peerId)
    {
        if (peerId <= 0)
        {
            return;
        }

        var networkPeer = new NetworkPeerId(checked((ulong)peerId));
        StatusChanged?.Invoke($"ENet peer {peerId} connected");
        PeerConnected?.Invoke(networkPeer);
    }

    private void OnPeerDisconnected(long peerId)
    {
        if (peerId <= 0)
        {
            return;
        }

        var networkPeer = new NetworkPeerId(checked((ulong)peerId));
        StatusChanged?.Invoke($"ENet peer {peerId} disconnected");
        PeerDisconnected?.Invoke(networkPeer);
    }

    private void ReportConnectionStatusChange()
    {
        if (_peer is null)
        {
            return;
        }

        var current = _peer.GetConnectionStatus();
        if (current == _lastConnectionStatus)
        {
            return;
        }

        _lastConnectionStatus = current;
        StatusChanged?.Invoke($"ENet connection state: {current}");
    }
}
