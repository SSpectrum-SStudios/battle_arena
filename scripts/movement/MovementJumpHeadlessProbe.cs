#nullable enable

using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using Godot;

namespace BattleArena.Movement;

/// <summary>
/// Real-physics regression probe for launch, variable height, falling, and landing.
/// Run through scenes/movement/headless_jump_probe.tscn.
/// </summary>
public partial class MovementJumpHeadlessProbe : Node3D
{
    private const int SettleTicks = 10;
    private const int MaximumTicks = 240;
    private readonly JumpFallSimulator _simulator = new();
    private readonly AirborneLocomotionSimulator _airborneSimulator = new();
    private readonly SimulationRate _rate = new(60);
    private readonly List<ProbeCase> _cases = [];
    private JumpMovementAttributes _attributes = null!;
    private AirMovementAttributes _airAttributes = null!;
    private int _tick;

    public override void _Ready()
    {
        AddBox(new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 40f));
        _attributes = new JumpMovementAttributes(
            13.4d,
            0.04d,
            23d,
            16d,
            45d,
            36d,
            55d,
            1.5d,
            new SimulationDuration(7),
            new SimulationDuration(7));
        _airAttributes = new AirMovementAttributes(23.5d, 3.5d, 6d, 12.5d, 0.5d, 0.15d, Math.PI);
        _cases.Add(CreateCase(
            "stationary diagonal pillar jump",
            new Vector3(-6f, 0.05f, 0f),
            releaseTick: null,
            movement: new HorizontalVector(1d, -1d)));
        _cases.Add(CreateCase(
            "short hop",
            new Vector3(-2f, 0.05f, 0f),
            releaseTick: 5,
            movement: HorizontalVector.Zero));
        _cases.Add(CreateCase(
            "stationary lateral jump",
            new Vector3(2f, 0.05f, 0f),
            releaseTick: null,
            movement: new HorizontalVector(1d, 0d)));
        _cases.Add(CreateCase(
            "sprint momentum jump",
            new Vector3(6f, 0.05f, 0f),
            releaseTick: null,
            movement: HorizontalVector.Zero,
            initialVelocity: new HorizontalVector(0d, -12.5d)));
    }

    public override void _PhysicsProcess(double delta)
    {
        _tick++;
        foreach (var probe in _cases)
        {
            var activeTick = _tick - SettleTicks;
            var pressed = activeTick == 0 ? MovementButtons.Jump : MovementButtons.None;
            var released = activeTick == probe.ReleaseTick ? MovementButtons.Jump : MovementButtons.None;
            var now = new SimulationInstant(_tick);
            var command = new MovementCommand(
                (ulong)_tick,
                now,
                probe.Movement,
                0d,
                0d,
                pressedButtons: pressed,
                releasedButtons: released);
            probe.State = _simulator.Simulate(
                probe.State,
                command,
                probe.Body.IsOnFloor(),
                _attributes,
                _rate);
            if (probe.State.LocomotionMode == LocomotionMode.Airborne)
            {
                probe.State = _airborneSimulator.Simulate(
                    probe.State,
                    command,
                    _airAttributes,
                    _rate);
            }

            probe.Body.Velocity = new Vector3(
                (float)probe.State.HorizontalVelocity.X,
                (float)probe.State.VerticalVelocity,
                (float)probe.State.HorizontalVelocity.Z);
            if (probe.State.LocomotionMode == LocomotionMode.Grounded)
            {
                probe.Body.Velocity = new Vector3(0f, -0.5f, 0f);
            }

            probe.Body.MoveAndSlide();
            probe.State = probe.State with { VerticalVelocity = probe.Body.Velocity.Y };
            probe.MaximumHeight = Math.Max(probe.MaximumHeight, probe.Body.GlobalPosition.Y);
            probe.Launched |= probe.State.JumpPhase != JumpPhase.None;
            if (probe.Launched && activeTick > 5 && probe.Body.IsOnFloor())
            {
                probe.Landed = true;
            }
        }

        if (_cases.All(probe => probe.Landed))
        {
            Finish();
            return;
        }

        if (_tick >= MaximumTicks)
        {
            GD.PushError("[MovementJumpProbe] Timed out before both jumps landed.");
            GetTree().Quit(1);
        }
    }

    private void Finish()
    {
        var fullRise = _cases[0].MaximumHeight - _cases[0].StartHeight;
        var shortRise = _cases[1].MaximumHeight - _cases[1].StartHeight;
        var sprintRise = _cases[3].MaximumHeight - _cases[3].StartHeight;
        var standingJumpDistance = new Vector2(
            _cases[0].Body.GlobalPosition.X - _cases[0].StartPosition.X,
            _cases[0].Body.GlobalPosition.Z - _cases[0].StartPosition.Z).Length();
        var diagonalX = Math.Abs(_cases[0].Body.GlobalPosition.X - _cases[0].StartPosition.X);
        var diagonalZ = Math.Abs(_cases[0].Body.GlobalPosition.Z - _cases[0].StartPosition.Z);
        var lateralJumpDistance = new Vector2(
            _cases[2].Body.GlobalPosition.X - _cases[2].StartPosition.X,
            _cases[2].Body.GlobalPosition.Z - _cases[2].StartPosition.Z).Length();
        if (fullRise < 3.5f ||
            fullRise > 4.1f ||
            sprintRise < 4.2f ||
            sprintRise > 4.5f ||
            shortRise >= fullRise * 0.7f ||
            standingJumpDistance < 5.65f ||
            standingJumpDistance > 6f ||
            diagonalX < 4f ||
            diagonalZ < 4f ||
            lateralJumpDistance < 1.8f ||
            lateralJumpDistance > 2.6f)
        {
            GD.PushError(
                $"[MovementJumpProbe] Unexpected heights: full={fullRise:0.000} m, " +
                $"sprint={sprintRise:0.000} m, short={shortRise:0.000} m, " +
                $"diagonal={standingJumpDistance:0.000} m ({diagonalX:0.000}, {diagonalZ:0.000}), " +
                $"lateral={lateralJumpDistance:0.000} m.");
            GetTree().Quit(1);
            return;
        }

        GD.Print(
            $"[MovementJumpProbe] PASS: full={fullRise:0.000} m, " +
            $"sprint={sprintRise:0.000} m, short={shortRise:0.000} m, " +
            $"diagonal={standingJumpDistance:0.000} m ({diagonalX:0.000}, {diagonalZ:0.000}), " +
            $"lateral={lateralJumpDistance:0.000} m, " +
            "both landed.");
        GetTree().Quit(0);
    }

    private ProbeCase CreateCase(
        string name,
        Vector3 position,
        int? releaseTick,
        HorizontalVector movement,
        HorizontalVector? initialVelocity = null)
    {
        var body = new CharacterBody3D
        {
            Name = name.Replace(' ', '_'),
            Position = position,
            CollisionLayer = 2,
            CollisionMask = 1,
            FloorSnapLength = 0.5f,
            FloorMaxAngle = Mathf.DegToRad(50f),
        };
        body.AddChild(new CollisionShape3D
        {
            Position = new Vector3(0f, 0.9f, 0f),
            Shape = new CapsuleShape3D { Radius = 0.42f, Height = 1.8f },
        });
        AddChild(body);

        return new ProbeCase
        {
            Body = body,
            State = MovementRuntimeState.CreateGrounded(SimulationInstant.Zero) with
            {
                HorizontalVelocity = initialVelocity ?? HorizontalVector.Zero,
            },
            StartHeight = position.Y,
            StartPosition = position,
            MaximumHeight = position.Y,
            ReleaseTick = releaseTick,
            Movement = movement,
        };
    }

    private void AddBox(Vector3 position, Vector3 size)
    {
        var body = new StaticBody3D
        {
            Position = position,
            CollisionLayer = 1,
            CollisionMask = 2,
        };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        AddChild(body);
    }

    private sealed class ProbeCase
    {
        public required CharacterBody3D Body { get; init; }
        public required MovementRuntimeState State { get; set; }
        public required float StartHeight { get; init; }
        public required Vector3 StartPosition { get; init; }
        public required float MaximumHeight { get; set; }
        public required int? ReleaseTick { get; init; }
        public required HorizontalVector Movement { get; init; }
        public bool Launched { get; set; }
        public bool Landed { get; set; }
    }
}
