using BattleArena.Core.Common;

namespace BattleArena.Core.Movement.Simulation;

/// <summary>
/// A capsule sweep from an explicit transform along an explicit motion.
/// </summary>
/// <remarks>
/// Every query in this file carries its own start transform rather than reading
/// one from a live node. That is the entire point of the explicit motor: replay
/// asks "what would this capsule hit starting from <em>there</em>", where
/// "there" is a past position no node currently occupies. The P01-09 probe
/// proved the engine can answer that with precreated shape RIDs, without moving
/// anything.
/// </remarks>
public readonly record struct CapsuleSweepRequest
{
    public CapsuleSweepRequest(
        WorldPosition origin,
        HorizontalVector horizontalMotion,
        double verticalMotion,
        CollisionProfileKind profile,
        SupportIdentity excludedCollider,
        double walkableSlopeRadians)
    {
        if (!origin.IsFinite || !horizontalMotion.IsFinite || !double.IsFinite(verticalMotion))
        {
            throw new ArgumentOutOfRangeException(nameof(origin));
        }
        if (!Enum.IsDefined(profile))
        {
            throw new ArgumentOutOfRangeException(nameof(profile));
        }

        Origin = origin;
        HorizontalMotion = horizontalMotion;
        VerticalMotion = verticalMotion;
        Profile = profile;
        ExcludedCollider = excludedCollider;
        WalkableSlopeRadians = walkableSlopeRadians;
    }

    public WorldPosition Origin { get; }
    public HorizontalVector HorizontalMotion { get; }
    public double VerticalMotion { get; }
    public CollisionProfileKind Profile { get; }

    /// <summary>
    /// Optional collider to ignore, for the character's own body and for the
    /// step solver's deliberate re-query against a surface it just left.
    /// </summary>
    public SupportIdentity ExcludedCollider { get; }

    /// <summary>
    /// The threshold contacts are classified against, carried on the request.
    /// </summary>
    /// <remarks>
    /// Revisioned content, so it travels with the frame rather than living on the
    /// world adapter. Holding it on the adapter would mean a mid-match
    /// configuration change updated the motor while the adapter kept classifying
    /// against the old threshold — and it would be state read during a replayed
    /// frame that is neither in the rewind unit nor derived from its inputs.
    /// </remarks>
    public double WalkableSlopeRadians { get; }

    public bool IsValid =>
        Origin.IsFinite &&
        HorizontalMotion.IsFinite &&
        double.IsFinite(VerticalMotion) &&
        Enum.IsDefined(Profile);
}

/// <summary>
/// A downward probe for support beneath an explicit position.
/// </summary>
public readonly record struct GroundProbeRequest
{
    public GroundProbeRequest(
        WorldPosition origin,
        double maximumDistance,
        CollisionProfileKind profile)
    {
        if (!origin.IsFinite)
        {
            throw new ArgumentOutOfRangeException(nameof(origin));
        }
        if (!double.IsFinite(maximumDistance) || maximumDistance < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDistance));
        }
        if (!Enum.IsDefined(profile))
        {
            throw new ArgumentOutOfRangeException(nameof(profile));
        }

        Origin = origin;
        MaximumDistance = maximumDistance;
        Profile = profile;
    }

    public WorldPosition Origin { get; }

    /// <summary>
    /// How far down to look. Bounds ground snap: beyond this the character has
    /// genuinely left the surface rather than walked down a step.
    /// </summary>
    public double MaximumDistance { get; }
    public CollisionProfileKind Profile { get; }

    public bool IsValid =>
        Origin.IsFinite &&
        double.IsFinite(MaximumDistance) && MaximumDistance >= 0d &&
        Enum.IsDefined(Profile);
}

/// <summary>
/// A static overlap test asking whether a profile fits at a position.
/// </summary>
/// <remarks>
/// Separate from a sweep because it asks a different question — "does this fit
/// here" rather than "what does this hit on the way" — and because profile
/// expansion needs an answer with no motion at all.
/// </remarks>
public readonly record struct ClearanceRequest
{
    public ClearanceRequest(
        WorldPosition origin,
        CollisionProfileKind profile,
        SupportIdentity excludedCollider)
    {
        if (!origin.IsFinite)
        {
            throw new ArgumentOutOfRangeException(nameof(origin));
        }
        if (!Enum.IsDefined(profile))
        {
            throw new ArgumentOutOfRangeException(nameof(profile));
        }

        Origin = origin;
        Profile = profile;
        ExcludedCollider = excludedCollider;
    }

    public WorldPosition Origin { get; }
    public CollisionProfileKind Profile { get; }
    public SupportIdentity ExcludedCollider { get; }
    public bool IsValid => Origin.IsFinite && Enum.IsDefined(Profile);
}

/// <summary>
/// The result of a sweep: how far it got and what stopped it.
/// </summary>
/// <remarks>
/// Contacts come back in the world's own order and are <em>not</em> sorted here.
/// Sorting is the motor's job via
/// <see cref="CollisionContactState.CompareForStableResolution"/>, so the
/// determinism rule lives in one place rather than being re-implemented by every
/// world adapter.
/// </remarks>
public readonly record struct CapsuleSweepResult
{
    public CapsuleSweepResult(
        HorizontalVector achievedHorizontal,
        double achievedVertical,
        HorizontalVector remainingHorizontal,
        double remainingVertical,
        int contactCount)
    {
        if (!achievedHorizontal.IsFinite || !double.IsFinite(achievedVertical) ||
            !remainingHorizontal.IsFinite || !double.IsFinite(remainingVertical))
        {
            throw new ArgumentOutOfRangeException(nameof(achievedHorizontal));
        }
        if (contactCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(contactCount));
        }

        AchievedHorizontal = achievedHorizontal;
        AchievedVertical = achievedVertical;
        RemainingHorizontal = remainingHorizontal;
        RemainingVertical = remainingVertical;
        ContactCount = contactCount;
    }

    /// <summary>
    /// Motion actually achieved, as a vector rather than a fraction of the
    /// request.
    /// </summary>
    /// <remarks>
    /// The engine's achieved travel is not always parallel to the requested
    /// motion — depenetration is folded into it — so scaling the request by a
    /// scalar fraction does not reproduce the position it actually reached. A
    /// fraction is still derived for contact ordering, but the vector is what
    /// the motor commits.
    /// </remarks>
    public HorizontalVector AchievedHorizontal { get; }
    public double AchievedVertical { get; }

    /// <summary>Motion left unspent, which the slide stage projects onto the blocking plane.</summary>
    public HorizontalVector RemainingHorizontal { get; }
    public double RemainingVertical { get; }

    public int ContactCount { get; }
    public bool HasContact => ContactCount > 0;

    public static CapsuleSweepResult Clear =>
        new(HorizontalVector.Zero, 0d, HorizontalVector.Zero, 0d, 0);
}

/// <summary>
/// How far and in which direction a capsule must move to leave an overlap.
/// </summary>
/// <remarks>
/// Separate from <see cref="CapsuleSweepResult"/> because it answers a question
/// no sweep can: a sweep needs a motion to travel along, and its fraction is
/// undefined for zero motion, which is exactly the case penetration recovery
/// faces. Without an explicit separation query the recovery stage has nothing to
/// act on.
/// </remarks>
public readonly record struct OverlapResolution
{
    public OverlapResolution(
        bool isOverlapping,
        HorizontalVector separationHorizontal,
        double separationVertical,
        double depth)
    {
        if (!separationHorizontal.IsFinite || !double.IsFinite(separationVertical))
        {
            throw new ArgumentOutOfRangeException(nameof(separationHorizontal));
        }
        if (!double.IsFinite(depth) || depth < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(depth));
        }

        IsOverlapping = isOverlapping;
        SeparationHorizontal = separationHorizontal;
        SeparationVertical = separationVertical;
        Depth = depth;
    }

    public bool IsOverlapping { get; }

    /// <summary>Direction and distance out of the overlap, shortest-exit.</summary>
    public HorizontalVector SeparationHorizontal { get; }
    public double SeparationVertical { get; }

    /// <summary>
    /// Overlap depth, compared against the policy bound. Beyond it, recovery
    /// fails closed rather than guessing which side of the geometry the
    /// character belongs on.
    /// </summary>
    public double Depth { get; }

    public static OverlapResolution None =>
        new(false, HorizontalVector.Zero, 0d, 0d);
}

/// <summary>The result of a ground probe.</summary>
public readonly record struct GroundProbeResult
{
    public GroundProbeResult(
        bool foundGround,
        double distance,
        SurfaceNormal normal,
        SupportIdentity support)
    {
        if (!double.IsFinite(distance) || distance < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(distance));
        }

        FoundGround = foundGround;
        Distance = distance;
        Normal = foundGround && normal.IsValid ? normal : SurfaceNormal.Up;
        Support = foundGround ? support : SupportIdentity.None;
    }

    public bool FoundGround { get; }

    /// <summary>Distance travelled down before contact. Zero when already resting on it.</summary>
    public double Distance { get; }
    public SurfaceNormal Normal { get; }
    public SupportIdentity Support { get; }

    public static GroundProbeResult None =>
        new(false, 0d, SurfaceNormal.Up, SupportIdentity.None);
}

/// <summary>
/// The world a character collides against, expressed without any engine type.
/// </summary>
/// <remarks>
/// <para>
/// Two implementations exist by design: the Godot adapter, and a deterministic
/// fake used by tests. The fake is what makes the motor's rules — slide order,
/// slope classification, step solving, clearance — testable as pure logic with
/// exact expected geometry, instead of only through a headless engine run where
/// a failure could be the rule or could be the engine.
/// </para>
/// <para>
/// Every method is a pure query. Nothing here moves a node, mutates world state,
/// or touches presentation, which is what allows the same query to be issued
/// many times while resolving one frame and again during replay.
/// </para>
/// </remarks>
public interface ICharacterCollisionWorld
{
    /// <summary>
    /// Sweeps a capsule and reports how far it travelled and what it hit.
    /// </summary>
    /// <param name="contacts">
    /// Caller-owned buffer filled with up to its length contacts, so the query
    /// allocates nothing per frame.
    /// </param>
    CapsuleSweepResult Sweep(
        in CapsuleSweepRequest request,
        Span<CollisionContactState> contacts);

    GroundProbeResult ProbeGround(in GroundProbeRequest request);

    /// <summary>
    /// Reports whether a profile overlaps geometry at a position and, if so, the
    /// shortest way out.
    /// </summary>
    /// <remarks>
    /// Required by penetration recovery, which runs before any motion and
    /// therefore cannot use a sweep: a sweep needs a direction to travel and
    /// reports an undefined fraction for zero-length motion. This is the only
    /// query that can answer "which way, and how far".
    /// </remarks>
    OverlapResolution ResolveOverlap(in ClearanceRequest request);

    /// <summary>Whether the profile fits at the requested position without overlap.</summary>
    bool HasClearance(in ClearanceRequest request);

    /// <summary>
    /// Displacement a supporting collider applied between two frames, for moving
    /// platforms.
    /// </summary>
    /// <remarks>
    /// Queried rather than accumulated so a replayed frame gets the platform's
    /// motion for <em>that</em> frame. Returns zero for static geometry, which is
    /// every surface in the arena today; the contract exists so adding moving
    /// platforms later does not require reopening the motor.
    /// </remarks>
    bool TryGetSupportMotion(
        SupportIdentity support,
        SimulationInstant fromFrame,
        SimulationInstant toFrame,
        out HorizontalVector horizontal,
        out double vertical);
}
