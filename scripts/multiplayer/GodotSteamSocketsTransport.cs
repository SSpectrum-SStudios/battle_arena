#nullable enable

using BattleArena.Multiplayer.Transport;
using BattleArena.Multiplayer.Prediction;
using Godot;
using GodotSteam;
using Steam = GodotSteam.Steam;

namespace BattleArena.GodotNetworking;

public partial class GodotSteamSocketsTransport : Node, INetworkTransport,
    IAuthenticatedSteamIdentityDirectory
{
    private const uint InvalidHandle = 0;
    private const int MaximumMessagesPerPoll = 256;
    private readonly Dictionary<uint, ulong> _remoteSteamIds = [];
    private readonly HashSet<uint> _connectedConnections = [];
    private uint _listenSocket;
    private long _pollGroup;
    private bool _subscribed;

    public event Action<InboundTransportPacket>? PacketReceived;
    public event Action<TransportConnectionId>? ConnectionOpened;
    public event Action<TransportConnectionId>? ConnectionClosed;
    public event Action<string>? StatusChanged;

    public bool IsRunning => _pollGroup != 0;
    public bool IsAuthority { get; private set; }
    public TransportKind Kind => TransportKind.Steam;
    public Func<ulong, bool>? MayAcceptSteamPeer { get; set; }

    public Error Host()
    {
        Stop();
        EnsureSubscribed();
        _pollGroup = Steam.CreatePollGroup();
        if (_pollGroup == 0)
        {
            StatusChanged?.Invoke("Steam failed to create a networking poll group.");
            return Error.CantCreate;
        }

        _listenSocket = CreateListenSocketP2P(SteamApplicationConfiguration.VirtualPort);
        if (_listenSocket == InvalidHandle)
        {
            Stop();
            StatusChanged?.Invoke("Steam failed to create a P2P listen socket.");
            return Error.CantCreate;
        }

        IsAuthority = true;
        StatusChanged?.Invoke("Steam Networking Sockets host is listening");
        return Error.Ok;
    }

    public Error Join(ulong hostSteamId)
    {
        if (hostSteamId == 0)
        {
            return Error.InvalidParameter;
        }

        Stop();
        EnsureSubscribed();
        _pollGroup = Steam.CreatePollGroup();
        if (_pollGroup == 0)
        {
            StatusChanged?.Invoke("Steam failed to create a networking poll group.");
            return Error.CantCreate;
        }

        var connection = ConnectP2P(hostSteamId, SteamApplicationConfiguration.VirtualPort);
        if (connection == InvalidHandle || !Steam.SetConnectionPollGroup(connection, _pollGroup))
        {
            Stop();
            StatusChanged?.Invoke($"Steam failed to connect to host {hostSteamId}.");
            return Error.CantConnect;
        }

        _remoteSteamIds[connection] = hostSteamId;
        IsAuthority = false;
        StatusChanged?.Invoke($"Connecting to Steam host {hostSteamId}...");
        return Error.Ok;
    }

    public void Send(OutboundTransportPacket packet)
    {
        var connection = checked((uint)packet.Recipient.Value);
        if (!_remoteSteamIds.ContainsKey(connection))
        {
            throw new InvalidOperationException($"Steam connection {connection} is not active.");
        }

        var payload = new byte[packet.Payload.Length + 1];
        payload[0] = (byte)packet.Channel;
        packet.Payload.Span.CopyTo(payload.AsSpan(1));
        var flags = packet.Delivery switch
        {
            TransportDelivery.Unreliable => Steam.NetworkingSendNoDelay,
            TransportDelivery.ReliableOrdered => Steam.NetworkingSendReliable | Steam.NetworkingSendNoNagle,
            _ => throw new ArgumentOutOfRangeException(nameof(packet)),
        };
        var result = Steam.SendMessageToConnection(connection, payload, flags);
        if ((ErrorResult)result["result"].AsInt32() != ErrorResult.Ok)
        {
            throw new InvalidOperationException(
                $"Steam failed to send packet: {(ErrorResult)result["result"].AsInt32()}");
        }
    }

    public override void _Process(double delta)
    {
        if (_pollGroup == 0)
        {
            return;
        }

        Steam.RunNetworkingCallbacks();
        var messages = Steam.ReceiveMessagesOnPollGroup((uint)_pollGroup, MaximumMessagesPerPoll);
        foreach (var value in messages)
        {
            var message = value.AsGodotDictionary();
            var payload = message["payload"].AsByteArray();
            var connection = message["connection"].AsUInt32();
            if (payload.Length < 1 || !_remoteSteamIds.ContainsKey(connection))
            {
                continue;
            }

            var channel = (TransportChannel)payload[0];
            if (!TransportChannels.IsDefined(channel))
            {
                continue;
            }

            PacketReceived?.Invoke(new InboundTransportPacket(
                new TransportConnectionId(connection),
                channel,
                payload.AsMemory(1)));
        }
    }

    public override void _ExitTree()
    {
        Stop();
        if (_subscribed)
        {
            Steam.NetworkConnectionStatusChanged -= OnConnectionStatusChanged;
            _subscribed = false;
        }
    }

    public void Stop()
    {
        foreach (var connection in _remoteSteamIds.Keys.ToArray())
        {
            Steam.CloseConnection(connection, (int)Steam.NetworkingConnectionEnd.AppGeneric,
                "Battle Arena transport stopped", linger: false);
        }

        _remoteSteamIds.Clear();
        _connectedConnections.Clear();
        if (_listenSocket != InvalidHandle)
        {
            Steam.CloseListenSocket(_listenSocket);
            _listenSocket = InvalidHandle;
        }

        if (_pollGroup != 0)
        {
            Steam.DestroyPollGroup(_pollGroup);
            _pollGroup = 0;
        }

        IsAuthority = false;
    }

    private void EnsureSubscribed()
    {
        if (!_subscribed)
        {
            Steam.NetworkConnectionStatusChanged += OnConnectionStatusChanged;
            _subscribed = true;
        }
    }

    private static uint CreateListenSocketP2P(int virtualPort) =>
        Steam.GetInstance()
            .Call(Methods.CreateListenSocketP2P, virtualPort, new Godot.Collections.Dictionary())
            .As<uint>();

    private static uint ConnectP2P(ulong hostSteamId, int virtualPort) =>
        Steam.GetInstance()
            .Call(Methods.ConnectP2P, hostSteamId, virtualPort, new Godot.Collections.Dictionary())
            .As<uint>();

    private void OnConnectionStatusChanged(long connectionHandle, Godot.Collections.Dictionary details, long oldState)
    {
        var connection = checked((uint)connectionHandle);
        var state = (Steam.NetworkingConnectionState)details["connection_state"].AsInt64();
        var remoteSteamId = details["identity"].AsUInt64();
        var listenSocket = details["listen_socket"].AsUInt32();

        if (state == Steam.NetworkingConnectionState.Connecting && IsAuthority)
        {
            if (listenSocket != _listenSocket)
            {
                return;
            }

            if (_remoteSteamIds.ContainsKey(connection))
            {
                return;
            }

            if (remoteSteamId == 0 || MayAcceptSteamPeer?.Invoke(remoteSteamId) != true)
            {
                Steam.CloseConnection(connection, (int)Steam.NetworkingConnectionEnd.AppGeneric,
                    "Steam user is not a member of this lobby", linger: false);
                StatusChanged?.Invoke($"Rejected Steam peer {remoteSteamId}: not in lobby");
                return;
            }

            if ((ErrorResult)Steam.AcceptConnection(connection) != ErrorResult.Ok ||
                !Steam.SetConnectionPollGroup(connection, _pollGroup))
            {
                Steam.CloseConnection(connection, (int)Steam.NetworkingConnectionEnd.AppGeneric,
                    "Unable to accept connection", linger: false);
                return;
            }

            _remoteSteamIds[connection] = remoteSteamId;
            StatusChanged?.Invoke($"Accepted Steam peer {remoteSteamId}");
        }

        if (state == Steam.NetworkingConnectionState.Connected)
        {
            // Steam's callback is process-global. A client must ignore handles
            // owned by the separate prediction transport.
            if (!_remoteSteamIds.ContainsKey(connection))
            {
                return;
            }

            _remoteSteamIds[connection] = remoteSteamId;
            if (_connectedConnections.Add(connection))
            {
                StatusChanged?.Invoke($"Steam connection {connection} established with {remoteSteamId}");
                ConnectionOpened?.Invoke(new TransportConnectionId(connection));
            }
        }
        else if (state is Steam.NetworkingConnectionState.ClosedByPeer or
                 Steam.NetworkingConnectionState.ProblemDetectedLocally)
        {
            if (!_remoteSteamIds.ContainsKey(connection))
            {
                return;
            }

            Steam.CloseConnection(connection, (int)Steam.NetworkingConnectionEnd.AppGeneric,
                "Connection closed", linger: false);
            _remoteSteamIds.Remove(connection);
            if (_connectedConnections.Remove(connection))
            {
                StatusChanged?.Invoke($"Steam connection {connection} closed");
                ConnectionClosed?.Invoke(new TransportConnectionId(connection));
            }
        }
    }

    public bool TryGetRemoteSteamId(TransportConnectionId connectionId, out ulong steamId) =>
        _remoteSteamIds.TryGetValue(checked((uint)connectionId.Value), out steamId);
}
