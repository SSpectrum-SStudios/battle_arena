using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Core.Movement.Simulation;

namespace BattleArena.Core.Tests.Movement.Simulation;

/// <summary>
/// The step solver copes with a capsule's rounded bottom contacting a step's top
/// edge.
/// </summary>
/// <remarks>
/// <para>
/// This exists because <see cref="DeterministicCollisionWorld"/> approximates the
/// capsule as an axis-aligned box, and a box cannot reproduce the defect. A box's
/// flat bottom reports a step's flat top face the instant it overhangs, so
/// stepping succeeds trivially. A real capsule's hemisphere contacts the step's
/// top EDGE while its axis is still radius-scale short of the face — 0.37 m short
/// for a 0.4 m radius against a 0.25 m step — and an edge normal is steeper than
/// any walkable limit. In-engine that measured 53 degrees against a 45 degree
/// limit, so the step was refused every frame and the character was permanently
/// stuck on a stair while all 201 unit tests passed.
/// </para>
/// <para>
/// The world below is deliberately not a general capsule world. It encodes just
/// the one geometric fact the box reference cannot express: a down-probe taken
/// before the axis clears the face reports the corner, and one taken after it
/// clears reports the flat top. That is enough to hold the fix in place without a
/// running engine.
/// </para>
/// </remarks>
public sealed class CapsuleStepCornerTests
{
    private const double Radius = 0.4d;
    private const double StepFaceX = 2.0d;
    private const double StepTopY = 0.25d;

    [Fact]
    public void AStepIsClimbedEvenWhenTheCapsuleFirstContactsItsTopEdge()
    {
        var world = new EdgeContactWorld();
        var motor = new CapsuleMovementSimulator(world);

        // Standing where a 0.4 m capsule geometrically rests against a 0.25 m
        // step: axis short of the face by sqrt(r^2 - (r - h)^2).
        var contactX = StepFaceX - Math.Sqrt((Radius * Radius) - ((Radius - StepTopY) * (Radius - StepTopY)));
        var state = CharacterKinematicState.AtRest(new WorldPosition(contactX, 0d, 0d), 0d);

        var result = motor.SolveStep(
            state,
            CollisionProfileKind.Standing,
            new HorizontalVector(0.04d, 0d),
            Profiles,
            CapsuleMotorPolicy.TestDefault);

        Assert.Equal(CapsuleMotionOutcome.Stepped, result.Outcome);
        Assert.Equal(StepTopY, result.State.Position.Y, precision: 3);
        Assert.True(result.State.IsGrounded);
    }

    [Fact]
    public void TheStepCommitsOnlyTheMotionTheFrameEarnedRatherThanTheProbeDistance()
    {
        // The probe must reach past the face to find the walkable top, but
        // committing that distance would teleport the character up to a radius
        // forward whenever a step came into range.
        var world = new EdgeContactWorld();
        var motor = new CapsuleMovementSimulator(world);
        var contactX = StepFaceX - Math.Sqrt((Radius * Radius) - ((Radius - StepTopY) * (Radius - StepTopY)));
        var state = CharacterKinematicState.AtRest(new WorldPosition(contactX, 0d, 0d), 0d);

        var result = motor.SolveStep(
            state,
            CollisionProfileKind.Standing,
            new HorizontalVector(0.04d, 0d),
            Profiles,
            CapsuleMotorPolicy.TestDefault);

        Assert.Equal(CapsuleMotionOutcome.Stepped, result.Outcome);
        Assert.Equal(contactX + 0.04d, result.State.Position.X, precision: 4);
        Assert.True(
            world.LongestForwardProbe > 0.3d,
            $"The forward probe only reached {world.LongestForwardProbe:0.###} m, so it never " +
            "cleared the step face and the test is not exercising the defect.");
    }

    [Fact]
    public void AWallIsStillRefusedRatherThanClimbed()
    {
        // The probe reaching further must not turn a wall into a step. Gaining
        // height by pressing into a wall is the failure the forward gate exists
        // to prevent.
        var world = new EdgeContactWorld { WallInsteadOfStep = true };
        var motor = new CapsuleMovementSimulator(world);
        var state = CharacterKinematicState.AtRest(new WorldPosition(1.6d, 0d, 0d), 0d);

        var result = motor.SolveStep(
            state,
            CollisionProfileKind.Standing,
            new HorizontalVector(0.04d, 0d),
            Profiles,
            CapsuleMotorPolicy.TestDefault);

        Assert.Equal(CapsuleMotionOutcome.Blocked, result.Outcome);
        Assert.Equal(0d, result.State.Position.Y, precision: 6);
    }

    [Fact]
    public void AStepIsRefusedWhenTheCommittedLandingIsNotClear()
    {
        // The probe position cleared, but the position actually committed is
        // behind it. Committing without re-checking would place the character
        // inside geometry only the probe position escaped.
        var world = new EdgeContactWorld { CommittedLandingBlocked = true };
        var motor = new CapsuleMovementSimulator(world);
        var contactX = StepFaceX - Math.Sqrt((Radius * Radius) - ((Radius - StepTopY) * (Radius - StepTopY)));
        var state = CharacterKinematicState.AtRest(new WorldPosition(contactX, 0d, 0d), 0d);

        var result = motor.SolveStep(
            state,
            CollisionProfileKind.Standing,
            new HorizontalVector(0.04d, 0d),
            Profiles,
            CapsuleMotorPolicy.TestDefault);

        Assert.Equal(CapsuleMotionOutcome.Blocked, result.Outcome);
    }

    private static readonly CollisionProfileTable Profiles = new(
        new CollisionProfileDimensions(Radius, 1.8d),
        new CollisionProfileDimensions(Radius, 1.2d),
        new CollisionProfileDimensions(Radius, 0.9d));

    /// <summary>
    /// A world that reports a step's top edge before the capsule axis clears the
    /// face, and its flat top after — the one thing the box reference cannot do.
    /// </summary>
    private sealed class EdgeContactWorld : ICharacterCollisionWorld
    {
        private static readonly SupportIdentity StepSupport = new(7, 0);

        /// <summary>Treat the obstacle as a full-height wall with no walkable top.</summary>
        public bool WallInsteadOfStep { get; init; }

        /// <summary>Refuse clearance at the committed landing, but not at the probe.</summary>
        public bool CommittedLandingBlocked { get; init; }

        /// <summary>The furthest horizontal distance any sweep was asked to travel.</summary>
        public double LongestForwardProbe { get; private set; }

        public CapsuleSweepResult Sweep(
            in CapsuleSweepRequest request,
            Span<CollisionContactState> contacts)
        {
            var requested = request.HorizontalMotion.Length;
            if (requested > LongestForwardProbe)
            {
                LongestForwardProbe = requested;
            }

            // Raised above the step, horizontal travel is unobstructed. Below the
            // top, the face blocks. A wall blocks at every height.
            var blocks = WallInsteadOfStep || request.Origin.Y < StepTopY;
            if (!blocks || request.HorizontalMotion.X <= 0d)
            {
                return new CapsuleSweepResult(
                    request.HorizontalMotion, request.VerticalMotion, HorizontalVector.Zero, 0d, 0);
            }

            var allowed = Math.Max(0d, StepFaceX - Radius - request.Origin.X);
            var achieved = Math.Min(request.HorizontalMotion.X, allowed);
            contacts[0] = new CollisionContactState(
                new WorldPosition(StepFaceX, request.Origin.Y, request.Origin.Z),
                new SurfaceNormal(-1d, 0d, 0d),
                requested <= 0d ? 0d : achieved / requested,
                0d,
                StepSupport,
                ContactSurfaceKind.Wall);
            return new CapsuleSweepResult(
                new HorizontalVector(achieved, 0d),
                request.VerticalMotion,
                new HorizontalVector(request.HorizontalMotion.X - achieved, 0d),
                0d,
                1);
        }

        public GroundProbeResult ProbeGround(in GroundProbeRequest request)
        {
            if (WallInsteadOfStep)
            {
                // Only the lower floor exists.
                return Floor(request);
            }

            // The capsule's axis has cleared the face, so its hemisphere rests on
            // the flat top and the normal is straight up.
            if (request.Origin.X >= StepFaceX)
            {
                var distance = request.Origin.Y - StepTopY;
                return distance >= 0d && distance <= request.MaximumDistance
                    ? new GroundProbeResult(true, distance, SurfaceNormal.Up, StepSupport)
                    : GroundProbeResult.None;
            }

            // Still short of the face: the hemisphere catches the top edge, and
            // the edge normal is steeper than any walkable slope. This is the
            // answer that used to refuse the step forever.
            var lateral = StepFaceX - request.Origin.X;
            if (lateral < Radius)
            {
                var reach = Math.Sqrt(Math.Max(0d, (Radius * Radius) - (lateral * lateral)));
                var toEdge = request.Origin.Y - StepTopY + (Radius - reach);
                if (toEdge >= 0d && toEdge <= request.MaximumDistance)
                {
                    return new GroundProbeResult(
                        true, toEdge, new SurfaceNormal(-0.8d, 0.6d, 0d), StepSupport);
                }
            }

            return Floor(request);
        }

        private static GroundProbeResult Floor(in GroundProbeRequest request) =>
            request.Origin.Y >= 0d && request.Origin.Y <= request.MaximumDistance
                ? new GroundProbeResult(true, request.Origin.Y, SurfaceNormal.Up, new SupportIdentity(1, 0))
                : GroundProbeResult.None;

        public OverlapResolution ResolveOverlap(in ClearanceRequest request) =>
            OverlapResolution.None;

        public bool HasClearance(in ClearanceRequest request) =>
            !CommittedLandingBlocked || request.Origin.X >= StepFaceX;

        public bool TryGetSupportMotion(
            SupportIdentity support,
            SimulationInstant fromFrame,
            SimulationInstant toFrame,
            out HorizontalVector horizontal,
            out double vertical)
        {
            horizontal = HorizontalVector.Zero;
            vertical = 0d;
            return false;
        }
    }
}
