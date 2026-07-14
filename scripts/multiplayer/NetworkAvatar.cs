#nullable enable

using BattleArena.VerticalSlice;
using Godot;

namespace BattleArena.GodotNetworking;

public partial class NetworkAvatar : CharacterBody3D
{
    private const float PresentationCorrectionHalfLifeSeconds = 0.09f;
    private const float PresentationCorrectionDeadzoneMeters = 0.01f;
    private const float PresentationCorrectionSnapDistanceMeters = 1.5f;
    private const float PresentationCorrectionFinishedMeters = 0.001f;

    [Export]
    public NodePath CameraPitchPath { get; set; } = "";

    [Export]
    public NodePath ThirdPersonCameraPath { get; set; } = "";

    [Export]
    public NodePath FirstPersonCameraPath { get; set; } = "";

    [Export]
    public NodePath CollisionPath { get; set; } = "";

    [Export]
    public NodePath BodyMeshPath { get; set; } = "";

    [Export]
    public NodePath HeadMeshPath { get; set; } = "";

    [Export]
    public NodePath LabelPath { get; set; } = "";

    private Node3D _cameraPitch = null!;
    private Camera3D _thirdPersonCamera = null!;
    private Camera3D _firstPersonCamera = null!;
    private CollisionShape3D _collision = null!;
    private MeshInstance3D _bodyMesh = null!;
    private MeshInstance3D _headMesh = null!;
    private Label3D _label = null!;
    private bool _configured;
    private bool _locallyControlled;
    private bool _collisionEnabled;
    private bool _firstPerson;
    private bool _gameplayInputEnabled;
    private float _yaw;
    private float _pitch;
    private Color _color;
    private string _displayLabel = "Player";
    private Vector3 _visualRootBasePosition;
    private Vector3 _cameraPitchBasePosition;
    private Vector3 _labelBasePosition;
    private Vector3 _presentationOffset;

    public ulong CombatantId { get; private set; }

    public bool IsLocallyControlled => _locallyControlled;

    public float Yaw => _yaw;

    public float Pitch => _pitch;

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
            throw new InvalidOperationException("Configure must be called before adding a network avatar to the scene tree.");
        }

        _cameraPitch = GetNode<Node3D>(CameraPitchPath);
        _thirdPersonCamera = GetNode<Camera3D>(ThirdPersonCameraPath);
        _firstPersonCamera = GetNode<Camera3D>(FirstPersonCameraPath);
        _collision = GetNode<CollisionShape3D>(CollisionPath);
        _bodyMesh = GetNode<MeshInstance3D>(BodyMeshPath);
        _headMesh = GetNode<MeshInstance3D>(HeadMeshPath);
        _label = GetNode<Label3D>(LabelPath);
        _visualRootBasePosition = _bodyMesh.GetParent<Node3D>().Position;
        _cameraPitchBasePosition = _cameraPitch.Position;
        _labelBasePosition = _label.Position;

        _collision.Disabled = !_collisionEnabled;
        PhysicsInterpolationMode = _locallyControlled
            ? PhysicsInterpolationModeEnum.On
            : PhysicsInterpolationModeEnum.Off;
        _label.Text = _displayLabel;
        var material = new StandardMaterial3D
        {
            AlbedoColor = _color,
            Roughness = 0.68f,
        };
        _bodyMesh.MaterialOverride = material;
        _headMesh.MaterialOverride = material;
        ApplyCameraMode();

        if (_locallyControlled)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
            _gameplayInputEnabled = true;
        }
    }

    public override void _Process(double delta)
    {
        if (!_locallyControlled || _presentationOffset.IsZeroApprox())
        {
            return;
        }

        var remaining = Mathf.Pow(
            0.5f,
            (float)delta / PresentationCorrectionHalfLifeSeconds);
        _presentationOffset *= remaining;
        if (_presentationOffset.LengthSquared() <=
            PresentationCorrectionFinishedMeters * PresentationCorrectionFinishedMeters)
        {
            _presentationOffset = Vector3.Zero;
        }

        ApplyPresentationOffset();
    }

    public override void _Notification(int what)
    {
        if (!_locallyControlled || what != NotificationWMWindowFocusOut)
        {
            return;
        }

        _gameplayInputEnabled = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
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
            Input.MouseMode = Input.MouseModeEnum.Captured;
            _gameplayInputEnabled = true;
            GetViewport().SetInputAsHandled();
            return;
        }

        if (inputEvent is InputEventMouseMotion mouseMotion &&
            Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            RotateView(-mouseMotion.Relative.X * 0.0025f, -mouseMotion.Relative.Y * 0.0025f);
        }
    }

    public NetworkMovementInput CaptureInput(ulong sequence, ulong clientTick, float delta)
    {
        // This fallback reads the global action state even if another node consumed
        // the Escape event before normal gameplay input processing.
        if (_gameplayInputEnabled && Input.IsActionJustPressed("ui_cancel"))
        {
            ReleaseGameplayControl();
        }

        if (!_gameplayInputEnabled || !GetWindow().HasFocus())
        {
            return NetworkMovementInput.Neutral(clientTick, _yaw, _pitch) with { Sequence = sequence };
        }

        var controllerLook = Input.GetVector(
            VerticalSliceInput.LookLeft,
            VerticalSliceInput.LookRight,
            VerticalSliceInput.LookUp,
            VerticalSliceInput.LookDown);
        if (controllerLook.LengthSquared() > 0)
        {
            RotateView(-controllerLook.X * 2.5f * delta, -controllerLook.Y * 2.5f * delta);
        }

        if (Input.IsActionJustPressed(VerticalSliceInput.CameraToggle))
        {
            _firstPerson = !_firstPerson;
            ApplyCameraMode();
        }

        var movement = Input.GetVector(
            VerticalSliceInput.MoveLeft,
            VerticalSliceInput.MoveRight,
            VerticalSliceInput.MoveForward,
            VerticalSliceInput.MoveBackward);
        return new NetworkMovementInput(
            sequence,
            clientTick,
            movement.X,
            movement.Y,
            _yaw,
            _pitch,
            Input.IsActionJustPressed(VerticalSliceInput.Jump),
            Input.IsActionPressed(VerticalSliceInput.Sprint));
    }

    public void ApplyReplicatedTransform(
        Vector3 position,
        Vector3 velocity,
        float yaw,
        float pitch)
    {
        Position = position;
        Velocity = velocity;
        _yaw = yaw;
        Rotation = new Vector3(0, yaw, 0);
        SetPitch(pitch);
    }

    public void ApplyView(float yaw, float pitch)
    {
        _yaw = Mathf.Wrap(yaw, -Mathf.Pi, Mathf.Pi);
        Rotation = new Vector3(0, _yaw, 0);
        SetPitch(pitch);
    }

    public void PreservePresentationAfterCorrection(Vector3 previousPhysicsPosition)
    {
        if (!_locallyControlled)
        {
            return;
        }

        var correction = previousPhysicsPosition - Position;
        if (correction.LengthSquared() <=
            PresentationCorrectionDeadzoneMeters * PresentationCorrectionDeadzoneMeters)
        {
            return;
        }

        var existingWorldOffset = GlobalBasis * _presentationOffset;
        var combinedWorldOffset = existingWorldOffset + correction;
        if (combinedWorldOffset.LengthSquared() >=
            PresentationCorrectionSnapDistanceMeters * PresentationCorrectionSnapDistanceMeters)
        {
            _presentationOffset = Vector3.Zero;
        }
        else
        {
            _presentationOffset = GlobalBasis.Inverse() * combinedWorldOffset;
        }

        ApplyPresentationOffset();
    }

    public void SetPitch(float pitch)
    {
        _pitch = Mathf.Clamp(pitch, Mathf.DegToRad(-75f), Mathf.DegToRad(70f));
        if (IsNodeReady())
        {
            _cameraPitch.Rotation = new Vector3(_pitch, 0, 0);
        }
    }

    public void SetDiagnosticText(string text) => _label.Text = $"{_displayLabel}\n{text}";

    public override void _ExitTree()
    {
        if (_locallyControlled && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
    }

    private void RotateView(float yawDelta, float pitchDelta)
    {
        _yaw = Mathf.Wrap(_yaw + yawDelta, -Mathf.Pi, Mathf.Pi);
        SetPitch(_pitch + pitchDelta);
    }

    private void ReleaseGameplayControl()
    {
        _gameplayInputEnabled = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    private void ApplyPresentationOffset()
    {
        _bodyMesh.GetParent<Node3D>().Position = _visualRootBasePosition + _presentationOffset;
        _cameraPitch.Position = _cameraPitchBasePosition + _presentationOffset;
        _label.Position = _labelBasePosition + _presentationOffset;
    }

    private void ApplyCameraMode()
    {
        if (!IsNodeReady())
        {
            return;
        }

        _firstPersonCamera.Current = _locallyControlled && _firstPerson;
        _thirdPersonCamera.Current = _locallyControlled && !_firstPerson;
    }
}
