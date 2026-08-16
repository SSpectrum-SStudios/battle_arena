using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Core.Movement.Simulation;
using Godot;

namespace BattleArena.Movement;

/// <summary>
/// The Godot implementation of <see cref="ICharacterCollisionWorld"/>.
/// </summary>
/// <remarks>
/// <para>
/// Built on the approach the P01-09 feasibility probe validated: one precreated
/// kinematic body RID per collision profile, reusable query parameter and result
/// objects, and <c>PhysicsServer3D.BodyTestMotion</c> against an explicit
/// transform. The probe measured 6,528 historical-transform queries at about
/// 10.8 microseconds each, which is what makes per-frame replay affordable.
/// </para>
/// <para>
/// The critical property is that no live node moves. Every query supplies its
/// own <c>From</c> transform, so asking "what would the capsule hit starting from
/// that past position" never disturbs the character the player is looking at.
/// Moving a real node to answer a historical query — the obvious alternative —
/// would fight presentation and make replay visible.
/// </para>
/// <para>
/// Queries are masked to static geometry only. Dynamic bodies and other
/// characters are deliberately excluded: player-versus-player collision is
/// frame-aligned and belongs to Phase 9, and letting it leak in here would make
/// one character's replay depend on another's current position.
/// </para>
/// </remarks>
public sealed partial class GodotKinematicCollisionWorld : Node3D, ICharacterCollisionWorld
{
    /// <summary>
    /// Creates the per-profile query bodies and shapes.
    /// </summary>
    /// <remarks>
    /// Shapes are created once and reused for the world's lifetime. Creating them
    /// per query would allocate on the hot path and, worse, make query cost
    /// depend on how many frames replay is resimulating.
    /// </remarks>
    public void Initialize(CollisionProfileTable profiles, uint staticWorldMask) =>
        throw new NotImplementedException();

    /// <inheritdoc />
    public CapsuleSweepResult Sweep(
        in CapsuleSweepRequest request,
        Span<CollisionContactState> contacts) => throw new NotImplementedException();

    /// <inheritdoc />
    public GroundProbeResult ProbeGround(in GroundProbeRequest request) =>
        throw new NotImplementedException();

    /// <inheritdoc />
    public bool HasClearance(in ClearanceRequest request) =>
        throw new NotImplementedException();

    /// <inheritdoc />
    /// <remarks>
    /// Uses a rest-info style query rather than a motion test, because
    /// penetration recovery runs before any motion exists and needs a separation
    /// direction and depth, not a travel fraction. Note the P01-09 probe
    /// deliberately ran with recovery-as-collision disabled, so this specific
    /// path is not one it proved; P05-06 must verify it directly.
    /// </remarks>
    public OverlapResolution ResolveOverlap(in ClearanceRequest request) =>
        throw new NotImplementedException();

    /// <inheritdoc />
    /// <remarks>
    /// Returns false for every surface in the arena today, which is entirely
    /// static. The contract exists so moving platforms can be added later
    /// without reopening the motor.
    /// </remarks>
    public bool TryGetSupportMotion(
        SupportIdentity support,
        SimulationInstant fromFrame,
        SimulationInstant toFrame,
        out HorizontalVector horizontal,
        out double vertical) => throw new NotImplementedException();

    /// <summary>
    /// Releases the precreated physics-server bodies and shapes.
    /// </summary>
    /// <remarks>
    /// These are server-side resources that outlive node freeing, so they are
    /// released explicitly. The P01-09 probe verified the cleanup path.
    /// </remarks>
    protected override void Dispose(bool disposing) => throw new NotImplementedException();

    /// <summary>
    /// Converts a simulation position into the engine's vector type.
    /// </summary>
    /// <remarks>
    /// The one place the conversion happens, and the reason simulation carries
    /// its own <see cref="WorldPosition"/>: <c>BattleArena.Core</c> stays free of
    /// any engine reference, and the precision boundary between the double-based
    /// simulation and the engine's float transforms is explicit and auditable
    /// rather than scattered.
    /// </remarks>
    private static Vector3 ToEngine(WorldPosition position) =>
        throw new NotImplementedException();

    private static WorldPosition FromEngine(Vector3 position) =>
        throw new NotImplementedException();

    private static SurfaceNormal NormalFromEngine(Vector3 normal) =>
        throw new NotImplementedException();
}
