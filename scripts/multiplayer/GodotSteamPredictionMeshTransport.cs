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

    /// <summary>
    /// Virtual-port offset of the movement plane relative to the control plane.
    /// </summary>
    /// <remarks>
    /// Two connections per peer on adjacent ports, split by message class. The
    /// alternative was Steam's lanes, which is the mechanism designed for exactly
    /// this — but the GodotSteam C# binding exposes
    /// <c>ConfigureConnectionLanes</c> without a lane index on <c>SendMessages</c>,
    /// and the lane is assigned per message in Steamworks. Configuring lanes
    /// through that binding would succeed, report success, and change nothing.
    /// Separate connections give genuinely independent reliability streams with the
    /// API as shipped.
    /// </remarks>
    private const int MovementPortOffset = 1;

    private readonly Dictionary<SessionPeerId, RouteRuntime> _routes = [];
    private readonly Dictionary<uint, ConnectionBinding> _routesByConnection = [];
    private uint _listenSocket;
    private uint _movementListenSocket;
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

        if (virtualPort + MovementPortOffset > 65_535)
        {
            return Error.InvalidParameter;
        }

        _listenSocket = CreateListenSocketP2P(virtualPort);
        _movementListenSocket = CreateListenSocketP2P(virtualPort + MovementPortOffset);
        if (_listenSocket == InvalidHandle || _movementListenSocket == InvalidHandle)
        {
            Stop();
            StatusChanged?.Invoke("Steam failed to create the prediction listen sockets.");
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

        var control = ConnectP2P(endpoint.SteamId, endpoint.VirtualPort);
        var movement = ConnectP2P(endpoint.SteamId, endpoint.VirtualPort + MovementPortOffset);
        if (control == InvalidHandle || movement == InvalidHandle ||
            !Steam.SetConnectionPollGroup(control, _pollGroup) ||
            !Steam.SetConnectionPollGroup(movement, _pollGroup))
        {
            // Either plane failing takes the whole route down. A route with only a
            // control plane would accept commands and silently drop movement, which
            // presents as a frozen character rather than as a transport fault.
            if (control != InvalidHandle)
            {
                Close(control, "Unable to start Steam prediction route");
            }

            if (movement != InvalidHandle)
            {
                Close(movement, "Unable to start Steam prediction route");
            }

            _routes.Remove(runtime.RemotePeerId);
            Report(runtime, PredictionTransportRouteState.Failed, "Steam prediction connect failed.");
            return;
        }

        runtime.ControlConnection = control;
        runtime.MovementConnection = movement;
        _routesByConnection[control] = new ConnectionBinding(runtime, ControlPacketKind);
        _routesByConnection[movement] = new ConnectionBinding(runtime, MovementPacketKind);
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
            if (!_routesByConnection.TryGetValue(connection, out var binding) || payload.Length < 2)
            {
                continue;
            }

            // The connection determines the class, because the connection is what
            // actually provides the isolation. The framing byte is kept as a
            // cross-check rather than as the source of truth: a packet arriving on
            // the wrong plane means a routing bug, and silently reclassifying it
            // would hide exactly the defect this split was made to prevent.
            if (payload[0] != binding.PacketKind)
            {
                StatusChanged?.Invoke(
                    $"Steam prediction packet arrived on the wrong plane for peer " +
                    $"{binding.Runtime.RemotePeerId.Value}: framed as {payload[0]} on the " +
                    $"{binding.PacketKind} plane. Dropped.");
                continue;
            }

            var delivery = binding.PacketKind == ControlPacketKind
                ? TransportDelivery.ReliableOrdered
                : TransportDelivery.Unreliable;

            PacketReceived?.Invoke(new InboundPredictionPacket(
                binding.Runtime.RemotePeerId,
                binding.Runtime.RouteGeneration,
                binding.Runtime.AttemptId,
                delivery,
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

        if (_movementListenSocket != InvalidHandle)
        {
            Steam.CloseListenSocket(_movementListenSocket);
            _movementListenSocket = InvalidHandle;
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
            !runtime.Connected)
        {
            return false;
        }

        // The plane is chosen by message class, which is the whole point: a
        // reliable control retransmit cannot delay movement because they are not on
        // the same connection.
        var connection = packetKind == ControlPacketKind
            ? runtime.ControlConnection
            : runtime.MovementConnection;
        if (connection == InvalidHandle)
        {
            return false;
        }

        var framed = new byte[payload.Length + 1];
        framed[0] = packetKind;
        payload.Span.CopyTo(framed.AsSpan(1));
        var result = Steam.SendMessageToConnection(connection, framed, flags);
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
                if (remoteSteamId != 0 && remoteSteamId != initiating.Runtime.Endpoint.SteamId)
                {
                    Reject(
                        initiating.Runtime,
                        connection,
                        "Steam identity did not match authorization.");
                }
                return;
            }

            // Which listen socket accepted the connection tells us which plane it
            // is, so an inbound movement connection cannot be mistaken for a control
            // one.
            byte inboundKind;
            if (listenSocket == _listenSocket)
            {
                inboundKind = ControlPacketKind;
            }
            else if (listenSocket == _movementListenSocket)
            {
                inboundKind = MovementPacketKind;
            }
            else
            {
                return;
            }

            var candidates = _routes.Values.Where(candidate =>
                    candidate.ConnectionFor(inboundKind) == InvalidHandle &&
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

            runtime.SetConnection(inboundKind, connection);
            _routesByConnection[connection] = new ConnectionBinding(runtime, inboundKind);
        }

        if (state == Steam.NetworkingConnectionState.Connected)
        {
            if (!_routesByConnection.TryGetValue(connection, out var binding))
            {
                return;
            }

            var runtime = binding.Runtime;
            if (remoteSteamId != runtime.Endpoint.SteamId ||
                MayAcceptSteamPeer?.Invoke(remoteSteamId) != true)
            {
                Reject(runtime, connection, "Connected Steam identity is not authorized.");
                return;
            }

            runtime.MarkConnected(binding.PacketKind);

            // Reported connected only once BOTH planes are up. A route announced on
            // the first plane would invite sends on the second and drop them
            // silently, which reads as packet loss rather than as a route that is
            // not ready.
            if (runtime.Connected)
            {
                Report(runtime, PredictionTransportRouteState.Connected);
            }
        }
        else if (state is Steam.NetworkingConnectionState.ClosedByPeer or
                 Steam.NetworkingConnectionState.ProblemDetectedLocally)
        {
            if (_routesByConnection.Remove(connection, out var binding))
            {
                // Losing either plane takes the route down. Half a route can carry
                // commands but not movement, or the reverse, and both present as a
                // character that is subtly broken rather than as a disconnection.
                CloseBothPlanes(binding.Runtime, "Steam prediction connection closed");
                Report(binding.Runtime, PredictionTransportRouteState.Disconnected);
            }
        }
    }

    private void Reject(RouteRuntime runtime, uint connection, string detail)
    {
        _routesByConnection.Remove(connection);
        CloseBothPlanes(runtime, detail);
        Report(runtime, PredictionTransportRouteState.Failed, detail);
    }

    /// <summary>
    /// Closes both planes of a route and forgets their bindings.
    /// </summary>
    /// <remarks>
    /// One place rather than repeated at each teardown site, because leaving one
    /// plane open leaks a Steam connection and leaves a stale binding that a later
    /// packet could resolve against a route that is otherwise gone.
    /// </remarks>
    private void CloseBothPlanes(RouteRuntime runtime, string detail)
    {
        foreach (var kind in new[] { ControlPacketKind, MovementPacketKind })
        {
            var connection = runtime.ConnectionFor(kind);
            if (connection == InvalidHandle)
            {
                continue;
            }

            _routesByConnection.Remove(connection);
            Close(connection, detail);
            runtime.SetConnection(kind, InvalidHandle);
        }

        runtime.ResetConnected();
    }

    private void Remove(SessionPeerId peer, bool report = true)
    {
        if (!_routes.Remove(peer, out var runtime))
        {
            return;
        }

        CloseBothPlanes(runtime, "Prediction route removed");
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

    /// <summary>Which route and which plane an inbound connection belongs to.</summary>
    /// <remarks>
    /// The plane is carried alongside the route rather than derived from the
    /// payload, so a packet's class comes from the connection that actually
    /// provides its isolation guarantee.
    /// </remarks>
    private readonly record struct ConnectionBinding(RouteRuntime Runtime, byte PacketKind);

    private sealed class RouteRuntime(
        SessionPeerId remotePeerId,
        uint routeGeneration,
        ulong attemptId,
        SteamPredictionEndpoint endpoint)
    {
        private bool _controlConnected;
        private bool _movementConnected;

        public SessionPeerId RemotePeerId { get; } = remotePeerId;
        public uint RouteGeneration { get; } = routeGeneration;
        public ulong AttemptId { get; } = attemptId;
        public SteamPredictionEndpoint Endpoint { get; } = endpoint;

        public uint ControlConnection { get; set; }
        public uint MovementConnection { get; set; }

        /// <summary>Usable only when both planes are up.</summary>
        /// <remarks>
        /// A route with one plane can carry commands but not movement, or the
        /// reverse. Both look like a subtly broken character rather than a transport
        /// problem, so neither counts as connected.
        /// </remarks>
        public bool Connected => _controlConnected && _movementConnected;

        public uint ConnectionFor(byte packetKind) =>
            packetKind == ControlPacketKind ? ControlConnection : MovementConnection;

        public void SetConnection(byte packetKind, uint connection)
        {
            if (packetKind == ControlPacketKind)
            {
                ControlConnection = connection;
            }
            else
            {
                MovementConnection = connection;
            }
        }

        public void MarkConnected(byte packetKind)
        {
            if (packetKind == ControlPacketKind)
            {
                _controlConnected = true;
            }
            else
            {
                _movementConnected = true;
            }
        }

        public void ResetConnected()
        {
            _controlConnected = false;
            _movementConnected = false;
        }
    }
}
