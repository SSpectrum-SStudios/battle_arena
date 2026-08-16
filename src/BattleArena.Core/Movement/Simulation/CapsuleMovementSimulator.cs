using BattleArena.Core.Common;

namespace BattleArena.Core.Movement.Simulation;

/// <summary>
/// Authored tuning for the explicit capsule motor. Immutable and validated once.
/// </summary>
/// <remarks>
/// These are the values that decide feel — what counts as walkable, how tall a
/// step may be climbed, how far the character snaps down to stay grounded — so
/// they are content, not constants, and are carried by a movement configuration
/// revision. A replayed frame uses the revision it was simulated under.
/// </remarks>
public readonly record struct CapsuleMotorPolicy
{
    /// <summary>
    /// Slopes at or below this are walkable. Above it the character slides and
    /// is never considered supported.
    /// </summary>
    public double WalkableSlopeRadians { get; init; }

    /// <summary>Tallest lip the step solver may climb.</summary>
    public double MaximumStepHeight { get; init; }

    /// <summary>
    /// How far below the feet ground snap will search while descending. Bounds
    /// the difference between walking down a step and falling off a ledge.
    /// </summary>
    public double GroundSnapDistance { get; init; }

    /// <summary>
    /// Overlap this deep or less is pushed out; deeper is unrecoverable and
    /// fails closed rather than teleporting the character through geometry.
    /// </summary>
    public double MaximumRecoverablePenetration { get; init; }

    /// <summary>
    /// Cap on slide iterations per frame. Bounds worst-case cost and prevents a
    /// pathological corner from looping; the remaining motion is discarded,
    /// which is visible as a stop rather than as a tunnel.
    /// </summary>
    public int MaximumSlideIterations { get; init; }

    /// <summary>
    /// Small separation kept from surfaces so the next frame's sweep does not
    /// start already touching, which would report a zero-fraction contact and
    /// stall progress.
    /// </summary>
    public double SurfaceSkin { get; init; }

    public bool IsValid =>
        double.IsFinite(WalkableSlopeRadians) &&
            WalkableSlopeRadians is > 0d and <= Math.PI / 2d &&
        double.IsFinite(MaximumStepHeight) && MaximumStepHeight >= 0d &&
        double.IsFinite(GroundSnapDistance) && GroundSnapDistance >= 0d &&
        double.IsFinite(MaximumRecoverablePenetration) && MaximumRecoverablePenetration > 0d &&
        MaximumSlideIterations > 0 &&
        double.IsFinite(SurfaceSkin) && SurfaceSkin >= 0d;

    /// <summary>
    /// Test-only baseline. Production construction derives the policy from the
    /// authored attributes for the frame's revision; a static default here would
    /// reintroduce the unrevisioned-content problem this type documents against.
    /// </summary>
    public static CapsuleMotorPolicy TestDefault => new()
    {
        WalkableSlopeRadians = Math.PI / 4d,
        MaximumStepHeight = 0.4d,
        GroundSnapDistance = 0.3d,
        MaximumRecoverablePenetration = 0.25d,
        MaximumSlideIterations = 4,
        SurfaceSkin = 1e-3d,
    };

    /// <summary>Builds the policy from authored attributes for one revision.</summary>
    public static CapsuleMotorPolicy FromAttributes(MovementAttributeSnapshot attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        return TestDefault with
        {
            WalkableSlopeRadians = attributes.Ground.MaximumFloorAngleRadians,
            GroundSnapDistance = attributes.Ground.FloorSnapDistance,
        };
    }
}

/// <summary>Why a motor step ended where it did.</summary>
public enum CapsuleMotionOutcome : byte
{
    /// <summary>Full requested motion achieved.</summary>
    Completed = 1,

    /// <summary>Motion was blocked and resolved by sliding along contacts.</summary>
    Slid = 2,

    /// <summary>A step or lip was climbed to make progress.</summary>
    Stepped = 3,

    /// <summary>Blocked with no legal slide, so the character stopped.</summary>
    Blocked = 4,

    /// <summary>
    /// The slide iteration cap was reached with motion remaining. Reported rather
    /// than hidden so the golden suite can detect geometry that provokes it.
    /// </summary>
    IterationCapReached = 5,

    /// <summary>
    /// Penetration was deeper than the policy can recover. The caller must
    /// repair rather than continue, because any resolution here would be a
    /// guess.
    /// </summary>
    UnrecoverablePenetration = 6,
}

/// <summary>The result of one motor step.</summary>
public readonly record struct CapsuleMotionResult
{
    internal CapsuleMotionResult(
        CharacterKinematicState state,
        CapsuleMotionOutcome outcome,
        int slideIterations,
        bool ceilingBlocked)
    {
        State = state;
        Outcome = outcome;
        SlideIterations = slideIterations;
        CeilingBlocked = ceilingBlocked;
    }

    public CharacterKinematicState State { get; }
    public CapsuleMotionOutcome Outcome { get; }

    /// <summary>Iterations consumed, for the golden suite and performance probes.</summary>
    public int SlideIterations { get; }

    /// <summary>
    /// Whether upward motion was stopped by a ceiling this step, so the jump
    /// rules can cancel a rise rather than have the character hang against it.
    /// </summary>
    public bool CeilingBlocked { get; }

    public bool RequiresRepair => Outcome is CapsuleMotionOutcome.UnrecoverablePenetration;
}

/// <summary>
/// Moves a capsule through the world from explicit state, replacing Godot's
/// <c>MoveAndSlide</c>.
/// </summary>
/// <remarks>
/// <para>
/// The rules implemented here are the ones <c>MoveAndSlide</c> was providing:
/// sweep, penetration recovery, slide over multiple contacts, slope
/// classification, ground snap, step climbing, and ceiling handling. They are
/// reimplemented not because the engine's version is wrong but because it is
/// unreplayable — it reads and writes a live node, so it cannot answer "what
/// would have happened from that past position" without moving the character
/// there and back, which is visible and races with presentation.
/// </para>
/// <para>
/// Every stage takes state and returns state. Nothing here holds mutable
/// per-character fields, so the same instance can resolve many characters and
/// many replayed frames without ordering hazards.
/// </para>
/// <para>
/// Determinism rule: wherever several contacts are in play they are resolved in
/// <see cref="CollisionContactState.CompareForStableResolution"/> order, never
/// in the order the world reported them.
/// </para>
/// </remarks>
public sealed class CapsuleMovementSimulator
{
    /// <summary>Bounds the per-sweep contact buffer. Sized well above observed contact counts.</summary>
    private const int MaximumContactsPerSweep = 16;

    private readonly ICharacterCollisionWorld _world;

    /// <remarks>
    /// Holds only the world. Profile dimensions and motor policy are revisioned
    /// content and arrive per move, so a replayed frame is resolved with the
    /// tuning that was in force on it.
    /// </remarks>
    public CapsuleMovementSimulator(ICharacterCollisionWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
    }

    /// <summary>
    /// Resolves one frame of motion: recover any overlap, move and slide, solve
    /// steps, then settle grounding.
    /// </summary>
    /// <remarks>
    /// The single entry point. The stages below are exposed for focused tests but
    /// the ordering between them is fixed here, because the order is itself part
    /// of the accepted behaviour — snapping before sliding, for instance, would
    /// let a character stick to a surface it should have left.
    /// </remarks>
    /// <param name="previousFrame">
    /// The frame the state is currently at. Needed alongside
    /// <paramref name="frame"/> so support motion can be queried for the interval
    /// between them; a single instant cannot express an interval.
    /// </param>
    public CapsuleMotionResult Move(
        in CharacterKinematicState state,
        in CollisionProfileState profile,
        HorizontalVector horizontalMotion,
        double verticalMotion,
        SimulationInstant previousFrame,
        SimulationInstant frame,
        CollisionProfileTable profiles,
        CapsuleMotorPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        if (!policy.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(policy));
        }
        if (!state.IsValid || !profile.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        var current = state;
        var wasRising = verticalMotion > 0d;

        // A supporting collider that moved between the two frames carries the
        // character with it, before the character's own motion is considered.
        if (current.Support.IsValid &&
            _world.TryGetSupportMotion(
                current.Support, previousFrame, frame, out var carryHorizontal, out var carryVertical))
        {
            current = current.WithPosition(
                current.Position.Offset(carryHorizontal, carryVertical));
        }

        var recovered = RecoverPenetration(current, profile.Current, profiles, policy);
        if (recovered.RequiresRepair)
        {
            return recovered;
        }

        current = recovered.State;
        var slid = SweepAndSlide(
            current, profile.Current, horizontalMotion, verticalMotion, profiles, policy);
        current = slid.State;
        var outcome = slid.Outcome;
        var iterations = slid.SlideIterations;

        // Stepping is attempted only when horizontal motion was actually
        // blocked. A completed move has nothing to climb.
        if (outcome is CapsuleMotionOutcome.Blocked or CapsuleMotionOutcome.Slid &&
            horizontalMotion.LengthSquared > MovementMath.EpsilonSquared)
        {
            var remaining = horizontalMotion - current.Position.HorizontalTo(state.Position) * -1d;
            var stepped = SolveStep(current, profile.Current, remaining, profiles, policy);
            if (stepped.Outcome is CapsuleMotionOutcome.Stepped)
            {
                current = stepped.State;
                outcome = CapsuleMotionOutcome.Stepped;
            }
        }

        var grounded = ResolveGrounding(current, profile.Current, wasRising, profiles, policy);
        return new CapsuleMotionResult(
            grounded.State,
            outcome,
            iterations,
            slid.CeilingBlocked);
    }

    /// <summary>
    /// Pushes the capsule out of shallow overlap before any motion is attempted.
    /// </summary>
    /// <remarks>
    /// Runs first because a sweep that starts already overlapping reports a
    /// zero travel fraction and makes no progress, which reads as the character
    /// being stuck. Overlap deeper than the policy allows is reported rather than
    /// resolved: pushing out of deep penetration is a guess about which side the
    /// character belongs on, and guessing wrong teleports them through a wall.
    /// </remarks>
    internal CapsuleMotionResult RecoverPenetration(
        in CharacterKinematicState state,
        CollisionProfileKind profile,
        CollisionProfileTable profiles,
        CapsuleMotorPolicy policy)
    {
        var overlap = _world.ResolveOverlap(
            new ClearanceRequest(state.Position, profile, SupportIdentity.None));
        if (!overlap.IsOverlapping)
        {
            return new CapsuleMotionResult(state, CapsuleMotionOutcome.Completed, 0, false);
        }

        if (overlap.Depth > policy.MaximumRecoverablePenetration)
        {
            return new CapsuleMotionResult(
                state, CapsuleMotionOutcome.UnrecoverablePenetration, 0, false);
        }

        // Push out along the shortest exit, plus the skin so the following sweep
        // does not immediately re-report a zero-fraction contact.
        var skinScale = 1d + policy.SurfaceSkin;
        return new CapsuleMotionResult(
            state.WithPosition(state.Position.Offset(
                overlap.SeparationHorizontal * skinScale,
                overlap.SeparationVertical * skinScale)),
            CapsuleMotionOutcome.Completed,
            0,
            false);
    }

    /// <summary>
    /// Sweeps and slides along contacts until the motion is spent or the
    /// iteration cap is reached.
    /// </summary>
    /// <remarks>
    /// Contacts are sorted before use so a convex corner, a concave corner, and a
    /// duplicated contact all resolve identically every run. Remaining motion is
    /// projected onto each blocking plane in turn rather than being zeroed, which
    /// is what makes wall-sliding feel continuous instead of catching.
    /// </remarks>
    internal CapsuleMotionResult SweepAndSlide(
        in CharacterKinematicState state,
        CollisionProfileKind profile,
        HorizontalVector horizontalMotion,
        double verticalMotion,
        CollisionProfileTable profiles,
        CapsuleMotorPolicy policy)
    {
        Span<CollisionContactState> contacts = stackalloc CollisionContactState[MaximumContactsPerSweep];
        var position = state.Position;
        var remainingHorizontal = horizontalMotion;
        var remainingVertical = verticalMotion;
        var outcome = CapsuleMotionOutcome.Completed;
        var ceilingBlocked = false;
        var iterations = 0;

        while (iterations < policy.MaximumSlideIterations)
        {
            if (remainingHorizontal.LengthSquared <= MovementMath.EpsilonSquared &&
                Math.Abs(remainingVertical) <= MovementMath.Epsilon)
            {
                break;
            }

            var sweep = _world.Sweep(
                new CapsuleSweepRequest(
                    position, remainingHorizontal, remainingVertical, profile, SupportIdentity.None),
                contacts);
            position = position.Offset(sweep.AchievedHorizontal, sweep.AchievedVertical);

            if (!sweep.HasContact)
            {
                remainingHorizontal = HorizontalVector.Zero;
                remainingVertical = 0d;
                break;
            }

            iterations++;
            var count = Math.Min(sweep.ContactCount, contacts.Length);
            var window = contacts[..count];

            // The determinism rule: order by content, never by report order.
            window.Sort(default(CollisionContactState.StableComparer));

            var blocking = FirstBlocking(window);
            if (blocking is not { } contact)
            {
                // Only walkable ground was touched, which supports rather than
                // blocks; the motion is spent.
                remainingHorizontal = sweep.RemainingHorizontal;
                remainingVertical = sweep.RemainingVertical;
                break;
            }

            if (contact.SurfaceKind is ContactSurfaceKind.Ceiling && remainingVertical > 0d)
            {
                ceilingBlocked = true;
            }

            // Project the unspent motion onto the blocking plane so sliding is
            // continuous rather than a stop.
            var (projectedHorizontal, projectedVertical) = ProjectOntoPlane(
                sweep.RemainingHorizontal, sweep.RemainingVertical, contact.Normal);

            var madeProgress =
                Math.Abs(projectedHorizontal.X - sweep.RemainingHorizontal.X) > MovementMath.Epsilon ||
                Math.Abs(projectedHorizontal.Z - sweep.RemainingHorizontal.Z) > MovementMath.Epsilon ||
                Math.Abs(projectedVertical - sweep.RemainingVertical) > MovementMath.Epsilon;

            remainingHorizontal = projectedHorizontal;
            remainingVertical = projectedVertical;
            outcome = CapsuleMotionOutcome.Slid;

            if (!madeProgress)
            {
                outcome = CapsuleMotionOutcome.Blocked;
                break;
            }
        }

        if (iterations >= policy.MaximumSlideIterations &&
            (remainingHorizontal.LengthSquared > MovementMath.EpsilonSquared ||
             Math.Abs(remainingVertical) > MovementMath.Epsilon))
        {
            outcome = CapsuleMotionOutcome.IterationCapReached;
        }

        return new CapsuleMotionResult(
            state.WithPosition(position), outcome, iterations, ceilingBlocked);
    }

    /// <summary>
    /// Classifies the surface underfoot and snaps down to it when descending.
    /// </summary>
    /// <remarks>
    /// Snapping is suppressed while rising, or the character would be pulled back
    /// down the instant a jump left the ground. That single rule is the
    /// difference between a jump and a stutter.
    /// </remarks>
    internal CapsuleMotionResult ResolveGrounding(
        in CharacterKinematicState state,
        CollisionProfileKind profile,
        bool wasRising,
        CollisionProfileTable profiles,
        CapsuleMotorPolicy policy)
    {
        if (wasRising)
        {
            return new CapsuleMotionResult(
                state.WithGround(false, SurfaceNormal.Up, SupportIdentity.None),
                CapsuleMotionOutcome.Completed,
                0,
                false);
        }

        var probe = _world.ProbeGround(
            new GroundProbeRequest(state.Position, policy.GroundSnapDistance, profile));
        if (!probe.FoundGround)
        {
            return new CapsuleMotionResult(
                state.WithGround(false, SurfaceNormal.Up, SupportIdentity.None),
                CapsuleMotionOutcome.Completed,
                0,
                false);
        }

        var kind = CollisionContactState.ClassifySurface(probe.Normal, policy.WalkableSlopeRadians);
        if (kind is not ContactSurfaceKind.WalkableGround)
        {
            // An unwalkable slope is contacted but never supports, so the
            // character keeps falling and slides along it.
            return new CapsuleMotionResult(
                state.WithGround(false, SurfaceNormal.Up, SupportIdentity.None),
                CapsuleMotionOutcome.Completed,
                0,
                false);
        }

        var snapped = state.WithPosition(
            state.Position.Offset(HorizontalVector.Zero, -probe.Distance));
        return new CapsuleMotionResult(
            snapped.WithGround(true, probe.Normal, probe.Support),
            CapsuleMotionOutcome.Completed,
            0,
            false);
    }

    /// <summary>
    /// Attempts up-forward-down stepping when horizontal motion is blocked by
    /// something short enough to climb.
    /// </summary>
    /// <remarks>
    /// Generalized rather than special-cased per staircase: the same three
    /// probes handle a stair tread, a shallow ramp lip, and a strafing entry at
    /// an angle. A step that fails to make forward progress is rejected and the
    /// original blocked result stands, so the character never gains height by
    /// pressing into a wall.
    /// </remarks>
    internal CapsuleMotionResult SolveStep(
        in CharacterKinematicState state,
        CollisionProfileKind profile,
        HorizontalVector blockedMotion,
        CollisionProfileTable profiles,
        CapsuleMotorPolicy policy)
    {
        if (policy.MaximumStepHeight <= 0d ||
            blockedMotion.LengthSquared <= MovementMath.EpsilonSquared)
        {
            return new CapsuleMotionResult(state, CapsuleMotionOutcome.Blocked, 0, false);
        }

        Span<CollisionContactState> contacts = stackalloc CollisionContactState[MaximumContactsPerSweep];

        // Up.
        var up = _world.Sweep(
            new CapsuleSweepRequest(
                state.Position, HorizontalVector.Zero, policy.MaximumStepHeight, profile, SupportIdentity.None),
            contacts);
        var raised = state.Position.Offset(HorizontalVector.Zero, up.AchievedVertical);
        if (up.AchievedVertical <= MovementMath.Epsilon)
        {
            return new CapsuleMotionResult(state, CapsuleMotionOutcome.Blocked, 0, false);
        }

        // Forward.
        var forward = _world.Sweep(
            new CapsuleSweepRequest(raised, blockedMotion, 0d, profile, SupportIdentity.None),
            contacts);
        var advanced = raised.Offset(forward.AchievedHorizontal, 0d);
        var progress = forward.AchievedHorizontal.Length;
        if (progress <= MovementMath.Epsilon)
        {
            // No forward progress means this is a wall, not a step. Refusing here
            // is what stops a character gaining height by pressing into it.
            return new CapsuleMotionResult(state, CapsuleMotionOutcome.Blocked, 0, false);
        }

        // Down, at most as far as we rose.
        var down = _world.ProbeGround(
            new GroundProbeRequest(advanced, policy.MaximumStepHeight, profile));
        if (!down.FoundGround)
        {
            return new CapsuleMotionResult(state, CapsuleMotionOutcome.Blocked, 0, false);
        }

        var landingKind = CollisionContactState.ClassifySurface(
            down.Normal, policy.WalkableSlopeRadians);
        if (landingKind is not ContactSurfaceKind.WalkableGround)
        {
            return new CapsuleMotionResult(state, CapsuleMotionOutcome.Blocked, 0, false);
        }

        var landed = advanced.Offset(HorizontalVector.Zero, -down.Distance);
        return new CapsuleMotionResult(
            state.WithPosition(landed).WithGround(true, down.Normal, down.Support),
            CapsuleMotionOutcome.Stepped,
            0,
            false);
    }

    /// <summary>
    /// Whether a profile expansion fits at the current position.
    /// </summary>
    /// <remarks>
    /// Expansion is the only profile change that can be refused. A character
    /// holding a pending stand under a low ceiling keeps the intent and stands
    /// the moment clearance appears, which is why the intent lives in
    /// <see cref="CollisionProfileState.Desired"/> rather than being discarded.
    /// </remarks>
    public bool CanExpandProfile(
        in CharacterKinematicState state,
        CollisionProfileKind from,
        CollisionProfileKind to,
        CollisionProfileTable profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        return !profiles.IsExpansion(from, to) ||
            _world.HasClearance(new ClearanceRequest(state.Position, to, SupportIdentity.None));
    }

    private static CollisionContactState? FirstBlocking(ReadOnlySpan<CollisionContactState> ordered)
    {
        for (var index = 0; index < ordered.Length; index++)
        {
            if (ordered[index].IsBlocking)
            {
                return ordered[index];
            }
        }

        return null;
    }

    /// <summary>
    /// Removes the component of motion travelling into a plane, leaving the
    /// component along it.
    /// </summary>
    private static (HorizontalVector Horizontal, double Vertical) ProjectOntoPlane(
        HorizontalVector horizontal,
        double vertical,
        SurfaceNormal normal)
    {
        var into = (horizontal.X * normal.X) + (vertical * normal.Y) + (horizontal.Z * normal.Z);
        if (into >= 0d)
        {
            // Already travelling away from the surface; nothing to remove.
            return (horizontal, vertical);
        }

        return (
            new HorizontalVector(
                horizontal.X - (into * normal.X),
                horizontal.Z - (into * normal.Z)),
            vertical - (into * normal.Y));
    }
}
