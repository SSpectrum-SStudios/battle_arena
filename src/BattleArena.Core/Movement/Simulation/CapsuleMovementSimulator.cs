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
    /// Separation kept from surfaces so the next frame's sweep does not start
    /// already touching.
    /// </summary>
    /// <remarks>
    /// This must comfortably exceed the collision world's own query margin.
    /// Godot's default margin is one millimetre, and a capsule resting inside
    /// that margin has the floor re-reported as a contact on every sweep —
    /// including sweeps of purely horizontal motion, which the floor does not
    /// obstruct at all. The measured cost of getting this wrong was a walking
    /// character covering six percent less ground in-engine than in simulation,
    /// and a step that could never be climbed.
    /// </remarks>
    public double SurfaceSkin { get; init; }

    /// <summary>
    /// Overlap at or below this is left alone.
    /// </summary>
    /// <remarks>
    /// A resting capsule legitimately reports contact-margin-scale overlap — the
    /// engine's query margin defaults to a millimetre. Recovering that every
    /// frame would push the character up and ground snap would pull it back
    /// down, a per-frame cycle that no unit test sees because the deterministic
    /// world uses exact geometry, and that a player would feel as jitter.
    /// </remarks>
    public double IgnoredPenetrationDepth { get; init; }

    public bool IsValid =>
        double.IsFinite(WalkableSlopeRadians) &&
            WalkableSlopeRadians is > 0d and <= Math.PI / 2d &&
        double.IsFinite(MaximumStepHeight) && MaximumStepHeight >= 0d &&
        double.IsFinite(GroundSnapDistance) && GroundSnapDistance >= 0d &&
        double.IsFinite(MaximumRecoverablePenetration) && MaximumRecoverablePenetration > 0d &&
        double.IsFinite(IgnoredPenetrationDepth) && IgnoredPenetrationDepth >= 0d &&
        IgnoredPenetrationDepth < MaximumRecoverablePenetration &&
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
        IgnoredPenetrationDepth = 0.002d,
        MaximumSlideIterations = 4,
        SurfaceSkin = 5e-3d,
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
        bool ceilingBlocked,
        FrameContactBuffer contacts = default)
    {
        State = state;
        Outcome = outcome;
        SlideIterations = slideIterations;
        CeilingBlocked = ceilingBlocked;
        Contacts = contacts;
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

    /// <summary>
    /// The contacts this step resolved against, in stable order.
    /// </summary>
    /// <remarks>
    /// Collected during sliding and from the grounding probe, so the set
    /// describes what actually shaped the frame rather than everything the world
    /// happened to report.
    /// </remarks>
    public FrameContactBuffer Contacts { get; }

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
        // Includes the iteration cap: a busy corner at the foot of a staircase
        // is exactly where stepping matters, and skipping it there would stop
        // the climb silently.
        if (outcome is CapsuleMotionOutcome.Blocked or CapsuleMotionOutcome.Slid or
                CapsuleMotionOutcome.IterationCapReached &&
            horizontalMotion.LengthSquared > MovementMath.EpsilonSquared)
        {
            // Spent request motion only. Measured from the post-recovery
            // position rather than the original, so a depenetration push or a
            // moving-platform carry is not mistaken for motion the frame already
            // spent.
            var remaining = horizontalMotion - recovered.State.Position.HorizontalTo(current.Position);
            var stepped = SolveStep(current, profile.Current, remaining, profiles, policy);
            if (stepped.Outcome is CapsuleMotionOutcome.Stepped)
            {
                current = stepped.State;
                outcome = CapsuleMotionOutcome.Stepped;
            }
        }

        var grounded = ResolveGrounding(current, profile.Current, wasRising, profiles, policy);

        // The supporting surface joins the contacts sliding resolved against, so
        // the retained set describes everything that shaped the frame.
        var contacts = slid.Contacts;
        if (grounded.State.IsGrounded && grounded.State.Support.IsValid)
        {
            contacts.TryAdd(new FrameContactRecord(
                grounded.State.Support,
                grounded.State.GroundNormal,
                ContactSurfaceKind.WalkableGround));
        }

        return new CapsuleMotionResult(
            grounded.State,
            outcome,
            iterations,
            slid.CeilingBlocked,
            contacts);
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
        if (!overlap.IsOverlapping || overlap.Depth <= policy.IgnoredPenetrationDepth)
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

        // Velocity is projected onto the same planes as the motion. This is the
        // deceleration: without it a character pressed into a wall keeps full
        // speed forever, so turning away feels glued, a jump gets the full
        // horizontal-speed bonus for speed it does not have, and a roll entry
        // gated on speed succeeds from a standstill. The accepted driver got
        // this for free by reading the body velocity back after MoveAndSlide.
        var velocityHorizontal = state.HorizontalVelocity;
        var velocityVertical = state.VerticalVelocity;
        var outcome = CapsuleMotionOutcome.Completed;
        var ceilingBlocked = false;
        var iterations = 0;
        var resolved = default(FrameContactBuffer);

        while (iterations < policy.MaximumSlideIterations)
        {
            if (remainingHorizontal.LengthSquared <= MovementMath.EpsilonSquared &&
                Math.Abs(remainingVertical) <= MovementMath.Epsilon)
            {
                break;
            }

            var sweep = _world.Sweep(
                new CapsuleSweepRequest(
                    position,
                    remainingHorizontal,
                    remainingVertical,
                    profile,
                    SupportIdentity.None,
                    policy.WalkableSlopeRadians),
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

            // The earliest contact the motion is actually driving into, blocking
            // or not. Walkable ground is not blocking, but it still has to remove
            // the motion travelling into it: a grounded character moving forward
            // under gravity hits the floor at travel fraction zero every frame,
            // and treating that as "nothing to slide against" would discard the
            // whole frame's motion — horizontal included — and leave the
            // character unable to walk.
            var opposing = FirstOpposing(window, sweep.RemainingHorizontal, sweep.RemainingVertical);
            if (opposing is not { } contact)
            {
                // Contacts were reported but none oppose the motion, so there is
                // nothing to project against and the motion is spent.
                remainingHorizontal = sweep.RemainingHorizontal;
                remainingVertical = sweep.RemainingVertical;
                break;
            }

            if (contact.SurfaceKind is ContactSurfaceKind.Ceiling && remainingVertical > 0d)
            {
                ceilingBlocked = true;
            }

            resolved.TryAdd(FrameContactRecord.From(contact));

            // Project the unspent motion onto the blocking plane so sliding is
            // continuous rather than a stop, and the velocity onto the same
            // plane so the character actually loses the speed it drove into the
            // surface.
            var (projectedHorizontal, projectedVertical) = ProjectOntoPlane(
                sweep.RemainingHorizontal, sweep.RemainingVertical, contact.Normal);
            (velocityHorizontal, velocityVertical) = ProjectOntoPlane(
                velocityHorizontal, velocityVertical, contact.Normal);

            var madeProgress =
                Math.Abs(projectedHorizontal.X - sweep.RemainingHorizontal.X) > MovementMath.Epsilon ||
                Math.Abs(projectedHorizontal.Z - sweep.RemainingHorizontal.Z) > MovementMath.Epsilon ||
                Math.Abs(projectedVertical - sweep.RemainingVertical) > MovementMath.Epsilon;

            remainingHorizontal = projectedHorizontal;
            remainingVertical = projectedVertical;

            // Sliding along walkable ground is ordinary grounded movement, not a
            // blocked frame; only a genuinely blocking surface changes the
            // reported outcome.
            if (contact.IsBlocking)
            {
                outcome = CapsuleMotionOutcome.Slid;
            }

            if (!madeProgress)
            {
                outcome = contact.IsBlocking
                    ? CapsuleMotionOutcome.Blocked
                    : CapsuleMotionOutcome.Completed;
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
            state.WithPosition(position).WithVelocity(velocityHorizontal, velocityVertical),
            outcome,
            iterations,
            ceilingBlocked,
            resolved);
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

        // Landing removes downward velocity. Without this a character that lands
        // carries its fall speed: harmless while the floor holds the position,
        // but it is a canonical comparison field, and any rule that reads it —
        // a landing roll, a ledge left mid-roll — sees a value that is not true.
        var landedVertical = state.VerticalVelocity < 0d ? 0d : state.VerticalVelocity;

        // Rest the skin distance above the surface rather than exactly on it.
        // A capsule sitting precisely on the floor is inside the engine's query
        // margin, so the next frame's sweep reports the floor as a contact even
        // for purely horizontal motion — which costs an iteration and a slice of
        // travel every frame, and shows up as the character walking measurably
        // slower in-engine than in simulation.
        var snapDistance = Math.Max(0d, probe.Distance - policy.SurfaceSkin);
        var snapped = state
            .WithPosition(state.Position.Offset(HorizontalVector.Zero, -snapDistance))
            .WithVelocity(state.HorizontalVelocity, landedVertical);
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
                state.Position,
                HorizontalVector.Zero,
                policy.MaximumStepHeight,
                profile,
                SupportIdentity.None,
                policy.WalkableSlopeRadians),
            contacts);
        var raised = state.Position.Offset(HorizontalVector.Zero, up.AchievedVertical);
        if (up.AchievedVertical <= MovementMath.Epsilon)
        {
            return new CapsuleMotionResult(state, CapsuleMotionOutcome.Blocked, 0, false);
        }

        // Forward.
        var forward = _world.Sweep(
            new CapsuleSweepRequest(
                raised, blockedMotion, 0d, profile, SupportIdentity.None, policy.WalkableSlopeRadians),
            contacts);
        var advanced = raised.Offset(forward.AchievedHorizontal, 0d);
        var progress = forward.AchievedHorizontal.Length;
        if (progress <= MovementMath.Epsilon)
        {
            // No forward progress means this is a wall, not a step. Refusing here
            // is what stops a character gaining height by pressing into it.
            return new CapsuleMotionResult(state, CapsuleMotionOutcome.Blocked, 0, false);
        }

        // Down, at most as far as we actually rose. Probing the full step height
        // when headroom cut the lift short would let the solver descend further
        // than it climbed.
        var down = _world.ProbeGround(
            new GroundProbeRequest(advanced, up.AchievedVertical, profile));
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

    /// <summary>
    /// The first contact, in stable order, whose plane the remaining motion is
    /// actually travelling into.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Blocking and non-blocking contacts are both candidates. A walkable floor
    /// does not stop a character, but it does remove the downward component of a
    /// frame that is moving forward under gravity — and that case happens every
    /// single grounded frame, so skipping non-blocking contacts here would leave
    /// nothing to project against and discard the whole frame's motion.
    /// </para>
    /// <para>
    /// The opposition test matters because a query can report contacts at the
    /// resting pose whose normals the motion is travelling away from. Projecting
    /// against one of those would make no progress and abandon the rest of the
    /// frame.
    /// </para>
    /// </remarks>
    private static CollisionContactState? FirstOpposing(
        ReadOnlySpan<CollisionContactState> ordered,
        HorizontalVector remainingHorizontal,
        double remainingVertical)
    {
        for (var index = 0; index < ordered.Length; index++)
        {
            ref readonly var candidate = ref ordered[index];
            var into = (remainingHorizontal.X * candidate.Normal.X) +
                (remainingVertical * candidate.Normal.Y) +
                (remainingHorizontal.Z * candidate.Normal.Z);
            if (into < 0d)
            {
                return candidate;
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
