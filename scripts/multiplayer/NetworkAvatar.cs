#nullable enable

using BattleArena.Core.Common;
using BattleArena.Core.Combat.Attacks;
using BattleArena.Core.Movement;
using BattleArena.Combat;
using BattleArena.Movement;
using BattleArena.Multiplayer.Replication;
using BattleArena.Presentation;
using BattleArena.Protocol.V1;
using BattleArena.VerticalSlice;
using Godot;

namespace BattleArena.GodotNetworking;

public partial class NetworkAvatar : Node3D
{
    [Export(PropertyHint.File, "*.json")]
    public string MovementProfilePath { get; set; } =
        "res://assets/classes/fighter/movement.json";

    [Export(PropertyHint.File, "*.json")]
    public string WeaponAttackDefinitionPath { get; set; } =
        "res://assets/classes/fighter/starter_sword_attacks.json";

    [Export]
    public NodePath SimulationBodyPath { get; set; } = "";

    [Export]
    public NodePath VisualAnchorPath { get; set; } = "";

    [Export]
    public NodePath CameraAnchorPath { get; set; } = "";

    [Export]
    public NodePath VisualRootPath { get; set; } = "";

    [Export]
    public NodePath CharacterViewPath { get; set; } = "";

    [Export]
    public NodePath CameraYawPath { get; set; } = "";

    [Export]
    public NodePath CameraPitchPath { get; set; } = "";

    [Export]
    public NodePath ThirdPersonCameraPath { get; set; } = "";

    [Export]
    public NodePath CollisionPath { get; set; } = "";

    [Export]
    public NodePath LabelPath { get; set; } = "";

    [Export(PropertyHint.Range, "0.0005,0.02,0.0001")]
    public float MouseSensitivity { get; set; } = 0.0025f;

    [Export(PropertyHint.Range, "0.1,2.0,0.05")]
    public float CameraVisualIntersectionRadius { get; set; } = 0.85f;

    [Export(PropertyHint.Range, "0.1,2.0,0.05")]
    public float LocalCameraHideDistance { get; set; } = 0.8f;

    private CharacterBody3D _simulationBody = null!;
    private Node3D _visualAnchor = null!;
    private Node3D _cameraAnchor = null!;
    private Node3D _visualRoot = null!;
    private RiggedCharacterView _characterView = null!;
    private Node3D _cameraYaw = null!;
    private Node3D _cameraPitch = null!;
    private Camera3D _thirdPersonCamera = null!;
    private CollisionShape3D _collision = null!;
    private Label3D _label = null!;
    private GodotCharacterMovementDriver _movementDriver = null!;
    private StarterSwordAttackPolicy _attackPolicy = null!;
    private readonly ReplicatedAttackPresentationTracker _replicatedAttackTracker = new();
    private AttackRuntimeState? _attackState;
    private bool _configured;
    private bool _locallyControlled;
    private bool _collisionEnabled;
    private bool _gameplayInputEnabled;
    private bool _suppressAttackUntilRelease;
    private bool _jumpPressedQueued;
    private bool _jumpReleasedQueued;
    private bool _crouchRollPressedQueued;
    private bool _crouchRollReleasedQueued;
    private bool _attackPressedQueued;
    private bool _attackReleasedQueued;
    private bool _wasRolling;
    private bool _eliminated;
    private bool _respawnPresentationHidden;
    private bool _fixedPhysicsBlockingCommitsRequired;
    private long _blockingCommitPhaseViolationCount;
    private long _fixedPhysicsLifecycleCommitCount;
    private bool _cameraIntersectsVisual;
    private long _currentHealth = 100;
    private long _maximumHealth = 100;
    private ReplicatedLifeState _lifeState = ReplicatedLifeState.Alive;
    private double _respawnSecondsRemaining;
    private NetworkAvatarStatus? _lastPublishedStatus;
    private float _yaw;
    private float _pitch = Mathf.DegToRad(-12f);
    private Color _color;
    private string _displayLabel = "Player";

    public ulong CombatantId { get; private set; }

    public bool IsLocallyControlled => _locallyControlled;

    public string DisplayLabel => _displayLabel;

    public float Yaw => _yaw;

    public float Pitch => _pitch;

    /// <summary>
    /// Compatibility facade for callers that previously addressed the avatar's
    /// CharacterBody3D root. The authoritative transform now belongs only to the
    /// sibling simulation body.
    /// </summary>
    public new Vector3 Position
    {
        get => _simulationBody.Position;
        set
        {
            BeginBlockingBodyCommit(BlockingBodyCommitKind.Motion);
            _simulationBody.Position = value;
        }
    }

    public Vector3 Velocity
    {
        get => _simulationBody.Velocity;
        set
        {
            BeginBlockingBodyCommit(BlockingBodyCommitKind.Motion);
            _simulationBody.Velocity = value;
        }
    }

    public Vector3 VisualPresentationPosition => _visualAnchor.GlobalPosition;

    public long BlockingCommitPhaseViolationCount => _blockingCommitPhaseViolationCount;

    public long FixedPhysicsLifecycleCommitCount => _fixedPhysicsLifecycleCommitCount;

    public Rid GetRid() => _simulationBody.GetRid();

    public bool IsOnFloor() => _simulationBody.IsOnFloor();

    public MovementRuntimeState MovementState => _movementDriver.State;

    public MovementAttributeSnapshot MovementAttributes => _movementDriver.Attributes;

    public AttackRuntimeState? AttackState => _attackState;

    public AttackTickResult LastAttackTick { get; private set; } =
        new(null, null, null, false, false, false, false);

    public ulong EndedAttackExecutionId { get; private set; }

    public bool IsEliminated => _eliminated;

    public NetworkAvatarBlockingBodySnapshot CaptureBlockingBodySnapshot()
    {
        var capsule = _collision.Shape as CapsuleShape3D;
        return new NetworkAvatarBlockingBodySnapshot(
            _simulationBody.GlobalPosition,
            _simulationBody.Velocity,
            _movementDriver.State,
            _collision.Disabled,
            capsule?.Height ?? 0f,
            capsule?.Radius ?? 0f);
    }

    /// <summary>
    /// Enables the runtime phase invariant after the one-time scene spawn has
    /// established the initial body. Every later blocking-body mutation must
    /// originate from fixed physics.
    /// </summary>
    public void RequireFixedPhysicsBlockingCommits() =>
        _fixedPhysicsBlockingCommitsRequired = true;

    public NetworkAvatarStatus Status => new(
        _displayLabel,
        _currentHealth,
        _maximumHealth,
        _lifeState,
        _respawnSecondsRemaining);

    public event Action<NetworkAvatarStatus>? StatusChanged;

    public WeaponAttackDefinition AttackDefinition => _attackPolicy.Definition;

    public void Configure(
        ulong combatantId,
        string displayLabel,
        bool locallyControlled,
        bool collisionEnabled,
        Color color)
    {
        if (_configured)
        {
            throw new InvalidOperationException("Network avatar is already configured.");
        }

        CombatantId = combatantId;
        _displayLabel = displayLabel;
        _locallyControlled = locallyControlled;
        _collisionEnabled = collisionEnabled;
        _color = color;
        _configured = true;
    }

    public override void _Ready()
    {
        if (!_configured)
        {
            throw new InvalidOperationException(
                "Configure must be called before adding a network avatar to the scene tree.");
        }

        _simulationBody = GetNode<CharacterBody3D>(SimulationBodyPath);
        _visualAnchor = GetNode<Node3D>(VisualAnchorPath);
        _cameraAnchor = GetNode<Node3D>(CameraAnchorPath);
        _visualRoot = GetNode<Node3D>(VisualRootPath);
        _characterView = GetNode<RiggedCharacterView>(CharacterViewPath);
        _cameraYaw = GetNode<Node3D>(CameraYawPath);
        _cameraPitch = GetNode<Node3D>(CameraPitchPath);
        _thirdPersonCamera = GetNode<Camera3D>(ThirdPersonCameraPath);
        _collision = GetNode<CollisionShape3D>(CollisionPath);
        _label = GetNode<Label3D>(LabelPath);

        SetCollisionDisabled(!_collisionEnabled);
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        _simulationBody.PhysicsInterpolationMode = _locallyControlled
            ? PhysicsInterpolationModeEnum.On
            : PhysicsInterpolationModeEnum.Off;
        _visualAnchor.PhysicsInterpolationMode = _locallyControlled
            ? PhysicsInterpolationModeEnum.On
            : PhysicsInterpolationModeEnum.Off;
        _cameraAnchor.PhysicsInterpolationMode = _locallyControlled
            ? PhysicsInterpolationModeEnum.On
            : PhysicsInterpolationModeEnum.Off;
        _label.Modulate = _color.Lightened(0.35f);
        RefreshStatusPresentation();

        var attributes = MovementProfileLoader.Load(
            MovementProfilePath,
            simulationRate: new SimulationRate(Engine.PhysicsTicksPerSecond)).Attributes;
        _movementDriver = new GodotCharacterMovementDriver(
            _simulationBody,
            _collision,
            attributes,
            new SimulationRate(Engine.PhysicsTicksPerSecond),
            MovementRuntimeState.CreateGrounded(SimulationInstant.Zero, _yaw));
        _attackPolicy = new StarterSwordAttackPolicy(
            WeaponAttackDefinitionLoader.Load(
                WeaponAttackDefinitionPath,
                new SimulationRate(Engine.PhysicsTicksPerSecond)));

        ApplyView();
        SnapPresentationAnchorsToSimulation();
        _thirdPersonCamera.Current = _locallyControlled;
        _characterView.SetLocomotion(
            Vector2.Zero,
            airborne: false,
            1d / Engine.PhysicsTicksPerSecond);

        if (_locallyControlled)
        {
            CaptureGameplayControl();
        }
    }

    public override void _Notification(int what)
    {
        if (_locallyControlled && what == NotificationWMWindowFocusOut)
        {
            ReleaseGameplayControl();
        }
    }

    public override void _Process(double delta)
    {
        UpdateCameraSafeVisibility();
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (!_locallyControlled)
        {
            return;
        }

        if (inputEvent.IsActionPressed("ui_cancel"))
        {
            ReleaseGameplayControl();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (inputEvent is InputEventMouseButton mouseButton &&
            mouseButton.Pressed &&
            Input.MouseMode != Input.MouseModeEnum.Captured)
        {
            CaptureGameplayControl();
            _suppressAttackUntilRelease = true;
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_suppressAttackUntilRelease &&
            inputEvent.IsActionReleased(VerticalSliceInput.Attack))
        {
            _suppressAttackUntilRelease = false;
            GetViewport().SetInputAsHandled();
            return;
        }

        if (!_gameplayInputEnabled)
        {
            return;
        }

        var isEcho = inputEvent is InputEventKey { Echo: true };
        if (!isEcho && inputEvent.IsActionPressed(VerticalSliceInput.Jump))
        {
            _jumpPressedQueued = true;
        }

        if (!isEcho && inputEvent.IsActionReleased(VerticalSliceInput.Jump))
        {
            _jumpReleasedQueued = true;
        }

        if (!isEcho && inputEvent.IsActionPressed(VerticalSliceInput.CrouchOrRoll))
        {
            _crouchRollPressedQueued = true;
        }

        if (!isEcho && inputEvent.IsActionReleased(VerticalSliceInput.CrouchOrRoll))
        {
            _crouchRollReleasedQueued = true;
        }

        if (!isEcho && !_suppressAttackUntilRelease &&
            inputEvent.IsActionPressed(VerticalSliceInput.Attack))
        {
            _attackPressedQueued = true;
        }

        if (!isEcho && inputEvent.IsActionReleased(VerticalSliceInput.Attack))
        {
            _attackReleasedQueued = true;
        }

        if (inputEvent is InputEventMouseMotion mouseMotion)
        {
            RotateView(
                -mouseMotion.Relative.X * MouseSensitivity,
                -mouseMotion.Relative.Y * MouseSensitivity);
        }
    }

    public NetworkMovementInput CaptureInput(ulong sequence, ulong clientTick, float delta)
    {
        if (_gameplayInputEnabled && Input.IsActionJustPressed("ui_cancel"))
        {
            ReleaseGameplayControl();
        }

        if (!_gameplayInputEnabled || !GetWindow().HasFocus())
        {
            ClearQueuedEdges();
            return NetworkMovementInput.Neutral(clientTick, _yaw, _pitch) with
            {
                Sequence = sequence,
            };
        }

        var controllerLook = Input.GetVector(
            VerticalSliceInput.LookLeft,
            VerticalSliceInput.LookRight,
            VerticalSliceInput.LookUp,
            VerticalSliceInput.LookDown);
        if (controllerLook.LengthSquared() > 0)
        {
            RotateView(
                -controllerLook.X * 2.5f * delta,
                -controllerLook.Y * 2.5f * delta);
        }

        var movement = Input.GetVector(
            VerticalSliceInput.MoveLeft,
            VerticalSliceInput.MoveRight,
            VerticalSliceInput.MoveForward,
            VerticalSliceInput.MoveBackward);
        var held = MovementButtons.None;
        var pressed = MovementButtons.None;
        var released = MovementButtons.None;
        if (Input.IsActionPressed(VerticalSliceInput.Sprint))
        {
            held |= MovementButtons.Sprint;
        }

        if (Input.IsActionPressed(VerticalSliceInput.Jump))
        {
            held |= MovementButtons.Jump;
        }

        if (Input.IsActionPressed(VerticalSliceInput.CrouchOrRoll))
        {
            held |= MovementButtons.CrouchOrRoll;
        }

        if (!_suppressAttackUntilRelease && Input.IsActionPressed(VerticalSliceInput.Attack))
        {
            held |= MovementButtons.Attack;
        }

        if (_jumpPressedQueued)
        {
            pressed |= MovementButtons.Jump;
        }

        if (_jumpReleasedQueued)
        {
            released |= MovementButtons.Jump;
        }

        if (_crouchRollPressedQueued)
        {
            pressed |= MovementButtons.CrouchOrRoll;
        }

        if (_crouchRollReleasedQueued)
        {
            released |= MovementButtons.CrouchOrRoll;
        }

        if (_attackPressedQueued)
        {
            pressed |= MovementButtons.Attack;
        }

        if (_attackReleasedQueued)
        {
            released |= MovementButtons.Attack;
        }

        ClearQueuedEdges();
        return new NetworkMovementInput(
            sequence,
            clientTick,
            movement.X,
            movement.Y,
            _yaw,
            _pitch,
            held,
            pressed,
            released);
    }

    public void Simulate(
        NetworkMovementInput input,
        double delta,
        bool? groundedOverride = null)
    {
        SimulateStateOnly(input, delta, groundedOverride);
        PublishCommittedPresentation(
            input.YawRadians,
            input.PitchRadians,
            delta,
            emitCurrentSimulationCues: true);
    }

    public void SimulateStateOnly(
        NetworkMovementInput input,
        double delta,
        bool? groundedOverride = null)
    {
        BeginBlockingBodyCommit(BlockingBodyCommitKind.Motion);
        var command = input.ToMovementCommand();
        var context =
            _movementDriver.State.LocomotionMode == LocomotionMode.Grounded &&
            _movementDriver.State.PostureMode == PostureMode.Standing
                ? AttackContext.GroundedCombo
                : AttackContext.CrouchedOrAirborne;
        var previousAttack = _attackState;
        LastAttackTick = _attackPolicy.Advance(_attackState, command, context);
        _attackState = LastAttackTick.State;
        EndedAttackExecutionId =
            previousAttack is not null &&
            previousAttack.ExecutionId != (_attackState?.ExecutionId ?? 0)
                ? previousAttack.ExecutionId
                : 0;
        if (LastAttackTick.StepStarted && LastAttackTick.Step is { } startedStep)
        {
            _movementDriver.ReplaceState(_movementDriver.State with
            {
                FacingYawRadians = input.YawRadians,
            });
            var duration = (double)new SimulationRate(Engine.PhysicsTicksPerSecond)
                .SecondsFromDuration(startedStep.TotalDuration);
            if (startedStep.LungeDistance > 0d)
            {
                var yaw = _movementDriver.State.FacingYawRadians;
                var lungeSpeed = startedStep.LungeDistance / Math.Max(duration, 0.001d);
                var velocity = _movementDriver.State.HorizontalVelocity +
                    new HorizontalVector(
                        -Math.Sin(yaw) * lungeSpeed,
                        -Math.Cos(yaw) * lungeSpeed);
                _movementDriver.ReplaceState(
                    _movementDriver.State with { HorizontalVelocity = velocity });
            }
        }

        _movementDriver.ReplaceState(_movementDriver.State with
        {
            ActionMode = _attackState is null
                ? MovementActionMode.Ready
                : MovementActionMode.Attacking,
        });
        _movementDriver.Simulate(
            command,
            delta,
            groundedOverride,
            LastAttackTick.Step?.MovementInfluence);
    }

    /// <summary>
    /// Replays an unacknowledged movement input after an authority correction.
    /// Action requests and attack state have their own authoritative stream and
    /// must not be advanced a second time by movement reconciliation.
    /// </summary>
    public void ReplayPredictedMovement(
        NetworkMovementInput input,
        double delta,
        bool? groundedOverride = null)
    {
        BeginBlockingBodyCommit(BlockingBodyCommitKind.Motion);
        _movementDriver.Simulate(
            input.ToMovementCommand(),
            delta,
            groundedOverride,
            LastAttackTick.Step?.MovementInfluence);
    }

    /// <summary>
    /// Publishes the final current state once after any authority restore,
    /// historical replay, and deferred current simulation. This is the sole
    /// deferred-frame boundary allowed to touch model/camera anchors,
    /// continuous presentation, or newly discovered first-run cues.
    /// </summary>
    public void PublishCommittedPresentation(
        float yaw,
        float pitch,
        double delta,
        bool emitCurrentSimulationCues)
    {
        ApplyView(yaw, pitch);
        CommitLivePresentationAnchors();
        UpdatePresentation(delta);
        if (emitCurrentSimulationCues &&
            LastAttackTick.StepStarted &&
            LastAttackTick.Step is { } startedStep)
        {
            var duration = (double)new SimulationRate(Engine.PhysicsTicksPerSecond)
                .SecondsFromDuration(startedStep.TotalDuration);
            _characterView.PlayForDuration(startedStep.AnimationId, duration);
        }
    }

    public bool TryRecordAttackHit(ulong targetCombatantId)
    {
        if (_attackState is null || _attackState.HitCombatants.Contains(targetCombatantId))
        {
            return false;
        }

        _attackState = _attackPolicy.RecordHit(_attackState, targetCombatantId);
        return true;
    }

    public void SetEliminated(bool eliminated, ulong authorityTick)
    {
        if (_eliminated == eliminated)
        {
            return;
        }

        BeginBlockingBodyCommit(BlockingBodyCommitKind.Lifecycle);
        _eliminated = eliminated;
        _lifeState = eliminated
            ? ReplicatedLifeState.Eliminated
            : ReplicatedLifeState.Alive;
        if (!eliminated)
        {
            _respawnSecondsRemaining = 0d;
        }
        _attackState = null;
        SetCollisionDisabled(eliminated || !_collisionEnabled);
        if (eliminated)
        {
            Velocity = Vector3.Zero;
            _movementDriver.ReplaceState(_movementDriver.State with
            {
                HorizontalVelocity = HorizontalVector.Zero,
                VerticalVelocity = 0d,
                LocomotionMode = LocomotionMode.Disabled,
                ActionMode = MovementActionMode.Ready,
                ModeStartedAt = new SimulationInstant(checked((long)authorityTick)),
            });
            _characterView.Play("reaction.death");
        }

        RefreshStatusPresentation();
    }

    public void ResetForRespawn(
        Vector3 position,
        ulong authorityTick,
        float yaw,
        long currentHealth,
        long maximumHealth)
    {
        BeginBlockingBodyCommit(BlockingBodyCommitKind.Lifecycle);
        Position = position;
        SnapPresentationAnchorsToSimulation();
        _yaw = yaw;
        _attackState = null;
        _replicatedAttackTracker.Reset();
        _eliminated = false;
        _lifeState = ReplicatedLifeState.Alive;
        _currentHealth = currentHealth;
        _maximumHealth = maximumHealth;
        _respawnSecondsRemaining = 0d;
        _respawnPresentationHidden = false;
        SetCollisionDisabled(!_collisionEnabled);
        UpdateVisualVisibility();
        _movementDriver.Reset(
            MovementRuntimeState.CreateGrounded(
                new SimulationInstant(checked((long)authorityTick)),
                yaw));
        ApplyView();
        UpdatePresentation(1d / Engine.PhysicsTicksPerSecond);
        RefreshStatusPresentation();
    }

    /// <summary>
    /// Clears every client-predicted transient that must not cross a life or
    /// owner-control epoch. The state-bearing authority baseline is applied by
    /// the arena immediately after this idempotent preparation step.
    /// </summary>
    public void ResetPredictionForEpoch()
    {
        BeginBlockingBodyCommit(BlockingBodyCommitKind.Lifecycle);
        ClearQueuedEdges();
        _suppressAttackUntilRelease =
            _locallyControlled && Input.IsActionPressed(VerticalSliceInput.Attack);
        _attackState = null;
        _replicatedAttackTracker.Reset();
        LastAttackTick = new AttackTickResult(
            null,
            null,
            null,
            false,
            false,
            false,
            false);
        EndedAttackExecutionId = 0;
        _wasRolling = false;

        var state = _movementDriver.State;
        _movementDriver.Reset(MovementRuntimeState.CreateGrounded(
            state.ModeStartedAt,
            state.FacingYawRadians));
        Velocity = Vector3.Zero;
        if (IsNodeReady())
        {
            UpdatePresentation(1d / Engine.PhysicsTicksPerSecond);
        }
    }

    public void SetRespawnTransitionPosition(Vector3 position)
    {
        BeginBlockingBodyCommit(BlockingBodyCommitKind.Motion);
        Position = position;
        CommitLivePresentationAnchors();
        _lifeState = ReplicatedLifeState.Respawning;
        _respawnPresentationHidden = true;
        UpdateVisualVisibility();
        RefreshStatusPresentation();
    }

    public void SetHealth(long current, long maximum)
    {
        _currentHealth = current;
        _maximumHealth = maximum;
        RefreshStatusPresentation();
    }

    public void SetRespawnCountdown(double secondsRemaining)
    {
        _respawnSecondsRemaining = Math.Max(0d, secondsRemaining);
        RefreshStatusPresentation();
    }

    public void ShowDamage(long damage)
    {
        _characterView.Flash(new Color(1f, 0.18f, 0.08f, 0.58f));
        var floating = new Label3D
        {
            Text = $"-{damage}",
            Position = new Vector3(0f, 2.45f, 0f),
            FontSize = 34,
            OutlineSize = 7,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Modulate = new Color(1f, 0.35f, 0.2f),
        };
        _visualAnchor.AddChild(floating);
        var tween = CreateTween().SetParallel();
        tween.TweenProperty(floating, "position:y", 3.2f, 0.65);
        tween.TweenProperty(floating, "modulate:a", 0f, 0.65);
        tween.Chain().TweenCallback(Callable.From(floating.QueueFree));
    }

    public void ShowHitConfirmation()
    {
        if (!_locallyControlled)
        {
            return;
        }

        var confirmation = new Label3D
        {
            Text = "HIT",
            Position = new Vector3(0f, 2.75f, -0.3f),
            FontSize = 30,
            OutlineSize = 7,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Modulate = new Color(1f, 0.86f, 0.2f),
        };
        _visualAnchor.AddChild(confirmation);
        var tween = CreateTween().SetParallel();
        tween.TweenProperty(confirmation, "position:y", 3.15f, 0.4);
        tween.TweenProperty(confirmation, "modulate:a", 0f, 0.4);
        tween.Chain().TweenCallback(Callable.From(confirmation.QueueFree));
    }

    public void PlayReplicatedAttack(ActionStateEvent action, ulong authorityTick)
    {
        if (_eliminated)
        {
            return;
        }

        var context = action.AttackIsCrouchedOrAirborne
            ? AttackContext.CrouchedOrAirborne
            : AttackContext.GroundedCombo;
        var stepIndex = context == AttackContext.CrouchedOrAirborne
            ? 0
            : checked((int)action.AttackStepIndex);
        var identity = new AttackPresentationIdentity(
            action.AttackExecutionId,
            stepIndex);
        var decision = _replicatedAttackTracker.Observe(
            authorityTick,
            identity,
            CurrentAttackPresentationIdentity(),
            HasUnconfirmedLocalAttackPrediction());
        if (decision.Action != AttackPresentationAction.Apply)
        {
            return;
        }

        _attackState = new AttackRuntimeState(
            action.AttackExecutionId,
            context,
            stepIndex,
            new SimulationInstant(checked((long)action.AttackStartedTick)),
            false,
            false,
            new HashSet<ulong>());
        if (decision.RestartAnimation)
        {
            PlayAttackStep(context, stepIndex);
        }
    }

    public void ApplyAuthoritativeSnapshot(
        CombatantSnapshot snapshot,
        ulong authorityTick)
    {
        BeginBlockingBodyCommit(BlockingBodyCommitKind.Motion);
        Position = ToGodot(snapshot.Position);
        _yaw = snapshot.ViewYawRadians;
        _pitch = snapshot.ViewPitchRadians;
        _movementDriver.Restore(
            NetworkMovementStateMapper.FromSnapshot(snapshot),
            ToGodot(snapshot.Velocity));
        ApplyReplicatedStatus(snapshot);
        ApplyReplicatedAttack(snapshot, authorityTick);
        ApplyView();
        UpdatePresentation(1d / Engine.PhysicsTicksPerSecond);
    }

    public void ApplyAuthoritativeMovementState(AuthoritativeMovementState state)
    {
        BeginBlockingBodyCommit(BlockingBodyCommitKind.Motion);
        Position = ToGodot(state.Position);
        _movementDriver.Restore(
            NetworkMovementStateMapper.FromMovementState(state),
            ToGodot(state.Velocity));
    }

    public void ApplyReplicatedSnapshot(
        CombatantSnapshot snapshot,
        ulong authorityTick)
    {
        ApplyAuthoritativeSnapshot(snapshot, authorityTick);
    }

    public void ApplyReplicatedMetadata(
        CombatantSnapshot snapshot,
        ulong authorityTick)
    {
        ApplyReplicatedStatus(snapshot);
        ApplyReplicatedAttack(snapshot, authorityTick);
    }

    public void ApplyReplicatedTransform(
        Vector3 position,
        Vector3 velocity,
        float yaw,
        float pitch,
        MovementRuntimeState? state = null)
    {
        BeginBlockingBodyCommit(BlockingBodyCommitKind.Motion);
        Position = position;
        _yaw = yaw;
        _pitch = pitch;
        _movementDriver.Restore(
            state ?? _movementDriver.State with
            {
                HorizontalVelocity = new HorizontalVector(velocity.X, velocity.Z),
                VerticalVelocity = velocity.Y,
                FacingYawRadians = yaw,
            },
            velocity);
        SnapPresentationAnchorsToSimulation();
        ApplyView();
        UpdatePresentation(1d / Engine.PhysicsTicksPerSecond);
    }

    /// <summary>
    /// Moves only the visible character. The CharacterBody3D remains at its
    /// latest authority-confirmed pose so remote prediction cannot influence
    /// local collision or gameplay queries.
    /// </summary>
    public void ApplyRemotePresentation(RemoteMovementPrediction prediction)
    {
        ArgumentNullException.ThrowIfNull(prediction);
        _yaw = prediction.ViewYawRadians;
        _pitch = prediction.ViewPitchRadians;
        ApplyView();
        UpdatePresentation(
            prediction.State,
            1d / Engine.PhysicsTicksPerSecond);
        _visualAnchor.GlobalPosition = prediction.Position;
    }

    public void ResetRemotePresentationOffset()
    {
        _visualAnchor.GlobalPosition = _simulationBody.GlobalPosition;
    }

    private void CommitLivePresentationAnchors()
    {
        var simulationPosition = _simulationBody.GlobalPosition;
        _visualAnchor.GlobalPosition = simulationPosition;
        _cameraAnchor.GlobalPosition = simulationPosition;
    }

    private void SnapPresentationAnchorsToSimulation()
    {
        CommitLivePresentationAnchors();
        _simulationBody.ResetPhysicsInterpolation();
        _visualAnchor.ResetPhysicsInterpolation();
        _cameraAnchor.ResetPhysicsInterpolation();
    }

    public void ApplyView(float yaw, float pitch)
    {
        _yaw = Mathf.Wrap(yaw, -Mathf.Pi, Mathf.Pi);
        _pitch = Mathf.Clamp(pitch, Mathf.DegToRad(-70f), Mathf.DegToRad(55f));
        ApplyView();
    }

    public override void _ExitTree()
    {
        if (_locallyControlled && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
    }

    private void UpdatePresentation(double delta) =>
        UpdatePresentation(_movementDriver.State, delta);

    private void UpdatePresentation(MovementRuntimeState state, double delta)
    {
        _visualRoot.Rotation = new Vector3(0f, (float)state.FacingYawRadians, 0f);
        if (_eliminated)
        {
            _wasRolling = false;
            return;
        }

        if (_attackState is not null)
        {
            return;
        }

        var rolling = state.LocomotionMode == LocomotionMode.Rolling;
        if (rolling)
        {
            if (!_wasRolling)
            {
                var duration = (double)new SimulationRate(Engine.PhysicsTicksPerSecond)
                    .SecondsFromDuration(state.RollDuration);
                _characterView.PlayForDuration(CharacterAnimationIds.RollForward, duration);
            }

            _wasRolling = true;
            return;
        }

        _wasRolling = false;
        if (state.PostureMode == PostureMode.Crouched)
        {
            _characterView.Play(
                state.HorizontalVelocity.Length > 0.1d
                    ? CharacterAnimationIds.CrouchMove
                    : CharacterAnimationIds.CrouchIdle);
            return;
        }

        var cosine = Math.Cos(state.FacingYawRadians);
        var sine = Math.Sin(state.FacingYawRadians);
        var velocity = state.HorizontalVelocity;
        var localRight = (velocity.X * cosine) - (velocity.Z * sine);
        var localForward = (-velocity.X * sine) - (velocity.Z * cosine);
        var runSpeed = _movementDriver.Attributes.Ground.MaximumRunSpeed;
        _characterView.SetLocomotion(
            new Vector2(
                (float)(localRight / runSpeed),
                (float)(localForward / runSpeed)),
            state.LocomotionMode == LocomotionMode.Airborne,
            delta);
    }

    private void ApplyReplicatedAttack(
        CombatantSnapshot snapshot,
        ulong authorityTick)
    {
        var observedIdentity =
            snapshot.LifeState != ReplicatedLifeState.Alive ||
            snapshot.AttackExecutionId == 0
            ? (AttackPresentationIdentity?)null
            : new AttackPresentationIdentity(
                snapshot.AttackExecutionId,
                snapshot.AttackIsCrouchedOrAirborne
                    ? 0
                    : checked((int)snapshot.AttackStepIndex));
        var decision = _replicatedAttackTracker.Observe(
            authorityTick,
            observedIdentity,
            CurrentAttackPresentationIdentity(),
            HasUnconfirmedLocalAttackPrediction());
        if (decision.Action is AttackPresentationAction.Ignore or
            AttackPresentationAction.PreserveLocalPrediction)
        {
            return;
        }

        if (snapshot.AttackExecutionId == 0)
        {
            _attackState = null;
            return;
        }

        var context = snapshot.AttackIsCrouchedOrAirborne
            ? AttackContext.CrouchedOrAirborne
            : AttackContext.GroundedCombo;
        var stepIndex = context == AttackContext.CrouchedOrAirborne
            ? 0
            : checked((int)snapshot.AttackStepIndex);
        _attackState = new AttackRuntimeState(
            snapshot.AttackExecutionId,
            context,
            stepIndex,
            new SimulationInstant(checked((long)snapshot.AttackStartedTick)),
            snapshot.AttackContinuationQueued,
            snapshot.AttackReleasedDuringStep,
            new HashSet<ulong>());
        if (!decision.RestartAnimation)
        {
            return;
        }

        PlayAttackStep(context, stepIndex);
    }

    private void PlayAttackStep(AttackContext context, int stepIndex)
    {
        var step = context == AttackContext.CrouchedOrAirborne
            ? _attackPolicy.Definition.CrouchedOrAirborne
            : _attackPolicy.Definition.GroundedCombo[stepIndex];
        var duration = (double)new SimulationRate(Engine.PhysicsTicksPerSecond)
            .SecondsFromDuration(step.TotalDuration);
        _characterView.PlayForDuration(step.AnimationId, duration);
    }

    private AttackPresentationIdentity? CurrentAttackPresentationIdentity() =>
        _attackState is null
            ? null
            : new AttackPresentationIdentity(
                _attackState.ExecutionId,
                _attackState.StepIndex);

    private bool HasUnconfirmedLocalAttackPrediction()
    {
        var current = CurrentAttackPresentationIdentity();
        return _locallyControlled &&
            current is not null &&
            _replicatedAttackTracker.ConfirmedIdentity != current;
    }

    private void ApplyReplicatedStatus(CombatantSnapshot snapshot)
    {
        var eliminated = snapshot.LifeState != ReplicatedLifeState.Alive;
        var newlyEliminated = eliminated && !_eliminated;
        var newlyRespawned = !eliminated && _eliminated;
        if (newlyRespawned)
        {
            _replicatedAttackTracker.Reset();
        }

        if (newlyEliminated)
        {
            _attackState = null;
            _characterView.Play("reaction.death");
        }

        _eliminated = eliminated;
        _lifeState = snapshot.LifeState;
        if (!eliminated)
        {
            _respawnSecondsRemaining = 0d;
        }
        _currentHealth = snapshot.CurrentHealth;
        _maximumHealth = snapshot.MaximumHealth;
        SetCollisionDisabled(eliminated || !_collisionEnabled);
        _respawnPresentationHidden =
            snapshot.LifeState == ReplicatedLifeState.Respawning;
        UpdateVisualVisibility();
        RefreshStatusPresentation();
    }

    private void RefreshStatusPresentation()
    {
        var status = Status;
        var ratio = status.MaximumHealth <= 0
            ? 0d
            : Math.Clamp(
                status.CurrentHealth / (double)status.MaximumHealth,
                0d,
                1d);
        var filled = (int)Math.Round(ratio * 10d);
        var healthBar = new string('■', filled) + new string('·', 10 - filled);
        _label.Text = status.LifeState switch
        {
            ReplicatedLifeState.Alive =>
                $"{status.DisplayLabel}\n[{healthBar}] {status.CurrentHealth}/{status.MaximumHealth}",
            ReplicatedLifeState.Eliminated =>
                $"{status.DisplayLabel}\nELIMINATED  {status.RespawnSecondsRemaining:0.0}s",
            ReplicatedLifeState.Respawning =>
                $"{status.DisplayLabel}\nRESPAWNING  {status.RespawnSecondsRemaining:0.0}s",
            _ => $"{status.DisplayLabel}\n{status.LifeState}",
        };

        if (_lastPublishedStatus == status)
        {
            return;
        }

        _lastPublishedStatus = status;
        StatusChanged?.Invoke(status);
    }

    private void UpdateCameraSafeVisibility()
    {
        var activeCamera = GetViewport().GetCamera3D();
        if (activeCamera is null)
        {
            return;
        }

        var bodyCenter = _visualAnchor.GlobalPosition + new Vector3(0f, 0.9f, 0f);
        var cameraInsideBody =
            activeCamera.GlobalPosition.DistanceTo(bodyCenter) <
            CameraVisualIntersectionRadius;
        var localCameraCollapsed =
            _locallyControlled &&
            activeCamera.GlobalPosition.DistanceTo(_cameraPitch.GlobalPosition) <
            LocalCameraHideDistance;
        var intersectsVisual = cameraInsideBody || localCameraCollapsed;
        if (_cameraIntersectsVisual == intersectsVisual)
        {
            return;
        }

        _cameraIntersectsVisual = intersectsVisual;
        UpdateVisualVisibility();
    }

    private void UpdateVisualVisibility()
    {
        _visualRoot.Visible =
            !_respawnPresentationHidden &&
            !_cameraIntersectsVisual;
    }

    private void RotateView(float yawDelta, float pitchDelta)
    {
        _yaw = Mathf.Wrap(_yaw + yawDelta, -Mathf.Pi, Mathf.Pi);
        _pitch = Mathf.Clamp(
            _pitch + pitchDelta,
            Mathf.DegToRad(-70f),
            Mathf.DegToRad(55f));
        ApplyView();
    }

    private void ApplyView()
    {
        if (!IsNodeReady())
        {
            return;
        }

        _cameraYaw.Rotation = new Vector3(0f, _yaw, 0f);
        _cameraPitch.Rotation = new Vector3(_pitch, 0f, 0f);
    }

    private void CaptureGameplayControl()
    {
        Input.MouseMode = Input.MouseModeEnum.Captured;
        _gameplayInputEnabled = true;
    }

    private void ReleaseGameplayControl()
    {
        _gameplayInputEnabled = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        ClearQueuedEdges();
    }

    private void ClearQueuedEdges()
    {
        _jumpPressedQueued = false;
        _jumpReleasedQueued = false;
        _crouchRollPressedQueued = false;
        _crouchRollReleasedQueued = false;
        _attackPressedQueued = false;
        _attackReleasedQueued = false;
    }

    private void BeginBlockingBodyCommit(BlockingBodyCommitKind kind)
    {
        if (_fixedPhysicsBlockingCommitsRequired && !Engine.IsInPhysicsFrame())
        {
            _blockingCommitPhaseViolationCount++;
            throw new InvalidOperationException(
                $"Blocking-body {kind} commit for combatant {CombatantId} " +
                "was attempted outside fixed physics.");
        }

        if (_fixedPhysicsBlockingCommitsRequired &&
            kind == BlockingBodyCommitKind.Lifecycle)
        {
            _fixedPhysicsLifecycleCommitCount++;
        }
    }

    private void SetCollisionDisabled(bool disabled)
    {
        if (_collision.Disabled == disabled)
        {
            return;
        }

        BeginBlockingBodyCommit(BlockingBodyCommitKind.Lifecycle);
        _collision.Disabled = disabled;
    }

    private static Vector3 ToGodot(Vector3Value? value) => value is null
        ? Vector3.Zero
        : new Vector3(value.X, value.Y, value.Z);

    private enum BlockingBodyCommitKind
    {
        Motion,
        Lifecycle,
    }
}

public readonly record struct NetworkAvatarBlockingBodySnapshot(
    Vector3 GlobalPosition,
    Vector3 Velocity,
    MovementRuntimeState MovementState,
    bool CollisionDisabled,
    float CapsuleHeight,
    float CapsuleRadius);
