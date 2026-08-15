#nullable enable

using BattleArena.Core.Common;
using BattleArena.Core.Movement;
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

    private GodotCharacterMovementDriver _movementDriver = null!;
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
        _lastStepOutcome = _movementDriver.Simulate(command, delta);
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
