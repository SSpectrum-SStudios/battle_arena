using BattleArena.Core.Common;

namespace BattleArena.Core.Movement.Simulation;

/// <summary>
/// A point in the arena's world space, owned by simulation rather than by a
/// scene node.
/// </summary>
/// <remarks>
/// This is a FOOT position: the base of the capsule, matching how the authored
/// shapes and the existing driver treat the body origin. Getting this wrong by a
/// half-height sinks the character into the floor, so it is stated here rather
/// than left to each call site.
///
/// The engine's own vector type is deliberately not used here.
/// <c>BattleArena.Core</c> has no engine reference, and position must be
/// copyable, comparable, and quantizable by the protocol layer without dragging
/// in a rendering type.
/// </remarks>
public readonly record struct WorldPosition
{
    public WorldPosition(double x, double y, double z) => throw new NotImplementedException();

    public double X { get; }
    public double Y { get; }
    public double Z { get; }
    public bool IsFinite => throw new NotImplementedException();

    public static WorldPosition Zero => default;

    /// <summary>Horizontal displacement only, for locomotion rules that ignore height.</summary>
    public HorizontalVector HorizontalTo(WorldPosition other) =>
        throw new NotImplementedException();

    public WorldPosition Offset(HorizontalVector horizontal, double vertical) =>
        throw new NotImplementedException();
}

/// <summary>
/// A unit surface normal. Kept distinct from a position so slope classification
/// cannot be handed a point by mistake.
/// </summary>
public readonly record struct SurfaceNormal
{
    public SurfaceNormal(double x, double y, double z) => throw new NotImplementedException();

    public double X { get; }
    public double Y { get; }
    public double Z { get; }

    /// <summary>Straight up. The default for a flat floor and the identity for slope tests.</summary>
    public static SurfaceNormal Up => throw new NotImplementedException();

    public bool IsValid => throw new NotImplementedException();

    /// <summary>
    /// Angle from vertical, which is what walkability is expressed in. Returned
    /// in radians so the caller compares against an authored threshold rather
    /// than recomputing a dot product at each site.
    /// </summary>
    public double SlopeAngleRadians => throw new NotImplementedException();
}

/// <summary>
/// Which collider a character is standing on, so support can be recognised
/// across frames without comparing floating-point positions.
/// </summary>
/// <remarks>
/// Stable identity matters for replay: "still on the same surface" must be a
/// deterministic comparison, and it is also what lets moving-platform support
/// motion be applied later without the character appearing to re-land.
/// </remarks>
public readonly record struct SupportIdentity
{
    public SupportIdentity(ulong colliderId, int shapeIndex) =>
        throw new NotImplementedException();

    public ulong ColliderId { get; }
    public int ShapeIndex { get; }
    public bool IsValid => throw new NotImplementedException();
    public static SupportIdentity None => default;
}

/// <summary>
/// The complete kinematic result of one simulated frame: where the character is,
/// how it is moving, and what it is standing on.
/// </summary>
/// <remarks>
/// <para>
/// This is the part of the rewind unit the current design does not have.
/// <see cref="MovementRuntimeState"/> holds velocity and modes, but position and
/// contact live on the Godot body and are produced by <c>MoveAndSlide</c>, so
/// they cannot be restored to an arbitrary past frame. Owning them here is what
/// makes replay possible at all.
/// </para>
/// <para>
/// A value struct rather than a record class: replay copies this every frame for
/// every retained frame, and the Phase 1 performance probe bounds per-frame
/// allocation at zero on the warmed path.
/// </para>
/// </remarks>
public readonly record struct CharacterKinematicState
{
    public CharacterKinematicState(
        WorldPosition position,
        HorizontalVector horizontalVelocity,
        double verticalVelocity,
        double facingYawRadians,
        bool isGrounded,
        SurfaceNormal groundNormal,
        SupportIdentity support) => throw new NotImplementedException();

    public WorldPosition Position { get; }
    public HorizontalVector HorizontalVelocity { get; }
    public double VerticalVelocity { get; }
    public double FacingYawRadians { get; }

    /// <summary>
    /// Whether the character is supported this frame. Distinct from
    /// <see cref="LocomotionMode"/>, which is a gameplay mode that can lag a
    /// frame behind the physical fact.
    /// </summary>
    public bool IsGrounded { get; }

    /// <summary>Valid only while grounded; <see cref="SurfaceNormal.Up"/> otherwise.</summary>
    public SurfaceNormal GroundNormal { get; }
    public SupportIdentity Support { get; }

    public bool IsValid => throw new NotImplementedException();

    public static CharacterKinematicState AtRest(WorldPosition position, double facingYawRadians) =>
        throw new NotImplementedException();

    public CharacterKinematicState WithPosition(WorldPosition position) =>
        throw new NotImplementedException();

    public CharacterKinematicState WithVelocity(
        HorizontalVector horizontal,
        double vertical) => throw new NotImplementedException();

    /// <summary>
    /// Records the outcome of a ground probe. Clearing support also clears the
    /// normal, so an airborne state can never retain a stale slope that would
    /// change how the next landing is classified.
    /// </summary>
    public CharacterKinematicState WithGround(
        bool isGrounded,
        SurfaceNormal groundNormal,
        SupportIdentity support) => throw new NotImplementedException();
}
