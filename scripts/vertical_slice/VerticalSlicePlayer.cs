#nullable enable

using Godot;

namespace BattleArena.VerticalSlice;

public partial class VerticalSlicePlayer : CharacterBody3D
{
    [Export(PropertyHint.Range, "0.1,50,0.1")]
    public float MoveSpeed { get; set; } = 6f;

    [Export(PropertyHint.Range, "1,5,0.1")]
    public float SprintMultiplier { get; set; } = 1.5f;

    [Export(PropertyHint.Range, "0.1,30,0.1")]
    public float JumpVelocity { get; set; } = 6f;

    [Export(PropertyHint.Range, "0.0001,0.02,0.0001")]
    public float MouseSensitivity { get; set; } = 0.0025f;

    [Export(PropertyHint.Range, "0.1,10,0.1")]
    public float ControllerLookSpeed { get; set; } = 2.5f;

    [Export]
    public NodePath CameraPitchPath { get; set; } = "";

    [Export]
    public NodePath ThirdPersonCameraPath { get; set; } = "";

    [Export]
    public NodePath FirstPersonCameraPath { get; set; } = "";

    [Export]
    public NodePath SwordAttackPath { get; set; } = "";

    [Export]
    public NodePath PoisonAuraPath { get; set; } = "";

    private Node3D _cameraPitch = null!;
    private Camera3D _thirdPersonCamera = null!;
    private Camera3D _firstPersonCamera = null!;
    private SwordAttackAdapter _swordAttack = null!;
    private PoisonAuraAdapter _poisonAura = null!;
    private float _pitch;
    private bool _firstPerson;
    private bool _suppressAttackUntilReleased;

    public override void _Ready()
    {
        _cameraPitch = GetNode<Node3D>(CameraPitchPath);
        _thirdPersonCamera = GetNode<Camera3D>(ThirdPersonCameraPath);
        _firstPersonCamera = GetNode<Camera3D>(FirstPersonCameraPath);
        _swordAttack = GetNode<SwordAttackAdapter>(SwordAttackPath);
        _poisonAura = GetNode<PoisonAuraAdapter>(PoisonAuraPath);
        Input.MouseMode = Input.MouseModeEnum.Captured;
        ApplyCameraMode();
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventKey keyEvent &&
            keyEvent.Pressed &&
            keyEvent.Keycode == Key.Escape)
        {
            Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
                ? Input.MouseModeEnum.Visible
                : Input.MouseModeEnum.Captured;
            GetViewport().SetInputAsHandled();
            return;
        }

        if (inputEvent is InputEventMouseButton mouseButton &&
            mouseButton.Pressed &&
            Input.MouseMode != Input.MouseModeEnum.Captured)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
            _suppressAttackUntilReleased = true;
            GetViewport().SetInputAsHandled();
            return;
        }

        if (inputEvent is InputEventMouseMotion mouseMotion &&
            Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            RotateView(-mouseMotion.Relative.X * MouseSensitivity, -mouseMotion.Relative.Y * MouseSensitivity);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        var controllerLook = Input.GetVector(
            VerticalSliceInput.LookLeft,
            VerticalSliceInput.LookRight,
            VerticalSliceInput.LookUp,
            VerticalSliceInput.LookDown);
        if (controllerLook.LengthSquared() > 0f)
        {
            RotateView(
                -controllerLook.X * ControllerLookSpeed * (float)delta,
                -controllerLook.Y * ControllerLookSpeed * (float)delta);
        }

        if (Input.IsActionJustPressed(VerticalSliceInput.CameraToggle))
        {
            _firstPerson = !_firstPerson;
            ApplyCameraMode();
        }

        if (_suppressAttackUntilReleased)
        {
            if (!Input.IsActionPressed(VerticalSliceInput.Attack))
            {
                _suppressAttackUntilReleased = false;
            }
        }
        else if (Input.IsActionJustPressed(VerticalSliceInput.Attack) &&
                 Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _swordAttack.TryStartAttack();
        }

        if (Input.IsActionJustPressed(VerticalSliceInput.ItemActivate1))
        {
            _poisonAura.Toggle();
        }

        var input = Input.GetVector(
            VerticalSliceInput.MoveLeft,
            VerticalSliceInput.MoveRight,
            VerticalSliceInput.MoveForward,
            VerticalSliceInput.MoveBackward);
        var desired = (Transform.Basis.X * input.X) + (-Transform.Basis.Z * -input.Y);
        desired.Y = 0f;
        desired = desired.Normalized();

        var speed = MoveSpeed *
                    (Input.IsActionPressed(VerticalSliceInput.Sprint) ? SprintMultiplier : 1f);
        var velocity = Velocity;
        velocity.X = desired.X * speed;
        velocity.Z = desired.Z * speed;

        if (!IsOnFloor())
        {
            var gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity", 9.8f);
            velocity.Y -= gravity * (float)delta;
        }
        else if (Input.IsActionJustPressed(VerticalSliceInput.Jump))
        {
            velocity.Y = JumpVelocity;
        }

        Velocity = velocity;
        MoveAndSlide();
    }

    public override void _ExitTree()
    {
        if (Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
    }

    private void RotateView(float yawDelta, float pitchDelta)
    {
        RotateY(yawDelta);
        _pitch = Mathf.Clamp(_pitch + pitchDelta, Mathf.DegToRad(-75f), Mathf.DegToRad(70f));
        _cameraPitch.Rotation = new Vector3(_pitch, 0f, 0f);
    }

    private void ApplyCameraMode()
    {
        _firstPersonCamera.Current = _firstPerson;
        _thirdPersonCamera.Current = !_firstPerson;
    }
}
