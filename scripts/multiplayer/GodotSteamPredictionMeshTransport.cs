#nullable enable

using BattleArena.Multiplayer.Connection;
using BattleArena.Multiplayer.Prediction;
using BattleArena.Multiplayer.Transport;
using BattleArena.Protocol.V1;
using Godot;
using GodotSteam;
using Steam = GodotSteam.Steam;

namespace BattleArena.GodotNetworking;

/// <summary>
/// Optional Steam Networking Sockets prediction plane. It owns a separate
/// virtual port and poll group from the mandatory authority transport.
/// </summary>
public partial class GodotSteamPredictionMeshTransport : Node, IPredictionMeshTransport
{
    private const uint InvalidHandle = 0;
    private const int MaximumMessagesPerPoll = 256;
    private const byte MovementPacketKind = 0;
    private const byte ControlPacketKind = 1;
    private readonly Dictionary<SessionPeerId, RouteRuntime> _routes = [];
    private readonly Dictionary<uint, RouteRuntime> _routesByConnection = [];
    private uint _listenSocket;
    private long _pollGroup;
    private PredictionRouteDescriptor? _localDescriptor;
    private bool _subscribed;

    public event Action<PredictionTransportRouteChanged>? RouteChanged;
    public event Action<InboundPredictionPacket>? PacketReceived;
    public event Action<string>? StatusChanged;

    public TransportKind Kind => TransportKind.Steam;
    public bool SupportsDirectRoutes => true;
    public Func<ulong, bool>? MayAcceptSteamPeer { get; set; }
    public PredictionRouteDescriptor LocalRouteDescriptor =>
        _localDescriptor?.Clone() ??
        throw new InvalidOperationException("The Steam prediction endpoint has not started.");

    public Error Start(ulong localSteamId, int virtualPort)
    {
        if (localSteamId == 0 || virtualPort is < 0 or > 65_535)
        {
            return Error.InvalidParameter;
        }

        Stop();
        EnsureSubscribed();
        _pollGroup = Steam.CreatePollGroup();
        if (_pollGroup == 0)
        {
            StatusChanged?.Invoke("Steam failed to create the prediction poll group.");
            return Error.CantCreate;
        }

        _listenSocket = CreateListenSocketP2P(virtualPort);
        if (_listenSocket == InvalidHandle)
        {
            Stop();
            StatusChanged?.Invoke("Steam failed to create the prediction listen socket.");
            return Error.CantCreate;
        }

        _localDescriptor = SteamPredictionRouteDescriptorCodec.Encode(
            new SteamPredictionEndpoint(localSteamId, virtualPort));
        StatusChanged?.Invoke(
            $"Steam prediction endpoint {localSteamId} is listening on virtual port {virtualPort}");
        return Error.Ok;
    }

    public void Apply(PredictionMeshDirective directive)
    {
        ArgumentNullException.ThrowIfNull(directive);
        if (directive.Kind == PredictionMeshDirectiveKind.Remove)
        {
            Remove(directive.RemotePeerId);
            return;
        }

        if (_pollGroup == 0 || directive.Route is null || directive.AttemptId == 0 ||
            !SteamPredictionRouteDescriptorCodec.TryDecode(
                directive.Route.RemoteDescriptor,
                out var endpoint))
        {
            Report(
                directive.RemotePeerId,
                directive.Route?.RouteGeneration ?? 0,
                directive.AttemptId,
                PredictionTransportRouteState.Failed,
                "Steam prediction route is unavailable or malformed.");
            return;
        }

        Remove(directive.RemotePeerId, report: false);
        var runtime = new RouteRuntime(
            directive.RemotePeerId,
            directive.Route.RouteGeneration,
            directive.AttemptId,
            endpoint);
        _routes.Add(runtime.RemotePeerId, runtime);
        if (directive.Kind == PredictionMeshDirectiveKind.Listen)
        {
            Report(runtime, PredictionTransportRouteState.Listening);
            return;
        }

        var connection = ConnectP2P(endpoint.SteamId, endpoint.VirtualPort);
        if (connection == InvalidHandle || !Steam.SetConnectionPollGroup(connection, _pollGroup))
        {
            if (connection != InvalidHandle)
            {
                Close(connection, "Unable to start Steam prediction route");
            }

            _routes.Remove(runtime.RemotePeerId);
            Report(runtime, PredictionTransportRouteState.Failed, "Steam prediction connect failed.");
            return;
        }

        runtime.Connection = connection;
        _routesByConnection[connection] = runtime;
        Report(runtime, PredictionTransportRouteState.Connecting);
    }

    public bool TrySendControl(
        SessionPeerId recipient,
        uint routeGeneration,
        ulong attemptId,
        ReadOnlyMemory<byte> payload) =>
        TrySend(recipient, routeGeneration, attemptId, payload, ControlPacketKind,
            Steam.NetworkingSendReliable | Steam.NetworkingSendNoNagle);

    public bool TrySendMovement(
        SessionPeerId recipient,
        uint routeGeneration,
        ulong attemptId,
        ReadOnlyMemory<byte> payload) =>
        TrySend(recipient, routeGeneration, attemptId, payload, MovementPacketKind,
            Steam.NetworkingSendNoDelay);

    public override void _Process(double delta)
    {
        if (_pollGroup == 0)
        {
            return;
        }

        Steam.RunNetworkingCallbacks();
        var messages = Steam.ReceiveMessagesOnPollGroup(checked((uint)_pollGroup), MaximumMessagesPerPoll);
        foreach (var value in messages)
        {
            var message = value.AsGodotDictionary();
            var connection = message["connection"].AsUInt32();
            var payload = message["payload"].AsByteArray();
            if (!_routesByConnection.TryGetValue(connection, out var runtime) || payload.Length < 2)
            {
                continue;
            }

            var delivery = payload[0] switch
            {
                ControlPacketKind => TransportDelivery.ReliableOrdered,
                MovementPacketKind => TransportDelivery.Unreliable,
                _ => (TransportDelivery?)null,
            };
            if (delivery is null)
            {
                continue;
            }

            PacketReceived?.Invoke(new InboundPredictionPacket(
                runtime.RemotePeerId,
                runtime.RouteGeneration,
                runtime.AttemptId,
                delivery.Value,
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
        foreach (var connection in _routesByConnection.Keys.ToArray())
        {
            Close(connection, "Battle Arena prediction transport stopped");
        }

        _routes.Clear();
        _routesByConnection.Clear();
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

        _localDescriptor = null;
    }

    private bool TrySend(
        SessionPeerId recipient,
        uint routeGeneration,
        ulong attemptId,
        ReadOnlyMemory<byte> payload,
        byte packetKind,
        long flags)
    {
        if (!_routes.TryGetValue(recipient, out var runtime) ||
            runtime.RouteGeneration != routeGeneration || runtime.AttemptId != attemptId ||
            runtime.Connection == InvalidHandle || !runtime.Connected)
        {
            return false;
        }

        var framed = new byte[payload.Length + 1];
        framed[0] = packetKind;
        payload.Span.CopyTo(framed.AsSpan(1));
        var result = Steam.SendMessageToConnection(runtime.Connection, framed, flags);
        if ((ErrorResult)result["result"].AsInt32() == ErrorResult.Ok)
        {
            return true;
        }

        Report(runtime, PredictionTransportRouteState.Failed, "Steam prediction send failed.");
        return false;
    }

    private void OnConnectionStatusChanged(
        long connectionHandle,
        Godot.Collections.Dictionary details,
        long oldState)
    {
        var connection = checked((uint)connectionHandle);
        var state = (Steam.NetworkingConnectionState)details["connection_state"].AsInt64();
        var remoteSteamId = details["identity"].AsUInt64();
        var listenSocket = details["listen_socket"].AsUInt32();

        if (state == Steam.NetworkingConnectionState.Connecting)
        {
            if (_routesByConnection.TryGetValue(connection, out var initiating))
            {
                if (remoteSteamId != 0 && remoteSteamId != initiating.Endpoint.SteamId)
                {
                    Reject(initiating, connection, "Steam identity did not match authorization.");
                }
                return;
            }

            if (listenSocket != _listenSocket)
            {
                return;
            }

            var candidates = _routes.Values.Where(candidate =>
                    candidate.Connection == InvalidHandle &&
                    candidate.Endpoint.SteamId == remoteSteamId)
                .Take(2)
                .ToArray();
            if (remoteSteamId == 0 || candidates.Length != 1 ||
                MayAcceptSteamPeer?.Invoke(remoteSteamId) != true)
            {
                Close(connection, "No unique authority-approved Steam prediction route");
                return;
            }

            var runtime = candidates[0];
            if ((ErrorResult)Steam.AcceptConnection(connection) != ErrorResult.Ok ||
                !Steam.SetConnectionPollGroup(connection, _pollGroup))
            {
                Reject(runtime, connection, "Unable to accept Steam prediction connection.");
                return;
            }

            runtime.Connection = connection;
            _routesByConnection[connection] = runtime;
        }

        if (state == Steam.NetworkingConnectionState.Connected)
        {
            if (!_routesByConnection.TryGetValue(connection, out var runtime))
            {
                return;
            }

            if (remoteSteamId != runtime.Endpoint.SteamId ||
                MayAcceptSteamPeer?.Invoke(remoteSteamId) != true)
            {
                Reject(runtime, connection, "Connected Steam identity is not authorized.");
                return;
            }

            runtime.Connected = true;
            Report(runtime, PredictionTransportRouteState.Connected);
        }
        else if (state is Steam.NetworkingConnectionState.ClosedByPeer or
                 Steam.NetworkingConnectionState.ProblemDetectedLocally)
        {
            if (_routesByConnection.Remove(connection, out var runtime))
            {
                runtime.Connection = InvalidHandle;
                runtime.Connected = false;
                Close(connection, "Steam prediction connection closed");
                Report(runtime, PredictionTransportRouteState.Disconnected);
            }
        }
    }

    private void Reject(RouteRuntime runtime, uint connection, string detail)
    {
        _routesByConnection.Remove(connection);
        runtime.Connection = InvalidHandle;
        runtime.Connected = false;
        Close(connection, detail);
        Report(runtime, PredictionTransportRouteState.Failed, detail);
    }

    private void Remove(SessionPeerId peer, bool report = true)
    {
        if (!_routes.Remove(peer, out var runtime))
        {
            return;
        }

        if (runtime.Connection != InvalidHandle)
        {
            _routesByConnection.Remove(runtime.Connection);
            Close(runtime.Connection, "Prediction route removed");
            runtime.Connection = InvalidHandle;
        }

        runtime.Connected = false;
        if (report)
        {
            Report(runtime, PredictionTransportRouteState.Disconnected, "Route removed.");
        }
    }

    private void EnsureSubscribed()
    {
        if (_subscribed)
        {
            return;
        }

        Steam.NetworkConnectionStatusChanged += OnConnectionStatusChanged;
        _subscribed = true;
    }

    private static uint CreateListenSocketP2P(int virtualPort) =>
        Steam.GetInstance()
            .Call(Methods.CreateListenSocketP2P, virtualPort, new Godot.Collections.Dictionary())
            .As<uint>();

    private static uint ConnectP2P(ulong steamId, int virtualPort) =>
        Steam.GetInstance()
            .Call(Methods.ConnectP2P, steamId, virtualPort, new Godot.Collections.Dictionary())
            .As<uint>();

    private static void Close(uint connection, string detail) =>
        Steam.CloseConnection(
            connection,
            (int)Steam.NetworkingConnectionEnd.AppGeneric,
            detail,
            linger: false);

    private void Report(
        RouteRuntime runtime,
        PredictionTransportRouteState state,
        string? detail = null) =>
        Report(runtime.RemotePeerId, runtime.RouteGeneration, runtime.AttemptId, state, detail);

    private void Report(
        SessionPeerId peer,
        uint routeGeneration,
        ulong attemptId,
        PredictionTransportRouteState state,
        string? detail = null)
    {
        StatusChanged?.Invoke(
            $"Steam prediction peer {peer.Value}: {state}" +
            (string.IsNullOrWhiteSpace(detail) ? string.Empty : $" ({detail})"));
        RouteChanged?.Invoke(new PredictionTransportRouteChanged(
            peer, routeGeneration, attemptId, state, detail));
    }

    private sealed class RouteRuntime(
        SessionPeerId remotePeerId,
        uint routeGeneration,
        ulong attemptId,
        SteamPredictionEndpoint endpoint)
    {
        public SessionPeerId RemotePeerId { get; } = remotePeerId;
        public uint RouteGeneration { get; } = routeGeneration;
        public ulong AttemptId { get; } = attemptId;
        public SteamPredictionEndpoint Endpoint { get; } = endpoint;
        public uint Connection { get; set; }
        public bool Connected { get; set; }
    }
}
