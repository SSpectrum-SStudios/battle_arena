using BattleArena.Core.Common;

namespace BattleArena.Core.Movement.Simulation;

/// <summary>
/// How a contact relates to the character's up axis, decided once from the
/// contact normal and the authored walkable threshold.
/// </summary>
/// <remarks>
/// Classified at the point of contact rather than re-derived at each use, so
/// every consumer of a contact agrees about what it is. Re-deriving is how a
/// surface ends up walkable to the slide resolver and unwalkable to the step
/// solver on the same frame.
/// </remarks>
public enum ContactSurfaceKind : byte
{
    /// <summary>Slope at or under the walkable threshold: supports and permits ground motion.</summary>
    WalkableGround = 1,

    /// <summary>Steeper than walkable: blocks and slides, never supports.</summary>
    UnwalkableSlope = 2,

    /// <summary>Effectively vertical: a wall to slide along.</summary>
    Wall = 3,

    /// <summary>Faces downward: stops upward motion and blocks profile expansion.</summary>
    Ceiling = 4,
}

/// <summary>
/// One contact produced by a capsule sweep, carrying everything later stages
/// need and nothing they do not.
/// </summary>
/// <remarks>
/// <para>
/// The ordering key is the reason this type exists. Slide resolution over
/// several contacts must be deterministic, and engine query results are not
/// ordered in any guaranteed way — the same geometry can return the same
/// contacts in a different order on a different run or a different machine.
/// Sorting by travel fraction, then collider, then shape, then normal gives a
/// total order derived only from the contact's own content, so replay resolves
/// the identical sequence.
/// </para>
/// <para>
/// A tolerance-aware equality is deliberately <em>not</em> provided: two contacts
/// that differ only within tolerance must still order stably, and a fuzzy
/// comparison would make the sort itself non-deterministic.
/// </para>
/// </remarks>
public readonly record struct CollisionContactState
{
    public CollisionContactState(
        WorldPosition point,
        SurfaceNormal normal,
        double travelFraction,
        double penetrationDepth,
        SupportIdentity collider,
        ContactSurfaceKind surfaceKind) => throw new NotImplementedException();

    public WorldPosition Point { get; }
    public SurfaceNormal Normal { get; }

    /// <summary>
    /// How far along the attempted motion the contact occurred, in [0, 1]. Zero
    /// means the sweep was blocked before moving, which is the signal
    /// penetration recovery keys on.
    /// </summary>
    public double TravelFraction { get; }

    /// <summary>
    /// Overlap depth at the contact, zero when the sweep merely touched. Shallow
    /// overlap is recoverable; beyond the authored bound it is a fail-closed
    /// condition rather than something to push through.
    /// </summary>
    public double PenetrationDepth { get; }

    public SupportIdentity Collider { get; }
    public ContactSurfaceKind SurfaceKind { get; }
    public bool IsValid => throw new NotImplementedException();

    /// <summary>
    /// Total order used to make multi-contact resolution reproducible. Compares
    /// travel fraction, then collider id, then shape index, then normal
    /// components, so the result depends only on contact content and never on
    /// the order the engine happened to report them in.
    /// </summary>
    public static int CompareForStableResolution(
        in CollisionContactState left,
        in CollisionContactState right) => throw new NotImplementedException();

    /// <summary>
    /// Classifies a normal against the authored walkable slope threshold. The
    /// single place this decision is made.
    /// </summary>
    public static ContactSurfaceKind ClassifySurface(
        SurfaceNormal normal,
        double walkableSlopeRadians) => throw new NotImplementedException();
}
