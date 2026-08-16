#nullable enable

using BattleArena.Combat;
using BattleArena.Core.Actions;
using BattleArena.Core.Application;
using BattleArena.Core.Combat;
using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Core.Movement.Simulation;
using BattleArena.Multiplayer.AuthoritySimulation;
using BattleArena.Multiplayer.Connection;
using BattleArena.Multiplayer.OwnerPrediction;
using BattleArena.Multiplayer.Presentation;
using BattleArena.Multiplayer.Prediction;
using BattleArena.Multiplayer.Protocol;
using BattleArena.Multiplayer.Replication;
using BattleArena.Multiplayer.Sequencing;
using BattleArena.Multiplayer.Timing;
using BattleArena.Multiplayer.Transport;
using BattleArena.Protocol.V1;
using BattleArena.VerticalSlice;
using Godot;

namespace BattleArena.GodotNetworking;

public partial class NetworkArena : Node3D, IAuthorityFrameSimulator
{
    private const ulong HostCombatantId = 1;
    private const float FixedDelta = 1f / 60f;
    private const double MaximumInterpolationClockDriftTicks = 2.0;
    private const ulong ClockSyncIntervalTicks = 15;
    private const int MaximumOutstandingClockProbes = 32;
    private const int RedundantInputFrameCount = 3;
    private const int RedundantAcceptedCommandCount = 3;
    private const int MaximumPredictionHistory = 256;
    private const int MaximumPendingClientAuthorityPackets = 1024;
    private const ulong RespawnDelayTicks = 120;
    private const ulong RespawnCameraHoldTicks = 60;
    private const ulong RespawnCameraTravelTicks = 15;
    private const float MeleeReach = 2.6f;
    private const float MeleeVerticalReach = 1.6f;
    private const float MeleeHalfAngleRadians = 1.22173f;
    private const int MaximumPoseHistoryTicks = 20;
    private static readonly StringName ToggleRemoteInterpolationAction =
        new("debug_toggle_remote_interpolation");
    private static readonly StringName ToggleLagCompensationAction =
        new("debug_toggle_lag_compensation");

    [Export]
    public PackedScene AvatarScene { get; set; } = null!;

    [Export]
    public NodePath AvatarsPath { get; set; } = "";

    [Export]
    public NodePath StatusLabelPath { get; set; } = "";

    [Export]
    public NodePath LocalPlayerHudPath { get; set; } = "";

    [Export]
    public OwnerPredictionMode OwnerPredictionModeSelection { get; set; } =
        OwnerPredictionMode.Legacy;

    private readonly Dictionary<ulong, NetworkAvatar> _avatars = [];
    private readonly Dictionary<TransportConnectionId, RemotePlayerSimulation> _remoteInputs = [];
    private readonly Dictionary<TransportConnectionId, MonotonicSequenceTracker> _clientPacketSequences = [];
    private readonly Dictionary<ulong, RemoteMovementTimeline<AuthoritativeMovementState>>
        _remoteMovementTimelines = [];
    private readonly Dictionary<ulong, double> _remoteRenderTicks = [];
    private readonly Dictionary<ulong, ulong> _remoteCollisionCommitTicks = [];
    private readonly Dictionary<ulong, AdaptivePredictionTimingPolicy> _remoteTimingPolicies = [];
    private readonly Dictionary<NetworkAttackKey, ActionExecutionId> _coreAttackExecutions = [];
    private readonly Dictionary<ulong, EliminationRuntime> _eliminations = [];
    private readonly Dictionary<ulong, ulong> _lifeGenerations = [];
    private readonly Dictionary<TransportConnectionId, Dictionary<ulong, ulong>> _snapshotSendTimes = [];
    private readonly Dictionary<TransportConnectionId, PeerLatencyEstimate> _latencyByPeer = [];
    private readonly Dictionary<ulong, Queue<HistoricalPose>> _poseHistory = [];
    private readonly Dictionary<TransportConnectionId, MonotonicSequenceTracker> _clientActionSequences = [];
    private readonly Dictionary<TransportConnectionId, MonotonicSequenceTracker> _clockProbeSequences = [];
    private readonly List<AuthorityEvent> _outboundEvents = [];
    private readonly HashSet<TransportConnectionId> _disconnectedConnections = [];
    private readonly MonotonicSequenceTracker _authoritySnapshotSequences = new();
    private readonly MonotonicSequenceTracker _authorityEventEnvelopeSequences = new();
    private readonly MonotonicSequenceTracker _authorityMovementStreamSequences = new();
    private readonly MonotonicSequenceTracker _authorityAcceptedMovementSequences = new();
    private readonly MonotonicSequenceTracker _clockReplySequences = new();
    private readonly Dictionary<ulong, ulong> _outstandingClockProbes = [];
    private readonly List<NetworkMovementInput> _predictionHistory = [];
    private readonly ClientAuthorityPacketInbox _pendingClientAuthorityPackets =
        new(MaximumPendingClientAuthorityPackets);
    private readonly ProtobufProtocolCodec _codec = new();
    private readonly InboundMessageValidator _validator = new();
    private readonly RandomNumberGenerator _spawnRandom = new();
    private readonly INetworkTimeSource _networkTimeSource = new GodotNetworkTimeSource();
    private readonly AuthorityClockSynchronizer _authorityClock = new();
    private readonly NetworkPathEstimator _authorityMovementPath = new(60);
    private readonly PredictionFramePresentationCoordinator
        _clientFramePresentation = new();
    private readonly IAuthorityMovementRelayBuffer _movementRelayBuffer =
        new AuthorityMovementRelayBuffer();
    private readonly IAuthorityMovementDistribution _authorityMovementDistribution =
        new AuthorityMovementDistribution();
    private readonly IMovementPredictionBundleFactory _movementBundleFactory =
        new MovementPredictionBundleFactory();
    private OwnerPredictionModePolicy _ownerPredictionModePolicy =
        OwnerPredictionModePolicy.Default;

    /// <summary>
    /// V2 exact-scheduling composition. Null on the Legacy path, which is the
    /// default and stays byte-for-byte unchanged by this integration.
    /// </summary>
    /// <remarks>
    /// The scheduling subsystem lives in <c>BattleArena.Multiplayer</c> and is
    /// engine-free; this node holds one instance and delegates. That keeps the
    /// V2 path unit-testable rather than reachable only through a headless
    /// two-process run, and keeps this already-large node from absorbing another
    /// subsystem.
    /// </remarks>
    private AuthorityOwnerSchedulingHost? _schedulingHost;

    /// <summary>
    /// Combatants whose authority epoch changed during a frame run, rebuilt
    /// before the next one. Respawn is the common case and it fires from inside
    /// EndFrame, where rebuilding is refused.
    /// </summary>
    private readonly HashSet<ulong> _pendingV2EpochRebuilds = [];

    /// <summary>Tick the headless combat/V2 smoke gates finish at.</summary>
    private const ulong CombatSmokeCompletionTick = 75;

    private long _v2RemoteCommandsAdmitted;
    private long _v2RemoteCommandsDuplicate;
    private long _v2RemoteCommandsLate;
    private long _v2RemoteCommandsRefused;
    private long _v2OwnerStatesBuilt;
    private long _v2OwnerStateBacklogs;
    private long _v2LeadUpdatesEmitted;
    private CombatApplicationFacade _combat = null!;
    private INetworkTransport _transport = null!;
    private AuthorityConnectionService? _authorityService;
    private ClientConnectionService? _clientService;
    private ClientPredictionControlService? _clientPredictionControl;
    private ClientPredictionLifecycleRouter? _clientPredictionLifecycle;
    private Node3D _avatarsRoot = null!;
    private Label _statusLabel = null!;
    private NetworkPlayerHud _localPlayerHud = null!;
    private NetworkAvatar _localAvatar = null!;
    private PendingAuthoritativeMovement? _pendingLocalMovement;
    private IRemoteMovementPredictor _remoteMovementPredictor = null!;
    private ArenaMode _mode;
    private ulong _simulationTick;
    private ulong _nextInputSequence = 1;
    private ulong _nextHostInputSequence = 1;
    private ulong _nextEnvelopeSequence = 1;
    private ulong _nextPredictionBundleSequence = 1;
    private ulong _entityRevision = 1;
    private ulong _nextActionSequence = 1;
    private ulong _nextEventSequence = 1;
    private ulong _nextMovementStreamSequence = 1;
    private ulong _nextAcceptedMovementStreamSequence = 1;
    private ulong _nextClockProbeSequence = 1;
    private ulong _localLifeId = 1;
    private float _lastCorrectionDistance;
    private bool _configured;
    private bool _authorityAvailable = true;
    private bool? _localGroundedOverride;
    private long _acceptedInputPackets;
    private long _acceptedSnapshots;
    private long _acceptedMovementFrames;
    private long _acceptedMovementRelayBatches;
    private long _acceptedDirectPredictionBundles;
    private ulong _latestAuthoritySnapshotTick;
    private ulong _latestAuthorityEnvelopeSequence;
    private ulong _lastSnapshotArrivalMicroseconds;
    private ulong _lastMovementArrivalMicroseconds;
    private double _remoteInterpolationLagTicks;
    private int _currentPresentationDelayTicks = 1;
    private int _remotePredictedAvatarCount;
    private int _remoteFrozenAvatarCount;
    private bool _remoteInterpolationEnabled = true;
    private bool _lagCompensationEnabled = true;
    private bool _combatSmokeTest;
    private bool _movementBacklogSmokeTest;
    private bool _remoteCommitPhaseSmokeTest;
    private long _remoteCollisionPhysicsCommitCount;
    private long _remoteCollisionCommitPhaseViolationCount;
    private long _remoteVisualRenderCommitCount;
    private long _remoteVisualPositionChangeCount;
    private long _remoteRenderBodyMutationCount;
    private RemoteCommitSmokeLifecycleStage _remoteCommitSmokeLifecycleStage;
    private ulong _arenaStartedAtTick;
    private float _measuredRttMilliseconds;
    private float _measuredJitterMilliseconds;
    private float _rewindAllowanceMilliseconds;
    private float _lastRequestedAttackAgeMilliseconds;
    private float _lastActualRewindMilliseconds;

    public void InitializeAuthority(
        INetworkTransport transport,
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
        INetworkTransport transport,
        ClientConnectionService clientService,
        ClientPredictionControlService? clientPredictionControl,
        ulong authorityStartTick)
    {
        EnsureNotConfigured();
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _clientService = clientService ?? throw new ArgumentNullException(nameof(clientService));
        // The direct prediction mesh is an optional visual accelerator. The
        // authenticated authority transport is sufficient to enter and play.
        _clientPredictionControl = clientPredictionControl;
        if (clientService.Identity is null || clientService.AuthorityConnection is null)
        {
            throw new InvalidOperationException("Client session must be authenticated before entering the arena.");
        }

        _clientPredictionLifecycle = new ClientPredictionLifecycleRouter(
            clientService.Identity.SessionId,
            OnClientPredictionEpochTransitioned);

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

        ConfigureOwnerPredictionMode();
        VerticalSliceInput.EnsureDefaultBindings();
        EnsureDebugInputBinding();
        _combatSmokeTest = OS.GetCmdlineArgs()
            .Concat(OS.GetCmdlineUserArgs())
            .Contains("--combat-smoke", StringComparer.Ordinal);
        _movementBacklogSmokeTest = OS.GetCmdlineArgs()
            .Concat(OS.GetCmdlineUserArgs())
            .Contains("--movement-backlog-smoke", StringComparer.Ordinal);
        _remoteCommitPhaseSmokeTest = OS.GetCmdlineArgs()
            .Concat(OS.GetCmdlineUserArgs())
            .Contains("--remote-commit-phase-smoke", StringComparer.Ordinal);
        _arenaStartedAtTick = _simulationTick;
        _avatarsRoot = GetNode<Node3D>(AvatarsPath);
        _statusLabel = GetNode<Label>(StatusLabelPath);
        _localPlayerHud = GetNode<NetworkPlayerHud>(LocalPlayerHudPath);
        _transport.PacketReceived += OnPacketReceived;
        _transport.ConnectionClosed += OnConnectionClosed;
        if (_clientPredictionControl is not null)
        {
            _clientPredictionControl.AuthenticatedPacketReceived +=
                OnDirectPredictionPacketReceived;
        }
        InitializeCombat();
        _remoteMovementPredictor = new GodotRemoteMovementPredictor(
            new SimulationRate(Engine.PhysicsTicksPerSecond));

        if (_mode == ArenaMode.Authority)
        {
            SpawnAuthorityAvatars();

            // After avatars exist and every already-connected peer is known.
            // A client neither schedules nor paces authority frames, so it never
            // builds a host even under V2.
            if (_ownerPredictionModePolicy.SelectedMode == OwnerPredictionMode.FrameRewindV2)
            {
                BuildOwnerSchedulingHost();
            }
        }
        else
        {
            SpawnClientAvatars();
        }

        // Scene construction establishes the one-time initial body state. From
        // this point onward, gameplay blocking bodies are fixed-physics only.
        foreach (var avatar in _avatars.Values)
        {
            avatar.RequireFixedPhysicsBlockingCommits();
        }

        _localPlayerHud.Bind(_localAvatar);

        UpdateStatus();
    }

    private void ConfigureOwnerPredictionMode()
    {
        // A command-line override exists so the headless V2 smoke gate can select
        // the path without editing the scene, keeping the exported default
        // Legacy for every real build.
        var selection = OS.GetCmdlineArgs()
            .Concat(OS.GetCmdlineUserArgs())
            .Contains("--owner-prediction-v2", StringComparer.Ordinal)
            ? OwnerPredictionMode.FrameRewindV2
            : OwnerPredictionModeSelection;
        var policy = new OwnerPredictionModePolicy(selection);
        _ownerPredictionModePolicy = policy;
        GD.Print($"[NetworkArena] Owner prediction mode: {_ownerPredictionModePolicy.SelectedMode}");
    }

    /// <summary>
    /// Constructs the V2 scheduling host and registers the listen host plus every
    /// already-connected combatant.
    /// </summary>
    /// <remarks>
    /// Only the authority builds one. A client neither schedules nor paces
    /// authority frames, so on a client this stays null even under V2 and the
    /// existing prediction/relay path is untouched.
    /// </remarks>
    private void BuildOwnerSchedulingHost()
    {
        var rate = new SimulationRate(Engine.PhysicsTicksPerSecond);

        // Capacity is also the acceptance horizon and the flood bound. Sized to
        // twice the negotiated lead ceiling so a client legitimately sending at
        // the maximum lead is never refused as beyond horizon, with headroom for
        // the burst that follows a recovered stall.
        var capacityFrames = Math.Clamp(
            PredictionLeadControllerPolicy.MaximumLeadFramesForRate(rate) * 2,
            AuthorityOwnerInputScheduler.MinimumCapacityFrames,
            AuthorityOwnerInputScheduler.MaximumCapacityFrames);
        var fallbackPolicy = new AuthorityInputFallbackPolicy(
            AuthorityInputFallbackPolicy.DefaultMaximumRepeatedContinuousFrames);

        _schedulingHost = new AuthorityOwnerSchedulingHost(
            rate,
            AuthoritySimulationClockPolicy.Default,
            MatchFrameEpochId.Initial,
            new SimulationInstant(checked((long)_simulationTick)));

        _schedulingHost.RegisterCombatant(
            AuthorityEpochFor(HostCombatantId),
            capacityFrames,
            fallbackPolicy,
            isListenHost: true);
        foreach (var remote in _remoteInputs.Values)
        {
            _schedulingHost.RegisterCombatant(
                AuthorityEpochFor(remote.Player.CombatantId),
                capacityFrames,
                fallbackPolicy,
                isListenHost: false);
        }

        GD.Print(
            $"[NetworkArena] V2 scheduling host built; combatants " +
            $"{_schedulingHost.Combatants.Count}; capacity {capacityFrames} frames");
    }

    /// <summary>
    /// The authority prediction epoch for one combatant at its current life.
    /// </summary>
    /// <remarks>
    /// Authority discontinuity and owner control are both seeded from the life
    /// generation while V1 lifecycle mapping remains in place; the full
    /// lifecycle-driven epoch is Phase 6 work. Session identity comes from the
    /// authority service so a reconnecting peer cannot reuse another session's
    /// scope.
    /// </remarks>
    private CombatantAuthorityPredictionEpoch AuthorityEpochFor(ulong combatantId)
    {
        var life = _lifeGenerations.GetValueOrDefault(combatantId, 1UL);
        return new CombatantAuthorityPredictionEpoch(
            _authorityService?.SessionId ?? 1UL,
            _schedulingHost?.MatchFrameEpoch ?? MatchFrameEpochId.Initial,
            new CombatantId(checked((long)combatantId)),
            new LifeGenerationId(checked((long)life)),
            new AuthorityDiscontinuityId(life),
            new OwnerControlEpoch(life));
    }

    /// <summary>
    /// The V2 counterpart of the legacy per-combatant input consumption in
    /// <see cref="SimulateAuthority"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Where the legacy path calls <c>ConsumeRevisioned</c> once per combatant
    /// per engine callback — which could compact several client ticks into one
    /// integration step — this hands the elapsed wall time to the scheduling
    /// host, which runs every due frame for every combatant and calls back into
    /// this node through <see cref="IAuthorityFrameSimulator"/>. A burst of four
    /// commands therefore produces four simulated frames.
    /// </para>
    /// <para>
    /// The host owns the loop, so this method cannot skip a combatant, resolve
    /// one twice, or complete a frame that was not simulated.
    /// </para>
    /// </remarks>
    private void SimulateAuthorityV2(float delta)
    {
        DrainPendingEpochRebuildsV2();
        var summary = _schedulingHost!.RunDueFrames(
            delta * 1000d,
            checked((long)_networkTimeSource.GetTimestampMicroseconds()),
            this);

        if (summary.TimelineReset is { } reset)
        {
            // Unrecoverable lag. The clock latches this, so it arrives once
            // rather than every callback. Resuming adopts a new match-frame
            // epoch and rebuilds every combatant against it.
            GD.PrintErr(
                $"[NetworkArena] Authority timeline reset: {reset.Reason}; " +
                $"resuming at frame {_simulationTick + 1}");
            _schedulingHost.ResumeAfterMatchEpochReset(
                _schedulingHost.MatchFrameEpoch.Next(),
                new SimulationInstant(checked((long)_simulationTick) + 1));
            return;
        }

        if (summary.FramesRun > 0)
        {
            SendOwnerLeadUpdatesV2();
            SendOwnerSchedulingStateV2();
        }

        // The legacy body owns the smoke-test exit, and V2 returns before
        // reaching it, so V2 needs its own. Without this the headless gate has
        // no way to terminate and every V2 run hangs.
        if (_combatSmokeTest && _simulationTick >= CombatSmokeCompletionTick)
        {
            var accountedFrames = _schedulingHost.LastCompletedFrame?.Tick ?? -1;
            var passed = accountedFrames >= 0 && _v2RemoteCommandsAdmitted > 0;
            GD.Print(
                $"[NetworkArena] V2 scheduling smoke {(passed ? "passed" : "failed")}; " +
                $"accounted frames {accountedFrames}");
            GetTree().Quit(passed ? 0 : 1);
        }
    }

    /// <summary>
    /// Captures the listen host's input for one target frame and routes it
    /// through the in-memory publisher, so host input is admitted by the same
    /// scheduler rules a remote client's command faces.
    /// </summary>
    /// <remarks>
    /// Supplies only the input. The command's sequence is derived from the target
    /// frame by the scheduling group, because a node-side counter that advanced
    /// on a frame the host did not publish would break the scheduler's fixed
    /// sequence/frame offset and wedge host input for the rest of the epoch.
    /// </remarks>
    private void PublishHostOwnerCommandV2(SimulationInstant targetFrame, float delta)
    {
        if (_localAvatar.IsEliminated)
        {
            // No command for a frame the override path will resolve. The
            // sequence is derived from the target frame, so skipping publication
            // cannot desynchronise the scheduler's sequence/frame offset.
            return;
        }

        var captured = _localAvatar.CaptureInput(
            _nextHostInputSequence++,
            checked((ulong)targetFrame.Tick),
            delta);
        var sample = new OwnerCommandInputSample(
            MovementAxes.FromUnitVector(new HorizontalVector(captured.MoveX, captured.MoveZ)),
            ViewOrientation.FromRadians(captured.YawRadians, captured.PitchRadians),
            new MovementHeldState(MovementHeldButtonsFrom(captured.HeldButtons)),
            new CombatInputState(CombatHeldButtonsFrom(captured.HeldButtons)),
            new MovementConfigurationRevision(1),
            new MovementCapabilityRevision(1));

        _schedulingHost!.PublishHostCommand(
            new CombatantId(checked((long)HostCombatantId)),
            targetFrame,
            sample.ToSimulationInput(default, default));
    }

    private static MovementHeldButtons MovementHeldButtonsFrom(MovementButtons buttons)
    {
        var held = MovementHeldButtons.None;
        if (buttons.HasFlag(MovementButtons.Sprint))
        {
            held |= MovementHeldButtons.Sprint;
        }
        if (buttons.HasFlag(MovementButtons.Jump))
        {
            held |= MovementHeldButtons.Jump;
        }
        if (buttons.HasFlag(MovementButtons.CrouchOrRoll))
        {
            held |= MovementHeldButtons.CrouchOrRoll;
        }
        return held;
    }

    private static CombatHeldButtons CombatHeldButtonsFrom(MovementButtons buttons) =>
        buttons.HasFlag(MovementButtons.Attack)
            ? CombatHeldButtons.Attack
            : CombatHeldButtons.None;

    /// <inheritdoc />
    void IAuthorityFrameSimulator.BeginFrame(SimulationInstant frame)
    {
        // V2 drives the frame counter, so everything keyed off _simulationTick
        // sees the frame actually being simulated rather than a callback count.
        _simulationTick = checked((ulong)frame.Tick);
        _combat.AdvanceOneTick();

        // The host's own command for this frame is published before it resolves,
        // so it is admitted in time to be the frame's received input rather than
        // arriving late against a frame already consumed.
        PublishHostOwnerCommandV2(frame, (float)(1d / Engine.PhysicsTicksPerSecond));
    }

    /// <inheritdoc />
    AuthorityFallbackInputBasis IAuthorityFrameSimulator.GetFallbackBasis(
        CombatantId combatantId,
        SimulationInstant frame)
    {
        var avatar = _avatars[checked((ulong)combatantId.Value)];
        return new AuthorityFallbackInputBasis(
            ViewOrientation.FromRadians(avatar.Yaw, avatar.Pitch),
            new MovementConfigurationRevision(1),
            new MovementCapabilityRevision(1));
    }

    /// <inheritdoc />
    bool IAuthorityFrameSimulator.TryGetAuthorityOverride(
        CombatantId combatantId,
        SimulationInstant frame,
        out CharacterSimulationInput overrideInput,
        out AuthorityInputOverrideReason reason)
    {
        var avatar = _avatars[checked((ulong)combatantId.Value)];
        if (!avatar.IsEliminated)
        {
            overrideInput = default;
            reason = default;
            return false;
        }

        // An eliminated combatant still consumes its frame, with neutral intent
        // under a declared reason. Skipping the frame instead would leave a hole
        // a late command could later be applied to.
        overrideInput = new OwnerCommandInputSample(
            default,
            ViewOrientation.FromRadians(avatar.Yaw, avatar.Pitch),
            default,
            default,
            new MovementConfigurationRevision(1),
            new MovementCapabilityRevision(1)).ToSimulationInput(default, default);
        reason = AuthorityInputOverrideReason.Eliminated;
        return true;
    }

    /// <inheritdoc />
    void IAuthorityFrameSimulator.IntegrateFrame(
        CombatantId combatantId,
        in AuthorityInputFrameDecision decision)
    {
        var id = checked((ulong)combatantId.Value);
        var avatar = _avatars[id];
        var delta = (float)(1d / Engine.PhysicsTicksPerSecond);

        // The decision already carries the exact input this frame applies,
        // whether it came from an owner command, the fallback policy, or an
        // authority override. Phase 5 replaces this legacy motor call with the
        // explicit-state kinematic step; the decision contract does not change.
        var input = NetworkMovementInputFrom(decision, id);
        avatar.Simulate(input, delta);
        TrackAttackLifecycle(avatar);

        if (decision.ReceivedCommand is not null &&
            _remoteInputs.Values.FirstOrDefault(r => r.Player.CombatantId == id)
                is { } remote)
        {
            var source = new SessionPeer(
                remote.Player.SessionPeerId,
                remote.Player.ConnectionGeneration,
                remote.Player.PlayerId,
                remote.Player.CombatantId,
                remote.Player.DisplayName,
                isAuthority: false);
            var canonical = _authorityMovementDistribution.AcceptAppliedCommand(
                source,
                _lifeGenerations.GetValueOrDefault(id, 1UL),
                new RevisionedMovementCommand(input.ToMovementCommand(), 1, 1),
                _simulationTick);
            _movementRelayBuffer.Record(
                MovementCommandProtocolMapper.FromProtocol(canonical));
        }
    }

    /// <inheritdoc />
    void IAuthorityFrameSimulator.EndFrame(SimulationInstant frame)
    {
        // Cross-combatant resolution: every combatant now holds its post-state
        // for this frame.
        RecordPoseHistory();
        ResolveAuthorityMeleeHits();
        ProcessCombatFacts();
        AdvanceRespawns();
        SendAcceptedMovementCommands();
        SendAuthorityEvents();
        SendAuthorityMovementFrames();
    }

    /// <summary>
    /// Projects one committed authority decision back into the legacy movement
    /// input the current motor consumes.
    /// </summary>
    /// <remarks>
    /// A bridge, not a design: Phase 5 replaces the motor with one that takes
    /// <see cref="CharacterSimulationInput"/> directly and this disappears. The
    /// sequence is reported for diagnostics only and is zero for a frame no owner
    /// command supplied, which is exactly what the decision already records.
    /// </remarks>
    private NetworkMovementInput NetworkMovementInputFrom(
        in AuthorityInputFrameDecision decision,
        ulong combatantId)
    {
        var applied = decision.AppliedInput;
        var buttons = MovementButtons.None;
        if (applied.MovementHeld.Buttons.HasFlag(MovementHeldButtons.Sprint))
        {
            buttons |= MovementButtons.Sprint;
        }
        if (applied.MovementHeld.Buttons.HasFlag(MovementHeldButtons.Jump))
        {
            buttons |= MovementButtons.Jump;
        }
        if (applied.MovementHeld.Buttons.HasFlag(MovementHeldButtons.CrouchOrRoll))
        {
            buttons |= MovementButtons.CrouchOrRoll;
        }
        if (applied.CombatInput.HeldButtons.HasFlag(CombatHeldButtons.Attack))
        {
            buttons |= MovementButtons.Attack;
        }

        return new NetworkMovementInput(
            decision.AppliedInputSequence?.Value ?? 0UL,
            checked((ulong)decision.Identity.Frame.Tick),
            applied.Movement.XQ15 / (float)MovementAxes.MaximumMagnitude,
            applied.Movement.ZQ15 / (float)MovementAxes.MaximumMagnitude,
            (float)applied.View.YawRadians,
            (float)applied.View.PitchRadians,
            buttons,
            MovementButtons.None,
            MovementButtons.None);
    }

    /// <summary>
    /// Evaluates lead control for every V2 combatant and sends any due absolute
    /// lead update, honouring the debounce so a persistent rebase condition
    /// emits one reliable message rather than one per frame.
    /// </summary>
    /// <summary>
    /// Bridges one legacy movement frame into a V2 owner command and offers it to
    /// the scheduler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A bridge, not the destination. `OwnerCommandBatchDraft` is deliberately
    /// absent from `PacketEnvelope` v1 until the coordinated protocol bump, so
    /// until then the only remote owner input on the wire is the legacy frame.
    /// Mapping it here is what lets both host and client drive the V2 scheduler
    /// now instead of leaving remote players resolving from fallback forever.
    /// </para>
    /// <para>
    /// The scheduler requires a fixed <c>sequence - targetFrame</c> offset for an
    /// epoch's lifetime. A well-behaved legacy client advances both by one per
    /// physics frame, so the offset holds; a client that violates it is rejected
    /// as <see cref="AuthorityInputAdmissionFault.SequenceFrameSkew"/> rather
    /// than corrupting the timeline, which is the correct outcome for input this
    /// scheduler cannot place exactly.
    /// </para>
    /// </remarks>
    private void AdmitRemoteOwnerCommandV2(ulong combatantId, ClientInputFrame frame)
    {
        var epoch = AuthorityEpochFor(combatantId);
        var sample = new OwnerCommandInputSample(
            MovementAxes.FromUnitVector(new HorizontalVector(frame.MoveX, frame.MoveZ)),
            ViewOrientation.FromRadians(frame.ViewYawRadians, frame.ViewPitchRadians),
            new MovementHeldState(MovementHeldButtonsFrom((MovementButtons)frame.ButtonBits)),
            new CombatInputState(CombatHeldButtonsFrom((MovementButtons)frame.ButtonBits)),
            new MovementConfigurationRevision(Math.Max(1UL, frame.MovementProfileRevision)),
            new MovementCapabilityRevision(Math.Max(1UL, frame.MovementCapabilityRevision)));

        var admission = _schedulingHost!.TryAdmitRemoteCommand(
            new CombatantId(checked((long)combatantId)),
            new OwnerSimulationCommand(
                new OwnerInputIdentity(
                    OwnerIntentScope.From(epoch),
                    new InputSequence(Math.Max(1UL, frame.InputSequence))),
                epoch.AuthorityDiscontinuity,
                epoch.MatchFrameEpoch,
                new SimulationInstant(checked((long)frame.ClientTick)),
                sample.ToSimulationInput(default, default)));
        // Counted by disposition rather than by WasStored. A client legitimately
        // resends its recent command history in every bundle, so duplicates are
        // the expected majority and folding them into "refused" would make a
        // healthy link look broken.
        switch (admission.Disposition)
        {
            case OwnerInputArrivalDisposition.NewCommandAccepted:
                _v2RemoteCommandsAdmitted++;
                break;
            case OwnerInputArrivalDisposition.DuplicateCommand:
                _v2RemoteCommandsDuplicate++;
                break;
            case OwnerInputArrivalDisposition.LateCommand:
                _v2RemoteCommandsLate++;
                break;
            default:
                _v2RemoteCommandsRefused++;
                if (_v2RemoteCommandsRefused <= 3)
                {
                    GD.Print(
                        $"[NetworkArena] V2 refused remote command for combatant " +
                        $"{combatantId}: {admission.Fault}");
                }
                break;
        }
    }

    /// <summary>
    /// Rebuilds any combatant whose epoch changed while a frame run was in
    /// progress.
    /// </summary>
    /// <remarks>
    /// Respawn advances the life generation, and life is part of
    /// <see cref="OwnerIntentScope"/>, so a respawn is an epoch change that must
    /// rebuild the scheduler and publisher together. But respawns are advanced
    /// from <c>AdvanceRespawns</c>, which runs inside <c>EndFrame</c> — inside
    /// the frame run, where rebuilding is refused precisely because it would
    /// change scheduling state mid-frame. The rebuild is therefore queued there
    /// and drained here, between runs.
    /// </remarks>
    private void DrainPendingEpochRebuildsV2()
    {
        if (_pendingV2EpochRebuilds.Count == 0)
        {
            return;
        }

        var rate = new SimulationRate(Engine.PhysicsTicksPerSecond);
        var capacityFrames = Math.Clamp(
            PredictionLeadControllerPolicy.MaximumLeadFramesForRate(rate) * 2,
            AuthorityOwnerInputScheduler.MinimumCapacityFrames,
            AuthorityOwnerInputScheduler.MaximumCapacityFrames);
        var fallbackPolicy = new AuthorityInputFallbackPolicy(
            AuthorityInputFallbackPolicy.DefaultMaximumRepeatedContinuousFrames);

        foreach (var combatantId in _pendingV2EpochRebuilds)
        {
            _schedulingHost!.RebuildForEpoch(
                AuthorityEpochFor(combatantId),
                capacityFrames,
                fallbackPolicy,
                isListenHost: combatantId == HostCombatantId);
        }

        _pendingV2EpochRebuilds.Clear();
    }

    /// <summary>
    /// Records that a combatant's authority epoch changed, for the rebuild drained
    /// before the next frame run.
    /// </summary>
    private void QueueV2EpochRebuild(ulong combatantId)
    {
        if (_schedulingHost is not null)
        {
            _pendingV2EpochRebuilds.Add(combatantId);
        }
    }

    private void SendOwnerLeadUpdatesV2()
    {
        // Lead updates are computed but not transmitted: the draft messages are
        // deliberately absent from PacketEnvelope v1 until the coordinated
        // protocol bump, and P04-10's validator must gate them on arrival. The
        // control law still runs so its behaviour is observable in the V2 smoke
        // gate rather than first exercised on a real link.
        foreach (var combatantId in _schedulingHost!.Combatants)
        {
            var emission = _schedulingHost.EvaluateLead(
                combatantId,
                _authorityMovementPath.Current,
                clockConfidence: 1d);
            if (emission.RebaseClaimed)
            {
                GD.Print(
                    $"[NetworkArena] V2 rebase claimed for combatant {combatantId.Value}; " +
                    $"reason {emission.Evaluation.Reason}");
            }
            if (emission.Evaluation.Update is { } update)
            {
                _v2LeadUpdatesEmitted++;
                GD.Print(
                    $"[NetworkArena] V2 lead update for combatant {combatantId.Value}: " +
                    $"{update.TargetLead.Value} frames; revision {update.Revision.Value}");
            }
        }
    }

    /// <summary>
    /// Publishes each V2 combatant's exact scheduling acknowledgement.
    /// </summary>
    /// <remarks>
    /// Not routed on a real transport until P04-10 supplies context-relative
    /// validation for <c>AuthorityOwnerStateDraft</c>. Until then this records
    /// the state for diagnostics and the V2 smoke gate only.
    /// </remarks>
    private void SendOwnerSchedulingStateV2()
    {
        // Built but not routed. P04-10's validator exists, but
        // AuthorityOwnerStateDraft is not carried by PacketEnvelope v1, and the
        // blocking note on P04-10 requires it stay off a real transport until it
        // is. Building it every frame still proves the producer side and feeds
        // the V2 smoke gate.
        foreach (var combatantId in _schedulingHost!.Combatants)
        {
            if (_schedulingHost.BuildOwnerState(combatantId) is not { } publication)
            {
                continue;
            }

            _v2OwnerStatesBuilt++;
            if (publication.HasMoreToPublish)
            {
                _v2OwnerStateBacklogs++;
            }
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_mode == ArenaMode.Client)
        {
            DrainClientAuthorityPacketsAtPhysicsBoundary();
        }

        // Under V2 the scheduling host owns frame advancement: one engine
        // callback may run zero, one, or several fixed frames, so the counter is
        // driven from each completed frame in BeginFrame rather than bumped once
        // per callback. Everything downstream — pose history, snapshots, lag
        // compensation — keys off _simulationTick and must see the frame being
        // simulated, not the callback count.
        if (_schedulingHost is null)
        {
            _simulationTick++;
        }

        if (_mode == ArenaMode.Authority)
        {
            SimulateAuthority((float)delta);
        }
        else
        {
            CommitRemoteCollisionBodiesAtPhysicsBoundary();
            SimulateClient((float)delta);
        }

        UpdateStatus();
    }

    public override void _Process(double delta)
    {
        if (_mode == ArenaMode.Authority &&
            Input.IsActionJustPressed(ToggleLagCompensationAction))
        {
            _lagCompensationEnabled = !_lagCompensationEnabled;
        }

        if (_mode == ArenaMode.Client)
        {
            if (Input.IsActionJustPressed(ToggleRemoteInterpolationAction))
            {
                _remoteInterpolationEnabled = !_remoteInterpolationEnabled;
                _remoteRenderTicks.Clear();
            }

            var renderPhaseBaseline = _remoteCommitPhaseSmokeTest
                ? CaptureRemoteRenderPhaseSamples()
                : null;
            InterpolateRemoteAvatars(delta);
            if (renderPhaseBaseline is not null)
            {
                VerifyRemoteRenderPhase(renderPhaseBaseline);
            }
        }
    }

    public override void _ExitTree()
    {
        if (_configured)
        {
            _transport.PacketReceived -= OnPacketReceived;
            _transport.ConnectionClosed -= OnConnectionClosed;
            if (_clientPredictionControl is not null)
            {
                _clientPredictionControl.AuthenticatedPacketReceived -=
                    OnDirectPredictionPacketReceived;
            }
            GD.Print(
                $"[NetworkArena] {_mode} stopped at tick {_simulationTick}; " +
                $"accepted inputs {_acceptedInputPackets}; accepted snapshots {_acceptedSnapshots}");
            if (_schedulingHost is not null)
            {
                GD.Print(
                    $"[NetworkArena] V2 scheduling summary; frames {_schedulingHost.LastCompletedFrame?.Tick ?? -1}; " +
                    $"combatants {_schedulingHost.Combatants.Count}; " +
                    $"remote admitted {_v2RemoteCommandsAdmitted}; " +
                    $"remote duplicate {_v2RemoteCommandsDuplicate}; " +
                    $"remote late {_v2RemoteCommandsLate}; " +
                    $"remote refused {_v2RemoteCommandsRefused}; " +
                    $"owner states {_v2OwnerStatesBuilt}; " +
                    $"state backlogs {_v2OwnerStateBacklogs}; " +
                    $"lead updates {_v2LeadUpdatesEmitted}");
            }
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
            viewYaw: 0,
            bodyFacingYaw: 0);

        foreach (var player in _authorityService!.ConnectedPlayers)
        {
            var avatar = SpawnAvatar(
                player.CombatantId,
                $"{player.DisplayName} — Combatant {player.CombatantId}",
                locallyControlled: false,
                collisionEnabled: true,
                new Color(0.95f, 0.3f, 0.14f),
                SpawnPositionFor(player.CombatantId),
                viewYaw: Mathf.Pi,
                bodyFacingYaw: Mathf.Pi);
            _remoteInputs.Add(
                player.ConnectionId,
                new RemotePlayerSimulation(
                    avatar,
                    player,
                    new AuthorityMovementInputBuffer()));
            _clientPacketSequences.Add(player.ConnectionId, new MonotonicSequenceTracker());
            _clientActionSequences.Add(player.ConnectionId, new MonotonicSequenceTracker());
            _clockProbeSequences.Add(player.ConnectionId, new MonotonicSequenceTracker());
            _snapshotSendTimes.Add(player.ConnectionId, []);
            _latencyByPeer.Add(player.ConnectionId, new PeerLatencyEstimate());
        }
    }

    private void SpawnClientAvatars()
    {
        var identity = _clientService!.Identity!;
        SpawnAvatar(
            HostCombatantId,
            "Host — Combatant 1",
            locallyControlled: false,
            collisionEnabled: true,
            new Color(0.16f, 0.44f, 0.95f),
            new Vector3(0, 1, 4.5f),
            viewYaw: 0,
            bodyFacingYaw: 0);
        _remoteMovementTimelines.Add(
            HostCombatantId,
            new RemoteMovementTimeline<AuthoritativeMovementState>());
        _remoteTimingPolicies.Add(HostCombatantId, new AdaptivePredictionTimingPolicy());

        _localAvatar = SpawnAvatar(
            identity.CombatantId,
            $"You — Combatant {identity.CombatantId}",
            locallyControlled: true,
            collisionEnabled: true,
            new Color(0.95f, 0.3f, 0.14f),
            SpawnPositionFor(identity.CombatantId),
            viewYaw: Mathf.Pi,
            bodyFacingYaw: Mathf.Pi);

        // The current lobby starts the arena before any respawn can occur, so
        // these spawn identities are life 1. Authority snapshots/frames replace
        // this compatibility baseline before lifecycle-sensitive evidence runs.
        _ = TryObserveClientStateBaseline(
            HostCombatantId,
            1,
            ClientPredictionStateBaselineSource.Spawn,
            out _);
        _ = TryObserveClientStateBaseline(
            identity.CombatantId,
            1,
            ClientPredictionStateBaselineSource.Spawn,
            out _);
    }

    private NetworkAvatar SpawnAvatar(
        ulong combatantId,
        string label,
        bool locallyControlled,
        bool collisionEnabled,
        Color color,
        Vector3 position,
        float viewYaw,
        float bodyFacingYaw)
    {
        var avatar = AvatarScene.Instantiate<NetworkAvatar>();
        avatar.Name = $"Combatant{combatantId}";
        avatar.Configure(combatantId, label, locallyControlled, collisionEnabled, color);
        _avatarsRoot.AddChild(avatar);
        avatar.ApplyReplicatedTransform(
            position,
            Vector3.Zero,
            viewYaw,
            0,
            MovementRuntimeState.CreateGrounded(
                new SimulationInstant(checked((long)_simulationTick)),
                bodyFacingYaw));
        _avatars.Add(combatantId, avatar);
        if (_mode == ArenaMode.Authority)
        {
            _combat.RegisterCombatant(
                new Combatant(new CombatantId(checked((long)combatantId)), 100d),
                new ResistanceProfileCompiler().Compile([]));
            _lifeGenerations[combatantId] = 1;
            avatar.SetHealth(100, 100);
        }

        return avatar;
    }

    private void SimulateAuthority(float delta)
    {
        // V2 owns pacing and per-frame input resolution for the whole authority
        // when selected. The branch is here rather than inside the per-combatant
        // loop so the Legacy path below is reached only when Legacy is selected
        // and is therefore unchanged by this integration.
        if (_schedulingHost is not null)
        {
            SimulateAuthorityV2(delta);
            return;
        }

        _combat.AdvanceOneTick();
        var arenaTick = _simulationTick - _arenaStartedAtTick;
        if (_remoteCommitPhaseSmokeTest)
        {
            AdvanceAuthorityRemoteCommitPhaseSmoke(arenaTick);
        }

        if (_combatSmokeTest && _simulationTick == 2)
        {
            _avatars[HostCombatantId].Position = new Vector3(0f, 0f, 0f);
            foreach (var remote in _remoteInputs.Values)
            {
                remote.Avatar.Position = new Vector3(0f, 0f, -1.5f);
            }
        }

        if (!_localAvatar.IsEliminated)
        {
            var localInput = _remoteCommitPhaseSmokeTest && arenaTick is >= 20 and <= 39
                ? new NetworkMovementInput(
                    _nextHostInputSequence++,
                    _simulationTick,
                    0f,
                    -1f,
                    _localAvatar.Yaw,
                    _localAvatar.Pitch,
                    MovementButtons.Sprint,
                    MovementButtons.None,
                    MovementButtons.None)
                : _localAvatar.CaptureInput(
                    _nextHostInputSequence++,
                    _simulationTick,
                    delta);
            RecordAcceptedMovement(
                new SessionPeerId(HostCombatantId),
                ConnectionGeneration.Initial,
                HostCombatantId,
                new RevisionedMovementCommand(localInput.ToMovementCommand(), 1, 1));
            _localAvatar.Simulate(localInput, delta);
            TrackAttackLifecycle(_localAvatar);
        }

        foreach (var buffer in _remoteInputs.Values)
        {
            if (buffer.Avatar.IsEliminated)
            {
                buffer.Inputs.DiscardPending();
                continue;
            }

            // The three-process regression test deliberately lets one peer's
            // commands accumulate. Production never pauses a combatant here;
            // this recreates the scheduler hitch that previously became
            // permanent client-to-client latency.
            if (_movementBacklogSmokeTest &&
                buffer.Player.CombatantId == 3 &&
                arenaTick is >= 10 and <= 39)
            {
                continue;
            }

            var revisioned = buffer.Inputs.ConsumeRevisioned(
                new SimulationInstant(checked((long)_simulationTick)));
            var input = NetworkMovementInput.FromMovementCommand(revisioned.Command);
            if (revisioned.Command.Sequence != 0)
            {
                var source = new SessionPeer(
                    buffer.Player.SessionPeerId,
                    buffer.Player.ConnectionGeneration,
                    buffer.Player.PlayerId,
                    buffer.Player.CombatantId,
                    buffer.Player.DisplayName,
                    isAuthority: false);
                var canonical = _authorityMovementDistribution.AcceptAppliedCommand(
                    source,
                    _lifeGenerations.GetValueOrDefault(buffer.Player.CombatantId, 1UL),
                    revisioned,
                    _simulationTick);
                _movementRelayBuffer.Record(
                    MovementCommandProtocolMapper.FromProtocol(canonical));
            }
            buffer.Avatar.Simulate(input, delta);
            TrackAttackLifecycle(buffer.Avatar);
        }

        RecordPoseHistory();
        ResolveAuthorityMeleeHits();
        ProcessCombatFacts();
        AdvanceRespawns();
        SendAcceptedMovementCommands();
        SendAuthorityEvents();
        SendAuthorityMovementFrames();
        if (_movementBacklogSmokeTest && arenaTick == 70)
        {
            CompleteAuthorityMovementBacklogSmoke();
        }

        // Keep the authority alive beyond the client's tick-60 clock-sync
        // acceptance window. Quitting at tick 45 made the process gate depend
        // on scheduler timing even when every movement relay arrived.
        if (_combatSmokeTest && _simulationTick == 75)
        {
            _combat.TryGetCombatantSnapshot(
                CombatantIdFor(HostCombatantId),
                out var smokeTarget);
            var passed = smokeTarget is { CurrentHealth: 80d };
            GD.Print(
                $"[NetworkArena] Combat smoke {(passed ? "passed" : "failed")}; " +
                $"host health {smokeTarget?.CurrentHealth:0}");
            GetTree().Quit(passed ? 0 : 1);
        }

        if ((_simulationTick & 1) == 0)
        {
            SendAuthoritySnapshots();
        }
    }

    private void AdvanceAuthorityRemoteCommitPhaseSmoke(ulong arenaTick)
    {
        if (arenaTick == 45 && !_localAvatar.IsEliminated)
        {
            _localAvatar.SetEliminated(true, _simulationTick);
            _outboundEvents.Add(new AuthorityEvent
            {
                EventSequence = _nextEventSequence++,
                AuthorityTick = _simulationTick,
                LifeState = new LifeStateEvent
                {
                    CombatantId = HostCombatantId,
                    LifeId = _lifeGenerations[HostCombatantId],
                    State = ReplicatedLifeState.Eliminated,
                    RemainingLives = int.MaxValue,
                },
            });
            return;
        }

        if (arenaTick != 52 || !_localAvatar.IsEliminated)
        {
            return;
        }

        _combat.TryGetCombatantSnapshot(
            CombatantIdFor(HostCombatantId),
            out var health);
        _lifeGenerations[HostCombatantId] = checked(
            _lifeGenerations[HostCombatantId] + 1);

        // Life is part of OwnerIntentScope, so a respawn is an epoch change and
        // the V2 scheduler and publisher must be rebuilt together for it.
        QueueV2EpochRebuild(HostCombatantId);
        _localAvatar.ResetForRespawn(
            new Vector3(2f, 1f, 0f),
            _simulationTick,
            0f,
            checked((long)Math.Round(health?.CurrentHealth ?? 100d)),
            checked((long)Math.Round(health?.EffectiveMaximumHealth ?? 100d)));
        _outboundEvents.Add(new AuthorityEvent
        {
            EventSequence = _nextEventSequence++,
            AuthorityTick = _simulationTick,
            LifeState = new LifeStateEvent
            {
                CombatantId = HostCombatantId,
                LifeId = _lifeGenerations[HostCombatantId],
                State = ReplicatedLifeState.Alive,
                RemainingLives = int.MaxValue,
            },
        });
    }

    private void SimulateClient(float delta)
    {
        if (!_authorityAvailable)
        {
            if (_combatSmokeTest && _simulationTick >= 60)
            {
                CompleteClientNetworkTimingSmoke();
            }
            else if (_movementBacklogSmokeTest &&
                     _simulationTick - _arenaStartedAtTick >= 60)
            {
                CompleteClientMovementBacklogSmoke();
            }

            return;
        }

        _clientFramePresentation.BeginFrame();

        if (_pendingLocalMovement is { } pendingMovement)
        {
            ReconcileLocalPrediction(pendingMovement.State);
            _pendingLocalMovement = null;
        }

        var arenaTick = _simulationTick - _arenaStartedAtTick;
        var input = _movementBacklogSmokeTest
            ? CaptureMovementBacklogSmokeInput()
            : _combatSmokeTest && _simulationTick == 5
            ? new NetworkMovementInput(
                _nextInputSequence++,
                _simulationTick,
                0f,
                0f,
                Mathf.Pi,
                0f,
                MovementButtons.Attack,
                MovementButtons.Attack,
                MovementButtons.None)
            : _localAvatar.CaptureInput(_nextInputSequence++, _simulationTick, delta);
        if (input.PressedButtons.HasFlag(MovementButtons.Attack))
        {
            SendActionRequest(input.ClientTick);
        }

        _predictionHistory.Add(input);
        if (_predictionHistory.Count > MaximumPredictionHistory)
        {
            _predictionHistory.RemoveAt(0);
        }

        if (!_localAvatar.IsEliminated)
        {
            _localAvatar.SimulateStateOnly(input, delta, _localGroundedOverride);
            _clientFramePresentation.RecordStateOnlyStep(
                PredictionFrameStateOnlyStep.CurrentSimulation);
        }

        _localGroundedOverride = null;
        if (_clientFramePresentation.CompleteFrame() ==
            PredictionFramePublicationDecision.PublishFinal)
        {
            _localAvatar.PublishCommittedPresentation(
                input.YawRadians,
                input.PitchRadians,
                delta,
                emitCurrentSimulationCues: !_localAvatar.IsEliminated);
        }

        SendInputBatch();
        if (_simulationTick == 1 || _simulationTick % ClockSyncIntervalTicks == 0)
        {
            SendClockSyncProbe();
        }
        if (_combatSmokeTest && _simulationTick >= 60)
        {
            CompleteClientNetworkTimingSmoke();
        }

        else if (_movementBacklogSmokeTest && arenaTick >= 120)
        {
            CompleteClientMovementBacklogSmoke();
        }
    }

    private NetworkMovementInput CaptureMovementBacklogSmokeInput()
    {
        var moveZ = _clientService!.Identity!.CombatantId == 3 ? -1f : 0f;
        return new NetworkMovementInput(
            _nextInputSequence++,
            _simulationTick,
            0f,
            moveZ,
            Mathf.Pi,
            0f,
            MovementButtons.None,
            MovementButtons.None,
            MovementButtons.None);
    }

    private void CompleteAuthorityMovementBacklogSmoke()
    {
        var remote = _remoteInputs.Values.SingleOrDefault(
            candidate => candidate.Player.CombatantId == 3);
        var distance = remote is null
            ? 0f
            : HorizontalDistance(remote.Avatar.Position, SpawnPositionFor(3));
        var passed = remote is not null &&
                     remote.Inputs.LastProcessedSequence >= 40 &&
                     remote.Inputs.TotalCompactedCommandCount >= 8 &&
                     distance >= 0.25f;
        GD.Print(
            $"[NetworkArena] Three-player authority movement smoke " +
            $"{(passed ? "passed" : "failed")}; " +
            $"combatant 3 sequence {remote?.Inputs.LastProcessedSequence ?? 0}; " +
            $"compacted {remote?.Inputs.TotalCompactedCommandCount ?? 0}; " +
            $"distance {distance:0.00} m");
        GetTree().Quit(passed ? 0 : 1);
    }

    private void CompleteClientMovementBacklogSmoke()
    {
        var identity = _clientService!.Identity!;
        var combatantThree = _avatars.GetValueOrDefault(3UL);
        var distance = combatantThree is null
            ? 0f
            : HorizontalDistance(combatantThree.Position, SpawnPositionFor(3));
        var directPathMeasured = _clientPredictionControl?.DirectPathStatuses.Any(
            status => status.Path.SmoothedRttMilliseconds > 0d);
        var passed = combatantThree is not null &&
                     distance >= 0.25f &&
                     _acceptedMovementFrames >= 15 &&
                     _acceptedDirectPredictionBundles > 0 &&
                     directPathMeasured == true;
        var role = identity.CombatantId == 3 ? "owner" : "observer";
        GD.Print(
            $"[NetworkArena] Three-player {role} movement smoke " +
            $"{(passed ? "passed" : "failed")}; " +
            $"combatant 3 distance {distance:0.00} m; " +
            $"movement frames {_acceptedMovementFrames}; " +
            $"direct bundles {_acceptedDirectPredictionBundles}; " +
            $"direct RTT measured {directPathMeasured}");
        GetTree().Quit(passed ? 0 : 1);
    }

    private static float HorizontalDistance(Vector3 first, Vector3 second) =>
        new Vector2(first.X - second.X, first.Z - second.Z).Length();

    private void ObserveRemoteCommitSmokeSnapshot(
        CombatantSnapshot snapshot,
        NetworkAvatar avatar)
    {
        if (!_remoteCommitPhaseSmokeTest ||
            snapshot.CombatantId != HostCombatantId)
        {
            return;
        }

        var collisionDisabled = avatar
            .CaptureBlockingBodySnapshot()
            .CollisionDisabled;
        if (snapshot.LifeId == 1 &&
            snapshot.LifeState == ReplicatedLifeState.Alive &&
            !avatar.IsEliminated &&
            !collisionDisabled &&
            _remoteCommitSmokeLifecycleStage == RemoteCommitSmokeLifecycleStage.None)
        {
            _remoteCommitSmokeLifecycleStage =
                RemoteCommitSmokeLifecycleStage.LifeOneAlive;
        }
        else if (snapshot.LifeId == 1 &&
                 snapshot.LifeState != ReplicatedLifeState.Alive &&
                 avatar.IsEliminated &&
                 collisionDisabled &&
                 _remoteCommitSmokeLifecycleStage ==
                 RemoteCommitSmokeLifecycleStage.LifeOneAlive)
        {
            _remoteCommitSmokeLifecycleStage =
                RemoteCommitSmokeLifecycleStage.LifeOneEliminated;
        }
    }

    private void ObserveRemoteCommitSmokeElimination(
        ulong combatantId,
        ulong lifeId,
        NetworkAvatar avatar)
    {
        if (_remoteCommitPhaseSmokeTest &&
            combatantId == HostCombatantId &&
            lifeId == 1 &&
            avatar.IsEliminated &&
            avatar.CaptureBlockingBodySnapshot().CollisionDisabled &&
            _remoteCommitSmokeLifecycleStage ==
            RemoteCommitSmokeLifecycleStage.LifeOneAlive)
        {
            _remoteCommitSmokeLifecycleStage =
                RemoteCommitSmokeLifecycleStage.LifeOneEliminated;
        }
    }

    private void CompleteClientNetworkTimingSmoke()
    {
        var localCombatantId = _clientService!.Identity!.CombatantId;
        var remoteAvatars = _avatars
            .Where(pair => pair.Key != localCombatantId)
            .Select(pair => pair.Value)
            .ToArray();
        var avatarPhaseViolations = remoteAvatars.Sum(
            avatar => avatar.BlockingCommitPhaseViolationCount);
        var lifecycleCommits = remoteAvatars.Sum(
            avatar => avatar.FixedPhysicsLifecycleCommitCount);
        var hostRemote = _avatars.GetValueOrDefault(HostCombatantId);
        var finalLifeTwoAlive = hostRemote is not null &&
                                _lifeGenerations.GetValueOrDefault(HostCombatantId) == 2 &&
                                !hostRemote.IsEliminated &&
                                !hostRemote.CaptureBlockingBodySnapshot().CollisionDisabled;
        var remoteCommitPhasePassed = !_remoteCommitPhaseSmokeTest ||
                                      (_remoteCollisionPhysicsCommitCount > 0 &&
                                       _remoteVisualRenderCommitCount > 0 &&
                                       _remoteVisualPositionChangeCount > 0 &&
                                       lifecycleCommits > 0 &&
                                       _remoteCollisionCommitPhaseViolationCount == 0 &&
                                       avatarPhaseViolations == 0 &&
                                       _remoteRenderBodyMutationCount == 0 &&
                                       _remoteCommitSmokeLifecycleStage ==
                                       RemoteCommitSmokeLifecycleStage.LifeTwoPoseCommitted &&
                                       finalLifeTwoAlive);
        var passed = _acceptedMovementFrames >= 15 &&
                     _acceptedMovementRelayBatches >= 15 &&
                     _authorityClock.HasEstimate &&
                     _authorityMovementPath.Current.LatestSequence > 0 &&
                     remoteCommitPhasePassed;
        GD.Print(
            $"[NetworkArena] Network timing smoke {(passed ? "passed" : "failed")}; " +
            $"movement frames {_acceptedMovementFrames}; " +
            $"accepted relays {_acceptedMovementRelayBatches}; " +
            $"clock synchronized {_authorityClock.HasEstimate}; " +
            $"latest stream {_authorityMovementPath.Current.LatestSequence}");
        if (_remoteCommitPhaseSmokeTest)
        {
            GD.Print(
                $"[NetworkArena] Remote commit phase smoke " +
                $"{(remoteCommitPhasePassed ? "passed" : "failed")}; " +
                $"physics collision commits {_remoteCollisionPhysicsCommitCount}; " +
                $"render visual commits {_remoteVisualRenderCommitCount}; " +
                $"visual position changes {_remoteVisualPositionChangeCount}; " +
                $"lifecycle commits {lifecycleCommits}; " +
                $"lifecycle stage {_remoteCommitSmokeLifecycleStage}; " +
                $"final life 2 alive {finalLifeTwoAlive}; " +
                $"phase violations " +
                $"{_remoteCollisionCommitPhaseViolationCount + avatarPhaseViolations}; " +
                $"render body mutations {_remoteRenderBodyMutationCount}");
        }

        GetTree().Quit(passed ? 0 : 1);
    }

    private void InitializeCombat()
    {
        var attacks = WeaponAttackDefinitionLoader.Load(
            "res://assets/classes/fighter/starter_sword_attacks.json",
            new SimulationRate(Engine.PhysicsTicksPerSecond));
        var definitions = attacks.GroundedCombo
            .Append(attacks.CrouchedOrAirborne)
            .Select(step => new CombatActionDefinition(
                ActionDefinitionIdFor(step.Id),
                new TargetHitPolicy.Limited(1),
                [new CombatActionEffectDefinition.ImmediateDamage(
                    [new DamagePortion(DamageType.Physical, step.PhysicalDamage)])]))
            .ToArray();
        _combat = new CombatApplicationFacade(
            new CombatActionCatalog(definitions),
            new CombatResolver());
        _spawnRandom.Seed = _authorityService?.SessionId ??
            _clientService?.Identity?.SessionId ??
            1;
    }

    private void TrackAttackLifecycle(NetworkAvatar avatar)
    {
        if (avatar.EndedAttackExecutionId != 0 &&
            _coreAttackExecutions.Remove(
                new NetworkAttackKey(avatar.CombatantId, avatar.EndedAttackExecutionId),
                out var ended))
        {
            _combat.EndAction(ended);
        }

        if (!avatar.LastAttackTick.StepStarted ||
            avatar.LastAttackTick.Step is not { } step ||
            avatar.AttackState is not { } attack)
        {
            return;
        }

        var begin = _combat.BeginAction(
            CombatantIdFor(avatar.CombatantId),
            ActionDefinitionIdFor(step.Id));
        if (begin.Started && begin.ExecutionId is { } executionId)
        {
            _coreAttackExecutions[
                new NetworkAttackKey(avatar.CombatantId, attack.ExecutionId)] = executionId;
            _outboundEvents.Add(new AuthorityEvent
            {
                EventSequence = _nextEventSequence++,
                AuthorityTick = _simulationTick,
                ActionState = new ActionStateEvent
                {
                    SourceCombatantId = avatar.CombatantId,
                    SourceLifeId = _lifeGenerations[avatar.CombatantId],
                    AttackExecutionId = attack.ExecutionId,
                    AttackStepIndex = checked((uint)attack.StepIndex),
                    AttackIsCrouchedOrAirborne =
                        attack.Context ==
                        BattleArena.Core.Combat.Attacks.AttackContext.CrouchedOrAirborne,
                    AttackStartedTick = checked((ulong)attack.StartedAt.Tick),
                    Lifecycle = ReplicatedActionLifecycleKind.Started,
                    Phase = ReplicatedAttackPhase.Startup,
                    PhaseStartedTick = checked((ulong)attack.StartedAt.Tick),
                    WeaponDefinitionId = avatar.AttackDefinition.Id,
                    AttackPolicyRevision = 1,
                },
            });
        }
    }

    private void ResolveAuthorityMeleeHits()
    {
        var candidates = new List<AuthorityHitCandidate>();
        foreach (var source in _avatars.Values)
        {
            if (source.IsEliminated ||
                !source.LastAttackTick.HitWindowActive ||
                source.AttackState is not { } attack ||
                !_coreAttackExecutions.TryGetValue(
                    new NetworkAttackKey(source.CombatantId, attack.ExecutionId),
                    out var executionId))
            {
                continue;
            }

            foreach (var target in _avatars.Values)
            {
                if (target == source ||
                    target.IsEliminated ||
                    attack.HitCombatants.Contains(target.CombatantId) ||
                    !IsInsideMeleeQuery(
                        source,
                        target,
                        RewoundTargetPosition(source, target)))
                {
                    continue;
                }

                candidates.Add(new AuthorityHitCandidate(source, target, executionId));
            }
        }

        if (candidates.Count == 0)
        {
            return;
        }

        var results = _combat.RegisterHitsSimultaneously(
            candidates.Select(candidate => new SimultaneousHitRequest(
                candidate.ExecutionId,
                CombatantIdFor(candidate.Target.CombatantId))));
        for (var index = 0; index < results.Count; index++)
        {
            if (results[index].Accepted)
            {
                candidates[index].Source.TryRecordAttackHit(
                    candidates[index].Target.CombatantId);
            }
        }
    }

    private void ProcessCombatFacts()
    {
        foreach (var fact in _combat.DrainFacts())
        {
            switch (fact)
            {
                case CombatFact.DamageResolved damage:
                {
                    var targetId = checked((ulong)damage.TargetCombatantId.Value);
                    if (_avatars.TryGetValue(targetId, out var target))
                    {
                        target.SetHealth(
                            checked((long)Math.Round(damage.HealthApplication.HealthAfter)),
                            checked((long)Math.Round(damage.HealthApplication.MaximumHealth)));
                        target.ShowDamage(
                            checked((long)Math.Round(damage.HealthApplication.DamageResolved)));
                    }

                    var sourceId = checked((ulong)damage.SourceCombatantId.Value);
                    if (_avatars.TryGetValue(sourceId, out var source))
                    {
                        source.ShowHitConfirmation();
                    }

                    _outboundEvents.Add(new AuthorityEvent
                    {
                        EventSequence = _nextEventSequence++,
                        AuthorityTick = _simulationTick,
                        Damage = new DamageEvent
                        {
                            SourceCombatantId = sourceId,
                            SourceLifeId = checked(
                                (ulong)damage.SourceLifeGenerationId.Value),
                            TargetCombatantId = targetId,
                            TargetLifeId = _lifeGenerations.GetValueOrDefault(targetId, 1UL),
                            NetDamage = checked(
                                (long)Math.Round(damage.HealthApplication.DamageResolved)),
                            Overkill = checked(
                                (long)Math.Round(damage.HealthApplication.Overkill)),
                            CurrentHealth = checked(
                                (long)Math.Round(damage.HealthApplication.HealthAfter)),
                            MaximumHealth = checked(
                                (long)Math.Round(damage.HealthApplication.MaximumHealth)),
                        },
                    });

                    break;
                }
                case CombatFact.CombatantEliminated eliminated:
                {
                    var combatantId = checked((ulong)eliminated.CombatantId.Value);
                    if (_avatars.TryGetValue(combatantId, out var avatar))
                    {
                        avatar.SetEliminated(true, _simulationTick);
                        _eliminations[combatantId] = new EliminationRuntime(
                            _simulationTick,
                            avatar.Position,
                            SelectSpawnPosition(combatantId));
                        _outboundEvents.Add(new AuthorityEvent
                        {
                            EventSequence = _nextEventSequence++,
                            AuthorityTick = _simulationTick,
                            LifeState = new LifeStateEvent
                            {
                                CombatantId = combatantId,
                                LifeId = _lifeGenerations[combatantId],
                                State = ReplicatedLifeState.Eliminated,
                                RemainingLives = int.MaxValue,
                            },
                        });
                    }

                    break;
                }
                case CombatFact.CombatantRespawned respawned:
                {
                    var combatantId = checked((ulong)respawned.Health.CombatantId.Value);
                    if (_avatars.TryGetValue(combatantId, out var avatar))
                    {
                        avatar.SetHealth(
                            checked((long)Math.Round(respawned.Health.CurrentHealth)),
                            checked((long)Math.Round(
                                respawned.Health.EffectiveMaximumHealth)));
                    }

                    break;
                }
            }
        }
    }

    private void AdvanceRespawns()
    {
        foreach (var pair in _eliminations.ToArray())
        {
            var elapsed = _simulationTick - pair.Value.EliminatedAtTick;
            if (elapsed >= RespawnCameraHoldTicks)
            {
                var travelElapsed = Math.Min(
                    RespawnCameraTravelTicks,
                    elapsed - RespawnCameraHoldTicks);
                var linear = travelElapsed / (float)RespawnCameraTravelTicks;
                var smooth = linear * linear * (3f - (2f * linear));
                _avatars[pair.Key].SetRespawnTransitionPosition(
                    pair.Value.DeathPosition.Lerp(pair.Value.SpawnPosition, smooth));
            }

            if (elapsed < RespawnDelayTicks)
            {
                _avatars[pair.Key].SetRespawnCountdown(
                    (RespawnDelayTicks - elapsed) /
                    (double)Engine.PhysicsTicksPerSecond);
                continue;
            }

            var result = _combat.Respawn(CombatantIdFor(pair.Key));
            if (result.Status == RespawnStatus.Respawned)
            {
                var health = result.Respawn!.After;
                _lifeGenerations[pair.Key] = checked(_lifeGenerations[pair.Key] + 1);
                QueueV2EpochRebuild(pair.Key);
                _avatars[pair.Key].ResetForRespawn(
                    pair.Value.SpawnPosition,
                    _simulationTick,
                    Mathf.Pi,
                    checked((long)Math.Round(health.CurrentHealth)),
                    checked((long)Math.Round(health.EffectiveMaximumHealth)));
                _outboundEvents.Add(new AuthorityEvent
                {
                    EventSequence = _nextEventSequence++,
                    AuthorityTick = _simulationTick,
                    LifeState = new LifeStateEvent
                    {
                        CombatantId = pair.Key,
                        LifeId = _lifeGenerations[pair.Key],
                        State = ReplicatedLifeState.Alive,
                        RemainingLives = int.MaxValue,
                    },
                });
                _eliminations.Remove(pair.Key);
            }
        }

        ProcessCombatFacts();
    }

    private void RecordPoseHistory()
    {
        foreach (var avatar in _avatars.Values)
        {
            if (!_poseHistory.TryGetValue(avatar.CombatantId, out var history))
            {
                history = [];
                _poseHistory.Add(avatar.CombatantId, history);
            }

            history.Enqueue(new HistoricalPose(_simulationTick, avatar.Position));
            while (history.Count > MaximumPoseHistoryTicks)
            {
                history.Dequeue();
            }
        }
    }

    private Vector3 RewoundTargetPosition(
        NetworkAvatar source,
        NetworkAvatar target)
    {
        if (!_lagCompensationEnabled)
        {
            return target.Position;
        }

        var player = _authorityService!.ConnectedPlayers.FirstOrDefault(
            candidate => candidate.CombatantId == source.CombatantId);
        if (player is null ||
            !_latencyByPeer.TryGetValue(player.ConnectionId, out var latency) ||
            latency.RewindAllowanceMilliseconds <= 0d ||
            !_poseHistory.TryGetValue(target.CombatantId, out var history))
        {
            return target.Position;
        }

        var requestedAgeTicks = source.AttackState is { } attack &&
            _simulationTick >= (ulong)attack.StartedAt.Tick
                ? _simulationTick - (ulong)attack.StartedAt.Tick
                : 0;
        var rewindTicks = Math.Min(
            checked((int)Math.Min(requestedAgeTicks, int.MaxValue)),
            Math.Min(
            MaximumPoseHistoryTicks - 1,
            (int)Math.Round(
                latency.RewindAllowanceMilliseconds *
                Engine.PhysicsTicksPerSecond /
                1000d)));
        var targetTick = _simulationTick > (ulong)rewindTicks
            ? _simulationTick - (ulong)rewindTicks
            : 0;
        var pose = history.FirstOrDefault(candidate => candidate.Tick >= targetTick) ??
            history.LastOrDefault();
        latency.RecordRewind(
            requestedAgeTicks * (1000d / Engine.PhysicsTicksPerSecond),
            rewindTicks * (1000d / Engine.PhysicsTicksPerSecond));
        return pose?.Position ?? target.Position;
    }

    private bool IsInsideMeleeQuery(
        NetworkAvatar source,
        NetworkAvatar target,
        Vector3 targetPosition)
    {
        var offset = targetPosition - source.Position;
        if (Mathf.Abs(offset.Y) > MeleeVerticalReach)
        {
            return false;
        }

        var planar = new Vector2(offset.X, offset.Z);
        if (planar.LengthSquared() > MeleeReach * MeleeReach ||
            planar.LengthSquared() < 0.0001f)
        {
            return false;
        }

        var yaw = (float)source.MovementState.FacingYawRadians;
        var forward = new Vector2(-Mathf.Sin(yaw), -Mathf.Cos(yaw));
        if (forward.Dot(planar.Normalized()) < Mathf.Cos(MeleeHalfAngleRadians))
        {
            return false;
        }

        var worldQuery = PhysicsRayQueryParameters3D.Create(
            source.Position + (Vector3.Up * 0.9f),
            target.Position + (Vector3.Up * 0.9f),
            collisionMask: 1);
        worldQuery.CollideWithAreas = false;
        worldQuery.CollideWithBodies = true;
        worldQuery.Exclude = new Godot.Collections.Array<Rid>
        {
            source.GetRid(),
            target.GetRid(),
        };
        return GetWorld3D().DirectSpaceState.IntersectRay(worldQuery).Count == 0;
    }

    private void ObserveSnapshotAcknowledgement(
        TransportConnectionId connectionId,
        ulong acknowledgedSequence)
    {
        if (acknowledgedSequence == 0 ||
            !_snapshotSendTimes.TryGetValue(connectionId, out var sendTimes) ||
            !sendTimes.TryGetValue(acknowledgedSequence, out var sentAt) ||
            !_latencyByPeer.TryGetValue(connectionId, out var latency))
        {
            return;
        }

        var sampleMilliseconds = (Time.GetTicksUsec() - sentAt) / 1000d;
        latency.Observe(sampleMilliseconds);
        foreach (var sequence in sendTimes.Keys
                     .Where(sequence => sequence <= acknowledgedSequence)
                     .ToArray())
        {
            sendTimes.Remove(sequence);
        }
    }

    private void SendInputBatch()
    {
        var identity = _clientService!.Identity!;
        var authorityConnection = _clientService.AuthorityConnection!.Value;
        var estimatedAuthorityTick = EstimateCurrentAuthorityTick();
        var frames = new List<ClientInputFrame>();
        foreach (var input in _predictionHistory.TakeLast(RedundantInputFrameCount))
        {
            var elapsed = _simulationTick >= input.ClientTick
                ? _simulationTick - input.ClientTick
                : 0;
            var inputAuthorityTick = estimatedAuthorityTick > elapsed
                ? estimatedAuthorityTick - elapsed
                : 1;
            var frame = input.ToProtocol();
            frame.EstimatedAuthorityTick = inputAuthorityTick;
            frame.ButtonBits &= ProtocolConstants.KnownPredictionMovementButtonMask;
            frame.PressedButtonBits &= ProtocolConstants.KnownPredictionMovementButtonMask;
            frame.ReleasedButtonBits &= ProtocolConstants.KnownPredictionMovementButtonMask;
            frames.Add(frame);
        }

        var bundle = _movementBundleFactory.Create(
            _nextPredictionBundleSequence++,
            identity.CombatantId,
            _localLifeId,
            frames,
            rollbackState: null);
        bundle.LatestAuthoritySequence = _latestAuthorityEnvelopeSequence;

        var envelope = new PacketEnvelope
        {
            ProtocolVersion = ProtocolConstants.CurrentVersion,
            SessionId = identity.SessionId,
            Sequence = _nextEnvelopeSequence++,
            SimulationTick = _simulationTick,
            MovementPredictionBundle = bundle.Clone(),
        };
        _transport.Send(new OutboundTransportPacket(
            authorityConnection,
            TransportChannel.Input,
            TransportDelivery.Unreliable,
            _codec.Encode(envelope)));
        _clientPredictionControl?.PublishMovement(bundle);
    }

    private ulong EstimateCurrentAuthorityTick()
    {
        if (_authorityClock.HasEstimate)
        {
            var estimate = _authorityClock.Estimate(
                _networkTimeSource.GetTimestampMicroseconds()).SimulationTick;
            return Math.Max(1UL, checked((ulong)Math.Floor(estimate)));
        }

        return Math.Max(1UL, Math.Max(_latestAuthoritySnapshotTick, _simulationTick));
    }

    private void SendActionRequest(ulong clientTick)
    {
        var identity = _clientService!.Identity!;
        var envelope = new PacketEnvelope
        {
            ProtocolVersion = ProtocolConstants.CurrentVersion,
            SessionId = identity.SessionId,
            Sequence = _nextEnvelopeSequence++,
            SimulationTick = _simulationTick,
            ClientActionRequest = new ClientActionRequest
            {
                ActionSequence = _nextActionSequence++,
                ClientTick = clientTick,
                Kind = ClientActionKind.Attack,
                EquipmentSlot = 0,
                SourceLifeId = _localLifeId,
            },
        };
        _transport.Send(new OutboundTransportPacket(
            _clientService.AuthorityConnection!.Value,
            TransportChannel.Action,
            TransportDelivery.ReliableOrdered,
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
                ?.Inputs.LastProcessedSequence ?? 0;
            snapshot.Combatants.Add(ToSnapshot(avatar, processedInput));
        }

        foreach (var player in _authorityService!.ConnectedPlayers)
        {
            if (_disconnectedConnections.Contains(player.ConnectionId))
            {
                continue;
            }

            var peerSnapshot = snapshot.Clone();
            if (_latencyByPeer.TryGetValue(player.ConnectionId, out var latency))
            {
                peerSnapshot.MeasuredRttMilliseconds = (float)latency.SmoothedRttMilliseconds;
                peerSnapshot.MeasuredJitterMilliseconds = (float)latency.JitterMilliseconds;
                peerSnapshot.RewindAllowanceMilliseconds = (float)latency.RewindAllowanceMilliseconds;
                peerSnapshot.LastRequestedAttackAgeMilliseconds =
                    (float)latency.LastRequestedAttackAgeMilliseconds;
                peerSnapshot.LastActualRewindMilliseconds =
                    (float)latency.LastActualRewindMilliseconds;
            }

            peerSnapshot.LagCompensationEnabled = _lagCompensationEnabled;
            var envelope = new PacketEnvelope
            {
                ProtocolVersion = ProtocolConstants.CurrentVersion,
                SessionId = _authorityService.SessionId,
                Sequence = _nextEnvelopeSequence++,
                SimulationTick = _simulationTick,
                AuthoritySnapshot = peerSnapshot,
            };
            _snapshotSendTimes[player.ConnectionId][envelope.Sequence] = Time.GetTicksUsec();
            _transport.Send(new OutboundTransportPacket(
                player.ConnectionId,
                TransportChannel.Snapshot,
                TransportDelivery.Unreliable,
                _codec.Encode(envelope)));
        }
    }

    private void SendAuthorityMovementFrames()
    {
        var batch = new AuthorityMovementFrameBatch
        {
            StreamSequence = _nextMovementStreamSequence++,
        };
        foreach (var avatar in _avatars.Values)
        {
            var processedInput = _remoteInputs.Values
                .FirstOrDefault(buffer => buffer.Avatar == avatar)
                ?.Inputs.LastProcessedSequence ?? 0;
            batch.Combatants.Add(ToMovementState(avatar, processedInput));
        }

        foreach (var player in _authorityService!.ConnectedPlayers)
        {
            if (_disconnectedConnections.Contains(player.ConnectionId))
            {
                continue;
            }

            var envelope = new PacketEnvelope
            {
                ProtocolVersion = ProtocolConstants.CurrentVersion,
                SessionId = _authorityService.SessionId,
                Sequence = _nextEnvelopeSequence++,
                SimulationTick = _simulationTick,
                AuthorityMovementFrameBatch = batch,
            };
            _transport.Send(new OutboundTransportPacket(
                player.ConnectionId,
                TransportChannel.Movement,
                TransportDelivery.Unreliable,
                _codec.Encode(envelope)));
        }
    }

    private void RecordAcceptedMovement(
        SessionPeerId sourcePeerId,
        ConnectionGeneration connectionGeneration,
        ulong combatantId,
        RevisionedMovementCommand input)
    {
        if (input.Command.Sequence == 0)
        {
            return;
        }

        _movementRelayBuffer.Record(new AcceptedMovementCommand(
            sourcePeerId,
            connectionGeneration,
            combatantId,
            _lifeGenerations.GetValueOrDefault(combatantId, 1UL),
            _simulationTick,
            input.Command,
            input.MovementProfileRevision,
            input.MovementCapabilityRevision));
    }

    private void SendAcceptedMovementCommands()
    {
        var recent = _movementRelayBuffer.GetRecent(RedundantAcceptedCommandCount);
        if (recent.Count == 0)
        {
            return;
        }

        var streamSequence = _nextAcceptedMovementStreamSequence++;

        foreach (var player in _authorityService!.ConnectedPlayers)
        {
            if (_disconnectedConnections.Contains(player.ConnectionId))
            {
                continue;
            }

            // The owning client already predicts these commands locally. Each
            // recipient only needs the other combatants' accepted commands.
            var recipientBatch = new AuthorityAcceptedMovementBatch
            {
                StreamSequence = streamSequence,
            };
            recipientBatch.Commands.Add(recent
                .Where(command => command.CombatantId != player.CombatantId)
                .Select(MovementCommandProtocolMapper.ToProtocol));
            if (recipientBatch.Commands.Count == 0)
            {
                continue;
            }

            var envelope = new PacketEnvelope
            {
                ProtocolVersion = ProtocolConstants.CurrentVersion,
                SessionId = _authorityService.SessionId,
                Sequence = _nextEnvelopeSequence++,
                SimulationTick = _simulationTick,
                AuthorityAcceptedMovementBatch = recipientBatch,
            };
            _transport.Send(new OutboundTransportPacket(
                player.ConnectionId,
                TransportChannel.Movement,
                TransportDelivery.Unreliable,
                _codec.Encode(envelope)));
        }
    }

    private void SendClockSyncProbe()
    {
        var sequence = _nextClockProbeSequence++;
        var sentAt = _networkTimeSource.GetTimestampMicroseconds();
        _outstandingClockProbes[sequence] = sentAt;
        while (_outstandingClockProbes.Count > MaximumOutstandingClockProbes)
        {
            _outstandingClockProbes.Remove(_outstandingClockProbes.Keys.Min());
        }

        var envelope = new PacketEnvelope
        {
            ProtocolVersion = ProtocolConstants.CurrentVersion,
            SessionId = _clientService!.Identity!.SessionId,
            Sequence = _nextEnvelopeSequence++,
            SimulationTick = _simulationTick,
            ClockSyncProbe = new ClockSyncProbe
            {
                ProbeSequence = sequence,
                ClientSendTimestampMicroseconds = sentAt,
            },
        };
        _transport.Send(new OutboundTransportPacket(
            _clientService.AuthorityConnection!.Value,
            TransportChannel.Timing,
            TransportDelivery.Unreliable,
            _codec.Encode(envelope)));
    }

    private void OnPacketReceived(InboundTransportPacket packet)
    {
        if (_mode == ArenaMode.Client)
        {
            QueueClientAuthorityPacket(packet);
            return;
        }

        if (packet.Channel == TransportChannel.Input)
        {
            ReceiveClientInput(packet);
        }
        else if (packet.Channel == TransportChannel.Action)
        {
            ReceiveClientAction(packet);
        }
        else if (packet.Channel == TransportChannel.Timing)
        {
            ReceiveClockSyncProbe(packet);
        }
    }

    private void QueueClientAuthorityPacket(InboundTransportPacket packet)
    {
        if (packet.Sender != _clientService?.AuthorityConnection ||
            packet.Channel is not (
                TransportChannel.Snapshot or
                TransportChannel.PresentationEvent or
                TransportChannel.Timing or
                TransportChannel.Movement))
        {
            return;
        }

        _pendingClientAuthorityPackets.Enqueue(
            packet,
            _networkTimeSource.GetTimestampMicroseconds());
    }

    private void DrainClientAuthorityPacketsAtPhysicsBoundary()
    {
        if (!Engine.IsInPhysicsFrame())
        {
            throw new InvalidOperationException(
                "Client authority packets may only be applied during fixed physics.");
        }

        while (_pendingClientAuthorityPackets.TryDequeue(out var pending))
        {
            var packet = pending.Packet;
            switch (packet.Channel)
            {
                case TransportChannel.Snapshot:
                    ReceiveAuthoritySnapshot(packet, pending.ReceivedAtMicroseconds);
                    break;
                case TransportChannel.PresentationEvent:
                    ReceiveAuthorityEvents(packet);
                    break;
                case TransportChannel.Timing:
                    ReceiveClockSyncReply(packet, pending.ReceivedAtMicroseconds);
                    break;
                case TransportChannel.Movement:
                    ReceiveAuthorityMovementPacket(packet, pending.ReceivedAtMicroseconds);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Queued unsupported authority channel {packet.Channel}.");
            }
        }
    }

    private void ReceiveClientAction(InboundTransportPacket packet)
    {
        if (!_remoteInputs.TryGetValue(packet.Sender, out var inputBuffer) ||
            !_clientActionSequences.TryGetValue(packet.Sender, out var sequenceTracker))
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
            decoded.Envelope!.PayloadCase != PacketEnvelope.PayloadOneofCase.ClientActionRequest ||
            !sequenceTracker.Observe(
                decoded.Envelope.ClientActionRequest.ActionSequence).ShouldProcess)
        {
            return;
        }

        var player = _authorityService.ConnectedPlayers.FirstOrDefault(
            candidate => candidate.ConnectionId == packet.Sender);
        var request = decoded.Envelope.ClientActionRequest;
        if (player is null ||
            request.Kind != ClientActionKind.Attack ||
            request.SourceLifeId != _lifeGenerations.GetValueOrDefault(player.CombatantId, 1UL) ||
            request.ClientTick + MaximumPredictionHistory < _simulationTick ||
            request.ClientTick > _simulationTick + MaximumPredictionHistory)
        {
            return;
        }

        inputBuffer.Inputs.AuthorizeAttack(
            new SimulationInstant(checked((long)request.ClientTick)));
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
            decoded.Envelope!.PayloadCase !=
                PacketEnvelope.PayloadOneofCase.MovementPredictionBundle)
        {
            return;
        }

        if (!sequenceTracker.Observe(decoded.Envelope.Sequence).ShouldProcess)
        {
            return;
        }

        var bundle = decoded.Envelope.MovementPredictionBundle;
        var player = _authorityService!.ConnectedPlayers.FirstOrDefault(
            candidate => candidate.ConnectionId == packet.Sender);
        if (player is null ||
            bundle.SourceCombatantId != player.CombatantId ||
            bundle.SourceLifeId !=
                _lifeGenerations.GetValueOrDefault(player.CombatantId, 1UL))
        {
            return;
        }

        foreach (var frame in bundle.Commands)
        {
            var oldestAcceptedTick = _simulationTick > MaximumPredictionHistory
                ? _simulationTick - MaximumPredictionHistory
                : 0;
            var newestAcceptedTick = _simulationTick <= ulong.MaxValue - MaximumPredictionHistory
                ? _simulationTick + MaximumPredictionHistory
                : ulong.MaxValue;
            if (frame.ClientTick < oldestAcceptedTick ||
                frame.ClientTick > newestAcceptedTick)
            {
                continue;
            }

            if (_schedulingHost is not null)
            {
                AdmitRemoteOwnerCommandV2(player.CombatantId, frame);
            }
            else
            {
                inputBuffer.Inputs.Enqueue(RevisionedMovementCommand.FromProtocol(frame));
            }
        }

        ObserveSnapshotAcknowledgement(
            packet.Sender,
            bundle.LatestAuthoritySequence);

        _acceptedInputPackets++;
    }

    private void OnDirectPredictionPacketReceived(AuthenticatedPredictionPacket packet)
    {
        if (_mode != ArenaMode.Client ||
            packet.Delivery != TransportDelivery.Unreliable ||
            packet.Envelope.PayloadCase !=
                PredictionPacketEnvelope.PayloadOneofCase.MovementPredictionBundle ||
            _clientPredictionControl is null ||
            !_clientPredictionControl.TryGetPeer(packet.Sender, out var peer))
        {
            return;
        }

        var bundle = packet.Envelope.MovementPredictionBundle;
        if (peer.IsAuthority ||
            bundle.SourceCombatantId != peer.CombatantId ||
            !IsCurrentClientLifeEvidence(
                bundle.SourceCombatantId,
                bundle.SourceLifeId,
                ClientPredictionEvidenceRoute.DirectMovementHint))
        {
            return;
        }

        if (!_remoteMovementTimelines.TryGetValue(peer.CombatantId, out var timeline))
        {
            timeline = new RemoteMovementTimeline<AuthoritativeMovementState>();
            _remoteMovementTimelines.Add(peer.CombatantId, timeline);
        }

        foreach (var frame in bundle.Commands)
        {
            timeline.ObserveDirect(new DirectMovementCommand(
                peer.Id,
                peer.PeerSessionGeneration,
                peer.CombatantId,
                bundle.SourceLifeId,
                bundle.BundleSequence,
                frame.EstimatedAuthorityTick,
                MovementCommandProtocolMapper.FromProtocol(frame),
                frame.MovementProfileRevision,
                frame.MovementCapabilityRevision));
        }

        _acceptedDirectPredictionBundles++;
    }

    private void ReceiveAuthorityMovementPacket(
        InboundTransportPacket packet,
        ulong receivedAtMicroseconds)
    {
        if (packet.Sender != _clientService!.AuthorityConnection)
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
        if (!validation.IsValid)
        {
            return;
        }

        switch (decoded.Envelope!.PayloadCase)
        {
            case PacketEnvelope.PayloadOneofCase.AuthorityMovementFrameBatch:
                ReceiveAuthorityMovementFrames(
                    decoded.Envelope,
                    receivedAtMicroseconds);
                break;
            case PacketEnvelope.PayloadOneofCase.AuthorityAcceptedMovementBatch:
                ReceiveAcceptedMovementCommands(decoded.Envelope);
                break;
        }
    }

    private void ReceiveAuthorityMovementFrames(
        PacketEnvelope envelope,
        ulong receivedAtMicroseconds)
    {
        if (!_authorityMovementStreamSequences.Observe(
                envelope.AuthorityMovementFrameBatch.StreamSequence).ShouldProcess)
        {
            return;
        }

        _authorityMovementPath.ObservePacket(
            envelope.AuthorityMovementFrameBatch.StreamSequence,
            receivedAtMicroseconds);
        var identity = _clientService!.Identity!;
        foreach (var state in envelope.AuthorityMovementFrameBatch.Combatants)
        {
            if (!TryObserveClientStateBaseline(
                    state.CombatantId,
                    state.LifeId,
                    ClientPredictionStateBaselineSource.AuthorityMovement,
                    out _))
            {
                continue;
            }

            if (state.CombatantId == identity.CombatantId)
            {
                QueueLocalAuthoritativeMovement(
                    envelope.SimulationTick,
                    state);
                continue;
            }

            if (!_avatars.ContainsKey(state.CombatantId))
            {
                // The next broader snapshot supplies display and life metadata,
                // then subsequent compact frames can drive presentation.
                continue;
            }

            AddRemoteMovementSample(
                state.CombatantId,
                envelope.SimulationTick,
                state);
        }

        _latestAuthoritySnapshotTick = Math.Max(
            _latestAuthoritySnapshotTick,
            envelope.SimulationTick);
        _lastMovementArrivalMicroseconds = receivedAtMicroseconds;
        _acceptedMovementFrames++;
    }

    private void ReceiveAcceptedMovementCommands(PacketEnvelope envelope)
    {
        var batch = envelope.AuthorityAcceptedMovementBatch;
        if (!_authorityAcceptedMovementSequences.Observe(batch.StreamSequence).ShouldProcess)
        {
            return;
        }

        var localCombatantId = _clientService!.Identity!.CombatantId;
        foreach (var protocolCommand in batch.Commands)
        {
            if (protocolCommand.CombatantId == localCombatantId)
            {
                continue;
            }

            if (!IsCurrentClientLifeEvidence(
                    protocolCommand.CombatantId,
                    protocolCommand.LifeId,
                    ClientPredictionEvidenceRoute.AcceptedMovement))
            {
                continue;
            }

            var command = MovementCommandProtocolMapper.FromProtocol(protocolCommand);
            if (!_remoteMovementTimelines.TryGetValue(command.CombatantId, out var timeline))
            {
                timeline = new RemoteMovementTimeline<AuthoritativeMovementState>();
                _remoteMovementTimelines.Add(command.CombatantId, timeline);
            }

            timeline.ObserveAccepted(command);
        }

        _acceptedMovementRelayBatches++;
    }

    private void ReceiveClockSyncProbe(InboundTransportPacket packet)
    {
        if (!_clockProbeSequences.TryGetValue(packet.Sender, out var sequenceTracker))
        {
            return;
        }

        var authorityReceivedAt = _networkTimeSource.GetTimestampMicroseconds();
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
            decoded.Envelope!.PayloadCase != PacketEnvelope.PayloadOneofCase.ClockSyncProbe ||
            !sequenceTracker.Observe(decoded.Envelope.ClockSyncProbe.ProbeSequence).ShouldProcess)
        {
            return;
        }

        var authoritySentAt = _networkTimeSource.GetTimestampMicroseconds();
        var probe = decoded.Envelope.ClockSyncProbe;
        var reply = new PacketEnvelope
        {
            ProtocolVersion = ProtocolConstants.CurrentVersion,
            SessionId = _authorityService.SessionId,
            Sequence = _nextEnvelopeSequence++,
            SimulationTick = _simulationTick,
            ClockSyncReply = new ClockSyncReply
            {
                ProbeSequence = probe.ProbeSequence,
                ClientSendTimestampMicroseconds = probe.ClientSendTimestampMicroseconds,
                AuthorityReceiveTimestampMicroseconds = authorityReceivedAt,
                AuthoritySendTimestampMicroseconds = authoritySentAt,
                AuthorityTick = _simulationTick,
            },
        };
        _transport.Send(new OutboundTransportPacket(
            packet.Sender,
            TransportChannel.Timing,
            TransportDelivery.Unreliable,
            _codec.Encode(reply)));
    }

    private void ReceiveClockSyncReply(
        InboundTransportPacket packet,
        ulong receivedAtMicroseconds)
    {
        if (packet.Sender != _clientService!.AuthorityConnection)
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
                RemoteEndpointRole.Authority,
                _clientService.Identity!.SessionId,
                SessionEstablished: true));
        if (!validation.IsValid ||
            decoded.Envelope!.PayloadCase != PacketEnvelope.PayloadOneofCase.ClockSyncReply ||
            !_clockReplySequences.Observe(
                decoded.Envelope.ClockSyncReply.ProbeSequence).ShouldProcess)
        {
            return;
        }

        var reply = decoded.Envelope.ClockSyncReply;
        if (!_outstandingClockProbes.TryGetValue(reply.ProbeSequence, out var sentAt) ||
            sentAt != reply.ClientSendTimestampMicroseconds)
        {
            return;
        }

        foreach (var sequence in _outstandingClockProbes.Keys
                     .Where(sequence => sequence <= reply.ProbeSequence)
                     .ToArray())
        {
            _outstandingClockProbes.Remove(sequence);
        }

        _authorityClock.Observe(new AuthorityClockExchange(
            sentAt,
            reply.AuthorityReceiveTimestampMicroseconds,
            reply.AuthoritySendTimestampMicroseconds,
            receivedAtMicroseconds,
            reply.AuthorityTick,
            _clientService.Identity.SimulationTicksPerSecond));
        var authorityProcessing =
            reply.AuthoritySendTimestampMicroseconds -
            reply.AuthorityReceiveTimestampMicroseconds;
        var localElapsed = receivedAtMicroseconds - sentAt;
        _authorityMovementPath.ObserveRoundTrip(
            Math.Max(0d, (localElapsed - Math.Min(localElapsed, authorityProcessing)) / 1_000d));
    }

    private void ReceiveAuthoritySnapshot(
        InboundTransportPacket packet,
        ulong receivedAtMicroseconds)
    {
        if (packet.Sender != _clientService!.AuthorityConnection)
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
            if (!TryObserveClientStateBaseline(
                    combatant.CombatantId,
                    combatant.LifeId,
                    ClientPredictionStateBaselineSource.Snapshot,
                    out _))
            {
                continue;
            }

            if (combatant.LifeState != ReplicatedLifeState.Alive &&
                combatant.RespawnAtTick > decoded.Envelope.SimulationTick &&
                _avatars.TryGetValue(combatant.CombatantId, out var existingAvatar))
            {
                existingAvatar.SetRespawnCountdown(
                    (combatant.RespawnAtTick - decoded.Envelope.SimulationTick) /
                    (double)Engine.PhysicsTicksPerSecond);
            }

            if (combatant.CombatantId == identity.CombatantId)
            {
                _localAvatar.ApplyReplicatedMetadata(
                    combatant,
                    decoded.Envelope.SimulationTick);
                QueueLocalAuthoritativeMovement(
                    decoded.Envelope.SimulationTick,
                    ToMovementState(combatant));
                continue;
            }

            if (!_avatars.ContainsKey(combatant.CombatantId))
            {
                var spawnedAvatar = SpawnAvatar(
                    combatant.CombatantId,
                    string.IsNullOrWhiteSpace(combatant.DisplayName)
                        ? $"Combatant {combatant.CombatantId}"
                        : combatant.DisplayName,
                    locallyControlled: false,
                    collisionEnabled: true,
                    Color.FromHsv(
                        (float)((combatant.CombatantId * 0.173d) % 1d),
                        0.72f,
                        0.95f),
                    ToGodot(combatant.Position),
                    combatant.ViewYawRadians,
                    combatant.BodyFacingYawRadians);
                spawnedAvatar.RequireFixedPhysicsBlockingCommits();
                _remoteTimingPolicies[combatant.CombatantId] =
                    new AdaptivePredictionTimingPolicy();
                _remoteMovementTimelines.TryAdd(
                    combatant.CombatantId,
                    new RemoteMovementTimeline<AuthoritativeMovementState>());
            }

            _avatars[combatant.CombatantId].ApplyReplicatedMetadata(
                combatant,
                decoded.Envelope.SimulationTick);
            ObserveRemoteCommitSmokeSnapshot(
                combatant,
                _avatars[combatant.CombatantId]);
            AddRemoteMovementSample(
                combatant.CombatantId,
                decoded.Envelope.SimulationTick,
                ToMovementState(combatant));
        }

        _latestAuthoritySnapshotTick = decoded.Envelope.SimulationTick;
        _latestAuthorityEnvelopeSequence = decoded.Envelope.Sequence;
        _measuredRttMilliseconds =
            decoded.Envelope.AuthoritySnapshot.MeasuredRttMilliseconds;
        _measuredJitterMilliseconds =
            decoded.Envelope.AuthoritySnapshot.MeasuredJitterMilliseconds;
        _rewindAllowanceMilliseconds =
            decoded.Envelope.AuthoritySnapshot.RewindAllowanceMilliseconds;
        _lastRequestedAttackAgeMilliseconds =
            decoded.Envelope.AuthoritySnapshot.LastRequestedAttackAgeMilliseconds;
        _lastActualRewindMilliseconds =
            decoded.Envelope.AuthoritySnapshot.LastActualRewindMilliseconds;
        _lagCompensationEnabled =
            decoded.Envelope.AuthoritySnapshot.LagCompensationEnabled;
        _lastSnapshotArrivalMicroseconds = receivedAtMicroseconds;
        _acceptedSnapshots++;
    }

    private void SendAuthorityEvents()
    {
        if (_outboundEvents.Count == 0)
        {
            return;
        }

        foreach (var player in _authorityService!.ConnectedPlayers)
        {
            if (_disconnectedConnections.Contains(player.ConnectionId))
            {
                continue;
            }

            var batch = new AuthorityEventBatch();
            batch.Events.AddRange(_outboundEvents);
            var envelope = new PacketEnvelope
            {
                ProtocolVersion = ProtocolConstants.CurrentVersion,
                SessionId = _authorityService.SessionId,
                Sequence = _nextEnvelopeSequence++,
                SimulationTick = _simulationTick,
                AuthorityEventBatch = batch,
            };
            _transport.Send(new OutboundTransportPacket(
                player.ConnectionId,
                TransportChannel.PresentationEvent,
                TransportDelivery.Unreliable,
                _codec.Encode(envelope)));
        }

        _outboundEvents.Clear();
    }

    private void ReceiveAuthorityEvents(InboundTransportPacket packet)
    {
        if (packet.Sender != _clientService!.AuthorityConnection)
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
                RemoteEndpointRole.Authority,
                _clientService.Identity!.SessionId,
                SessionEstablished: true));
        if (!validation.IsValid ||
            decoded.Envelope!.PayloadCase != PacketEnvelope.PayloadOneofCase.AuthorityEventBatch ||
            !_authorityEventEnvelopeSequences.Observe(decoded.Envelope.Sequence).ShouldProcess)
        {
            return;
        }

        foreach (var authorityEvent in decoded.Envelope.AuthorityEventBatch.Events)
        {
            switch (authorityEvent.EventCase)
            {
                case AuthorityEvent.EventOneofCase.ActionState:
                    if (IsCurrentClientLifeEvidence(
                            authorityEvent.ActionState.SourceCombatantId,
                            authorityEvent.ActionState.SourceLifeId,
                            ClientPredictionEvidenceRoute.Action) &&
                        _avatars.TryGetValue(
                            authorityEvent.ActionState.SourceCombatantId,
                            out var attacker))
                    {
                        attacker.PlayReplicatedAttack(
                            authorityEvent.ActionState,
                            authorityEvent.AuthorityTick);
                    }

                    break;
                case AuthorityEvent.EventOneofCase.Damage:
                    if (IsCurrentClientLifeEvidence(
                            authorityEvent.Damage.TargetCombatantId,
                            authorityEvent.Damage.TargetLifeId,
                            ClientPredictionEvidenceRoute.DamageTarget) &&
                        _avatars.TryGetValue(
                            authorityEvent.Damage.TargetCombatantId,
                            out var target))
                    {
                        target.SetHealth(
                            authorityEvent.Damage.CurrentHealth,
                            authorityEvent.Damage.MaximumHealth);
                        target.ShowDamage(authorityEvent.Damage.NetDamage);
                    }

                    if (IsCurrentClientLifeEvidence(
                            authorityEvent.Damage.SourceCombatantId,
                            authorityEvent.Damage.SourceLifeId,
                            ClientPredictionEvidenceRoute.DamageSource) &&
                        _avatars.TryGetValue(
                            authorityEvent.Damage.SourceCombatantId,
                            out var source))
                    {
                        source.ShowHitConfirmation();
                    }

                    break;
                case AuthorityEvent.EventOneofCase.LifeState:
                    if (!ObserveClientLifeNotification(
                            authorityEvent.LifeState.CombatantId,
                            authorityEvent.LifeState.LifeId))
                    {
                        break;
                    }

                    if (authorityEvent.LifeState.State == ReplicatedLifeState.Eliminated &&
                        _avatars.TryGetValue(
                            authorityEvent.LifeState.CombatantId,
                            out var eliminatedAvatar))
                    {
                        eliminatedAvatar.SetEliminated(
                            true,
                            authorityEvent.AuthorityTick);
                        ObserveRemoteCommitSmokeElimination(
                            authorityEvent.LifeState.CombatantId,
                            authorityEvent.LifeState.LifeId,
                            eliminatedAvatar);
                    }

                    break;
            }
        }
    }

    private bool TryObserveClientStateBaseline(
        ulong combatantId,
        ulong lifeId,
        ClientPredictionStateBaselineSource source,
        out ClientPredictionBaselineResult result)
    {
        result = default;
        if (!TryCreateClientEpoch(combatantId, lifeId, out var baseline) ||
            _clientPredictionLifecycle is null)
        {
            return false;
        }

        result = _clientPredictionLifecycle.ObserveStateBaseline(baseline, source);
        if (!result.IsAccepted)
        {
            return false;
        }

        _lifeGenerations[combatantId] = lifeId;
        if (combatantId == _clientService!.Identity!.CombatantId)
        {
            _localLifeId = lifeId;
        }

        return true;
    }

    private bool IsCurrentClientLifeEvidence(
        ulong combatantId,
        ulong lifeId,
        ClientPredictionEvidenceRoute route)
    {
        if (!TryCreateClientEpoch(combatantId, lifeId, out var evidence) ||
            _clientPredictionLifecycle is null)
        {
            return false;
        }

        return _clientPredictionLifecycle.EvaluateEvidence(evidence, route) ==
            PredictionEpochEvidenceDecision.Accepted;
    }

    private bool ObserveClientLifeNotification(ulong combatantId, ulong lifeId)
    {
        if (!TryCreateClientEpoch(combatantId, lifeId, out var notification) ||
            _clientPredictionLifecycle is null)
        {
            return false;
        }

        // A future-life event has no spawn pose/runtime baseline. The router
        // stages it and this adapter performs no state change until a snapshot
        // or authority movement frame commits that life.
        return _clientPredictionLifecycle.ObserveLifeNotification(notification) ==
            ClientLifeNotificationDecision.Current;
    }

    private bool TryCreateClientEpoch(
        ulong combatantId,
        ulong lifeId,
        out CombatantAuthorityPredictionEpoch epoch)
    {
        epoch = default;
        if (_mode != ArenaMode.Client ||
            _clientService?.Identity is not { } identity ||
            combatantId == 0 ||
            combatantId > long.MaxValue ||
            lifeId == 0 ||
            lifeId > long.MaxValue)
        {
            return false;
        }

        // Protocol V1 carries only life here. Until protocol-next adds explicit
        // fields, state-bearing life baselines map discontinuity/control to the
        // same monotonic value. Stateless notifications never commit this bridge.
        epoch = new CombatantAuthorityPredictionEpoch(
            identity.SessionId,
            MatchFrameEpochId.Initial,
            new CombatantId(checked((long)combatantId)),
            new LifeGenerationId(checked((long)lifeId)),
            new AuthorityDiscontinuityId(lifeId),
            new OwnerControlEpoch(lifeId));
        return true;
    }

    private void OnClientPredictionEpochTransitioned(
        ClientPredictionEpochTransition transition)
    {
        var authority = transition.Current.Authority;
        var combatantId = checked((ulong)authority.CombatantId.Value);
        var lifeId = checked((ulong)authority.Life.Value);
        _lifeGenerations[combatantId] = lifeId;
        _remoteMovementTimelines.Remove(combatantId);
        _remoteRenderTicks.Remove(combatantId);
        _remoteCollisionCommitTicks.Remove(combatantId);
        _poseHistory.Remove(combatantId);
        if (_avatars.TryGetValue(combatantId, out var avatar))
        {
            if (_remoteCommitPhaseSmokeTest &&
                combatantId == HostCombatantId &&
                lifeId == 2 &&
                _remoteCommitSmokeLifecycleStage ==
                RemoteCommitSmokeLifecycleStage.LifeOneEliminated)
            {
                _remoteCommitSmokeLifecycleStage =
                    RemoteCommitSmokeLifecycleStage.LifeTwoEpochAccepted;
            }

            avatar.ResetPredictionForEpoch();
            // A state-bearing higher-life baseline is the authoritative respawn
            // boundary. Re-enable its collider in the same physics transaction;
            // the pose sample is committed after the inbound drain completes.
            avatar.SetEliminated(false, _simulationTick);
            if (_remoteCommitPhaseSmokeTest &&
                combatantId == HostCombatantId &&
                lifeId == 2 &&
                _remoteCommitSmokeLifecycleStage ==
                RemoteCommitSmokeLifecycleStage.LifeTwoEpochAccepted &&
                !avatar.CaptureBlockingBodySnapshot().CollisionDisabled)
            {
                _remoteCommitSmokeLifecycleStage =
                    RemoteCommitSmokeLifecycleStage.LifeTwoColliderEnabled;
            }
        }

        if (_clientService?.Identity?.CombatantId != combatantId)
        {
            return;
        }

        _localLifeId = lifeId;
        _predictionHistory.Clear();
        _pendingLocalMovement = null;
        _localGroundedOverride = null;
        _lastCorrectionDistance = 0f;
    }

    private void OnConnectionClosed(TransportConnectionId connectionId)
    {
        _disconnectedConnections.Add(connectionId);
        if (_mode == ArenaMode.Client && connectionId == _clientService!.AuthorityConnection)
        {
            _authorityAvailable = false;
        }
    }

    private void ReconcileLocalPrediction(AuthoritativeMovementState authoritative)
    {
        var predictedPosition = _localAvatar.Position;
        _predictionHistory.RemoveAll(input => input.Sequence <= authoritative.LastProcessedInputSequence);
        _localAvatar.ApplyAuthoritativeMovementState(authoritative);
        _clientFramePresentation.RecordStateOnlyStep(
            PredictionFrameStateOnlyStep.AuthorityRestore);

        if (_localAvatar.IsEliminated)
        {
            _predictionHistory.Clear();
            _localGroundedOverride = null;
        }
        else
        {
            bool? groundedOverride = authoritative.IsGrounded;
            foreach (var input in _predictionHistory)
            {
                _localAvatar.ReplayPredictedMovement(input, FixedDelta, groundedOverride);
                _clientFramePresentation.RecordStateOnlyStep(
                    PredictionFrameStateOnlyStep.HistoricalReplay);
                groundedOverride = null;
            }

            _localGroundedOverride = groundedOverride;
        }

        _lastCorrectionDistance = predictedPosition.DistanceTo(_localAvatar.Position);
    }

    /// <summary>
    /// Commits the newest canonical remote poses to collision bodies exactly at
    /// a fixed-physics boundary. Render interpolation deliberately reads the
    /// same timelines without mutating a CharacterBody3D.
    /// </summary>
    private void CommitRemoteCollisionBodiesAtPhysicsBoundary()
    {
        if (!Engine.IsInPhysicsFrame())
        {
            _remoteCollisionCommitPhaseViolationCount++;
            GD.PushError(
                "[NetworkArena] Rejected a remote collision commit outside fixed physics.");
            return;
        }

        var localCombatantId = _clientService?.Identity?.CombatantId;
        foreach (var pair in _remoteMovementTimelines)
        {
            if (pair.Key == localCombatantId ||
                !_avatars.TryGetValue(pair.Key, out var avatar) ||
                pair.Value.LatestAuthorityFrame is not { } latestFrame ||
                (_remoteCollisionCommitTicks.TryGetValue(pair.Key, out var committedTick) &&
                 committedTick >= latestFrame.AuthorityTick))
            {
                continue;
            }

            avatar.ApplyAuthoritativeMovementState(latestFrame.State);
            _remoteCollisionCommitTicks[pair.Key] = latestFrame.AuthorityTick;
            _remoteCollisionPhysicsCommitCount++;
            if (_remoteCommitPhaseSmokeTest &&
                pair.Key == HostCombatantId &&
                latestFrame.LifeId == 2 &&
                _remoteCommitSmokeLifecycleStage ==
                RemoteCommitSmokeLifecycleStage.LifeTwoColliderEnabled)
            {
                _remoteCommitSmokeLifecycleStage =
                    RemoteCommitSmokeLifecycleStage.LifeTwoPoseCommitted;
            }
        }
    }

    private Dictionary<ulong, RemoteRenderPhaseSample> CaptureRemoteRenderPhaseSamples()
    {
        var localCombatantId = _clientService?.Identity?.CombatantId;
        var samples = new Dictionary<ulong, RemoteRenderPhaseSample>();
        foreach (var pair in _avatars)
        {
            if (pair.Key == localCombatantId)
            {
                continue;
            }

            samples[pair.Key] = new RemoteRenderPhaseSample(
                pair.Value.CaptureBlockingBodySnapshot(),
                pair.Value.VisualPresentationPosition);
        }

        return samples;
    }

    private void VerifyRemoteRenderPhase(
        IReadOnlyDictionary<ulong, RemoteRenderPhaseSample> before)
    {
        foreach (var pair in before)
        {
            if (!_avatars.TryGetValue(pair.Key, out var avatar))
            {
                continue;
            }

            var afterBody = avatar.CaptureBlockingBodySnapshot();
            if (afterBody != pair.Value.BlockingBody)
            {
                _remoteRenderBodyMutationCount++;
                GD.PushError(
                    $"[NetworkArena] Remote combatant {pair.Key} mutated its " +
                    "blocking body during render processing.");
            }

            if (avatar.VisualPresentationPosition != pair.Value.VisualPosition)
            {
                _remoteVisualPositionChangeCount++;
            }
        }
    }

    private void InterpolateRemoteAvatars(double delta)
    {
        _remoteInterpolationLagTicks = 0;
        _currentPresentationDelayTicks = 0;
        _remotePredictedAvatarCount = 0;
        _remoteFrozenAvatarCount = 0;
        var simulationRate = _clientService?.Identity?.SimulationTicksPerSecond ?? 60U;
        var estimatedAuthorityTick = _authorityClock.HasEstimate
            ? _authorityClock.Estimate(
                _networkTimeSource.GetTimestampMicroseconds()).SimulationTick
            : _latestAuthoritySnapshotTick;
        foreach (var pair in _remoteMovementTimelines)
        {
            var timeline = pair.Value;
            if (!_avatars.TryGetValue(pair.Key, out var avatar) ||
                timeline.LatestAuthorityFrame is not { } latestFrame)
            {
                continue;
            }

            var latestTick = latestFrame.AuthorityTick;
            if (!_remoteTimingPolicies.TryGetValue(pair.Key, out var timingPolicy))
            {
                timingPolicy = new AdaptivePredictionTimingPolicy();
                _remoteTimingPolicies.Add(pair.Key, timingPolicy);
            }

            var measuredPath = _authorityMovementPath.Current;
            if (_clientPredictionControl?.TryGetDirectPathForCombatant(
                    pair.Key,
                    out var directPath) == true &&
                directPath.RouteHealth == PredictionRouteHealth.Authenticated &&
                directPath.Path.LatestSequence > 0)
            {
                measuredPath = directPath.Path;
            }

            var timing = timingPolicy.Evaluate(new PredictionTimingContext(
                simulationRate,
                measuredPath,
                RecentBufferUnderruns: 0));
            var interpolationDelayTicks = _remoteInterpolationEnabled
                ? checked((ulong)timing.PresentationDelayTicks)
                : 0;
            _currentPresentationDelayTicks = Math.Max(
                _currentPresentationDelayTicks,
                timing.PresentationDelayTicks);
            var desiredTargetTick = _remoteInterpolationEnabled
                ? Math.Max(0d, estimatedAuthorityTick - interpolationDelayTicks)
                : latestTick;
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
                    targetTick + (delta * simulationRate),
                    desiredTargetTick);
            }

            _remoteRenderTicks[pair.Key] = targetTick;
            var predictionCapabilityAvailable =
                latestFrame.State.MovementActionMode ==
                ReplicatedMovementActionMode.Ready;
            if (!predictionCapabilityAvailable && targetTick > latestTick)
            {
                targetTick = latestTick;
                _remoteRenderTicks[pair.Key] = targetTick;
            }

            _remoteInterpolationLagTicks = Math.Max(
                _remoteInterpolationLagTicks,
                Math.Max(0d, latestTick - targetTick));

            var window = timeline.Sample(
                targetTick,
                timing.NormalPredictionLimitTicks,
                timing.FreezeAfterTicks);
            if (window is null)
            {
                continue;
            }

            RemoteMovementPrediction remotePresentation;
            if (window.RequiresPrediction)
            {
                remotePresentation = _remoteMovementPredictor.Predict(
                    window.After,
                    window.PredictionCommands,
                    window.EffectiveTargetTick,
                    avatar.MovementAttributes);
                _remotePredictedAvatarCount++;
                if (window.Frozen)
                {
                    _remoteFrozenAvatarCount++;
                }
            }
            else
            {
                var before = window.Before;
                var after = window.After;
                var span = after.AuthorityTick - before.AuthorityTick;
                var weight = span == 0
                    ? 0f
                    : (float)((window.EffectiveTargetTick - before.AuthorityTick) / span);
                var position = ToGodot(before.State.Position).Lerp(
                    ToGodot(after.State.Position),
                    weight);
                var velocity = ToGodot(before.State.Velocity).Lerp(
                    ToGodot(after.State.Velocity),
                    weight);
                var yaw = Mathf.LerpAngle(
                    before.State.ViewYawRadians,
                    after.State.ViewYawRadians,
                    weight);
                var pitch = Mathf.Lerp(
                    before.State.ViewPitchRadians,
                    after.State.ViewPitchRadians,
                    weight);
                var bodyFacingYaw = Mathf.LerpAngle(
                    before.State.BodyFacingYawRadians,
                    after.State.BodyFacingYawRadians,
                    weight);
                var presentationState = weight < 0.5f ? before.State : after.State;
                var state = NetworkMovementStateMapper.FromMovementState(presentationState) with
                {
                    HorizontalVelocity = new BattleArena.Core.Movement.HorizontalVector(
                        velocity.X,
                        velocity.Z),
                    VerticalVelocity = velocity.Y,
                    FacingYawRadians = bodyFacingYaw,
                };
                remotePresentation = new RemoteMovementPrediction(
                    position,
                    velocity,
                    yaw,
                    pitch,
                    state);
            }

            avatar.ApplyRemotePresentation(remotePresentation);
            _remoteVisualRenderCommitCount++;
        }
    }

    private void UpdateStatus()
    {
        var horizontalSpeed = new Vector2(_localAvatar.Velocity.X, _localAvatar.Velocity.Z).Length();
        var snapshotAgeMilliseconds = _lastSnapshotArrivalMicroseconds == 0
            ? 0.0
            : (Time.GetTicksUsec() - _lastSnapshotArrivalMicroseconds) / 1000.0;
        var movementAgeMilliseconds = _lastMovementArrivalMicroseconds == 0
            ? 0.0
            : (_networkTimeSource.GetTimestampMicroseconds() -
               _lastMovementArrivalMicroseconds) / 1000.0;
        var interpolationLagMilliseconds = _remoteInterpolationLagTicks * (1000.0 / 60.0);
        var clock = _authorityClock.HasEstimate
            ? _authorityClock.Estimate(_networkTimeSource.GetTimestampMicroseconds())
            : default;
        _statusLabel.Text = _mode == ArenaMode.Authority
            ? $"HOST AUTHORITY  |  tick {_simulationTick}  |  speed {horizontalSpeed:0.0} m/s  |  snapshots 30 Hz  |  players {_avatars.Count}\n" +
              $"INPUT SCHEDULER  |  {FormatAuthorityInputDiagnostics()}\n" +
              $"COMBAT PLAYTEST  |  lag compensation {(_lagCompensationEnabled ? "ON" : "OFF")} (F10 toggles)\n" +
              "Escape releases the mouse; click the game to resume control (Alt+Tab is the fallback)"
            : _authorityAvailable
                ? $"CLIENT PREDICTION  |  tick {_simulationTick}  |  speed {horizontalSpeed:0.0} m/s  |  unacked {_predictionHistory.Count}  |  last correction {_lastCorrectionDistance:0.000} m\n" +
                  $"REMOTE VIEW  |  {(_remoteInterpolationEnabled ? $"ADAPTIVE {_currentPresentationDelayTicks} TICK" : "RAW LATEST FRAME")}  |  predicted {_remotePredictedAvatarCount}  |  frozen {_remoteFrozenAvatarCount}  |  F9 toggles  |  fps {Engine.GetFramesPerSecond()}  |  authority tick {_latestAuthoritySnapshotTick}  |  movement age {movementAgeMilliseconds:0} ms  |  snapshot age {snapshotAgeMilliseconds:0} ms  |  render lag {interpolationLagMilliseconds:0} ms\n" +
                  $"AUTHORITY CLOCK  |  {(_authorityClock.HasEstimate ? $"tick {clock.SimulationTick:0.0}  |  offset {clock.ClockOffsetMilliseconds:0.0} ms  |  confidence {clock.Confidence:P0}" : "synchronizing")}  |  movement frames {_acceptedMovementFrames}  |  accepted relays {_acceptedMovementRelayBatches}\n" +
                  $"DIRECT MESH  |  {FormatDirectPredictionDiagnostics()}\n" +
                  $"NETWORK COMBAT  |  RTT {_measuredRttMilliseconds:0} ms  |  jitter {_measuredJitterMilliseconds:0} ms  |  rewind {(_lagCompensationEnabled ? $"{_rewindAllowanceMilliseconds:0} ms" : "OFF")}\n" +
                  $"LAST ATTACK  |  requested age {_lastRequestedAttackAgeMilliseconds:0} ms  |  actual rewind {_lastActualRewindMilliseconds:0} ms\n" +
                  "Escape releases the mouse; click the game to resume control (Alt+Tab is the fallback)"
                : "CONNECTION INTERRUPTED  |  waiting for authority reconnection";
    }

    private string FormatDirectPredictionDiagnostics()
    {
        var paths = _clientPredictionControl?.DirectPathStatuses ?? [];
        if (paths.Count == 0)
        {
            return "no peer route  |  authority relay active";
        }

        var authenticated = paths.Count(path =>
            path.RouteHealth == PredictionRouteHealth.Authenticated);
        var fallback = paths.Count(path =>
            path.RouteHealth == PredictionRouteHealth.AuthorityFallback);
        var measured = paths.Where(path => path.Path.SmoothedRttMilliseconds > 0d).ToArray();
        var averageRtt = measured.Length == 0
            ? 0d
            : measured.Average(path => path.Path.SmoothedRttMilliseconds);
        var maximumJitter = paths.Max(path => path.Path.EffectiveJitterMilliseconds);
        var maximumLoss = paths.Max(path => path.Path.EstimatedLossRate);
        var now = _networkTimeSource.GetTimestampMicroseconds();
        var newestDirect = paths.Max(path => path.LastMovementArrivalTimestampMicroseconds);
        var directAge = newestDirect == 0 || now < newestDirect
            ? 0d
            : (now - newestDirect) / 1_000d;
        var confirmations = _remoteMovementTimelines.Values.Sum(
            timeline => timeline.DirectConfirmations);
        var mismatches = _remoteMovementTimelines.Values.Sum(
            timeline => timeline.DirectMismatches);
        var duplicates = _remoteMovementTimelines.Values.Sum(
            timeline => timeline.DuplicateDirectCommands);
        var late = _remoteMovementTimelines.Values.Sum(
            timeline => timeline.LateDirectCommands);
        var quarantineWarnings = _remoteMovementTimelines.Values.Count(
            timeline => timeline.DirectRouteQuarantineRecommended);
        return $"routes {authenticated} authenticated / {fallback} fallback  |  " +
               $"RTT {(measured.Length == 0 ? "measuring" : $"{averageRtt:0} ms")}  |  " +
               $"jitter {maximumJitter:0.0} ms  |  loss {maximumLoss:P1}  |  " +
               $"age {directAge:0} ms  |  confirm {confirmations}  |  " +
               $"mismatch {mismatches}  |  duplicate {duplicates}  |  late {late}" +
               (quarantineWarnings > 0 ? $"  |  WARN {quarantineWarnings} anomalous route(s)" : "");
    }

    private string FormatAuthorityInputDiagnostics()
    {
        if (_remoteInputs.Count == 0)
        {
            return "no remote players";
        }

        return string.Join(
            "  |  ",
            _remoteInputs.Values
                .OrderBy(remote => remote.Player.CombatantId)
                .Select(remote =>
                    $"C{remote.Player.CombatantId}: queued {remote.Inputs.PendingCommandCount}, " +
                    $"compacted {remote.Inputs.LastCompactedCommandCount} " +
                    $"(total {remote.Inputs.TotalCompactedCommandCount})"));
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

        if (!InputMap.HasAction(ToggleLagCompensationAction))
        {
            InputMap.AddAction(ToggleLagCompensationAction);
        }

        var lagKey = new InputEventKey { PhysicalKeycode = Key.F10 };
        if (!InputMap.ActionHasEvent(ToggleLagCompensationAction, lagKey))
        {
            InputMap.ActionAddEvent(ToggleLagCompensationAction, lagKey);
        }
    }

    private CombatantSnapshot ToSnapshot(NetworkAvatar avatar, ulong processedInput)
    {
        _combat.TryGetCombatantSnapshot(
            CombatantIdFor(avatar.CombatantId),
            out var health);
        var eliminated = health?.IsEliminated ?? false;
        var lifeState = ReplicatedLifeState.Alive;
        if (eliminated)
        {
            lifeState = _eliminations.TryGetValue(avatar.CombatantId, out var elimination) &&
                _simulationTick - elimination.EliminatedAtTick >= RespawnCameraHoldTicks
                    ? ReplicatedLifeState.Respawning
                    : ReplicatedLifeState.Eliminated;
        }

        var snapshot = new CombatantSnapshot
        {
            CombatantId = avatar.CombatantId,
            LifeId = _lifeGenerations.GetValueOrDefault(avatar.CombatantId, 1UL),
            ControllingPlayerId = avatar.CombatantId,
            Position = ToProtocol(avatar.Position),
            Velocity = ToProtocol(avatar.Velocity),
            ViewYawRadians = avatar.Yaw,
            ViewPitchRadians = avatar.Pitch,
            BodyFacingYawRadians = (float)avatar.MovementState.FacingYawRadians,
            CurrentHealth = checked((long)Math.Round(health?.CurrentHealth ?? 100d)),
            MaximumHealth = checked((long)Math.Round(health?.EffectiveMaximumHealth ?? 100d)),
            RemainingLives = int.MaxValue,
            LifeState = lifeState,
            DisplayName = avatar.DisplayLabel,
            RespawnAtTick = _eliminations.TryGetValue(
                avatar.CombatantId,
                out var respawn)
                    ? checked(respawn.EliminatedAtTick + RespawnDelayTicks)
                    : 0,
            LastProcessedInputSequence = processedInput,
            IsGrounded = avatar.IsOnFloor(),
            MovementProfileRevision = avatar.MovementAttributes.Revision,
            MovementCapabilityRevision = 1,
        };
        NetworkMovementStateMapper.WriteTo(snapshot, avatar.MovementState);
        if (avatar.AttackState is { } attack)
        {
            snapshot.AttackExecutionId = attack.ExecutionId;
            snapshot.AttackStepIndex = checked((uint)attack.StepIndex);
            snapshot.AttackIsCrouchedOrAirborne =
                attack.Context == BattleArena.Core.Combat.Attacks.AttackContext.CrouchedOrAirborne;
            snapshot.AttackStartedTick = checked((ulong)attack.StartedAt.Tick);
            snapshot.AttackContinuationQueued = attack.ContinuationQueued;
            snapshot.AttackReleasedDuringStep = attack.AttackReleasedDuringStep;
        }

        return snapshot;
    }

    private AuthoritativeMovementState ToMovementState(
        NetworkAvatar avatar,
        ulong processedInput)
    {
        var state = new AuthoritativeMovementState
        {
            CombatantId = avatar.CombatantId,
            LifeId = _lifeGenerations.GetValueOrDefault(avatar.CombatantId, 1UL),
            Position = ToProtocol(avatar.Position),
            Velocity = ToProtocol(avatar.Velocity),
            ViewYawRadians = avatar.Yaw,
            ViewPitchRadians = avatar.Pitch,
            BodyFacingYawRadians = (float)avatar.MovementState.FacingYawRadians,
            LastProcessedInputSequence = processedInput,
            IsGrounded = avatar.IsOnFloor(),
            MovementProfileRevision = avatar.MovementAttributes.Revision,
            MovementCapabilityRevision = 1,
        };
        NetworkMovementStateMapper.WriteTo(state, avatar.MovementState);
        return state;
    }

    private static AuthoritativeMovementState ToMovementState(
        CombatantSnapshot snapshot)
    {
        var state = new AuthoritativeMovementState
        {
            CombatantId = snapshot.CombatantId,
            LifeId = snapshot.LifeId,
            Position = snapshot.Position?.Clone(),
            Velocity = snapshot.Velocity?.Clone(),
            ViewYawRadians = snapshot.ViewYawRadians,
            ViewPitchRadians = snapshot.ViewPitchRadians,
            BodyFacingYawRadians = snapshot.BodyFacingYawRadians,
            LastProcessedInputSequence = snapshot.LastProcessedInputSequence,
            IsGrounded = snapshot.IsGrounded,
            MovementProfileRevision = snapshot.MovementProfileRevision,
            MovementCapabilityRevision = snapshot.MovementCapabilityRevision,
        };
        NetworkMovementStateMapper.WriteTo(
            state,
            NetworkMovementStateMapper.FromSnapshot(snapshot));
        return state;
    }

    private void QueueLocalAuthoritativeMovement(
        ulong authorityTick,
        AuthoritativeMovementState state)
    {
        if (_pendingLocalMovement is null ||
            authorityTick >= _pendingLocalMovement.AuthorityTick)
        {
            _pendingLocalMovement = new PendingAuthoritativeMovement(
                authorityTick,
                state.Clone());
        }
    }

    private void AddRemoteMovementSample(
        ulong combatantId,
        ulong authorityTick,
        AuthoritativeMovementState state)
    {
        if (!_remoteMovementTimelines.TryGetValue(combatantId, out var timeline))
        {
            timeline = new RemoteMovementTimeline<AuthoritativeMovementState>();
            _remoteMovementTimelines.Add(combatantId, timeline);
        }

        var previousLife = timeline.CurrentLifeId;
        timeline.ObserveAuthority(new RemoteMovementFrame<AuthoritativeMovementState>(
            authorityTick,
            state.LifeId,
            state.LastProcessedInputSequence,
            state.Clone()));
        if (previousLife is not null && previousLife != timeline.CurrentLifeId)
        {
            _remoteRenderTicks.Remove(combatantId);
            _remoteCollisionCommitTicks.Remove(combatantId);
        }
    }

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

    private Vector3 SelectSpawnPosition(ulong combatantId)
    {
        Vector3[] candidates =
        [
            new(-20f, 1f, 25f),
            new(20f, 1f, 18f),
            new(-15f, 1f, -22f),
            new(14f, 1f, -22f),
            new(-4f, 1f, 24f),
            new(5f, 1f, 14f),
            new(-6f, 1f, -14f),
            new(14f, 1f, 12f),
        ];
        var start = _spawnRandom.RandiRange(0, candidates.Length - 1);
        for (var offset = 0; offset < candidates.Length; offset++)
        {
            var candidate = candidates[(start + offset) % candidates.Length];
            if (_avatars.Values.All(
                    avatar =>
                        avatar.CombatantId == combatantId ||
                        avatar.Position.DistanceSquaredTo(candidate) >= 16f))
            {
                return candidate;
            }
        }

        return candidates[start];
    }

    private static CombatantId CombatantIdFor(ulong value) =>
        new(checked((long)value));

    private static CombatActionDefinitionId ActionDefinitionIdFor(string stepId) =>
        new($"base:fighter_starter_sword/{stepId}");

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

    private enum RemoteCommitSmokeLifecycleStage
    {
        None,
        LifeOneAlive,
        LifeOneEliminated,
        LifeTwoEpochAccepted,
        LifeTwoColliderEnabled,
        LifeTwoPoseCommitted,
    }

    private sealed record RemotePlayerSimulation(
        NetworkAvatar avatar,
        ConnectedPlayer player,
        IAuthorityMovementInputBuffer inputs)
    {
        public NetworkAvatar Avatar { get; } = avatar;

        public ConnectedPlayer Player { get; } = player;

        public IAuthorityMovementInputBuffer Inputs { get; } = inputs;
    }

    private readonly record struct NetworkAttackKey(
        ulong CombatantId,
        ulong AttackExecutionId);

    private sealed record AuthorityHitCandidate(
        NetworkAvatar Source,
        NetworkAvatar Target,
        ActionExecutionId ExecutionId);

    private sealed record EliminationRuntime(
        ulong EliminatedAtTick,
        Vector3 DeathPosition,
        Vector3 SpawnPosition);

    private sealed record HistoricalPose(ulong Tick, Vector3 Position);

    private sealed record PendingAuthoritativeMovement(
        ulong AuthorityTick,
        AuthoritativeMovementState State);

    private readonly record struct RemoteRenderPhaseSample(
        NetworkAvatarBlockingBodySnapshot BlockingBody,
        Vector3 VisualPosition);
}
