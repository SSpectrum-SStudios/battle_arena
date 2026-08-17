using BattleArena.Core.Common;

namespace BattleArena.Core.Movement.Simulation;

/// <summary>
/// A collider's identity, derived from the scene rather than from the engine.
/// </summary>
/// <remarks>
/// <para>
/// This exists because <see cref="SupportIdentity"/> as built today is a Godot
/// <c>RID.Id</c> — a physics-server allocation handle. P01-09 and P5B-01 both
/// verified it "stable", but both only ever re-queried the same collider inside a
/// single process, which is all a RID guarantees. Authority and owner are two
/// processes, and that is the entire premise of P06-11 and P06-12.
/// </para>
/// <para>
/// The consequence if this were not fixed: the comparer checks discrete facts
/// before numeric ones, and <c>Kinematic.Support</c> is one of them, so
/// <em>every grounded frame</em> would report a discrete mismatch and the owner
/// would be corrected continuously. That is the same correction-storm failure the
/// one-frame phase constraint exists to prevent, arriving through a different
/// door, and no tolerance can be widened to hide it.
/// </para>
/// <para>
/// Derived from a stable authored path plus a shape index, hashed to a fixed
/// width. Two processes loading the same scene produce byte-identical values
/// because the inputs are authored content, not runtime allocation order.
/// </para>
/// </remarks>
public readonly record struct SceneColliderIdentity
{
    /// <summary>
    /// Bumped when the derivation changes, so two endpoints on different
    /// derivations refuse to compare rather than comparing and disagreeing.
    /// </summary>
    /// <remarks>
    /// The same reasoning as <c>CanonicalMovementStateHash.SchemaVersion</c>: a
    /// silent derivation change would present as a permanent divergence on every
    /// contact, which is the most expensive kind of bug to attribute.
    /// </remarks>
    public const uint DerivationVersion = 1;

    /// <summary>
    /// Builds an identity from the owning body's path and the shape's ordinal
    /// within that body.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The body's path, never the shape node's.</b> The stub review caught this
    /// and it would have reproduced P01-09's and P5B-01's mistake through a new
    /// mechanism. `MovementTestCourse` creates every collider in code and adds
    /// shapes as <c>body.AddChild(new CollisionShape3D { Shape = ... })</c> without
    /// setting <c>Name</c>, so Godot assigns <c>@CollisionShape3D@&lt;counter&gt;</c>
    /// — a global instantiation counter, which is exactly the process-local
    /// ordering this type exists to escape. The bodies <em>do</em> get stable names,
    /// so the body path plus an ordinal is the stable pair.
    /// </para>
    /// <para>
    /// A scene walker's natural implementation — <c>shapeNode.GetPath()</c> — would
    /// pass every single-process identity test and fail across processes, which is
    /// precisely how this class of defect survived four phases.
    /// </para>
    /// <para>
    /// The ordinal is carried in <see cref="ShapeIndex"/> and is <em>not</em> folded
    /// into <see cref="Value"/>. Folding it would leave <c>ShapeIndex</c> dead and
    /// silently change <c>ExcludedCollider</c> from excluding a body to excluding
    /// one shape of a body, which <c>DeterministicCollisionWorld.Body.Matches</c>
    /// compares on collider alone.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The body path is null, empty, or whitespace, or the ordinal is negative.
    /// </exception>
    public static SceneColliderIdentity FromBodyPath(string bodyScenePath, int shapeOrdinal) =>
        throw new NotImplementedException();

    /// <summary>The stable hash of the owning body's authored path.</summary>
    public ulong Value { get; }

    /// <summary>The shape's ordinal within its body. Kept separate from <see cref="Value"/>.</summary>
    public int ShapeIndex { get; }

    public bool IsValid => Value != 0;
    public static SceneColliderIdentity None => default;

    /// <summary>
    /// Projects into the motor's existing <see cref="SupportIdentity"/>.
    /// </summary>
    /// <remarks>
    /// A projection rather than a replacement so the motor, the rewind unit, and
    /// every existing test keep one identity type. What changes is where the value
    /// comes from, not the shape of the state — which is what keeps this out of
    /// the protocol change that reopening the rewind unit would require.
    /// </remarks>
    public SupportIdentity ToSupportIdentity() => throw new NotImplementedException();
}

/// <summary>
/// One convex piece of authored static collision geometry.
/// </summary>
/// <remarks>
/// <para>
/// Convex pieces only, and deliberately so. The arena is authored from boxes,
/// wedges and cylinders; supporting arbitrary concave meshes would mean a
/// general-purpose collision library, which is not what this is for. A concave
/// authored shape is decomposed by the builder before it reaches here.
/// </para>
/// <para>
/// Stored as a support function plus a bounding box rather than as a shape union.
/// The sweep needs exactly two things from geometry — "what is the furthest point
/// in this direction" for the narrow phase, and bounds for the broad phase — and
/// expressing it that way lets a box, a wedge and a cylinder share one sweep
/// implementation instead of needing a pairwise matrix.
/// </para>
/// </remarks>
public readonly record struct StaticColliderShape
{
    /// <summary>What kind of convex primitive this is.</summary>
    public StaticColliderKind Kind { get; init; }

    /// <summary>Identity carried on the shape, so a contact can name its collider.</summary>
    public SceneColliderIdentity Collider { get; init; }

    /// <summary>Axis-aligned bounds in world space, for the broad phase.</summary>
    public WorldPosition BoundsMinimum { get; init; }

    /// <summary>Axis-aligned bounds in world space, for the broad phase.</summary>
    public WorldPosition BoundsMaximum { get; init; }

    /// <summary>Centre in world space.</summary>
    public WorldPosition Centre { get; init; }

    /// <summary>Half-extents for a box, or radius and half-height for a cylinder.</summary>
    public WorldPosition HalfExtents { get; init; }

    /// <summary>
    /// Rotation about the vertical axis only, in radians.
    /// </summary>
    /// <remarks>
    /// Yaw only, because arena geometry is authored either axis-aligned or rotated
    /// about the vertical. Wedges carry their slope in
    /// <see cref="SlopeRadians"/> instead of a general basis, which keeps the
    /// sweep's support function closed-form — and a closed-form support function
    /// is where the per-query cost target of a couple of microseconds comes from.
    /// </remarks>
    public double YawRadians { get; init; }

    /// <summary>Slope for a wedge, measured from horizontal. Zero for other kinds.</summary>
    public double SlopeRadians { get; init; }

    public bool IsValid => throw new NotImplementedException();

    /// <summary>
    /// Furthest point on this shape along a direction, in world space.
    /// </summary>
    /// <remarks>
    /// The one geometric primitive the sweep needs. Every kind implements it
    /// closed-form; none allocates, and none branches on more than its own kind.
    /// </remarks>
    public WorldPosition SupportPoint(double directionX, double directionY, double directionZ) =>
        throw new NotImplementedException();

    /// <summary>Whether world-space bounds overlap this shape's bounds.</summary>
    public bool BoundsOverlap(in WorldPosition minimum, in WorldPosition maximum) =>
        throw new NotImplementedException();
}

/// <summary>The convex primitives authored arena geometry is built from.</summary>
public enum StaticColliderKind : byte
{
    /// <summary>Unset. Never valid.</summary>
    None = 0,

    /// <summary>Axis-aligned or yaw-rotated box. Most of the arena.</summary>
    Box = 1,

    /// <summary>
    /// Ramp or slope, carrying its incline explicitly.
    /// </summary>
    /// <remarks>
    /// A distinct kind rather than a rotated box because the arena's slope
    /// stations exist specifically to be walked up, and slope classification is
    /// the rule most sensitive to normal accuracy. Deriving the normal from a
    /// rotated box's face would put a float rotation between the authored angle
    /// and the walkability decision.
    /// </remarks>
    Wedge = 2,

    /// <summary>Vertical cylinder, for the slalom obstacles.</summary>
    Cylinder = 3,
}

/// <summary>
/// The authored static geometry of one arena, immutable once built.
/// </summary>
/// <remarks>
/// <para>
/// Built once from the scene and then never mutated, which is what makes it safe
/// to query from a replay: there is no per-frame state for a replayed frame to
/// disturb, and no ordering dependence on when a body happened to be added.
/// </para>
/// <para>
/// Shapes are stored in a deterministic order — sorted by collider identity, not
/// by scene traversal order — so the broad phase visits them identically in every
/// process. That is what makes contact ordering a function of content rather than
/// of load order, and it is the root fix for the problem P06-A2 works around
/// downstream.
/// </para>
/// <para>
/// The broad phase is a uniform grid rather than a tree. Arena geometry is a few
/// hundred static shapes over a bounded volume, so a grid gives the same answer
/// with a fixed cost and no rebalancing, and its cell walk is trivially
/// deterministic — which a tree's traversal order is not, once shapes straddle
/// splits.
/// </para>
/// </remarks>
public sealed class StaticCollisionGeometry
{
    /// <summary>
    /// Builds immutable geometry from authored shapes.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Any shape is invalid, or two shapes share a collider identity — which would
    /// mean the derivation collided and contacts could not be attributed.
    /// </exception>
    public static StaticCollisionGeometry Build(IReadOnlyList<StaticColliderShape> shapes) =>
        throw new NotImplementedException();

    /// <summary>Shapes in deterministic identity order.</summary>
    public int ShapeCount => throw new NotImplementedException();

    public StaticColliderShape this[int index] => throw new NotImplementedException();

    /// <summary>
    /// Fills <paramref name="candidates"/> with the indices of shapes whose bounds
    /// overlap the query bounds, in deterministic order.
    /// </summary>
    /// <returns>
    /// How many candidates were written, or -1 when the buffer was too small —
    /// reported rather than truncated, because a silently dropped candidate is a
    /// character falling through a floor that a test would never reproduce.
    /// </returns>
    public int QueryBounds(
        in WorldPosition minimum,
        in WorldPosition maximum,
        Span<int> candidates) => throw new NotImplementedException();

    /// <summary>
    /// A content hash of all geometry, for cross-process agreement checks.
    /// </summary>
    /// <remarks>
    /// Two endpoints that disagree about geometry cannot possibly agree about
    /// simulation, and every divergence would then be misattributed to
    /// reconciliation. Comparing this once at join is far cheaper than debugging
    /// that.
    /// </remarks>
    public ulong ContentHash => throw new NotImplementedException();
}
