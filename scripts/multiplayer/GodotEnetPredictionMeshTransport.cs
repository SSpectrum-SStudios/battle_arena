#nullable enable

using BattleArena.Multiplayer.Connection;
using BattleArena.Multiplayer.Prediction;
using BattleArena.Multiplayer.Transport;
using BattleArena.Protocol.V1;
using Godot;

namespace BattleArena.GodotNetworking;

/// <summary>
/// Optional low-level ENet host dedicated to client-to-client prediction. It is
/// deliberately separate from GodotEnetTransport, so direct route failure can
/// never close or replace the mandatory authority connection.
/// </summary>
public partial class GodotEnetPredictionMeshTransport : Node, IPredictionMeshTransport
{
    private const int MaximumPeers = 7;
    private const int ChannelCount = 2;
    private const int MovementChannel = 0;
    private const int ControlChannel = 1;
    private ENetConnection? _host;
    private PredictionRouteDescriptor? _localDescriptor;
    private readonly Dictionary<SessionPeerId, RouteRuntime> _routes = [];
    private readonly Dictionary<ENetPacketPeer, RouteRuntime> _routesByConnection = [];
    private bool _forcedFailureConsumed;

    public event Action<PredictionTransportRouteChanged>? RouteChanged;
    public event Action<InboundPredictionPacket>? PacketReceived;
    public event Action<string>? StatusChanged;

    public TransportKind Kind => TransportKind.Enet;
    public bool SupportsDirectRoutes => true;
    public bool ForceNextConnectedRouteFailure { get; set; }
    public PredictionRouteDescriptor LocalRouteDescriptor =>
        _localDescriptor?.Clone() ??
        throw new InvalidOperationException("The ENet prediction endpoint has not started.");

    public Error Start(string bindAddress, string advertisedAddress, int port)
    {
        if (port is < 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        Stop();
        _forcedFailureConsumed = false;
        var host = new ENetConnection();
        var error = host.CreateHostBound(
            bindAddress,
            port,
            MaximumPeers,
            ChannelCount,
            0,
            0);
        if (error != Error.Ok)
        {
            host.Dispose();
            StatusChanged?.Invoke($"Unable to bind ENet prediction port {port}: {error}");
            return error;
        }

        var boundPort = host.GetLocalPort();
        if (boundPort is <= 0 or > ushort.MaxValue)
        {
            host.Destroy();
            host.Dispose();
            StatusChanged?.Invoke("ENet did not report a usable prediction port.");
            return Error.CantCreate;
        }

        _host = host;
        _localDescriptor = EnetPredictionRouteDescriptorCodec.Encode(
            new EnetPredictionEndpoint(advertisedAddress, checked((ushort)boundPort)));
        StatusChanged?.Invoke($"ENet prediction endpoint listening on {advertisedAddress}:{boundPort}");
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

        if (_host is null || directive.Route is null || directive.AttemptId == 0 ||
            !EnetPredictionRouteDescriptorCodec.TryDecode(
                directive.Route.RemoteDescriptor,
                out var endpoint))
        {
            Report(
                directive.RemotePeerId,
                directive.Route?.RouteGeneration ?? 0,
                directive.AttemptId,
                PredictionTransportRouteState.Failed,
                "ENet prediction route is unavailable or malformed.");
            return;
        }

        Remove(directive.RemotePeerId, report: false);
        var runtime = new RouteRuntime(
            directive.RemotePeerId,
            directive.Route.RouteGeneration,
            directive.AttemptId,
            endpoint);
        _routes.Add(directive.RemotePeerId, runtime);

        if (directive.Kind == PredictionMeshDirectiveKind.Listen)
        {
            Report(runtime, PredictionTransportRouteState.Listening);
            return;
        }

        var peer = _host.ConnectToHost(endpoint.Address, endpoint.Port, ChannelCount, 0);
        if (peer is null)
        {
            _routes.Remove(runtime.RemotePeerId);
            Report(runtime, PredictionTransportRouteState.Failed, "ENet connect request failed.");
            return;
        }

        runtime.Connection = peer;
        _routesByConnection[peer] = runtime;
        Report(runtime, PredictionTransportRouteState.Connecting);
    }

    public bool TrySendControl(
        SessionPeerId recipient,
        uint routeGeneration,
        ulong attemptId,
        ReadOnlyMemory<byte> payload)
    {
        if (!TryResolveConnected(recipient, routeGeneration, attemptId, out var runtime))
        {
            return false;
        }

        var error = runtime.Connection!.Send(
            ControlChannel,
            payload.Span,
            checked((int)ENetPacketPeer.FlagReliable));
        if (error != Error.Ok)
        {
            Report(runtime, PredictionTransportRouteState.Failed, $"ENet control send failed: {error}");
            return false;
        }

        return true;
    }

    public bool TrySendMovement(
        SessionPeerId recipient,
        uint routeGeneration,
        ulong attemptId,
        ReadOnlyMemory<byte> payload)
    {
        if (!TryResolveConnected(recipient, routeGeneration, attemptId, out var runtime))
        {
            return false;
        }

        var error = runtime.Connection!.Send(MovementChannel, payload.Span, 0);
        if (error == Error.Ok)
        {
            return true;
        }

        Report(runtime, PredictionTransportRouteState.Failed, $"ENet movement send failed: {error}");
        return false;
    }

    public override void _Process(double delta)
    {
        if (_host is null)
        {
            return;
        }

        while (true)
        {
            var networkEvent = _host.Service(0);
            if (networkEvent.Count < 4)
            {
                return;
            }

            var eventType = (ENetConnection.EventType)networkEvent[0].AsInt32();
            if (eventType == ENetConnection.EventType.None)
            {
                return;
            }

            var peer = networkEvent[1].AsGodotObject() as ENetPacketPeer;
            if (eventType == ENetConnection.EventType.Error)
            {
                foreach (var route in _routes.Values.ToArray())
                {
                    Report(route, PredictionTransportRouteState.Failed, "ENet prediction host error.");
                }
                return;
            }

            if (peer is null)
            {
                continue;
            }

            switch (eventType)
            {
                case ENetConnection.EventType.Connect:
                    HandleConnected(peer);
                    break;
                case ENetConnection.EventType.Disconnect:
                    HandleDisconnected(peer);
                    break;
                case ENetConnection.EventType.Receive:
                    HandlePacket(peer, networkEvent[3].AsInt32());
                    break;
            }
        }
    }

    public override void _ExitTree() => Stop();

    public void Stop()
    {
        if (_host is null)
        {
            return;
        }

        foreach (var runtime in _routes.Values)
        {
            runtime.Connection?.PeerDisconnectNow(0);
        }

        _host.Destroy();
        _host.Dispose();
        _host = null;
        _localDescriptor = null;
        _routes.Clear();
        _routesByConnection.Clear();
        StatusChanged?.Invoke("ENet prediction transport stopped");
    }

    private void HandleConnected(ENetPacketPeer peer)
    {
        if (!_routesByConnection.TryGetValue(peer, out var runtime))
        {
            var candidates = _routes.Values.Where(candidate =>
                    candidate.Connection is null &&
                    EnetPredictionRouteDescriptorCodec.EndpointMatches(
                        candidate.Endpoint,
                        peer.GetRemoteAddress(),
                        peer.GetRemotePort()))
                .Take(2)
                .ToArray();
            if (candidates.Length != 1)
            {
                peer.PeerDisconnectNow(0);
                StatusChanged?.Invoke(
                    candidates.Length == 0
                        ? "Rejected an ENet prediction connection with no authorized route hint."
                        : "Rejected an ambiguous ENet prediction connection hint.");
                return;
            }

            runtime = candidates[0];

            runtime.Connection = peer;
            _routesByConnection[peer] = runtime;
        }

        if (ForceNextConnectedRouteFailure && !_forcedFailureConsumed)
        {
            _forcedFailureConsumed = true;
            _routesByConnection.Remove(peer);
            runtime.Connection = null;
            peer.PeerDisconnectNow(0);
            Report(
                runtime,
                PredictionTransportRouteState.Failed,
                "Forced one-shot direct-route failure for fallback verification.");
            return;
        }

        Report(runtime, PredictionTransportRouteState.Connected);
    }

    private void HandleDisconnected(ENetPacketPeer peer)
    {
        if (!_routesByConnection.Remove(peer, out var runtime))
        {
            return;
        }

        runtime.Connection = null;
        Report(runtime, PredictionTransportRouteState.Disconnected);
    }

    private void HandlePacket(ENetPacketPeer peer, int channel)
    {
        if (!_routesByConnection.TryGetValue(peer, out var runtime) ||
            channel is not MovementChannel and not ControlChannel)
        {
            peer.GetPacket();
            return;
        }

        var payload = peer.GetPacket();
        if (peer.GetPacketError() != Error.Ok || payload.Length == 0)
        {
            return;
        }

        PacketReceived?.Invoke(new InboundPredictionPacket(
            runtime.RemotePeerId,
            runtime.RouteGeneration,
            runtime.AttemptId,
            channel == ControlChannel
                ? TransportDelivery.ReliableOrdered
                : TransportDelivery.Unreliable,
            payload));
    }

    private void Remove(SessionPeerId peer, bool report = true)
    {
        if (!_routes.Remove(peer, out var runtime))
        {
            return;
        }

        if (runtime.Connection is not null)
        {
            _routesByConnection.Remove(runtime.Connection);
            runtime.Connection.PeerDisconnectNow(0);
        }

        if (report)
        {
            Report(runtime, PredictionTransportRouteState.Disconnected, "Route removed.");
        }
    }

    private bool TryResolveConnected(
        SessionPeerId peer,
        uint routeGeneration,
        ulong attemptId,
        out RouteRuntime runtime)
    {
        if (_routes.TryGetValue(peer, out runtime!) &&
            runtime.RouteGeneration == routeGeneration &&
            runtime.AttemptId == attemptId &&
            runtime.Connection?.GetState() == ENetPacketPeer.PeerState.Connected)
        {
            return true;
        }

        runtime = null!;
        return false;
    }

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
            $"ENet prediction peer {peer.Value}: {state}" +
            (string.IsNullOrWhiteSpace(detail) ? string.Empty : $" ({detail})"));
        RouteChanged?.Invoke(new PredictionTransportRouteChanged(
            peer,
            routeGeneration,
            attemptId,
            state,
            detail));
    }

    private sealed class RouteRuntime(
        SessionPeerId remotePeerId,
        uint routeGeneration,
        ulong attemptId,
        EnetPredictionEndpoint endpoint)
    {
        public SessionPeerId RemotePeerId { get; } = remotePeerId;
        public uint RouteGeneration { get; } = routeGeneration;
        public ulong AttemptId { get; } = attemptId;
        public EnetPredictionEndpoint Endpoint { get; } = endpoint;
        public ENetPacketPeer? Connection { get; set; }
    }
}
