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
    /// <summary>
    /// A normal within this of vertical is treated as flat ground regardless of
    /// the walkable threshold, and within this of horizontal as a wall. Bounds
    /// classification chatter on surfaces that are nominally flat but carry a
    /// few microradians of authoring or floating-point noise.
    /// </summary>
    public const double NormalTolerance = 1e-4d;

    public CollisionContactState(
        WorldPosition point,
        SurfaceNormal normal,
        double travelFraction,
        double penetrationDepth,
        SupportIdentity collider,
        ContactSurfaceKind surfaceKind)
    {
        if (!point.IsFinite || !normal.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(point));
        }
        if (!double.IsFinite(travelFraction) || travelFraction is < 0d or > 1d)
        {
            throw new ArgumentOutOfRangeException(nameof(travelFraction));
        }
        if (!double.IsFinite(penetrationDepth) || penetrationDepth < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(penetrationDepth));
        }
        if (!Enum.IsDefined(surfaceKind))
        {
            throw new ArgumentOutOfRangeException(nameof(surfaceKind));
        }

        Point = point;
        Normal = normal;
        TravelFraction = travelFraction;
        PenetrationDepth = penetrationDepth;
        Collider = collider;
        SurfaceKind = surfaceKind;
    }

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

    public bool IsValid =>
        Point.IsFinite &&
        Normal.IsValid &&
        double.IsFinite(TravelFraction) && TravelFraction is >= 0d and <= 1d &&
        double.IsFinite(PenetrationDepth) && PenetrationDepth >= 0d &&
        Enum.IsDefined(SurfaceKind);

    /// <summary>Whether this contact blocks motion rather than merely supporting it.</summary>
    public bool IsBlocking => SurfaceKind is not ContactSurfaceKind.WalkableGround;

    /// <summary>
    /// Total order used to make multi-contact resolution reproducible. Compares
    /// travel fraction, then collider id, then shape index, then normal
    /// components, so the result depends only on contact content and never on
    /// the order the engine happened to report them in.
    /// </summary>
    public static int CompareForStableResolution(
        in CollisionContactState left,
        in CollisionContactState right)
    {
        // NaN would break the total order and make the sort itself
        // non-deterministic, which is the one thing this method exists to
        // prevent. Order NaN last and consistently rather than letting it
        // poison comparisons.
        var order = CompareDouble(left.TravelFraction, right.TravelFraction);
        if (order != 0)
        {
            return order;
        }

        order = left.Collider.ColliderId.CompareTo(right.Collider.ColliderId);
        if (order != 0)
        {
            return order;
        }

        order = left.Collider.ShapeIndex.CompareTo(right.Collider.ShapeIndex);
        if (order != 0)
        {
            return order;
        }

        order = CompareDouble(left.Normal.X, right.Normal.X);
        if (order != 0)
        {
            return order;
        }

        order = CompareDouble(left.Normal.Y, right.Normal.Y);
        return order != 0 ? order : CompareDouble(left.Normal.Z, right.Normal.Z);
    }

    /// <summary>
    /// Classifies a normal against the authored walkable slope threshold. The
    /// single place this decision is made.
    /// </summary>
    /// <remarks>
    /// Compares the normal's vertical component against the cosine of the
    /// threshold rather than taking an arc-cosine and comparing angles. The two
    /// are equivalent, but the cosine form avoids a transcendental exactly at the
    /// boundary where classification flips, which is where precision matters
    /// most.
    /// </remarks>
    public static ContactSurfaceKind ClassifySurface(
        SurfaceNormal normal,
        double walkableSlopeRadians)
    {
        if (!normal.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(normal));
        }
        if (!double.IsFinite(walkableSlopeRadians) ||
            walkableSlopeRadians is < 0d or > Math.PI / 2d)
        {
            throw new ArgumentOutOfRangeException(nameof(walkableSlopeRadians));
        }

        if (normal.Y <= NormalTolerance && normal.Y >= -NormalTolerance)
        {
            return ContactSurfaceKind.Wall;
        }
        if (normal.Y < 0d)
        {
            return ContactSurfaceKind.Ceiling;
        }

        var walkableCosine = Math.Cos(walkableSlopeRadians);
        return normal.Y >= walkableCosine - NormalTolerance
            ? ContactSurfaceKind.WalkableGround
            : ContactSurfaceKind.UnwalkableSlope;
    }

    private static int CompareDouble(double left, double right)
    {
        var leftNaN = double.IsNaN(left);
        var rightNaN = double.IsNaN(right);
        if (leftNaN || rightNaN)
        {
            return leftNaN && rightNaN ? 0 : leftNaN ? 1 : -1;
        }

        return left.CompareTo(right);
    }

    /// <summary>
    /// Cached comparer for sorting a contact span.
    /// </summary>
    /// <remarks>
    /// A struct comparer rather than a <see cref="Comparison{T}"/> delegate: the
    /// comparison takes its operands by <c>in</c>, which no delegate signature
    /// matches, and a delegate would allocate on a path that runs several times
    /// per frame per character.
    /// </remarks>
    public readonly struct StableComparer : IComparer<CollisionContactState>
    {
        public int Compare(CollisionContactState x, CollisionContactState y) =>
            CompareForStableResolution(in x, in y);
    }
}
