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

    public bool IsValid => throw new NotImplementedException();

    /// <summary>
    /// Test-only baseline. Production construction derives the policy from the
    /// authored attributes for the frame's revision; a static default here would
    /// reintroduce the unrevisioned-content problem this type documents against.
    /// </summary>
    internal static CapsuleMotorPolicy TestDefault => throw new NotImplementedException();

    /// <summary>Builds the policy from authored attributes for one revision.</summary>
    public static CapsuleMotorPolicy FromAttributes(MovementAttributeSnapshot attributes) =>
        throw new NotImplementedException();
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
        bool ceilingBlocked) => throw new NotImplementedException();

    public CharacterKinematicState State { get; }
    public CapsuleMotionOutcome Outcome { get; }

    /// <summary>Iterations consumed, for the golden suite and performance probes.</summary>
    public int SlideIterations { get; }

    /// <summary>
    /// Whether upward motion was stopped by a ceiling this step, so the jump
    /// rules can cancel a rise rather than have the character hang against it.
    /// </summary>
    public bool CeilingBlocked { get; }
    public bool RequiresRepair => throw new NotImplementedException();
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
    /// <remarks>
    /// Holds only the world. Profile dimensions and motor policy are revisioned
    /// content and arrive per move, so a replayed frame is resolved with the
    /// tuning that was in force on it.
    /// </remarks>
    public CapsuleMovementSimulator(ICharacterCollisionWorld world) =>
        throw new NotImplementedException();

    /// <summary>
    /// Resolves one frame of motion: recover any overlap, move and slide, solve
    /// steps, apply ceiling limits, then settle grounding.
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
        CapsuleMotorPolicy policy) => throw new NotImplementedException();

    /// <summary>
    /// Stops upward motion against a ceiling and reports it, so the jump rules
    /// can cancel a rise rather than leave the character pinned against it.
    /// </summary>
    /// <remarks>
    /// Exposed as its own stage so P05-11 has a seam to test directly, rather
    /// than only observing the flag on the result.
    /// </remarks>
    internal CapsuleMotionResult ApplyCeilingLimit(
        in CharacterKinematicState state,
        CollisionProfileKind profile,
        double verticalMotion,
        CollisionProfileTable profiles,
        CapsuleMotorPolicy policy) => throw new NotImplementedException();

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
        CollisionProfileKind profile) => throw new NotImplementedException();

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
        double verticalMotion) => throw new NotImplementedException();

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
        bool wasRising) => throw new NotImplementedException();

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
        HorizontalVector blockedMotion) => throw new NotImplementedException();

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
        CollisionProfileTable profiles) => throw new NotImplementedException();
}
