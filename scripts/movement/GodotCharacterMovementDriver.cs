#nullable enable

using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using Godot;

namespace BattleArena.Movement;

/// <summary>
/// The single Godot-facing movement orchestrator used by offline play,
/// authority simulation, and owning-client prediction. Plain C# simulators own
/// rules; this adapter supplies collision facts and feeds Godot's resolved
/// velocity back into the replayable runtime state.
/// </summary>
public sealed class GodotCharacterMovementDriver
{
    private readonly CharacterBody3D _body;
    private readonly CollisionShape3D _bodyCollision;
    private readonly SimulationRate _simulationRate;
    private readonly GroundLocomotionSimulator _groundSimulator = new();
    private readonly AirborneLocomotionSimulator _airborneSimulator = new();
    private readonly JumpFallSimulator _jumpFallSimulator = new();
    private readonly CrouchRollSimulator _crouchRollSimulator = new();
    private readonly GodotGroundMotor _groundMotor = new();

    public GodotCharacterMovementDriver(
        CharacterBody3D body,
        CollisionShape3D bodyCollision,
        MovementAttributeSnapshot attributes,
        SimulationRate simulationRate,
        MovementRuntimeState initialState)
    {
        _body = body ?? throw new ArgumentNullException(nameof(body));
        _bodyCollision = bodyCollision ?? throw new ArgumentNullException(nameof(bodyCollision));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        _simulationRate = simulationRate;
        State = initialState ?? throw new ArgumentNullException(nameof(initialState));

        _body.FloorSnapLength = (float)Attributes.Ground.FloorSnapDistance;
        _body.FloorMaxAngle = (float)Attributes.Ground.MaximumFloorAngleRadians;
        _body.FloorStopOnSlope = true;
        _body.FloorConstantSpeed = false;
        ApplyCollisionProfile();
    }

    public MovementAttributeSnapshot Attributes { get; }

    public MovementRuntimeState State { get; private set; }

    public StepTraversalOutcome LastStepOutcome { get; private set; }

    public StepTraversalRejectionReason LastStepRejectionReason =>
        _groundMotor.LastRejectionReason;

    public StepTraversalOutcome Simulate(
        MovementCommand command,
        double delta,
        bool? groundedOverride = null,
        MovementInfluence? movementInfluence = null)
    {
        var grounded = groundedOverride ?? _body.IsOnFloor();
        var canStand = State.PostureMode == PostureMode.Standing || CanStand();
        State = _crouchRollSimulator.Simulate(
            State,
            command,
            grounded,
            canStand,
            Attributes.CrouchRoll,
            Attributes.Jump,
            Attributes.Ground,
            _simulationRate);
        ApplyCollisionProfile();

        if (State.PostureMode == PostureMode.Crouched)
        {
            command = canStand
                ? AsCrouchCommand(command)
                : WithoutJump(AsCrouchCommand(command));
        }

        if (State.LocomotionMode == LocomotionMode.Rolling)
        {
            _body.Velocity = new Vector3(
                (float)State.HorizontalVelocity.X,
                grounded ? -0.5f : (float)State.VerticalVelocity,
                (float)State.HorizontalVelocity.Z);
            _body.MoveAndSlide();
            LastStepOutcome = StepTraversalOutcome.NotNeeded;
        }
        else
        {
            State = _jumpFallSimulator.Simulate(
                State,
                command,
                grounded,
                Attributes.Jump,
                _simulationRate);
            ApplyCollisionProfile();
            if (State.LocomotionMode == LocomotionMode.Grounded)
            {
                State = _groundSimulator.Simulate(
                    State,
                    command,
                    Attributes.Ground,
                    _simulationRate,
                    movementInfluence);
                LastStepOutcome = _groundMotor.Move(
                    _body,
                    State.HorizontalVelocity,
                    proposedVerticalVelocity: -0.5d,
                    delta,
                    Attributes.Ground,
                    grounded);
            }
            else
            {
                State = _airborneSimulator.Simulate(
                    State,
                    command,
                    Attributes.Air,
                    _simulationRate);
                _body.Velocity = new Vector3(
                    (float)State.HorizontalVelocity.X,
                    (float)State.VerticalVelocity,
                    (float)State.HorizontalVelocity.Z);
                _body.MoveAndSlide();
                LastStepOutcome = StepTraversalOutcome.NotNeeded;
            }
        }

        State = State with
        {
            HorizontalVelocity = new HorizontalVector(_body.Velocity.X, _body.Velocity.Z),
            VerticalVelocity = _body.Velocity.Y,
        };
        return LastStepOutcome;
    }

    public void Restore(MovementRuntimeState state, Vector3 velocity)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        _body.Velocity = velocity;
        ApplyCollisionProfile();
    }

    public void Reset(MovementRuntimeState state)
    {
        Restore(state, Vector3.Zero);
        LastStepOutcome = StepTraversalOutcome.NotNeeded;
    }

    public void ReplaceState(MovementRuntimeState state)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        ApplyCollisionProfile();
    }

    private MovementCommand AsCrouchCommand(MovementCommand command)
    {
        var ratio = Attributes.CrouchRoll.MaximumCrouchSpeed /
            Attributes.Ground.MaximumRunSpeed;
        return new MovementCommand(
            command.Sequence,
            command.ClientTick,
            command.Movement * ratio,
            command.ViewYawRadians,
            command.ViewPitchRadians,
            command.HeldButtons & ~MovementButtons.Sprint,
            command.PressedButtons,
            command.ReleasedButtons);
    }

    private static MovementCommand WithoutJump(MovementCommand command) => new(
        command.Sequence,
        command.ClientTick,
        command.Movement,
        command.ViewYawRadians,
        command.ViewPitchRadians,
        command.HeldButtons & ~MovementButtons.Jump,
        command.PressedButtons & ~MovementButtons.Jump,
        command.ReleasedButtons & ~MovementButtons.Jump);

    private bool CanStand()
    {
        var profile = Attributes.CrouchRoll;
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new CapsuleShape3D
            {
                Radius = (float)profile.CapsuleRadius,
                Height = (float)profile.StandingCapsuleHeight,
            },
            Transform = new Transform3D(
                Basis.Identity,
                _body.GlobalPosition +
                (Vector3.Up * (float)((profile.StandingCapsuleHeight * 0.5d) + 0.01d))),
            CollisionMask = _body.CollisionMask,
            CollideWithAreas = false,
            CollideWithBodies = true,
            Exclude = new Godot.Collections.Array<Rid> { _body.GetRid() },
        };
        return _body.GetWorld3D().DirectSpaceState.IntersectShape(query, 1).Count == 0;
    }

    private void ApplyCollisionProfile()
    {
        if (_bodyCollision.Shape is not CapsuleShape3D capsule)
        {
            return;
        }

        var profile = Attributes.CrouchRoll;
        var height = State.LocomotionMode == LocomotionMode.Rolling
            ? profile.RollingCapsuleHeight
            : State.PostureMode == PostureMode.Crouched
                ? profile.CrouchingCapsuleHeight
                : profile.StandingCapsuleHeight;
        capsule.Radius = (float)profile.CapsuleRadius;
        capsule.Height = (float)height;
        _bodyCollision.Position = new Vector3(0f, (float)(height * 0.5d), 0f);
    }
}
