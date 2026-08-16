using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Core.Movement.Simulation;

namespace BattleArena.Core.Tests.Movement.Simulation;

public sealed class CharacterKinematicStateTests
{
    [Fact]
    public void AnAirborneStateNeverRetainsAStaleGroundNormalOrSupport()
    {
        // A retained slope would change how the next landing is classified.
        var grounded = CharacterKinematicState
            .AtRest(new WorldPosition(1d, 2d, 3d), 0.5d)
            .WithGround(true, new SurfaceNormal(0.3d, 0.9d, 0d), new SupportIdentity(7, 1));

        var airborne = grounded.WithGround(false, new SurfaceNormal(0.3d, 0.9d, 0d), new SupportIdentity(7, 1));

        Assert.Equal(SurfaceNormal.Up, airborne.GroundNormal);
        Assert.Equal(SupportIdentity.None, airborne.Support);
        Assert.True(airborne.IsValid);
    }

    [Fact]
    public void ANormalIsUnitLengthRegardlessOfHowItWasSupplied()
    {
        var normal = new SurfaceNormal(0d, 5d, 0d);

        Assert.Equal(1d, normal.Y, 12);
        Assert.True(normal.IsValid);
    }

    [Fact]
    public void ADegenerateNormalIsRefusedRatherThanSilentlyNormalized()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SurfaceNormal(0d, 0d, 0d));
    }

    [Theory]
    [InlineData(0d, 1d, 0d, ContactSurfaceKind.WalkableGround)]
    [InlineData(0.3d, 0.954d, 0d, ContactSurfaceKind.WalkableGround)]
    [InlineData(0.9d, 0.436d, 0d, ContactSurfaceKind.UnwalkableSlope)]
    [InlineData(1d, 0d, 0d, ContactSurfaceKind.Wall)]
    [InlineData(0d, -1d, 0d, ContactSurfaceKind.Ceiling)]
    public void SurfacesAreClassifiedOnceAgainstTheWalkableThreshold(
        double x,
        double y,
        double z,
        ContactSurfaceKind expected)
    {
        var walkable = Math.PI / 4d; // 45 degrees

        var kind = CollisionContactState.ClassifySurface(new SurfaceNormal(x, y, z), walkable);

        Assert.Equal(expected, kind);
    }

    [Fact]
    public void ContactOrderingIsTotalAndIndependentOfReportedOrder()
    {
        // Engine query results carry no ordering guarantee, so the same geometry
        // can report the same contacts in a different sequence on a different
        // run. Resolution must not depend on it.
        var a = Contact(travelFraction: 0.25d, collider: 5, shape: 0);
        var b = Contact(travelFraction: 0.25d, collider: 5, shape: 1);
        var c = Contact(travelFraction: 0.75d, collider: 2, shape: 0);

        var forward = new[] { a, b, c };
        var reversed = new[] { c, b, a };
        var comparer = default(CollisionContactState.StableComparer);
        Array.Sort(forward, comparer);
        Array.Sort(reversed, comparer);

        Assert.Equal(forward, reversed);
        Assert.Equal(0.25d, forward[0].TravelFraction);
        Assert.Equal(0, forward[0].Collider.ShapeIndex);
        Assert.Equal(1, forward[1].Collider.ShapeIndex);
    }

    private static CollisionContactState Contact(
        double travelFraction,
        ulong collider,
        int shape) => new(
            new WorldPosition(0d, 0d, 0d),
            SurfaceNormal.Up,
            travelFraction,
            0d,
            new SupportIdentity(collider, shape),
            ContactSurfaceKind.WalkableGround);
}

public sealed class CollisionProfileStateTests
{
    private static readonly CollisionProfileTable Profiles = new(
        new CollisionProfileDimensions(0.42d, 1.8d),
        new CollisionProfileDimensions(0.42d, 1.25d),
        new CollisionProfileDimensions(0.42d, 0.95d));

    [Fact]
    public void ShrinkingAppliesImmediatelyBecauseASmallerCapsuleAlwaysFits()
    {
        var state = CollisionProfileState.Standing;

        var crouched = state.WithDesired(CollisionProfileKind.Crouching, Profiles);

        Assert.Equal(CollisionProfileKind.Crouching, crouched.Current);
        Assert.False(crouched.HasPendingExpansion(Profiles));
    }

    [Fact]
    public void ExpandingRecordsIntentAndWaitsForClearance()
    {
        // The intent is held rather than discarded so the character stands the
        // moment clearance appears, instead of needing another press.
        var crouched = CollisionProfileState.Standing
            .WithDesired(CollisionProfileKind.Crouching, Profiles);

        var wantsToStand = crouched.WithDesired(CollisionProfileKind.Standing, Profiles);

        Assert.Equal(CollisionProfileKind.Crouching, wantsToStand.Current);
        Assert.Equal(CollisionProfileKind.Standing, wantsToStand.Desired);
        Assert.True(wantsToStand.HasPendingExpansion(Profiles));
        Assert.Equal(CollisionProfileKind.Standing, wantsToStand.ExpandToDesired().Current);
    }

    [Fact]
    public void ExpansionIsDecidedByDimensionsNotByEnumOrder()
    {
        // Standing is the smallest enum value and the tallest capsule, so enum
        // ordering would give exactly the wrong answer.
        Assert.True(Profiles.IsExpansion(CollisionProfileKind.Crouching, CollisionProfileKind.Standing));
        Assert.False(Profiles.IsExpansion(CollisionProfileKind.Standing, CollisionProfileKind.Crouching));
        Assert.True(Profiles.IsExpansion(CollisionProfileKind.Rolling, CollisionProfileKind.Crouching));
    }
}

public sealed class MovementSourceBufferTests
{
    [Fact]
    public void ASourceContributesOnExactlyItsHalfOpenWindow()
    {
        var source = Source(id: 1, start: 10, durationTicks: 3);

        Assert.False(source.IsActiveOn(new SimulationInstant(9)));
        Assert.True(source.IsActiveOn(new SimulationInstant(10)));
        Assert.True(source.IsActiveOn(new SimulationInstant(12)));
        Assert.False(source.IsActiveOn(new SimulationInstant(13)));
    }

    [Fact]
    public void ProgressIsDerivedFromTheStartFrameSoReplayReproducesIt()
    {
        var source = Source(id: 1, start: 100, durationTicks: 4);

        // Asked out of order, exactly as replay would.
        Assert.Equal(0.5d, source.ProgressOn(new SimulationInstant(102)));
        Assert.Equal(0d, source.ProgressOn(new SimulationInstant(100)));
        Assert.Equal(0.5d, source.ProgressOn(new SimulationInstant(102)));
        // A frame before the start must not throw; a source may start later.
        Assert.Equal(0d, source.ProgressOn(new SimulationInstant(50)));
    }

    [Fact]
    public void AddingTheSameIdentityTwiceIsIdempotentRatherThanDuplicating()
    {
        var buffer = default(MovementSourceBuffer);
        Assert.True(buffer.TryAddOrReplace(Source(id: 4, start: 0, durationTicks: 5)));
        Assert.True(buffer.TryAddOrReplace(Source(id: 4, start: 0, durationTicks: 9)));

        Assert.Equal(1, buffer.Count);
        Assert.Equal(9, buffer[0].Duration.Ticks);
    }

    [Fact]
    public void AggregationOrderIsStableByIdentityNotByInsertion()
    {
        // Floating-point addition is not associative, so an unstable order would
        // produce a different sum from identical inputs.
        var forward = default(MovementSourceBuffer);
        forward.TryAddOrReplace(Source(id: 1, start: 0, durationTicks: 10, speed: 0.1d));
        forward.TryAddOrReplace(Source(id: 2, start: 0, durationTicks: 10, speed: 0.2d));
        forward.TryAddOrReplace(Source(id: 3, start: 0, durationTicks: 10, speed: 0.3d));

        var reversed = default(MovementSourceBuffer);
        reversed.TryAddOrReplace(Source(id: 3, start: 0, durationTicks: 10, speed: 0.3d));
        reversed.TryAddOrReplace(Source(id: 2, start: 0, durationTicks: 10, speed: 0.2d));
        reversed.TryAddOrReplace(Source(id: 1, start: 0, durationTicks: 10, speed: 0.1d));

        var frame = new SimulationInstant(3);
        Assert.Equal(forward.Aggregate(frame).Horizontal, reversed.Aggregate(frame).Horizontal);
        Assert.Equal(forward, reversed);
    }

    [Fact]
    public void AFullBufferRefusesRatherThanDroppingTheOldest()
    {
        // Dropping would make the result depend on arrival order; growing would
        // break the copy cost this type exists to bound.
        var buffer = default(MovementSourceBuffer);
        for (var id = 1UL; id <= MovementSourceBuffer.Capacity; id++)
        {
            Assert.True(buffer.TryAddOrReplace(Source(id, start: 0, durationTicks: 5)));
        }

        Assert.True(buffer.IsFull);
        Assert.False(buffer.TryAddOrReplace(Source(999, start: 0, durationTicks: 5)));
        Assert.Equal(MovementSourceBuffer.Capacity, buffer.Count);
    }

    [Fact]
    public void ExpiryIsAFunctionOfTheFrameNotOfHowOftenItWasSimulated()
    {
        var buffer = default(MovementSourceBuffer);
        buffer.TryAddOrReplace(Source(id: 1, start: 0, durationTicks: 3));

        // Repeated, exactly as replay would.
        Assert.Equal(0, buffer.RemoveExpired(new SimulationInstant(2)));
        Assert.Equal(1, buffer.Count);
        Assert.Equal(1, buffer.RemoveExpired(new SimulationInstant(3)));
        Assert.Equal(0, buffer.RemoveExpired(new SimulationInstant(3)));
        Assert.Equal(0, buffer.Count);
    }

    [Fact]
    public void EqualityIsValueBasedOverOccupiedEntriesOnly()
    {
        var left = default(MovementSourceBuffer);
        var right = default(MovementSourceBuffer);
        left.TryAddOrReplace(Source(id: 1, start: 0, durationTicks: 5));
        right.TryAddOrReplace(Source(id: 1, start: 0, durationTicks: 5));

        Assert.True(left == right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());

        right.TryAddOrReplace(Source(id: 2, start: 0, durationTicks: 5));
        Assert.True(left != right);
    }

    private static MovementSourceState Source(
        ulong id,
        long start,
        long durationTicks,
        double speed = 1d) => new(
            MovementSourceKind.AttackLunge,
            id,
            new SimulationInstant(start),
            new SimulationDuration(durationTicks),
            new HorizontalVector(1d, 0d),
            speed,
            0d,
            MovementSourceFalloff.Linear);
}

public sealed class CharacterSimulationStateTests
{
    [Fact]
    public void TheRuntimeRoundTripPreservesEveryFieldTheRulesTouch()
    {
        // The rule simulators' entire output is velocity and facing plus the
        // mode/jump/roll fields. If any of them failed to cross, replay would
        // diverge silently.
        var state = Grounded();
        var runtime = state.ToRuntimeState() with
        {
            HorizontalVelocity = new HorizontalVector(3d, -4d),
            VerticalVelocity = 7.5d,
            FacingYawRadians = 1.25d,
            LocomotionMode = LocomotionMode.Airborne,
            PostureMode = PostureMode.Crouched,
            ActionMode = MovementActionMode.Attacking,
            JumpPhase = JumpPhase.Rising,
            JumpCutApplied = true,
            RollDirection = new HorizontalVector(0d, 1d),
            RollEntrySpeed = 6d,
            RollBoostDistance = 2.5d,
            RollDuration = new SimulationDuration(18),
            LandingRollQueued = true,
        };

        var folded = state.WithRuntimeState(runtime);

        Assert.Equal(new HorizontalVector(3d, -4d), folded.Kinematic.HorizontalVelocity);
        Assert.Equal(7.5d, folded.Kinematic.VerticalVelocity);
        Assert.Equal(1.25d, folded.Kinematic.FacingYawRadians, 12);
        Assert.Equal(LocomotionMode.Airborne, folded.LocomotionMode);
        Assert.Equal(PostureMode.Crouched, folded.PostureMode);
        Assert.Equal(MovementActionMode.Attacking, folded.ActionMode);
        Assert.Equal(JumpPhase.Rising, folded.JumpPhase);
        Assert.True(folded.JumpCutApplied);
        Assert.Equal(new HorizontalVector(0d, 1d), folded.RollDirection);
        Assert.Equal(6d, folded.RollEntrySpeed);
        Assert.Equal(2.5d, folded.RollBoostDistance);
        Assert.Equal(18, folded.RollDuration.Ticks);
        Assert.True(folded.LandingRollQueued);

        // And a second round trip is a fixed point.
        Assert.Equal(runtime, folded.ToRuntimeState());
    }

    [Fact]
    public void FoldingRuleOutputLeavesTheMotorsFieldsAlone()
    {
        // Position, grounding, normal, and support are the motor's to decide from
        // the world, after the rules have said how fast the character wants to go.
        var state = Grounded()
            .WithKinematic(CharacterKinematicState
                .AtRest(new WorldPosition(4d, 5d, 6d), 0d)
                .WithGround(true, new SurfaceNormal(0d, 1d, 0d), new SupportIdentity(9, 2)));

        var folded = state.WithRuntimeState(
            state.ToRuntimeState() with { HorizontalVelocity = new HorizontalVector(1d, 1d) });

        Assert.Equal(new WorldPosition(4d, 5d, 6d), folded.Kinematic.Position);
        Assert.True(folded.Kinematic.IsGrounded);
        Assert.Equal(new SupportIdentity(9, 2), folded.Kinematic.Support);
    }

    [Fact]
    public void TheStateCarriesItsOwnFrameSoARestoreCanIdentifyItself()
    {
        var state = Grounded().WithFrame(new SimulationInstant(500));

        Assert.Equal(new SimulationInstant(500), state.Frame);
        Assert.True(state.IsValid);
    }

    [Fact]
    public void AuthorityOnlyOutcomesAreStructurallyAbsent()
    {
        // Reflection rather than inspection: a later edit that adds a health or
        // damage field should fail here rather than in a playtest.
        var names = typeof(CharacterSimulationState)
            .GetProperties()
            .Select(property => property.Name.ToLowerInvariant())
            .ToArray();

        foreach (var forbidden in new[] { "health", "damage", "hit", "score", "kill" })
        {
            Assert.DoesNotContain(names, name => name.Contains(forbidden, StringComparison.Ordinal));
        }
    }

    private static CharacterSimulationState Grounded() =>
        CharacterSimulationState.CreateGrounded(
            new WorldPosition(0d, 0d, 0d),
            0d,
            new SimulationInstant(100),
            new MovementConfigurationRevision(1),
            new MovementCapabilityRevision(1));
}

public sealed class OwnerInputEdgeMapperTests
{
    [Fact]
    public void EdgesComeFromDurableTransitionsNotFromDiffingHeldState()
    {
        // P03-05 exists so a discrete edge does not depend on one transient bit
        // surviving three packets. An edge is present on exactly the frame its
        // transition applies, so a restored frame reproduces it without needing
        // the previous frame at all.
        ReadOnlySpan<MovementTransitionKindTag> transitions =
        [
            MovementTransitionKindTag.JumpPressed,
            MovementTransitionKindTag.CrouchOrRollReleased,
        ];

        var (pressed, released) = OwnerInputEdgeMapper.EdgesFor(transitions);

        Assert.True(pressed.HasFlag(MovementButtons.Jump));
        Assert.True(released.HasFlag(MovementButtons.CrouchOrRoll));
        Assert.False(pressed.HasFlag(MovementButtons.CrouchOrRoll));
    }

    [Fact]
    public void NoTransitionsMeansNoEdges()
    {
        var (pressed, released) = OwnerInputEdgeMapper.EdgesFor([]);

        Assert.Equal(MovementButtons.None, pressed);
        Assert.Equal(MovementButtons.None, released);
    }

    [Fact]
    public void APressImpliesTheButtonIsHeldOnThatFrame()
    {
        // A press and its held mask can race across packets; the edge is
        // authoritative about the frame it applies on.
        var input = new CharacterSimulationInput(
            default,
            ViewOrientation.FromRadians(0d, 0d),
            default,
            default,
            default,
            default,
            new MovementConfigurationRevision(1),
            new MovementCapabilityRevision(1));

        var command = OwnerInputEdgeMapper.ToMovementCommand(
            input,
            [MovementTransitionKindTag.JumpPressed],
            new SimulationInstant(7));

        Assert.True(command.HeldButtons.HasFlag(MovementButtons.Jump));
        Assert.True(command.PressedButtons.HasFlag(MovementButtons.Jump));
    }
}
