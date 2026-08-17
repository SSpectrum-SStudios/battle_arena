using BattleArena.Core.Common;

namespace BattleArena.Core.Movement.Simulation;

/// <summary>
/// The collision world the motor actually simulates against: capsule-accurate,
/// engine-free, and in-process.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> Two separate Phase 6 blockers turned out to have one
/// answer, which is why it is worth an architectural change rather than a
/// workaround.
/// </para>
/// <para>
/// <em>Cost.</em> Every query through <c>GodotKinematicCollisionWorld</c> is a
/// <c>PhysicsServer3D.BodyTestMotion</c> round trip, measured by P5B-03 at 25-29
/// microseconds. Covering the authored 48-frame prediction lead needs
/// 4166.7 / 48 = 87 microseconds per replayed frame, which at the 8 queries per
/// frame that probe measured is 10.9 microseconds per query — below even P01-11's
/// optimistic isolated-query figure. Engine queries cannot reach the target at any
/// depth that matters, so the depth cap had to be 8 and the design was heading for
/// permanent rebasing at real latency.
/// </para>
/// <para>
/// <em>Identity.</em> An engine query reports colliders as
/// <c>RID.Id</c>, a per-process allocation handle. Support and contacts are
/// discrete comparison fields, so cross-process reconciliation would report a
/// mismatch on every grounded frame.
/// </para>
/// <para>
/// Owning the world in-process fixes both: closed-form convex sweeps cost a
/// couple of microseconds instead of thirty, and colliders are identified by
/// authored scene path instead of allocation order.
/// </para>
/// <para>
/// <b>The Rocket League precedent.</b> Rocket League resimulates its entire
/// physics scene — every car and a fully physical ball — at 120 Hz, double our
/// tick rate, and affords it. It does that with Bullet in-process, stepping its
/// own world rather than issuing per-operation engine queries, and it chose Bullet
/// over the engine's own physics specifically to get deterministic networked
/// physics. Replay depth is not inherently expensive; paying an engine round trip
/// per motor operation is.
/// </para>
/// <para>
/// <b>What this does not do.</b> Only static geometry, exactly as the engine
/// adapter did. Player-versus-player collision is frame-aligned and belongs to
/// Phase 9, and letting it in here would make one character's replay depend on
/// another's current position. When Phase 9 does add remote players, the technique
/// to reach for is Rocket League's input decay — full remote input on the first
/// predicted frame, then roughly two thirds, a third, and none — which reads
/// better than overshooting and rubber-banding back.
/// </para>
/// <para>
/// <b>Relationship to the two existing worlds.</b> This replaces
/// <c>GodotKinematicCollisionWorld</c> as what simulation depends on at runtime;
/// that adapter becomes the reference this is validated <em>against</em>, which is
/// what P5B-01's probe should have been measuring all along.
/// <see cref="DeterministicCollisionWorld"/> stays a test fixture and must not be
/// promoted in its place: it approximates the capsule as an axis-aligned box, and
/// P5B-01 showed precisely where that diverges — a box's flat bottom reports a
/// step's flat top the instant it overhangs, while a capsule's hemisphere catches
/// the top edge a radius short of the face.
/// </para>
/// </remarks>
public sealed class StaticCollisionWorld : ICharacterCollisionWorld
{
    /// <summary>
    /// Most broad-phase candidates one query will consider.
    /// </summary>
    /// <remarks>
    /// A capsule sweep spans a bounded volume over arena-scale geometry, so this
    /// is generous. It is a hard cap rather than a growable buffer because this
    /// runs on an allocation-free path, and
    /// <see cref="StaticCollisionGeometry.QueryBounds"/> reports overflow rather
    /// than truncating — a silently dropped candidate is a character falling
    /// through a floor.
    /// </remarks>
    public const int MaximumBroadPhaseCandidates = 64;

    /// <summary>
    /// Builds a world over immutable geometry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Geometry only. The profile table is deliberately NOT held here.</b> An
    /// earlier version of this stub took it at construction and justified that by
    /// saying a configuration change would rebuild the world; the stub review
    /// pointed out that rebuilding is exactly how a replayed frame gets swept with
    /// the wrong capsule.
    /// </para>
    /// <para>
    /// Capsule dimensions are revisioned authored content, so they must travel with
    /// the request — the same rule
    /// <see cref="CapsuleSweepRequest.WalkableSlopeRadians"/> already states for the
    /// same reason: state read during a replayed frame that is neither in the rewind
    /// unit nor derived from its inputs is a divergence with no attributable cause.
    /// P06-05 exists to resolve the tick-effective revision per replayed frame, and
    /// holding the table here would defeat it. <c>GodotKinematicCollisionWorld</c>
    /// has this shape only because the engine needs precreated shape RIDs; in-process
    /// there is no such constraint.
    /// </para>
    /// </remarks>
    public StaticCollisionWorld(StaticCollisionGeometry geometry) =>
        throw new NotImplementedException();

    public StaticCollisionGeometry Geometry => throw new NotImplementedException();

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Conservative-advancement sweep against each broad-phase candidate, keeping
    /// the earliest impact. Conservative advancement rather than a fixed number of
    /// substeps because a substepped sweep tunnels at exactly the speeds a
    /// swept motor exists to handle, and because its cost varies with geometry
    /// rather than being a constant nobody can budget.
    /// </para>
    /// <para>
    /// Contacts are written in whatever order candidates were visited, which is
    /// deterministic because geometry is stored in identity order. Sorting stays
    /// the motor's job through
    /// <see cref="CollisionContactState.CompareForStableResolution"/>, so the
    /// determinism rule lives in one place rather than being re-implemented per
    /// adapter.
    /// </para>
    /// <para>
    /// Unlike the engine adapter, this reports a genuine per-contact travel
    /// fraction. That matters beyond accuracy: the adapter gave every contact of a
    /// sweep the same fraction, which made the stable comparer's primary key
    /// constant and let the real ordering fall through to a physics-server RID.
    /// </para>
    /// </remarks>
    public CapsuleSweepResult Sweep(
        in CapsuleSweepRequest request,
        Span<CollisionContactState> contacts) => throw new NotImplementedException();

    /// <inheritdoc />
    /// <remarks>
    /// A downward sweep that keeps the nearest walkable-or-not surface, reporting
    /// the true contact normal. The engine adapter could only report the normal of
    /// whichever collision the server ranked first; here the nearest is known.
    /// </remarks>
    public GroundProbeResult ProbeGround(in GroundProbeRequest request) =>
        throw new NotImplementedException();

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Minimum-translation resolution over every overlapping candidate, keeping
    /// the deepest, and resolving along whichever axis is least penetrated —
    /// <em>any</em> axis, not only downward.
    /// </para>
    /// <para>
    /// That last point is load-bearing and was a real defect in the engine path:
    /// the adapter probed with a small downward motion and so could not report a
    /// horizontal overlap at all. It also removes the engine's query margin from
    /// the equation entirely, which is what the 5 mm surface skin was raised to
    /// work around.
    /// </para>
    /// </remarks>
    public OverlapResolution ResolveOverlap(in ClearanceRequest request) =>
        throw new NotImplementedException();

    /// <inheritdoc />
    public bool HasClearance(in ClearanceRequest request) => throw new NotImplementedException();

    /// <inheritdoc />
    /// <remarks>
    /// Always false: this world is static by construction. The contract stays so
    /// moving platforms can be added without reopening the motor, and a moving
    /// platform would need its own world rather than mutating this one, because a
    /// replayed frame must see the platform where it was on <em>that</em> frame.
    /// </remarks>
    public bool TryGetSupportMotion(
        SupportIdentity support,
        SimulationInstant fromFrame,
        SimulationInstant toFrame,
        out HorizontalVector horizontal,
        out double vertical) => throw new NotImplementedException();
}

/// <summary>
/// Builds <see cref="StaticCollisionGeometry"/> from a description the engine side
/// produces, without referencing the engine.
/// </summary>
/// <remarks>
/// <para>
/// The seam that keeps <c>BattleArena.Core</c> engine-free. The Godot side walks
/// the scene and emits authored descriptions; this turns them into geometry. The
/// walk itself cannot live in Core, and the geometry must not live in Godot,
/// because the authority may run headless and both endpoints must build byte
/// identical worlds.
/// </para>
/// <para>
/// Deliberately takes descriptions rather than a scene path so the builder is
/// testable with exact expected geometry, which is the same reasoning that put
/// <see cref="ICharacterCollisionWorld"/> behind an interface in P05-05.
/// </para>
/// </remarks>
public sealed class StaticCollisionGeometryBuilder
{
    /// <summary>
    /// Adds one authored collider.
    /// </summary>
    /// <param name="scenePath">
    /// The authored node path. Must be the scene-authored path rather than a
    /// runtime-generated name: a generated name embeds instantiation order, which
    /// is the process-locality this whole subsection exists to remove.
    /// </param>
    public StaticCollisionGeometryBuilder AddBox(
        string scenePath,
        int shapeIndex,
        in WorldPosition centre,
        in WorldPosition halfExtents,
        double yawRadians) => throw new NotImplementedException();

    public StaticCollisionGeometryBuilder AddWedge(
        string scenePath,
        int shapeIndex,
        in WorldPosition centre,
        in WorldPosition halfExtents,
        double yawRadians,
        double slopeRadians) => throw new NotImplementedException();

    public StaticCollisionGeometryBuilder AddCylinder(
        string scenePath,
        int shapeIndex,
        in WorldPosition centre,
        double radius,
        double halfHeight) => throw new NotImplementedException();

    /// <summary>
    /// Produces immutable geometry in deterministic identity order.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No colliders were added. An empty world is the most dangerous possible
    /// result — every query finds nothing, every character falls, and every
    /// comparison agrees — so it faults rather than being built.
    /// </exception>
    public StaticCollisionGeometry Build() => throw new NotImplementedException();
}
