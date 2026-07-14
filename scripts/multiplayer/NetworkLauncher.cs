#nullable enable

using BattleArena.Multiplayer.Connection;
using BattleArena.Multiplayer.Protocol;
using BattleArena.Multiplayer.Transport;
using BattleArena.Protocol.V1;
using Godot;

namespace BattleArena.GodotNetworking;

public partial class NetworkLauncher : Control
{
    private const string OfflineScenePath = "res://scenes/vertical_slice/vertical_slice_main.tscn";
    private const int DefaultPort = 7777;

    private GodotEnetTransport _transport = null!;
    private LineEdit _displayName = null!;
    private LineEdit _address = null!;
    private SpinBox _port = null!;
    private Button _offlineButton = null!;
    private Button _hostButton = null!;
    private Button _joinButton = null!;
    private Button _startButton = null!;
    private Label _status = null!;
    private ColorRect _background = null!;
    private CenterContainer _center = null!;
    private Node3D _arenaRoot = null!;
    private AuthorityConnectionService? _authorityService;
    private ClientConnectionService? _clientService;
    private string _requestedDisplayName = "Player";
    private bool _joinRequestSent;
    private bool _quitAfterConnected;
    private bool _autoStart;
    private NetworkArena? _arena;

    public override void _Ready()
    {
        _transport = GetNode<GodotEnetTransport>("EnetTransport");
        _displayName = GetNode<LineEdit>("Center/Panel/Margin/Layout/DisplayName");
        _address = GetNode<LineEdit>("Center/Panel/Margin/Layout/Address");
        _port = GetNode<SpinBox>("Center/Panel/Margin/Layout/Port");
        _offlineButton = GetNode<Button>("Center/Panel/Margin/Layout/Buttons/Offline");
        _hostButton = GetNode<Button>("Center/Panel/Margin/Layout/Buttons/Host");
        _joinButton = GetNode<Button>("Center/Panel/Margin/Layout/Buttons/Join");
        _startButton = GetNode<Button>("Center/Panel/Margin/Layout/StartMatch");
        _status = GetNode<Label>("Center/Panel/Margin/Layout/Status");
        _background = GetNode<ColorRect>("Background");
        _center = GetNode<CenterContainer>("Center");
        _arenaRoot = GetNode<Node3D>("ArenaRoot");

        _offlineButton.Pressed += StartOffline;
        _hostButton.Pressed += Host;
        _joinButton.Pressed += Join;
        _startButton.Pressed += StartHostedMatch;
        _transport.StatusChanged += SetStatus;
        _transport.PeerConnected += OnTransportPeerConnected;

        _port.Value = DefaultPort;
        _startButton.Disabled = true;
        ProcessCommandLine(OS.GetCmdlineUserArgs());
    }

    public override void _ExitTree()
    {
        _authorityService?.Dispose();
        _clientService?.Dispose();
        _transport.StatusChanged -= SetStatus;
        _transport.PeerConnected -= OnTransportPeerConnected;
    }

    private void StartOffline() => GetTree().ChangeSceneToFile(OfflineScenePath);

    private void Host()
    {
        ResetServices();
        if (!TryReadPort(out var port))
        {
            return;
        }

        var error = _transport.Host(port, maximumClients: 8);
        if (error != Error.Ok)
        {
            return;
        }

        _authorityService = new AuthorityConnectionService(
            _transport,
            new ProtobufProtocolCodec(),
            new InboundMessageValidator(),
            new CryptographicSessionCredentialGenerator(),
            new AuthoritySessionConfiguration(60, 30, 60));
        _authorityService.PlayerJoined += OnPlayerJoined;
        _authorityService.ProtocolViolationDetected += OnAuthorityProtocolViolation;
        SetConnectionControlsEnabled(false);
        DisplayServer.WindowSetTitle("Battle Arena — Local Host");
        SetStatus($"Hosting on 127.0.0.1:{port}\nSession {_authorityService.SessionId}\nWaiting for players...");
    }

    private void Join()
    {
        ResetServices();
        if (!TryReadPort(out var port))
        {
            return;
        }

        var address = _address.Text.Trim();
        if (string.IsNullOrWhiteSpace(address))
        {
            SetStatus("An address is required.");
            return;
        }

        _requestedDisplayName = _displayName.Text.Trim();
        if (string.IsNullOrWhiteSpace(_requestedDisplayName))
        {
            SetStatus("A display name is required.");
            return;
        }

        _clientService = new ClientConnectionService(
            _transport,
            new ProtobufProtocolCodec(),
            new InboundMessageValidator(),
            new CryptographicSessionCredentialGenerator());
        _clientService.JoinAccepted += OnJoinAccepted;
        _clientService.MatchStarted += OnRemoteMatchStarted;
        _clientService.ProtocolViolationDetected += violation =>
            SetStatus($"Protocol error: {violation.Message}");
        _clientService.AuthorityTransportDisconnected += () =>
            SetStatus("The host transport disconnected.");

        var error = _transport.Join(address, port);
        if (error != Error.Ok)
        {
            ResetServices();
            return;
        }

        SetConnectionControlsEnabled(false);
        DisplayServer.WindowSetTitle($"Battle Arena — {_requestedDisplayName}");
    }

    private void OnTransportPeerConnected(NetworkPeerId peerId)
    {
        if (_clientService is null || _joinRequestSent)
        {
            return;
        }

        _joinRequestSent = true;
        _clientService.BeginJoin(peerId, _requestedDisplayName);
        SetStatus($"Transport connected to peer {peerId.Value}. Authenticating session...");
    }

    private void OnPlayerJoined(ConnectedPlayer player)
    {
        SetStatus(
            $"{player.DisplayName} joined\n" +
            $"Player {player.PlayerId}, combatant {player.CombatantId}\n" +
            $"Connected remote players: {_authorityService?.ConnectedPlayers.Count ?? 0}");
        _startButton.Disabled = false;
        if (_autoStart)
        {
            StartHostedMatch();
        }

        ScheduleTestQuitIfRequested();
    }

    private void OnJoinAccepted(ClientSessionIdentity identity)
    {
        SetStatus(
            $"Joined session {identity.SessionId}\n" +
            $"Assigned player {identity.PlayerId}, combatant {identity.CombatantId}\n" +
            $"Simulation {identity.SimulationTicksPerSecond} Hz, snapshots {identity.SnapshotRate} Hz");
        ScheduleTestQuitIfRequested();
    }

    private void StartHostedMatch()
    {
        if (_authorityService is null || _authorityService.ConnectedPlayers.Count == 0 || _arena is not null)
        {
            return;
        }

        _startButton.Disabled = true;
        _authorityService.StartMatch(
            "base:vertical_slice_arena",
            _authorityService.SessionId,
            authorityStartTick: 0);
        EnterArenaAsAuthority(authorityStartTick: 0);
    }

    private void OnRemoteMatchStarted(MatchStart matchStart)
    {
        if (_clientService?.Identity is null || _arena is not null)
        {
            return;
        }

        EnterArenaAsClient(matchStart.AuthorityStartTick);
    }

    private void EnterArenaAsAuthority(ulong authorityStartTick)
    {
        var arena = GD.Load<PackedScene>("res://scenes/multiplayer/network_arena.tscn")
            .Instantiate<NetworkArena>();
        arena.InitializeAuthority(_transport, _authorityService!, authorityStartTick);
        EnterArena(arena);
    }

    private void EnterArenaAsClient(ulong authorityStartTick)
    {
        var arena = GD.Load<PackedScene>("res://scenes/multiplayer/network_arena.tscn")
            .Instantiate<NetworkArena>();
        arena.InitializeClient(_transport, _clientService!, authorityStartTick);
        EnterArena(arena);
    }

    private void EnterArena(NetworkArena arena)
    {
        _arena = arena;
        _background.Hide();
        _center.Hide();
        _arenaRoot.AddChild(arena);
        GD.Print("[NetworkLauncher] Synchronized network arena started");
    }

    private void OnAuthorityProtocolViolation(NetworkPeerId peerId, ProtocolViolation violation) =>
        SetStatus($"Peer {peerId.Value} protocol error: {violation.Message}");

    private void ResetServices()
    {
        if (_authorityService is not null)
        {
            _authorityService.PlayerJoined -= OnPlayerJoined;
            _authorityService.ProtocolViolationDetected -= OnAuthorityProtocolViolation;
            _authorityService.Dispose();
            _authorityService = null;
        }

        _clientService?.Dispose();
        _clientService = null;
        _joinRequestSent = false;
        _startButton.Disabled = true;
        _transport.Stop();
        SetConnectionControlsEnabled(true);
    }

    private void SetConnectionControlsEnabled(bool enabled)
    {
        _hostButton.Disabled = !enabled;
        _joinButton.Disabled = !enabled;
        _displayName.Editable = enabled;
        _address.Editable = enabled;
        _port.Editable = enabled;
    }

    private bool TryReadPort(out int port)
    {
        port = Mathf.RoundToInt(_port.Value);
        if (port is >= 1 and <= 65535)
        {
            return true;
        }

        SetStatus("Port must be between 1 and 65535.");
        return false;
    }

    private void SetStatus(string message)
    {
        _status.Text = message;
        GD.Print($"[NetworkLauncher] {message.Replace('\n', ' ')}");
    }

    private void ProcessCommandLine(string[] arguments)
    {
        var hostRequested = false;
        var joinAddress = string.Empty;

        foreach (var argument in arguments)
        {
            if (argument == "--host")
            {
                hostRequested = true;
            }
            else if (argument.StartsWith("--join=", StringComparison.Ordinal))
            {
                joinAddress = argument["--join=".Length..];
            }
            else if (argument.StartsWith("--port=", StringComparison.Ordinal) &&
                     int.TryParse(argument["--port=".Length..], out var port))
            {
                _port.Value = port;
            }
            else if (argument.StartsWith("--name=", StringComparison.Ordinal))
            {
                _displayName.Text = argument["--name=".Length..];
            }
            else if (argument == "--quit-after-connected")
            {
                _quitAfterConnected = true;
            }
            else if (argument == "--auto-start")
            {
                _autoStart = true;
            }
        }

        if (hostRequested)
        {
            Host();
        }
        else if (!string.IsNullOrWhiteSpace(joinAddress))
        {
            _address.Text = joinAddress;
            Join();
        }
    }

    private void ScheduleTestQuitIfRequested()
    {
        if (!_quitAfterConnected)
        {
            return;
        }

        var timer = GetTree().CreateTimer(0.5);
        timer.Timeout += () => GetTree().Quit();
    }
}
