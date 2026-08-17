using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Core.Movement.Simulation;

namespace BattleArena.Core.Tests.Movement.Simulation;

public sealed class CapsuleMovementSimulatorTests
{
    private static readonly CollisionProfileTable Profiles = new(
        new CollisionProfileDimensions(0.4d, 1.8d),
        new CollisionProfileDimensions(0.4d, 1.2d),
        new CollisionProfileDimensions(0.4d, 0.9d));

    private static readonly CapsuleMotorPolicy Policy = CapsuleMotorPolicy.TestDefault;

    [Fact]
    public void FreeTravelAchievesTheWholeRequestedMotion()
    {
        var world = new DeterministicCollisionWorld(Profiles).AddGround();
        var motor = new CapsuleMovementSimulator(world);

        var result = Move(motor, At(0d, 0d, 0d), new HorizontalVector(1d, 0d), 0d);

        Assert.Equal(CapsuleMotionOutcome.Completed, result.Outcome);
        Assert.Equal(1d, result.State.Position.X, 6);
        Assert.True(result.State.IsGrounded);
    }

    [Fact]
    public void ForwardMotionUnderGravitySurvivesTheFloorContact()
    {
        // Every grounded frame looks like this: the driver proposes a small
        // downward velocity to keep the character on the floor, so the floor is
        // contacted at travel fraction zero. Only the downward component may be
        // removed — cancelling the frame outright leaves the character unable to
        // walk, and no test that passed zero vertical motion would ever see it.
        var world = new DeterministicCollisionWorld(Profiles).AddGround();
        var motor = new CapsuleMovementSimulator(world);

        var state = At(0d, 0d, 0d);
        for (var frame = 0; frame < 20; frame++)
        {
            state = motor.Move(
                state,
                CollisionProfileState.Standing,
                new HorizontalVector(0.1d, 0d),
                -0.05d,
                new SimulationInstant(frame),
                new SimulationInstant(frame + 1),
                Profiles,
                Policy).State;
        }

        Assert.True(
            state.Position.X > 1.9d,
            $"Expected ~2 m of travel under gravity, reached X={state.Position.X:0.###}.");
        Assert.True(state.IsGrounded);
        Assert.InRange(state.Position.Y, 0d, Policy.SurfaceSkin * 2d);
    }

    [Fact]
    public void AWallStopsForwardTravelAndTheCharacterSlidesAlongIt()
    {
        // Sliding rather than stopping is what makes wall contact feel
        // continuous instead of catching.
        var world = new DeterministicCollisionWorld(Profiles)
            .AddGround()
            .AddBox(2, new WorldPosition(2d, 0d, -10d), new WorldPosition(3d, 5d, 10d));
        var motor = new CapsuleMovementSimulator(world);

        var result = Move(motor, At(0d, 0d, 0d), new HorizontalVector(4d, 1d), 0d);

        Assert.True(result.State.Position.X < 2d, "The wall must stop forward travel.");
        Assert.True(
            result.State.Position.Z > 0.5d,
            "Motion along the wall must survive the projection.");
        Assert.Equal(CapsuleMotionOutcome.Slid, result.Outcome);
    }

    [Fact]
    public void DrivingIntoAWallActuallyBleedsOffTheVelocity()
    {
        // The deceleration. Without it a character pressed into a wall keeps full
        // speed forever: turning away feels glued, a jump gets the full
        // horizontal-speed bonus for speed it does not have, and a roll gated on
        // entry speed succeeds from a standstill. The accepted driver got this by
        // reading body velocity back after MoveAndSlide.
        var world = new DeterministicCollisionWorld(Profiles)
            .AddGround()
            .AddBox(2, new WorldPosition(2d, 0d, -10d), new WorldPosition(3d, 5d, 10d));
        var motor = new CapsuleMovementSimulator(world);

        // Enough frames at a realistic per-tick step to actually reach the wall
        // at x=2 from the origin.
        var state = At(0d, 0d, 0d).WithVelocity(new HorizontalVector(6d, 0d), 0d);
        for (var frame = 0; frame < 40; frame++)
        {
            state = motor.Move(
                state,
                CollisionProfileState.Standing,
                new HorizontalVector(0.1d, 0d),
                0d,
                new SimulationInstant(frame),
                new SimulationInstant(frame + 1),
                Profiles,
                Policy).State;
        }

        Assert.True(
            Math.Abs(state.HorizontalVelocity.X) < 0.01d,
            $"Velocity into the wall must be removed; X={state.HorizontalVelocity.X}.");
    }

    [Fact]
    public void SlidingAlongAWallKeepsTheVelocityParallelToIt()
    {
        // Only the component driven into the surface is removed. Removing all of
        // it would make wall contact a dead stop rather than a slide.
        var world = new DeterministicCollisionWorld(Profiles)
            .AddGround()
            .AddBox(2, new WorldPosition(2d, 0d, -10d), new WorldPosition(3d, 5d, 10d));
        var motor = new CapsuleMovementSimulator(world);

        var state = At(0d, 0d, 0d).WithVelocity(new HorizontalVector(6d, 4d), 0d);
        state = motor.Move(
            state,
            CollisionProfileState.Standing,
            new HorizontalVector(4d, 2d),
            0d,
            new SimulationInstant(0),
            new SimulationInstant(1),
            Profiles,
            Policy).State;

        Assert.True(state.HorizontalVelocity.Z > 3d, "Motion along the wall must survive.");
        Assert.True(state.HorizontalVelocity.X < 1d, "Motion into the wall must not.");
    }

    [Fact]
    public void LandingClearsDownwardVelocity()
    {
        // A landed character carrying its fall speed is a canonical comparison
        // field that is simply untrue, and any rule reading it — a landing roll,
        // a ledge left mid-roll — acts on a lie.
        var world = new DeterministicCollisionWorld(Profiles).AddGround();
        var motor = new CapsuleMovementSimulator(world);

        var falling = At(0d, 0.2d, 0d).WithVelocity(HorizontalVector.Zero, -15d);
        var result = motor.Move(
            falling,
            CollisionProfileState.Standing,
            HorizontalVector.Zero,
            -0.5d,
            new SimulationInstant(0),
            new SimulationInstant(1),
            Profiles,
            Policy);

        Assert.True(result.State.IsGrounded);
        Assert.Equal(0d, result.State.VerticalVelocity, 6);
    }

    [Fact]
    public void ContactResolutionDoesNotDependOnTheOrderTheWorldReportsContacts()
    {
        // Two worlds with identical geometry added in opposite order must
        // produce bit-identical results, or replay diverges from first run.
        var forward = new DeterministicCollisionWorld(Profiles)
            .AddGround()
            .AddBox(2, new WorldPosition(2d, 0d, -10d), new WorldPosition(3d, 5d, 10d))
            .AddBox(3, new WorldPosition(-10d, 0d, 2d), new WorldPosition(10d, 5d, 3d));
        var reversed = new DeterministicCollisionWorld(Profiles)
            .AddBox(3, new WorldPosition(-10d, 0d, 2d), new WorldPosition(10d, 5d, 3d))
            .AddBox(2, new WorldPosition(2d, 0d, -10d), new WorldPosition(3d, 5d, 10d))
            .AddGround();

        var a = Move(new CapsuleMovementSimulator(forward), At(0d, 0d, 0d), new HorizontalVector(5d, 5d), 0d);
        var b = Move(new CapsuleMovementSimulator(reversed), At(0d, 0d, 0d), new HorizontalVector(5d, 5d), 0d);

        Assert.Equal(a.State.Position, b.State.Position);
        Assert.Equal(a.Outcome, b.Outcome);
    }

    [Fact]
    public void ShallowOverlapIsRecoveredBeforeAnyMotionIsAttempted()
    {
        // A sweep starting inside geometry reports zero travel and makes no
        // progress, which reads to a player as being stuck.
        var world = new DeterministicCollisionWorld(Profiles)
            .AddGround()
            .AddBox(2, new WorldPosition(0d, 0d, 0d), new WorldPosition(4d, 5d, 4d));
        var motor = new CapsuleMovementSimulator(world);

        // Start slightly inside the box's -X face.
        var overlapping = At(-0.35d, 0d, 2d);
        var recovered = motor.RecoverPenetration(
            overlapping, CollisionProfileKind.Standing, Profiles, Policy);

        Assert.NotEqual(CapsuleMotionOutcome.UnrecoverablePenetration, recovered.Outcome);
        Assert.True(
            recovered.State.Position.X < overlapping.Position.X,
            "Recovery must push out along the shortest exit.");
    }

    [Fact]
    public void PenetrationDeeperThanThePolicyFailsClosedRatherThanGuessing()
    {
        // Pushing out of deep penetration is a guess about which side the
        // character belongs on, and guessing wrong teleports them through a wall.
        var world = new DeterministicCollisionWorld(Profiles)
            .AddBox(2, new WorldPosition(-5d, -5d, -5d), new WorldPosition(5d, 5d, 5d));
        var motor = new CapsuleMovementSimulator(world);

        var result = motor.RecoverPenetration(
            At(0d, 0d, 0d), CollisionProfileKind.Standing, Profiles, Policy);

        Assert.Equal(CapsuleMotionOutcome.UnrecoverablePenetration, result.Outcome);
        Assert.True(result.RequiresRepair);
    }

    [Fact]
    public void AWalkableSlopeSupportsButAnUnwalkableOneDoesNot()
    {
        var walkable = new DeterministicCollisionWorld(Profiles)
            .AddSloped(2, new WorldPosition(-5d, -1d, -5d), new WorldPosition(5d, 0d, 5d),
                new SurfaceNormal(0.3d, 0.954d, 0d));
        var steep = new DeterministicCollisionWorld(Profiles)
            .AddSloped(2, new WorldPosition(-5d, -1d, -5d), new WorldPosition(5d, 0d, 5d),
                new SurfaceNormal(0.95d, 0.312d, 0d));

        var onWalkable = new CapsuleMovementSimulator(walkable).ResolveGrounding(
            At(0d, 0.05d, 0d), CollisionProfileKind.Standing, wasRising: false, Profiles, Policy);
        var onSteep = new CapsuleMovementSimulator(steep).ResolveGrounding(
            At(0d, 0.05d, 0d), CollisionProfileKind.Standing, wasRising: false, Profiles, Policy);

        Assert.True(onWalkable.State.IsGrounded);
        Assert.False(onSteep.State.IsGrounded);
    }

    [Fact]
    public void GroundSnapIsSuppressedWhileRisingSoAJumpIsNotPulledBackDown()
    {
        // This single rule is the difference between a jump and a stutter.
        var world = new DeterministicCollisionWorld(Profiles).AddGround();
        var motor = new CapsuleMovementSimulator(world);

        var rising = motor.ResolveGrounding(
            At(0d, 0.05d, 0d), CollisionProfileKind.Standing, wasRising: true, Profiles, Policy);
        var falling = motor.ResolveGrounding(
            At(0d, 0.05d, 0d), CollisionProfileKind.Standing, wasRising: false, Profiles, Policy);

        Assert.False(rising.State.IsGrounded);
        Assert.Equal(0.05d, rising.State.Position.Y, 6);
        Assert.True(falling.State.IsGrounded);

        // Rests the skin distance above the surface, not exactly on it: sitting
        // precisely on the floor puts the capsule inside the engine's query
        // margin and costs travel every frame.
        Assert.InRange(falling.State.Position.Y, 0d, Policy.SurfaceSkin * 2d);
    }

    [Fact]
    public void LeavingALedgeBeyondTheSnapDistanceBecomesAirborne()
    {
        var world = new DeterministicCollisionWorld(Profiles)
            .AddBox(2, new WorldPosition(-5d, -1d, -5d), new WorldPosition(0d, 0d, 5d));
        var motor = new CapsuleMovementSimulator(world);

        // Past the ledge edge, with nothing within the snap distance.
        var result = motor.ResolveGrounding(
            At(3d, 0d, 0d), CollisionProfileKind.Standing, wasRising: false, Profiles, Policy);

        Assert.False(result.State.IsGrounded);
        Assert.Equal(SupportIdentity.None, result.State.Support);
    }

    [Fact]
    public void AStepShorterThanTheLimitIsClimbed()
    {
        var world = new DeterministicCollisionWorld(Profiles)
            .AddGround()
            .AddBox(2, new WorldPosition(1d, 0d, -5d), new WorldPosition(5d, 0.25d, 5d));
        var motor = new CapsuleMovementSimulator(world);

        var result = Move(motor, At(0d, 0d, 0d), new HorizontalVector(1.2d, 0d), 0d);

        Assert.True(
            result.State.Position.Y > 0.2d,
            $"Expected to climb the 0.25 step, ended at Y={result.State.Position.Y}.");
        Assert.True(result.State.IsGrounded);
    }

    [Fact]
    public void AWallTallerThanTheStepLimitIsNotClimbed()
    {
        // Refusing a step that makes no forward progress is what stops a
        // character gaining height by pressing into a wall.
        var world = new DeterministicCollisionWorld(Profiles)
            .AddGround()
            .AddBox(2, new WorldPosition(1d, 0d, -5d), new WorldPosition(5d, 4d, 5d));
        var motor = new CapsuleMovementSimulator(world);

        var result = Move(motor, At(0d, 0d, 0d), new HorizontalVector(2d, 0d), 0d);

        Assert.True(result.State.Position.Y < 0.05d, "A wall must not be climbed.");
        Assert.True(result.State.Position.X < 1d);
    }

    [Fact]
    public void AProfileExpansionIsRefusedUnderALowCeilingAndPermittedInTheOpen()
    {
        var blocked = new DeterministicCollisionWorld(Profiles)
            .AddGround()
            .AddBox(2, new WorldPosition(-5d, 1.3d, -5d), new WorldPosition(5d, 2d, 5d));
        var open = new DeterministicCollisionWorld(Profiles).AddGround();

        var underCeiling = new CapsuleMovementSimulator(blocked).CanExpandProfile(
            At(0d, 0d, 0d), CollisionProfileKind.Crouching, CollisionProfileKind.Standing, Profiles);
        var inTheOpen = new CapsuleMovementSimulator(open).CanExpandProfile(
            At(0d, 0d, 0d), CollisionProfileKind.Crouching, CollisionProfileKind.Standing, Profiles);

        Assert.False(underCeiling);
        Assert.True(inTheOpen);
    }

    [Fact]
    public void ShrinkingNeverNeedsClearance()
    {
        // A smaller capsule always fits where a larger one already did.
        var world = new DeterministicCollisionWorld(Profiles)
            .AddGround()
            .AddBox(2, new WorldPosition(-5d, 1.3d, -5d), new WorldPosition(5d, 2d, 5d));
        var motor = new CapsuleMovementSimulator(world);

        Assert.True(motor.CanExpandProfile(
            At(0d, 0d, 0d), CollisionProfileKind.Standing, CollisionProfileKind.Crouching, Profiles));
    }

    [Fact]
    public void SlideIterationsAreBoundedAndTheCapIsReported()
    {
        // Reported rather than hidden so the golden suite can detect geometry
        // that provokes it.
        var world = new DeterministicCollisionWorld(Profiles).AddGround();
        for (var i = 0; i < 8; i++)
        {
            world.AddBox(
                (ulong)(10 + i),
                new WorldPosition(0.5d + (i * 0.01d), 0d, -5d),
                new WorldPosition(0.6d + (i * 0.01d), 5d, 5d),
                shapeIndex: i);
        }

        var motor = new CapsuleMovementSimulator(world);
        var result = Move(motor, At(0d, 0d, 0d), new HorizontalVector(5d, 0d), 0d);

        Assert.True(
            result.SlideIterations <= Policy.MaximumSlideIterations,
            $"Iterations {result.SlideIterations} exceeded the cap.");
    }

    [Fact]
    public void ACeilingStopsUpwardMotionAndIsReported()
    {
        var world = new DeterministicCollisionWorld(Profiles)
            .AddGround()
            .AddBox(2, new WorldPosition(-5d, 2d, -5d), new WorldPosition(5d, 3d, 5d));
        var motor = new CapsuleMovementSimulator(world);

        var result = Move(motor, At(0d, 0d, 0d), HorizontalVector.Zero, 1.5d);

        Assert.True(result.CeilingBlocked, "Upward motion into a ceiling must be reported.");
        Assert.True(result.State.Position.Y < 0.5d);
    }

    [Fact]
    public void TheSameInputsFromTheSameStateAlwaysProduceTheSameResult()
    {
        // The property replay depends on.
        var world = new DeterministicCollisionWorld(Profiles)
            .AddGround()
            .AddBox(2, new WorldPosition(2d, 0d, -10d), new WorldPosition(3d, 5d, 10d))
            .AddBox(3, new WorldPosition(1d, 0d, 1d), new WorldPosition(6d, 0.3d, 6d));
        var motor = new CapsuleMovementSimulator(world);
        var start = At(0d, 0d, 0d);

        var first = Move(motor, start, new HorizontalVector(3d, 2d), -0.5d);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var again = Move(motor, start, new HorizontalVector(3d, 2d), -0.5d);
            Assert.Equal(first.State, again.State);
            Assert.Equal(first.Outcome, again.Outcome);
            Assert.Equal(first.SlideIterations, again.SlideIterations);
        }
    }

    private static CharacterKinematicState At(double x, double y, double z) =>
        CharacterKinematicState.AtRest(new WorldPosition(x, y, z), 0d);

    private static CapsuleMotionResult Move(
        CapsuleMovementSimulator motor,
        CharacterKinematicState state,
        HorizontalVector horizontal,
        double vertical) => motor.Move(
            state,
            CollisionProfileState.Standing,
            horizontal,
            vertical,
            new SimulationInstant(0),
            new SimulationInstant(1),
            Profiles,
            Policy);
}
