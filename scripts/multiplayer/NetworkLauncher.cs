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

    private GodotEnetTransport _enetTransport = null!;
    private GodotSteamSocketsTransport _steamTransport = null!;
    private SteamRuntimeAdapter _steamRuntime = null!;
    private SteamLobbyService? _steamLobby;
    private INetworkTransport? _activeTransport;
    private LineEdit _displayName = null!;
    private LineEdit _address = null!;
    private SpinBox _port = null!;
    private LineEdit _lobbyId = null!;
    private Button _offlineButton = null!;
    private Button _hostButton = null!;
    private Button _joinButton = null!;
    private Button _steamHostButton = null!;
    private Button _steamJoinButton = null!;
    private Button _inviteButton = null!;
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
        _enetTransport = GetNode<GodotEnetTransport>("EnetTransport");
        _steamTransport = GetNode<GodotSteamSocketsTransport>("SteamTransport");
        _steamRuntime = GetNode<SteamRuntimeAdapter>("SteamRuntime");
        _displayName = GetNode<LineEdit>("Center/Panel/Margin/Layout/DisplayName");
        _address = GetNode<LineEdit>("Center/Panel/Margin/Layout/Address");
        _port = GetNode<SpinBox>("Center/Panel/Margin/Layout/Port");
        _lobbyId = GetNode<LineEdit>("Center/Panel/Margin/Layout/SteamLobbyId");
        _offlineButton = GetNode<Button>("Center/Panel/Margin/Layout/Buttons/Offline");
        _hostButton = GetNode<Button>("Center/Panel/Margin/Layout/Buttons/Host");
        _joinButton = GetNode<Button>("Center/Panel/Margin/Layout/Buttons/Join");
        _steamHostButton = GetNode<Button>("Center/Panel/Margin/Layout/SteamButtons/SteamHost");
        _steamJoinButton = GetNode<Button>("Center/Panel/Margin/Layout/SteamButtons/SteamJoin");
        _inviteButton = GetNode<Button>("Center/Panel/Margin/Layout/SteamButtons/Invite");
        _startButton = GetNode<Button>("Center/Panel/Margin/Layout/StartMatch");
        _status = GetNode<Label>("Center/Panel/Margin/Layout/Status");
        _background = GetNode<ColorRect>("Background");
        _center = GetNode<CenterContainer>("Center");
        _arenaRoot = GetNode<Node3D>("ArenaRoot");

        _offlineButton.Pressed += StartOffline;
        _hostButton.Pressed += Host;
        _joinButton.Pressed += Join;
        _steamHostButton.Pressed += HostSteam;
        _steamJoinButton.Pressed += JoinSteam;
        _inviteButton.Pressed += () => _steamLobby?.OpenInviteOverlay();
        _startButton.Pressed += StartHostedMatch;
        _enetTransport.StatusChanged += SetStatus;
        _steamTransport.StatusChanged += SetStatus;
        _steamRuntime.StatusChanged += SetStatus;

        _port.Value = DefaultPort;
        _startButton.Disabled = true;
        _inviteButton.Disabled = true;
        ProcessCommandLine(OS.GetCmdlineArgs()
            .Concat(OS.GetCmdlineUserArgs())
            .Distinct(StringComparer.Ordinal)
            .ToArray());
    }

    public override void _ExitTree()
    {
        ResetServices();
        _steamLobby?.Dispose();
        _steamLobby = null;
        _enetTransport.StatusChanged -= SetStatus;
        _steamTransport.StatusChanged -= SetStatus;
        _steamRuntime.StatusChanged -= SetStatus;
    }

    private void StartOffline() => GetTree().ChangeSceneToFile(OfflineScenePath);

    private void Host()
    {
        ResetServices();
        if (!TryReadPort(out var port))
        {
            return;
        }

        if (_enetTransport.Host(port, maximumClients: 8) != Error.Ok)
        {
            return;
        }

        SetActiveTransport(_enetTransport);
        CreateAuthorityService();
        SetConnectionControlsEnabled(false);
        DisplayServer.WindowSetTitle("Battle Arena — Local Host");
        SetStatus($"Hosting on 127.0.0.1:{port}\nSession {_authorityService!.SessionId}\nWaiting for players...");
    }

    private void Join()
    {
        ResetServices();
        if (!TryReadPort(out var port))
        {
            return;
        }

        var address = _address.Text.Trim();
        _requestedDisplayName = _displayName.Text.Trim();
        if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(_requestedDisplayName))
        {
            SetStatus("An address and display name are required.");
            return;
        }

        SetActiveTransport(_enetTransport);
        CreateClientService();
        if (_enetTransport.Join(address, port) != Error.Ok)
        {
            ResetServices();
            return;
        }

        SetConnectionControlsEnabled(false);
        DisplayServer.WindowSetTitle($"Battle Arena — {_requestedDisplayName}");
    }

    private void HostSteam()
    {
        ResetServices();
        if (!EnsureSteamReady())
        {
            return;
        }

        _steamLobby!.Host();
        SetConnectionControlsEnabled(false);
    }

    private void JoinSteam()
    {
        ResetServices();
        if (!EnsureSteamReady())
        {
            return;
        }

        if (!ulong.TryParse(_lobbyId.Text.Trim(), out var lobbyId) || lobbyId == 0)
        {
            SetStatus("Enter a valid numeric Steam lobby ID.");
            return;
        }

        _requestedDisplayName = ReadSteamDisplayName();
        _steamLobby!.Join(lobbyId);
        SetConnectionControlsEnabled(false);
    }

    private bool EnsureSteamReady()
    {
        if (!_steamRuntime.Initialize())
        {
            return false;
        }

        if (_steamLobby is null)
        {
            _steamLobby = new SteamLobbyService(_steamRuntime);
            _steamLobby.StatusChanged += SetStatus;
            _steamLobby.HostLobbyCreated += OnSteamHostLobbyCreated;
            _steamLobby.ClientLobbyJoined += OnSteamClientLobbyJoined;
            _steamLobby.OperationFailed += ResetServices;
        }

        if (_displayName.Text == "Player")
        {
            _displayName.Text = _steamRuntime.PersonaName;
        }

        return true;
    }

    private void OnSteamHostLobbyCreated(ulong lobbyId)
    {
        _lobbyId.Text = lobbyId.ToString();
        _steamTransport.MayAcceptSteamPeer = _steamLobby!.IsMember;
        if (_steamTransport.Host() != Error.Ok)
        {
            ResetServices();
            return;
        }

        SetActiveTransport(_steamTransport);
        CreateAuthorityService();
        _inviteButton.Disabled = false;
        DisplayServer.WindowSetTitle("Battle Arena — Steam Host");
        SetStatus($"Steam lobby {lobbyId}\nSession {_authorityService!.SessionId}\nInvite friends or share the lobby ID.");
    }

    private void OnSteamClientLobbyJoined(ulong lobbyId, ulong ownerSteamId)
    {
        SetActiveTransport(_steamTransport);
        CreateClientService();
        if (_steamTransport.Join(ownerSteamId) != Error.Ok)
        {
            ResetServices();
            return;
        }

        DisplayServer.WindowSetTitle($"Battle Arena — {_requestedDisplayName}");
    }

    private void CreateAuthorityService()
    {
        _authorityService = new AuthorityConnectionService(
            _activeTransport!, new ProtobufProtocolCodec(), new InboundMessageValidator(),
            new CryptographicSessionCredentialGenerator(), new AuthoritySessionConfiguration(60, 30, 60));
        _authorityService.PlayerJoined += OnPlayerJoined;
        _authorityService.ProtocolViolationDetected += OnAuthorityProtocolViolation;
    }

    private void CreateClientService()
    {
        _clientService = new ClientConnectionService(
            _activeTransport!, new ProtobufProtocolCodec(), new InboundMessageValidator(),
            new CryptographicSessionCredentialGenerator());
        _clientService.JoinAccepted += OnJoinAccepted;
        _clientService.MatchStarted += OnRemoteMatchStarted;
        _clientService.ProtocolViolationDetected += violation => SetStatus($"Protocol error: {violation.Message}");
        _clientService.AuthorityTransportDisconnected += () => SetStatus("The host transport disconnected.");
    }

    private void SetActiveTransport(INetworkTransport? transport)
    {
        if (_activeTransport is not null)
        {
            _activeTransport.PeerConnected -= OnTransportPeerConnected;
        }

        _activeTransport = transport;
        if (_activeTransport is not null)
        {
            _activeTransport.PeerConnected += OnTransportPeerConnected;
        }
    }

    private string ReadSteamDisplayName()
    {
        var displayName = _displayName.Text.Trim();
        return string.IsNullOrWhiteSpace(displayName) ? _steamRuntime.PersonaName : displayName;
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
        SetStatus($"{player.DisplayName} joined\nPlayer {player.PlayerId}, combatant {player.CombatantId}\n" +
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
        SetStatus($"Joined session {identity.SessionId}\nAssigned player {identity.PlayerId}, combatant {identity.CombatantId}\n" +
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
        _authorityService.StartMatch("base:vertical_slice_arena", _authorityService.SessionId, authorityStartTick: 0);
        EnterArenaAsAuthority(authorityStartTick: 0);
    }

    private void OnRemoteMatchStarted(MatchStart matchStart)
    {
        if (_clientService?.Identity is not null && _arena is null)
        {
            EnterArenaAsClient(matchStart.AuthorityStartTick);
        }
    }

    private void EnterArenaAsAuthority(ulong authorityStartTick)
    {
        var arena = GD.Load<PackedScene>("res://scenes/multiplayer/network_arena.tscn").Instantiate<NetworkArena>();
        arena.InitializeAuthority(_activeTransport!, _authorityService!, authorityStartTick);
        EnterArena(arena);
    }

    private void EnterArenaAsClient(ulong authorityStartTick)
    {
        var arena = GD.Load<PackedScene>("res://scenes/multiplayer/network_arena.tscn").Instantiate<NetworkArena>();
        arena.InitializeClient(_activeTransport!, _clientService!, authorityStartTick);
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
        SetActiveTransport(null);
        _enetTransport.Stop();
        _steamTransport.Stop();
        _steamLobby?.Leave();
        _inviteButton.Disabled = true;
        SetConnectionControlsEnabled(true);
    }

    private void SetConnectionControlsEnabled(bool enabled)
    {
        _hostButton.Disabled = !enabled;
        _joinButton.Disabled = !enabled;
        _steamHostButton.Disabled = !enabled;
        _steamJoinButton.Disabled = !enabled;
        _displayName.Editable = enabled;
        _address.Editable = enabled;
        _port.Editable = enabled;
        _lobbyId.Editable = enabled;
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
        ulong steamLobbyId = 0;
        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = arguments[index];
            if (argument == "--host") hostRequested = true;
            else if (argument.StartsWith("--join=", StringComparison.Ordinal)) joinAddress = argument["--join=".Length..];
            else if (argument.StartsWith("--steam-lobby=", StringComparison.Ordinal))
                ulong.TryParse(argument["--steam-lobby=".Length..], out steamLobbyId);
            else if (argument == "+connect_lobby" && index + 1 < arguments.Length)
                ulong.TryParse(arguments[++index], out steamLobbyId);
            else if (argument.StartsWith("--port=", StringComparison.Ordinal) &&
                     int.TryParse(argument["--port=".Length..], out var port)) _port.Value = port;
            else if (argument.StartsWith("--name=", StringComparison.Ordinal)) _displayName.Text = argument["--name=".Length..];
            else if (argument == "--quit-after-connected") _quitAfterConnected = true;
            else if (argument == "--auto-start") _autoStart = true;
        }

        if (steamLobbyId != 0)
        {
            _lobbyId.Text = steamLobbyId.ToString();
            JoinSteam();
        }
        else if (hostRequested) Host();
        else if (!string.IsNullOrWhiteSpace(joinAddress))
        {
            _address.Text = joinAddress;
            Join();
        }
    }

    private void ScheduleTestQuitIfRequested()
    {
        if (!_quitAfterConnected) return;
        var timer = GetTree().CreateTimer(0.5);
        timer.Timeout += () => GetTree().Quit();
    }
}
