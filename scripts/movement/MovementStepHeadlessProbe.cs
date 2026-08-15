#nullable enable

using BattleArena.Core.Movement;
using Godot;

namespace BattleArena.Movement;

/// <summary>
/// Real-physics regression probe for geometry-independent step traversal.
/// Run through scenes/movement/headless_step_probe.tscn.
/// </summary>
public partial class MovementStepHeadlessProbe : Node3D
{
    private const int SettleTicks = 10;
    private const int SimulationTicks = 180;
    private readonly List<ProbeCase> _cases = [];
    private int _tick;

    public override void _Ready()
    {
        AddBox("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 30f));

        AddBox("HeadOnLedge", new Vector3(-12f, 0.1f, 0f), new Vector3(3f, 0.2f, 4f));
        _cases.Add(CreateCase(
            "head-on 0.20 m ledge",
            new Vector3(-12f, 0.05f, 4f),
            new HorizontalVector(0d, -3d),
            body => body.GlobalPosition.Z < -1f,
            minimumRise: 0.15f,
            shouldStep: true));

        AddBox("LateralLedge", new Vector3(0f, 0.1f, 0f), new Vector3(4f, 0.2f, 3f));
        _cases.Add(CreateCase(
            "lateral 0.20 m ledge",
            new Vector3(-4f, 0.05f, 0f),
            new HorizontalVector(3d, 0d),
            body => body.GlobalPosition.X > 1f,
            minimumRise: 0.15f,
            shouldStep: true));

        AddBox("DiagonalLedge", new Vector3(7f, 0.175f, 1f), new Vector3(4f, 0.35f, 4f));
        var diagonal = new HorizontalVector(1d, -1d).Normalized * 3d;
        _cases.Add(CreateCase(
            "diagonal 0.35 m ledge",
            new Vector3(4f, 0.05f, 4f),
            diagonal,
            body => body.GlobalPosition.X > 8f && body.GlobalPosition.Z < 0f,
            minimumRise: 0.28f,
            shouldStep: true));

        AddBox("TooTallLedge", new Vector3(12f, 0.3f, 0f), new Vector3(3f, 0.6f, 4f));
        _cases.Add(CreateCase(
            "over-height 0.60 m ledge",
            new Vector3(12f, 0.05f, 4f),
            new HorizontalVector(0d, -3d),
            body => body.GlobalPosition.Z >= 2.35f,
            minimumRise: 0f,
            shouldStep: false));

        AddBox("ParallelThenStrafeLedge", new Vector3(-5f, 0.1f, -8f), new Vector3(4f, 0.2f, 3f));
        var parallelThenStrafe = CreateCase(
            "parallel then lateral 0.20 m ledge",
            new Vector3(-7.45f, 0.05f, -6.8f),
            new HorizontalVector(0d, -3d),
            body => body.GlobalPosition.X > -4f,
            minimumRise: 0.15f,
            shouldStep: true);
        parallelThenStrafe.VelocityProvider = activeTick => activeTick < 30
            ? new HorizontalVector(0d, -3d)
            : new HorizontalVector(3d, 0d);
        _cases.Add(parallelThenStrafe);
    }

    public override void _PhysicsProcess(double delta)
    {
        _tick++;
        foreach (var probe in _cases)
        {
            var activeTick = Math.Max(0, _tick - SettleTicks);
            var requestedVelocity = probe.VelocityProvider(activeTick);
            if (_tick <= SettleTicks || !probe.Body.IsOnFloor())
            {
                var velocity = probe.Body.Velocity;
                velocity.X = (float)requestedVelocity.X;
                velocity.Z = (float)requestedVelocity.Z;
                velocity.Y -= 26f * (float)delta;
                probe.Body.Velocity = velocity;
                probe.Body.MoveAndSlide();
            }
            else
            {
                var outcome = probe.Motor.Move(
                    probe.Body,
                    requestedVelocity,
                    proposedVerticalVelocity: -0.5d,
                    delta,
                    probe.Attributes);
                if (outcome == StepTraversalOutcome.Accepted)
                {
                    probe.AcceptedSteps++;
                }
            }

            probe.MaximumHeight = Math.Max(probe.MaximumHeight, probe.Body.GlobalPosition.Y);
        }

        if (_tick < SimulationTicks)
        {
            return;
        }

        var failures = new List<string>();
        foreach (var probe in _cases)
        {
            var crossedAsExpected = probe.FinalPositionPredicate(probe.Body);
            var riseWasObserved = probe.MaximumHeight >= probe.MinimumRise;
            var stepCountWasExpected = probe.ShouldStep
                ? probe.AcceptedSteps > 0
                : probe.AcceptedSteps == 0;
            if (!crossedAsExpected || !riseWasObserved || !stepCountWasExpected)
            {
                failures.Add(
                    $"{probe.Name}: position={probe.Body.GlobalPosition}, " +
                    $"maxY={probe.MaximumHeight:0.000}, accepted={probe.AcceptedSteps}, " +
                    $"rejection={probe.Motor.LastRejectionReason}");
            }
        }

        if (failures.Count > 0)
        {
            foreach (var failure in failures)
            {
                GD.PushError($"[MovementStepProbe] {failure}");
            }

            GetTree().Quit(1);
            return;
        }

        GD.Print(
            "[MovementStepProbe] PASS: head-on, lateral, diagonal, parallel-then-strafe, " +
            "and over-height cases.");
        GetTree().Quit(0);
    }

    private ProbeCase CreateCase(
        string name,
        Vector3 position,
        HorizontalVector velocity,
        Func<CharacterBody3D, bool> finalPositionPredicate,
        float minimumRise,
        bool shouldStep)
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
            Name = name,
            Body = body,
            VelocityProvider = _ => velocity,
            FinalPositionPredicate = finalPositionPredicate,
            MinimumRise = minimumRise,
            ShouldStep = shouldStep,
            Attributes = new GroundMovementAttributes(
                maximumRunSpeed: 6d,
                maximumSprintSpeed: 12.5d,
                runAcceleration: 16d,
                sprintAcceleration: 14d,
                brakingDeceleration: 32d,
                reversalDeceleration: 42d,
                lowSpeedTurnRateRadians: Mathf.DegToRad(720f),
                highSpeedTurnRateRadians: Mathf.DegToRad(260f),
                reversalDotThreshold: -0.25d,
                maximumStepHeight: 0.4d,
                floorSnapDistance: 0.5d,
                maximumFloorAngleRadians: Mathf.DegToRad(50f),
                stepForwardAssistDistance: 0.08d),
        };
    }

    private void AddBox(string name, Vector3 position, Vector3 size)
    {
        var body = new StaticBody3D
        {
            Name = name,
            Position = position,
            CollisionLayer = 1,
            CollisionMask = 2,
        };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        AddChild(body);
    }

    private sealed class ProbeCase
    {
        public required string Name { get; init; }

        public required CharacterBody3D Body { get; init; }

        public required Func<int, HorizontalVector> VelocityProvider { get; set; }

        public required Func<CharacterBody3D, bool> FinalPositionPredicate { get; init; }

        public required GroundMovementAttributes Attributes { get; init; }

        public required float MinimumRise { get; init; }

        public required bool ShouldStep { get; init; }

        public float MaximumHeight { get; set; }

        public int AcceptedSteps { get; set; }

        public GodotGroundMotor Motor { get; } = new();
    }
}
