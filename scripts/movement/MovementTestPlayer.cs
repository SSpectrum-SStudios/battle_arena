#nullable enable

using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Core.Movement.Simulation;
using BattleArena.Presentation;
using BattleArena.VerticalSlice;
using Godot;

namespace BattleArena.Movement;

public partial class MovementTestPlayer : CharacterBody3D
{
    [Export(PropertyHint.File, "*.json")]
    public string MovementProfilePath { get; set; } = "res://assets/classes/fighter/movement.json";

    [Export]
    public NodePath VisualRootPath { get; set; } = "";

    [Export]
    public NodePath CharacterViewPath { get; set; } = "";

    [Export]
    public NodePath CameraYawPath { get; set; } = "";

    [Export]
    public NodePath CameraPitchPath { get; set; } = "";

    [Export]
    public NodePath DiagnosticsPath { get; set; } = "";

    [Export(PropertyHint.Range, "0.0005,0.02,0.0001")]
    public float MouseSensitivity { get; set; } = 0.0025f;

    [Export(PropertyHint.Range, "-1000,-1,1")]
    public float ResetHeight { get; set; } = -15f;

    /// <summary>
    /// Which motor drives this player.
    /// </summary>
    /// <remarks>
    /// The offline arena is where movement feel is judged, so being able to
    /// switch motors here — without changing a single authored attribute — is
    /// what makes "does the explicit motor still feel right" an answerable
    /// question rather than an opinion. Legacy stays the default so the arena
    /// behaves as before unless the switch is deliberately thrown.
    /// </remarks>
    [Export]
    public MovementTestMotorMode MotorMode { get; set; } = MovementTestMotorMode.Legacy;

    private GodotCharacterMovementDriver _movementDriver = null!;
    private GodotKinematicCollisionWorld? _collisionWorld;
    private CharacterMovementSimulator? _explicitSimulator;
    private CharacterSimulationState _explicitState;
    private MovementAttributeSnapshot _attributes = null!;
    private MovementCapabilitySnapshot _capabilities = null!;
    private CapsuleMotionOutcome _lastExplicitOutcome = CapsuleMotionOutcome.Completed;
    private Node3D _visualRoot = null!;
    private RiggedCharacterView _characterView = null!;
    private Node3D _cameraYaw = null!;
    private Node3D _cameraPitch = null!;
    private Label _diagnostics = null!;
    private Transform3D _spawnTransform;
    private ulong _sequence;
    private long _tick;
    private float _viewYaw;
    private float _viewPitch = Mathf.DegToRad(-12f);
    private bool _gameplayInputEnabled;
    private StepTraversalOutcome _lastStepOutcome;
    private Vector3 _visualRootBasePosition;
    private Vector3 _cameraYawBasePosition;
    private float _stepPresentationOffset;
    private bool _jumpPressedQueued;
    private bool _jumpReleasedQueued;
    private bool _crouchRollPressedQueued;
    private float _posturePresentationOffset;
    private bool _wasRolling;

    public override void _Ready()
    {
        VerticalSliceInput.EnsureDefaultBindings();
        var attributes = MovementProfileLoader.Load(
            MovementProfilePath,
            simulationRate: new SimulationRate(Engine.PhysicsTicksPerSecond)).Attributes;
        _visualRoot = GetNode<Node3D>(VisualRootPath);
        _characterView = GetNode<RiggedCharacterView>(CharacterViewPath);
        _cameraYaw = GetNode<Node3D>(CameraYawPath);
        _cameraPitch = GetNode<Node3D>(CameraPitchPath);
        _diagnostics = GetNode<Label>(DiagnosticsPath);
        _movementDriver = new GodotCharacterMovementDriver(
            this,
            GetNode<CollisionShape3D>("BodyCollision"),
            attributes,
            new SimulationRate(Engine.PhysicsTicksPerSecond),
            MovementRuntimeState.CreateGrounded(SimulationInstant.Zero));
        _attributes = attributes;
        _capabilities = MovementCapabilitySnapshot.CreateBaseFighter(1);
        if (MotorMode == MovementTestMotorMode.ExplicitQueryMotor)
        {
            BuildExplicitMotor();
        }

        GD.Print($"[MovementTestPlayer] Motor mode: {MotorMode}");
        _spawnTransform = GlobalTransform;
        _visualRootBasePosition = _visualRoot.Position;
        _cameraYawBasePosition = _cameraYaw.Position;

        ApplyView();
        CaptureMouse();
        _characterView.SetLocomotion(Vector2.Zero, airborne: false, 1d / Engine.PhysicsTicksPerSecond);
    }

    public override void _Process(double delta)
    {
        var smoothingWeight = 1f - Mathf.Exp(-18f * (float)delta);
        _stepPresentationOffset = Mathf.Lerp(_stepPresentationOffset, 0f, smoothingWeight);
        var targetPostureOffset = _movementDriver.State.LocomotionMode == LocomotionMode.Rolling
            ? -0.7f
            : _movementDriver.State.PostureMode == PostureMode.Crouched ? -0.42f : 0f;
        _posturePresentationOffset = Mathf.Lerp(
            _posturePresentationOffset,
            targetPostureOffset,
            smoothingWeight);
        var presentationOffset = Vector3.Up * _stepPresentationOffset;
        _visualRoot.Position = _visualRootBasePosition + presentationOffset;
        _cameraYaw.Position = _cameraYawBasePosition +
            presentationOffset +
            (Vector3.Up * _posturePresentationOffset);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMWindowFocusOut)
        {
            ReleaseMouse();
        }
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (inputEvent.IsActionPressed("ui_cancel"))
        {
            ReleaseMouse();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (inputEvent is InputEventMouseButton { Pressed: true } &&
            Input.MouseMode != Input.MouseModeEnum.Captured)
        {
            CaptureMouse();
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

        if (inputEvent is InputEventMouseMotion mouseMotion)
        {
            _viewYaw = Mathf.Wrap(_viewYaw - (mouseMotion.Relative.X * MouseSensitivity), -Mathf.Pi, Mathf.Pi);
            _viewPitch = Mathf.Clamp(
                _viewPitch - (mouseMotion.Relative.Y * MouseSensitivity),
                Mathf.DegToRad(-70f),
                Mathf.DegToRad(55f));
            ApplyView();
        }

        if (inputEvent is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Backspace })
        {
            ResetToSpawn();
        }
    }

    /// <summary>
    /// Creates the explicit motor and seeds its state from the node's current
    /// transform.
    /// </summary>
    /// <remarks>
    /// Seeding from the node happens exactly once. From here on the simulation
    /// state is the source of truth and the node follows it, which is the whole
    /// inversion this phase performs.
    /// </remarks>
    private void BuildExplicitMotor()
    {
        _collisionWorld = new GodotKinematicCollisionWorld();
        AddChild(_collisionWorld);
        _collisionWorld.Initialize(
            CollisionProfileTable.FromAttributes(_attributes),
            staticWorldMask: CollisionMask);
        _collisionWorld.SetWalkableSlope(_attributes.Ground.MaximumFloorAngleRadians);

        _explicitSimulator = new CharacterMovementSimulator(
            new CapsuleMovementSimulator(_collisionWorld),
            new MovementSourceSimulator());
        _explicitState = CharacterSimulationState.CreateGrounded(
            new WorldPosition(GlobalPosition.X, GlobalPosition.Y, GlobalPosition.Z),
            _viewYaw,
            SimulationInstant.Zero,
            new MovementConfigurationRevision(1),
            new MovementCapabilityRevision(1));
    }

    /// <summary>
    /// Advances the explicit motor one frame and moves the node to follow it.
    /// </summary>
    /// <remarks>
    /// The node is positioned from simulation rather than the reverse. Nothing
    /// reads the body's transform back into state, which is what makes the frame
    /// replayable.
    /// </remarks>
    private void SimulateExplicitMotor(MovementCommand command, SimulationInstant now)
    {
        var input = new CharacterSimulationInput(
            MovementAxes.FromUnitVector(command.Movement),
            ViewOrientation.FromRadians(command.ViewYawRadians, command.ViewPitchRadians),
            new MovementHeldState(HeldFrom(command.HeldButtons)),
            default,
            default,
            default,
            new MovementConfigurationRevision(1),
            new MovementCapabilityRevision(1));

        Span<MovementTransitionKindTag> transitions = stackalloc MovementTransitionKindTag[4];
        var count = 0;
        if (command.WasPressed(MovementButtons.Jump))
        {
            transitions[count++] = MovementTransitionKindTag.JumpPressed;
        }
        if (command.WasReleased(MovementButtons.Jump))
        {
            transitions[count++] = MovementTransitionKindTag.JumpReleased;
        }
        if (command.WasPressed(MovementButtons.CrouchOrRoll))
        {
            transitions[count++] = MovementTransitionKindTag.CrouchOrRollPressed;
        }
        if (command.WasReleased(MovementButtons.CrouchOrRoll))
        {
            transitions[count++] = MovementTransitionKindTag.CrouchOrRollReleased;
        }

        var result = _explicitSimulator!.Simulate(
            _explicitState,
            input,
            transitions[..count],
            SimulationStepContext.Current(now, new SimulationRate(Engine.PhysicsTicksPerSecond)),
            _attributes,
            _capabilities);

        _explicitState = result.State;
        _lastExplicitOutcome = result.Outcome;
        _lastStepOutcome = result.Outcome == CapsuleMotionOutcome.Stepped
            ? StepTraversalOutcome.Accepted
            : StepTraversalOutcome.NotNeeded;

        var position = _explicitState.Kinematic.Position;
        GlobalPosition = new Vector3((float)position.X, (float)position.Y, (float)position.Z);
        Velocity = new Vector3(
            (float)_explicitState.Kinematic.HorizontalVelocity.X,
            (float)_explicitState.Kinematic.VerticalVelocity,
            (float)_explicitState.Kinematic.HorizontalVelocity.Z);
    }

    private static MovementHeldButtons HeldFrom(MovementButtons buttons)
    {
        var held = MovementHeldButtons.None;
        if (buttons.HasFlag(MovementButtons.Jump))
        {
            held |= MovementHeldButtons.Jump;
        }
        if (buttons.HasFlag(MovementButtons.Sprint))
        {
            held |= MovementHeldButtons.Sprint;
        }
        if (buttons.HasFlag(MovementButtons.CrouchOrRoll))
        {
            held |= MovementHeldButtons.CrouchOrRoll;
        }
        return held;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (GlobalPosition.Y < ResetHeight)
        {
            ResetToSpawn();
        }

        var controllerLook = Input.GetVector(
            VerticalSliceInput.LookLeft,
            VerticalSliceInput.LookRight,
            VerticalSliceInput.LookUp,
            VerticalSliceInput.LookDown);
        if (_gameplayInputEnabled && controllerLook.LengthSquared() > 0f)
        {
            _viewYaw = Mathf.Wrap(_viewYaw - (controllerLook.X * 2.5f * (float)delta), -Mathf.Pi, Mathf.Pi);
            _viewPitch = Mathf.Clamp(
                _viewPitch - (controllerLook.Y * 2.5f * (float)delta),
                Mathf.DegToRad(-70f),
                Mathf.DegToRad(55f));
            ApplyView();
        }

        var movementInput = _gameplayInputEnabled
            ? Input.GetVector(
                VerticalSliceInput.MoveLeft,
                VerticalSliceInput.MoveRight,
                VerticalSliceInput.MoveForward,
                VerticalSliceInput.MoveBackward)
            : Vector2.Zero;
        var held = MovementButtons.None;
        var pressed = MovementButtons.None;
        var released = MovementButtons.None;
        if (_gameplayInputEnabled && Input.IsActionPressed(VerticalSliceInput.Sprint))
        {
            held |= MovementButtons.Sprint;
        }

        if (_gameplayInputEnabled && Input.IsActionPressed(VerticalSliceInput.Jump))
        {
            held |= MovementButtons.Jump;
        }

        if (_gameplayInputEnabled && Input.IsActionPressed(VerticalSliceInput.CrouchOrRoll))
        {
            held |= MovementButtons.CrouchOrRoll;
        }

        if (_gameplayInputEnabled && _jumpPressedQueued)
        {
            pressed |= MovementButtons.Jump;
        }

        if (_gameplayInputEnabled && _jumpReleasedQueued)
        {
            released |= MovementButtons.Jump;
        }

        if (_gameplayInputEnabled && _crouchRollPressedQueued)
        {
            pressed |= MovementButtons.CrouchOrRoll;
        }

        _jumpPressedQueued = false;
        _jumpReleasedQueued = false;
        _crouchRollPressedQueued = false;
        var now = new SimulationInstant(_tick++);
        var command = new MovementCommand(
            ++_sequence,
            now,
            new HorizontalVector(movementInput.X, movementInput.Y),
            _viewYaw,
            _viewPitch,
            held,
            pressed,
            released);

        var heightBeforeMove = GlobalPosition.Y;
        if (_explicitSimulator is not null)
        {
            SimulateExplicitMotor(command, now);
        }
        else
        {
            _lastStepOutcome = _movementDriver.Simulate(command, delta);
        }

        if (_lastStepOutcome == StepTraversalOutcome.Accepted)
        {
            _stepPresentationOffset += heightBeforeMove - GlobalPosition.Y;
        }

        _visualRoot.Rotation = new Vector3(
            0f,
            (float)_movementDriver.State.FacingYawRadians,
            0f);
        UpdateAnimation(delta);
        UpdateDiagnostics();
    }

    private void UpdateAnimation(double delta)
    {
        var rolling = _movementDriver.State.LocomotionMode == LocomotionMode.Rolling;
        if (rolling)
        {
            if (!_wasRolling)
            {
                var duration = (double)new SimulationRate(Engine.PhysicsTicksPerSecond)
                    .SecondsFromDuration(_movementDriver.State.RollDuration);
                _characterView.PlayForDuration(CharacterAnimationIds.RollForward, duration);
            }

            _wasRolling = true;
            return;
        }

        _wasRolling = false;
        if (_movementDriver.State.PostureMode == PostureMode.Crouched)
        {
            _characterView.Play(
                _movementDriver.State.HorizontalVelocity.Length > 0.1d
                    ? CharacterAnimationIds.CrouchMove
                    : CharacterAnimationIds.CrouchIdle);
            return;
        }

        var yaw = _movementDriver.State.FacingYawRadians;
        var cosine = Math.Cos(yaw);
        var sine = Math.Sin(yaw);
        var velocity = _movementDriver.State.HorizontalVelocity;
        var localRight = (velocity.X * cosine) - (velocity.Z * sine);
        var localForward = (-velocity.X * sine) - (velocity.Z * cosine);
        var normalizedVelocity = new Vector2(
            (float)(localRight / _movementDriver.Attributes.Ground.MaximumRunSpeed),
            (float)(localForward / _movementDriver.Attributes.Ground.MaximumRunSpeed));
        _characterView.SetLocomotion(
            normalizedVelocity,
            _movementDriver.State.LocomotionMode == LocomotionMode.Airborne,
            delta);
    }

    private void UpdateDiagnostics()
    {
        var speed = _movementDriver.State.HorizontalVelocity.Length;
        _diagnostics.Text =
            $"GROUND MOVEMENT LAB\n" +
            $"Speed  {speed:0.00} m/s\n" +
            $"Target {(_movementDriver.Attributes.Ground.MaximumRunSpeed):0.0} run / " +
            $"{(_movementDriver.Attributes.Ground.MaximumSprintSpeed):0.0} sprint\n" +
            $"Facing {Mathf.RadToDeg((float)_movementDriver.State.FacingYawRadians):0}°\n" +
            $"Mode   {_movementDriver.State.LocomotionMode}\n" +
            $"Jump   {_movementDriver.State.JumpPhase}\n" +
            $"Posture {_movementDriver.State.PostureMode}\n" +
            $"Floor  {(IsOnFloor() ? "grounded" : "falling")}\n" +
            $"Vert   {_movementDriver.State.VerticalVelocity:0.00} m/s\n\n" +
            $"Step   {_lastStepOutcome}\n\n" +
            $"Reject {_movementDriver.LastStepRejectionReason}\n\n" +
            $"WASD / left stick   Move\n" +
            $"Ctrl / L3           Sprint\n" +
            $"Space / A           Jump\n" +
            $"Shift / B           Hold crouch / moving roll\n" +
            $"Mouse / right stick Look\n" +
            $"Esc                  Release mouse\n" +
            $"Click                Capture mouse\n" +
            $"Backspace            Reset";
    }

    private void ResetToSpawn()
    {
        GlobalTransform = _spawnTransform;
        Velocity = Vector3.Zero;
        _movementDriver.Reset(
            MovementRuntimeState.CreateGrounded(new SimulationInstant(_tick)));
        _stepPresentationOffset = 0f;
        _posturePresentationOffset = 0f;
    }

    private void ApplyView()
    {
        if (!IsNodeReady())
        {
            return;
        }

        _cameraYaw.Rotation = new Vector3(0f, _viewYaw, 0f);
        _cameraPitch.Rotation = new Vector3(_viewPitch, 0f, 0f);
    }

    private void CaptureMouse()
    {
        Input.MouseMode = Input.MouseModeEnum.Captured;
        _gameplayInputEnabled = true;
    }

    private void ReleaseMouse()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _gameplayInputEnabled = false;
        _jumpPressedQueued = false;
        _jumpReleasedQueued = false;
        _crouchRollPressedQueued = false;
    }

}
