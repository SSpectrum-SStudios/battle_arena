namespace BattleArena.Core.Movement.Simulation;

/// <summary>
/// The authored collision shapes a character can occupy. Identity rather than
/// dimensions, so the wire carries one small enum instead of repeated capsule
/// measurements and both endpoints resolve the same authored values.
/// </summary>
public enum CollisionProfileKind : byte
{
    Standing = 1,
    Crouching = 2,
    Rolling = 3,
}

/// <summary>
/// One authored capsule, validated once.
/// </summary>
/// <remarks>
/// Height is the full capsule height including both hemispheres, matching how
/// the shapes are authored in the scene. Keeping that convention explicit
/// matters because the query adapter creates its shape RIDs from these numbers,
/// and a half-height mismatch would silently sink the character into the floor.
/// </remarks>
public readonly record struct CollisionProfileDimensions
{
    public CollisionProfileDimensions(double radius, double height) =>
        throw new NotImplementedException();

    public double Radius { get; }
    public double Height { get; }

    /// <summary>
    /// Half the full height. Note the capsule origin is the FOOT, so the distance
    /// from the origin to the top is <see cref="Height"/>, not this.
    /// </summary>
    public double HalfHeight => throw new NotImplementedException();
    public bool IsValid => throw new NotImplementedException();
}

/// <summary>
/// Which profile a character currently occupies, and which it wants to occupy.
/// </summary>
/// <remarks>
/// <para>
/// Current and desired are separate because expansion is not always permitted:
/// a crouching character under a low ceiling wants to stand and cannot. Holding
/// the intent rather than discarding it is what lets the character stand
/// automatically the moment clearance appears, instead of requiring the player
/// to press again.
/// </para>
/// <para>
/// Shrinking is always legal; only expansion is gated on clearance. That
/// asymmetry is deliberate and is why the two directions are named separately
/// rather than sharing one "change profile" operation.
/// </para>
/// </remarks>
public readonly record struct CollisionProfileState
{
    public CollisionProfileState(
        CollisionProfileKind current,
        CollisionProfileKind desired) => throw new NotImplementedException();

    public CollisionProfileKind Current { get; }

    /// <summary>
    /// What the character would occupy if clearance allowed. Equal to
    /// <see cref="Current"/> whenever no expansion is pending.
    /// </summary>
    public CollisionProfileKind Desired { get; }

    /// <summary>
    /// True when the character is held in a smaller profile than it wants. Takes
    /// the table for the same reason as <see cref="WithDesired"/>: enum order is
    /// not size order.
    /// </summary>
    public bool HasPendingExpansion(CollisionProfileTable profiles) =>
        throw new NotImplementedException();
    public bool IsValid => throw new NotImplementedException();

    public static CollisionProfileState Standing => throw new NotImplementedException();

    /// <summary>
    /// Requests a profile. Shrinking applies immediately; expanding records the
    /// intent and leaves <see cref="Current"/> alone for the clearance stage to
    /// resolve.
    /// </summary>
    /// <remarks>
    /// Needs the table because whether a change shrinks or grows the capsule is a
    /// question about dimensions, and the profile kinds are not ordered by size —
    /// Standing is the smallest enum value and the largest capsule.
    /// </remarks>
    public CollisionProfileState WithDesired(
        CollisionProfileKind desired,
        CollisionProfileTable profiles) => throw new NotImplementedException();

    /// <summary>
    /// Applies a pending expansion the clearance stage has approved.
    /// </summary>
    public CollisionProfileState ExpandToDesired() => throw new NotImplementedException();
}

/// <summary>
/// The authored dimension table, resolved by profile identity.
/// </summary>
/// <remarks>
/// Immutable and validated on construction so the motor can look up dimensions
/// on the hot path without re-checking, and so a content error is a startup
/// failure rather than a mid-match one.
/// </remarks>
/// <remarks>
/// Owned by the movement configuration revision rather than constructed once at
/// startup. Capsule dimensions are already authored on
/// <c>CrouchRollAttributes</c>, and a replayed frame must use the dimensions in
/// force on that frame — a table injected once would silently apply current
/// dimensions to historic frames, changing what the character collided with.
/// </remarks>
public sealed class CollisionProfileTable
{
    /// <summary>
    /// Builds the table from the authored attributes for one revision, so the
    /// dimensions and the rules that use them can never disagree.
    /// </summary>
    public static CollisionProfileTable FromAttributes(MovementAttributeSnapshot attributes) =>
        throw new NotImplementedException();

    public CollisionProfileTable(
        CollisionProfileDimensions standing,
        CollisionProfileDimensions crouching,
        CollisionProfileDimensions rolling) => throw new NotImplementedException();

    public CollisionProfileDimensions Standing => throw new NotImplementedException();
    public CollisionProfileDimensions Crouching => throw new NotImplementedException();
    public CollisionProfileDimensions Rolling => throw new NotImplementedException();

    public CollisionProfileDimensions For(CollisionProfileKind kind) =>
        throw new NotImplementedException();

    /// <summary>
    /// Whether moving between two profiles grows the capsule, and therefore
    /// needs a clearance check before it may be applied.
    /// </summary>
    public bool IsExpansion(CollisionProfileKind from, CollisionProfileKind to) =>
        throw new NotImplementedException();
}
