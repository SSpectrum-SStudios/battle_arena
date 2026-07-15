#nullable enable

using BattleArena.Multiplayer.Connection;
using BattleArena.Multiplayer.Protocol;
using BattleArena.Multiplayer.Sequencing;
using BattleArena.Multiplayer.Transport;
using BattleArena.Protocol.V1;
using BattleArena.VerticalSlice;
using Godot;

namespace BattleArena.GodotNetworking;

public partial class NetworkArena : Node3D
{
    private const ulong HostCombatantId = 1;
    private const float FixedDelta = 1f / 60f;
    private const ulong BufferedInterpolationDelayTicks = 2;
    private const double MaximumInterpolationClockDriftTicks = 2.0;
    private const int RedundantInputFrameCount = 3;
    private const int MaximumPredictionHistory = 256;
    private static readonly StringName ToggleRemoteInterpolationAction =
        new("debug_toggle_remote_interpolation");

    [Export]
    public PackedScene AvatarScene { get; set; } = null!;

    [Export]
    public NodePath AvatarsPath { get; set; } = "";

    [Export]
    public NodePath StatusLabelPath { get; set; } = "";

    private readonly Dictionary<ulong, NetworkAvatar> _avatars = [];
    private readonly Dictionary<NetworkPeerId, RemoteInputBuffer> _remoteInputs = [];
    private readonly Dictionary<NetworkPeerId, MonotonicSequenceTracker> _clientPacketSequences = [];
    private readonly Dictionary<ulong, List<RemoteSnapshotSample>> _remoteSnapshots = [];
    private readonly Dictionary<ulong, double> _remoteRenderTicks = [];
    private readonly HashSet<NetworkPeerId> _disconnectedPeers = [];
    private readonly MonotonicSequenceTracker _authoritySnapshotSequences = new();
    private readonly List<NetworkMovementInput> _predictionHistory = [];
    private readonly NetworkMovementMotor _movementMotor = new();
    private readonly ProtobufProtocolCodec _codec = new();
    private readonly InboundMessageValidator _validator = new();
    private GodotEnetTransport _transport = null!;
    private AuthorityConnectionService? _authorityService;
    private ClientConnectionService? _clientService;
    private Node3D _avatarsRoot = null!;
    private Label _statusLabel = null!;
    private NetworkAvatar _localAvatar = null!;
    private CombatantSnapshot? _pendingLocalSnapshot;
    private ArenaMode _mode;
    private ulong _simulationTick;
    private ulong _nextInputSequence = 1;
    private ulong _nextEnvelopeSequence = 1;
    private ulong _entityRevision = 1;
    private float _lastCorrectionDistance;
    private bool _configured;
    private bool _authorityAvailable = true;
    private long _acceptedInputPackets;
    private long _acceptedSnapshots;
    private ulong _latestAuthoritySnapshotTick;
    private ulong _lastSnapshotArrivalMicroseconds;
    private double _remoteInterpolationLagTicks;
    private bool _remoteInterpolationEnabled = true;

    public void InitializeAuthority(
        GodotEnetTransport transport,
        AuthorityConnectionService authorityService,
        ulong authorityStartTick)
    {
        EnsureNotConfigured();
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _authorityService = authorityService ?? throw new ArgumentNullException(nameof(authorityService));
        _simulationTick = authorityStartTick;
        _mode = ArenaMode.Authority;
        _configured = true;
    }

    public void InitializeClient(
        GodotEnetTransport transport,
        ClientConnectionService clientService,
        ulong authorityStartTick)
    {
        EnsureNotConfigured();
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _clientService = clientService ?? throw new ArgumentNullException(nameof(clientService));
        if (clientService.Identity is null || clientService.AuthorityPeer is null)
        {
            throw new InvalidOperationException("Client session must be authenticated before entering the arena.");
        }

        _simulationTick = authorityStartTick;
        _mode = ArenaMode.Client;
        _configured = true;
    }

    public override void _Ready()
    {
        if (!_configured)
        {
            throw new InvalidOperationException("Network arena must be initialized before entering the scene tree.");
        }

        VerticalSliceInput.EnsureDefaultBindings();
        EnsureDebugInputBinding();
        _avatarsRoot = GetNode<Node3D>(AvatarsPath);
        _statusLabel = GetNode<Label>(StatusLabelPath);
        _transport.PacketReceived += OnPacketReceived;
        _transport.PeerDisconnected += OnPeerDisconnected;

        if (_mode == ArenaMode.Authority)
        {
            SpawnAuthorityAvatars();
        }
        else
        {
            SpawnClientAvatars();
        }

        UpdateStatus();
    }

    public override void _PhysicsProcess(double delta)
    {
        _simulationTick++;
        if (_mode == ArenaMode.Authority)
        {
            SimulateAuthority((float)delta);
        }
        else
        {
            SimulateClient((float)delta);
        }

        UpdateStatus();
    }

    public override void _Process(double delta)
    {
        if (_mode == ArenaMode.Client)
        {
            if (Input.IsActionJustPressed(ToggleRemoteInterpolationAction))
            {
                _remoteInterpolationEnabled = !_remoteInterpolationEnabled;
                _remoteRenderTicks.Clear();
            }

            InterpolateRemoteAvatars(delta);
        }
    }

    public override void _ExitTree()
    {
        if (_configured)
        {
            _transport.PacketReceived -= OnPacketReceived;
            _transport.PeerDisconnected -= OnPeerDisconnected;
            GD.Print(
                $"[NetworkArena] {_mode} stopped at tick {_simulationTick}; " +
                $"accepted inputs {_acceptedInputPackets}; accepted snapshots {_acceptedSnapshots}");
        }
    }

    private void SpawnAuthorityAvatars()
    {
        _localAvatar = SpawnAvatar(
            HostCombatantId,
            "Host — Combatant 1",
            locallyControlled: true,
            collisionEnabled: true,
            new Color(0.16f, 0.44f, 0.95f),
            new Vector3(0, 1, 4.5f),
            yaw: 0);

        foreach (var player in _authorityService!.ConnectedPlayers)
        {
            var avatar = SpawnAvatar(
                player.CombatantId,
                $"{player.DisplayName} — Combatant {player.CombatantId}",
                locallyControlled: false,
                collisionEnabled: true,
                new Color(0.95f, 0.3f, 0.14f),
                SpawnPositionFor(player.CombatantId),
                yaw: Mathf.Pi);
            _remoteInputs.Add(player.PeerId, new RemoteInputBuffer(avatar));
            _clientPacketSequences.Add(player.PeerId, new MonotonicSequenceTracker());
        }
    }

    private void SpawnClientAvatars()
    {
        var identity = _clientService!.Identity!;
        SpawnAvatar(
            HostCombatantId,
            "Host — Combatant 1",
            locallyControlled: false,
            collisionEnabled: false,
            new Color(0.16f, 0.44f, 0.95f),
            new Vector3(0, 1, 4.5f),
            yaw: 0);
        _remoteSnapshots.Add(HostCombatantId, []);

        _localAvatar = SpawnAvatar(
            identity.CombatantId,
            $"You — Combatant {identity.CombatantId}",
            locallyControlled: true,
            collisionEnabled: true,
            new Color(0.95f, 0.3f, 0.14f),
            SpawnPositionFor(identity.CombatantId),
            yaw: Mathf.Pi);
    }

    private NetworkAvatar SpawnAvatar(
        ulong combatantId,
        string label,
        bool locallyControlled,
        bool collisionEnabled,
        Color color,
        Vector3 position,
        float yaw)
    {
        var avatar = AvatarScene.Instantiate<NetworkAvatar>();
        avatar.Name = $"Combatant{combatantId}";
        avatar.Configure(combatantId, label, locallyControlled, collisionEnabled, color);
        _avatarsRoot.AddChild(avatar);
        avatar.ApplyReplicatedTransform(position, Vector3.Zero, yaw, 0);
        _avatars.Add(combatantId, avatar);
        return avatar;
    }

    private void SimulateAuthority(float delta)
    {
        var localInput = _localAvatar.CaptureInput(0, _simulationTick, delta);
        _movementMotor.Simulate(_localAvatar, localInput, delta);

        foreach (var buffer in _remoteInputs.Values)
        {
            var input = buffer.Consume(_simulationTick);
            _movementMotor.Simulate(buffer.Avatar, input, delta);
        }

        if ((_simulationTick & 1) == 0)
        {
            SendAuthoritySnapshots();
        }
    }

    private void SimulateClient(float delta)
    {
        if (!_authorityAvailable)
        {
            return;
        }

        if (_pendingLocalSnapshot is not null)
        {
            ReconcileLocalPrediction(_pendingLocalSnapshot);
            _pendingLocalSnapshot = null;
        }

        var input = _localAvatar.CaptureInput(_nextInputSequence++, _simulationTick, delta);
        _predictionHistory.Add(input);
        if (_predictionHistory.Count > MaximumPredictionHistory)
        {
            _predictionHistory.RemoveAt(0);
        }

        _movementMotor.Simulate(_localAvatar, input, delta);
        SendInputBatch();
    }

    private void SendInputBatch()
    {
        var identity = _clientService!.Identity!;
        var authorityPeer = _clientService.AuthorityPeer!.Value;
        var batch = new ClientInputBatch();
        foreach (var input in _predictionHistory.TakeLast(RedundantInputFrameCount))
        {
            batch.Frames.Add(input.ToProtocol());
        }

        var envelope = new PacketEnvelope
        {
            ProtocolVersion = ProtocolConstants.CurrentVersion,
            SessionId = identity.SessionId,
            Sequence = _nextEnvelopeSequence++,
            SimulationTick = _simulationTick,
            ClientInputBatch = batch,
        };
        _transport.Send(new OutboundTransportPacket(
            authorityPeer,
            TransportChannel.Input,
            TransportDelivery.UnreliableOrdered,
            _codec.Encode(envelope)));
    }

    private void SendAuthoritySnapshots()
    {
        var snapshot = new AuthoritySnapshot
        {
            Revision = new ReplicatedStateRevision { EntityRevision = _entityRevision++ },
        };
        foreach (var avatar in _avatars.Values)
        {
            var processedInput = _remoteInputs.Values
                .FirstOrDefault(buffer => buffer.Avatar == avatar)
                ?.LastProcessedSequence ?? 0;
            snapshot.Combatants.Add(ToSnapshot(avatar, processedInput));
        }

        foreach (var player in _authorityService!.ConnectedPlayers)
        {
            if (_disconnectedPeers.Contains(player.PeerId))
            {
                continue;
            }

            var envelope = new PacketEnvelope
            {
                ProtocolVersion = ProtocolConstants.CurrentVersion,
                SessionId = _authorityService.SessionId,
                Sequence = _nextEnvelopeSequence++,
                SimulationTick = _simulationTick,
                AuthoritySnapshot = snapshot.Clone(),
            };
            _transport.Send(new OutboundTransportPacket(
                player.PeerId,
                TransportChannel.Snapshot,
                TransportDelivery.UnreliableOrdered,
                _codec.Encode(envelope)));
        }
    }

    private void OnPacketReceived(InboundTransportPacket packet)
    {
        if (_mode == ArenaMode.Authority && packet.Channel == TransportChannel.Input)
        {
            ReceiveClientInput(packet);
        }
        else if (_mode == ArenaMode.Client && packet.Channel == TransportChannel.Snapshot)
        {
            ReceiveAuthoritySnapshot(packet);
        }
    }

    private void ReceiveClientInput(InboundTransportPacket packet)
    {
        if (!_remoteInputs.TryGetValue(packet.Sender, out var inputBuffer) ||
            !_clientPacketSequences.TryGetValue(packet.Sender, out var sequenceTracker))
        {
            return;
        }

        var decoded = _codec.Decode(packet.Payload.Span);
        if (!decoded.IsSuccess)
        {
            return;
        }

        var validation = _validator.Validate(
            decoded.Envelope!,
            new ProtocolValidationContext(
                RemoteEndpointRole.Client,
                _authorityService!.SessionId,
                SessionEstablished: true));
        if (!validation.IsValid ||
            decoded.Envelope!.PayloadCase != PacketEnvelope.PayloadOneofCase.ClientInputBatch)
        {
            return;
        }

        if (!sequenceTracker.Observe(decoded.Envelope.Sequence).ShouldProcess)
        {
            return;
        }

        foreach (var frame in decoded.Envelope.ClientInputBatch.Frames)
        {
            inputBuffer.Enqueue(NetworkMovementInput.FromProtocol(frame));
        }

        _acceptedInputPackets++;
    }

    private void ReceiveAuthoritySnapshot(InboundTransportPacket packet)
    {
        if (packet.Sender != _clientService!.AuthorityPeer)
        {
            return;
        }

        var decoded = _codec.Decode(packet.Payload.Span);
        if (!decoded.IsSuccess)
        {
            return;
        }

        var identity = _clientService.Identity!;
        var validation = _validator.Validate(
            decoded.Envelope!,
            new ProtocolValidationContext(
                RemoteEndpointRole.Authority,
                identity.SessionId,
                SessionEstablished: true));
        if (!validation.IsValid ||
            decoded.Envelope!.PayloadCase != PacketEnvelope.PayloadOneofCase.AuthoritySnapshot ||
            !_authoritySnapshotSequences.Observe(decoded.Envelope.Sequence).ShouldProcess)
        {
            return;
        }

        foreach (var combatant in decoded.Envelope.AuthoritySnapshot.Combatants)
        {
            if (combatant.CombatantId == identity.CombatantId)
            {
                _pendingLocalSnapshot = combatant.Clone();
                continue;
            }

            if (!_remoteSnapshots.TryGetValue(combatant.CombatantId, out var samples))
            {
                samples = [];
                _remoteSnapshots.Add(combatant.CombatantId, samples);
            }

            samples.Add(new RemoteSnapshotSample(decoded.Envelope.SimulationTick, combatant.Clone()));
            if (samples.Count > 32)
            {
                samples.RemoveAt(0);
            }
        }

        _latestAuthoritySnapshotTick = decoded.Envelope.SimulationTick;
        _lastSnapshotArrivalMicroseconds = Time.GetTicksUsec();
        _acceptedSnapshots++;
    }

    private void OnPeerDisconnected(NetworkPeerId peerId)
    {
        _disconnectedPeers.Add(peerId);
        if (_mode == ArenaMode.Client && peerId == _clientService!.AuthorityPeer)
        {
            _authorityAvailable = false;
        }
    }

    private void ReconcileLocalPrediction(CombatantSnapshot authoritative)
    {
        var predictedPosition = _localAvatar.Position;
        var localYaw = _localAvatar.Yaw;
        var localPitch = _localAvatar.Pitch;
        _predictionHistory.RemoveAll(input => input.Sequence <= authoritative.LastProcessedInputSequence);
        _localAvatar.ApplyReplicatedTransform(
            ToGodot(authoritative.Position),
            ToGodot(authoritative.Velocity),
            authoritative.ViewYawRadians,
            authoritative.ViewPitchRadians);

        foreach (var input in _predictionHistory)
        {
            _movementMotor.Simulate(_localAvatar, input, FixedDelta);
        }

        // The client owns its camera orientation. Authority snapshots correct
        // motion, but must not rewind locally sampled look input.
        _localAvatar.ApplyView(localYaw, localPitch);
        _lastCorrectionDistance = predictedPosition.DistanceTo(_localAvatar.Position);
        _localAvatar.SetDiagnosticText($"correction {_lastCorrectionDistance:0.000} m");
    }

    private void InterpolateRemoteAvatars(double delta)
    {
        _remoteInterpolationLagTicks = 0;
        foreach (var pair in _remoteSnapshots)
        {
            if (!_avatars.TryGetValue(pair.Key, out var avatar) || pair.Value.Count == 0)
            {
                continue;
            }

            var samples = pair.Value;
            var latestTick = samples[^1].AuthorityTick;
            var interpolationDelayTicks = _remoteInterpolationEnabled
                ? BufferedInterpolationDelayTicks
                : 0;
            var desiredTargetTick = latestTick > interpolationDelayTicks
                ? latestTick - interpolationDelayTicks
                : 0;
            if (!_remoteInterpolationEnabled)
            {
                _remoteRenderTicks[pair.Key] = desiredTargetTick;
            }

            if (!_remoteRenderTicks.TryGetValue(pair.Key, out var targetTick) ||
                targetTick > desiredTargetTick ||
                desiredTargetTick - targetTick > MaximumInterpolationClockDriftTicks)
            {
                targetTick = desiredTargetTick;
            }
            else
            {
                targetTick = Math.Min(
                    targetTick + (delta * 60.0),
                    desiredTargetTick);
            }

            _remoteRenderTicks[pair.Key] = targetTick;
            _remoteInterpolationLagTicks = Math.Max(
                _remoteInterpolationLagTicks,
                latestTick - targetTick);
            var before = samples[0];
            var after = samples[^1];
            foreach (var sample in samples)
            {
                if (sample.AuthorityTick <= targetTick)
                {
                    before = sample;
                }

                if (sample.AuthorityTick >= targetTick)
                {
                    after = sample;
                    break;
                }
            }

            var span = after.AuthorityTick - before.AuthorityTick;
            var weight = span == 0
                ? 0f
                : (float)((targetTick - before.AuthorityTick) / span);
            var position = ToGodot(before.Snapshot.Position).Lerp(ToGodot(after.Snapshot.Position), weight);
            var velocity = ToGodot(before.Snapshot.Velocity).Lerp(ToGodot(after.Snapshot.Velocity), weight);
            var yaw = Mathf.LerpAngle(before.Snapshot.ViewYawRadians, after.Snapshot.ViewYawRadians, weight);
            var pitch = Mathf.Lerp(before.Snapshot.ViewPitchRadians, after.Snapshot.ViewPitchRadians, weight);
            avatar.ApplyReplicatedTransform(position, velocity, yaw, pitch);
        }
    }

    private void UpdateStatus()
    {
        var horizontalSpeed = new Vector2(_localAvatar.Velocity.X, _localAvatar.Velocity.Z).Length();
        var snapshotAgeMilliseconds = _lastSnapshotArrivalMicroseconds == 0
            ? 0.0
            : (Time.GetTicksUsec() - _lastSnapshotArrivalMicroseconds) / 1000.0;
        var interpolationLagMilliseconds = _remoteInterpolationLagTicks * (1000.0 / 60.0);
        _statusLabel.Text = _mode == ArenaMode.Authority
            ? $"HOST AUTHORITY  |  tick {_simulationTick}  |  speed {horizontalSpeed:0.0} m/s  |  snapshots 30 Hz  |  players {_avatars.Count}\n" +
              "MOVEMENT TEST ONLY — attacks and combat are not networked yet\n" +
              "Escape releases the mouse; click the game to resume control (Alt+Tab is the fallback)"
            : _authorityAvailable
                ? $"CLIENT PREDICTION  |  tick {_simulationTick}  |  speed {horizontalSpeed:0.0} m/s  |  unacked {_predictionHistory.Count}  |  last correction {_lastCorrectionDistance:0.000} m\n" +
                  $"REMOTE VIEW  |  {(_remoteInterpolationEnabled ? "33 ms BUFFER" : "RAW LATEST SNAPSHOT")}  |  F9 toggles  |  fps {Engine.GetFramesPerSecond()}  |  authority tick {_latestAuthoritySnapshotTick}  |  snapshot age {snapshotAgeMilliseconds:0} ms  |  render lag {interpolationLagMilliseconds:0} ms\n" +
                  "MOVEMENT TEST ONLY — attacks and combat are not networked yet\n" +
                  "Escape releases the mouse; click the game to resume control (Alt+Tab is the fallback)"
                : "CONNECTION INTERRUPTED  |  waiting for authority reconnection";
    }

    private static void EnsureDebugInputBinding()
    {
        if (!InputMap.HasAction(ToggleRemoteInterpolationAction))
        {
            InputMap.AddAction(ToggleRemoteInterpolationAction);
        }

        var key = new InputEventKey { PhysicalKeycode = Key.F9 };
        if (!InputMap.ActionHasEvent(ToggleRemoteInterpolationAction, key))
        {
            InputMap.ActionAddEvent(ToggleRemoteInterpolationAction, key);
        }
    }

    private static CombatantSnapshot ToSnapshot(NetworkAvatar avatar, ulong processedInput) => new()
    {
        CombatantId = avatar.CombatantId,
        LifeId = avatar.CombatantId,
        ControllingPlayerId = avatar.CombatantId,
        Position = ToProtocol(avatar.Position),
        Velocity = ToProtocol(avatar.Velocity),
        ViewYawRadians = avatar.Yaw,
        ViewPitchRadians = avatar.Pitch,
        CurrentHealth = 100,
        MaximumHealth = 100,
        RemainingLives = 1,
        LifeState = ReplicatedLifeState.Alive,
        LastProcessedInputSequence = processedInput,
    };

    private static Vector3Value ToProtocol(Vector3 value) => new()
    {
        X = value.X,
        Y = value.Y,
        Z = value.Z,
    };

    private static Vector3 ToGodot(Vector3Value? value) => value is null
        ? Vector3.Zero
        : new Vector3(value.X, value.Y, value.Z);

    private static Vector3 SpawnPositionFor(ulong combatantId) =>
        new((combatantId - 2) * 2.5f, 1, -1);

    private void EnsureNotConfigured()
    {
        if (_configured)
        {
            throw new InvalidOperationException("Network arena is already configured.");
        }
    }

    private enum ArenaMode
    {
        Authority,
        Client,
    }

    private sealed class RemoteInputBuffer(NetworkAvatar avatar)
    {
        private readonly SortedDictionary<ulong, NetworkMovementInput> _pending = [];
        private NetworkMovementInput _lastInput = NetworkMovementInput.Neutral(0, Mathf.Pi, 0);
        private int _ticksWithoutFreshInput;

        public NetworkAvatar Avatar { get; } = avatar;

        public ulong LastProcessedSequence { get; private set; }

        public void Enqueue(NetworkMovementInput input)
        {
            if (input.Sequence > LastProcessedSequence)
            {
                _pending.TryAdd(input.Sequence, input);
            }
        }

        public NetworkMovementInput Consume(ulong authorityTick)
        {
            while (_pending.Count > 0)
            {
                var next = _pending.First();
                _pending.Remove(next.Key);
                if (next.Key <= LastProcessedSequence)
                {
                    continue;
                }

                LastProcessedSequence = next.Key;
                _lastInput = next.Value;
                _ticksWithoutFreshInput = 0;
                return _lastInput;
            }

            _ticksWithoutFreshInput++;
            return _ticksWithoutFreshInput <= 6
                ? _lastInput.WithoutOneShotButtons()
                : NetworkMovementInput.Neutral(authorityTick, _lastInput.YawRadians, _lastInput.PitchRadians);
        }
    }

    private sealed record RemoteSnapshotSample(ulong AuthorityTick, CombatantSnapshot Snapshot);
}
