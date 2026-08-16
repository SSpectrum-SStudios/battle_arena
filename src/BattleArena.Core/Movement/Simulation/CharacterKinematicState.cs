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
    public WorldPosition(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public double X { get; }
    public double Y { get; }
    public double Z { get; }
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);

    public static WorldPosition Zero => default;

    /// <summary>Horizontal displacement only, for locomotion rules that ignore height.</summary>
    public HorizontalVector HorizontalTo(WorldPosition other) =>
        new(other.X - X, other.Z - Z);

    public WorldPosition Offset(HorizontalVector horizontal, double vertical) =>
        new(X + horizontal.X, Y + vertical, Z + horizontal.Z);

    public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###})";
}

/// <summary>
/// A unit surface normal. Kept distinct from a position so slope classification
/// cannot be handed a point by mistake.
/// </summary>
public readonly record struct SurfaceNormal
{
    private const double UnitTolerance = 1e-6d;

    public SurfaceNormal(double x, double y, double z)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
        {
            throw new ArgumentOutOfRangeException(nameof(x), "A surface normal must be finite.");
        }

        var lengthSquared = (x * x) + (y * y) + (z * z);
        if (lengthSquared <= MovementMath.EpsilonSquared)
        {
            throw new ArgumentOutOfRangeException(nameof(x), "A surface normal must be non-degenerate.");
        }

        // Normalized on construction so every consumer can assume unit length
        // rather than each re-normalizing and drifting apart by rounding.
        var length = Math.Sqrt(lengthSquared);
        X = x / length;
        Y = y / length;
        Z = z / length;
    }

    public double X { get; }
    public double Y { get; }
    public double Z { get; }

    /// <summary>Straight up. The default for a flat floor and the identity for slope tests.</summary>
    public static SurfaceNormal Up => new(0d, 1d, 0d);

    public bool IsValid =>
        double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z) &&
        Math.Abs(((X * X) + (Y * Y) + (Z * Z)) - 1d) <= UnitTolerance;

    /// <summary>
    /// Angle from vertical, which is what walkability is expressed in.
    /// </summary>
    /// <remarks>
    /// Provided for diagnostics and authoring. Classification itself compares
    /// <see cref="Y"/> against the cosine of the threshold instead, which avoids
    /// a transcendental exactly at the boundary where the answer flips.
    /// </remarks>
    public double SlopeAngleRadians => Math.Acos(Math.Clamp(Y, -1d, 1d));

    /// <summary>The horizontal part of this normal, used to project blocked motion.</summary>
    public HorizontalVector Horizontal => new(X, Z);
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
    public SupportIdentity(ulong colliderId, int shapeIndex)
    {
        if (shapeIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(shapeIndex));
        }

        ColliderId = colliderId;
        ShapeIndex = shapeIndex;
    }

    public ulong ColliderId { get; }
    public int ShapeIndex { get; }
    public bool IsValid => ColliderId != 0;
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
        SupportIdentity support)
    {
        if (!position.IsFinite ||
            !horizontalVelocity.IsFinite ||
            !double.IsFinite(verticalVelocity) ||
            !double.IsFinite(facingYawRadians))
        {
            throw new ArgumentOutOfRangeException(
                nameof(position),
                "Kinematic state values must be finite.");
        }

        Position = position;
        HorizontalVelocity = horizontalVelocity;
        VerticalVelocity = verticalVelocity;
        FacingYawRadians = MovementMath.WrapAngle(facingYawRadians);
        IsGrounded = isGrounded;

        // An airborne state never retains a ground normal or support. Keeping a
        // stale slope would change how the next landing is classified.
        GroundNormal = isGrounded && groundNormal.IsValid ? groundNormal : SurfaceNormal.Up;
        Support = isGrounded ? support : SupportIdentity.None;
    }

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

    public bool IsValid =>
        Position.IsFinite &&
        HorizontalVelocity.IsFinite &&
        double.IsFinite(VerticalVelocity) &&
        double.IsFinite(FacingYawRadians) &&
        GroundNormal.IsValid &&
        (IsGrounded || (!Support.IsValid && GroundNormal == SurfaceNormal.Up));

    public static CharacterKinematicState AtRest(
        WorldPosition position,
        double facingYawRadians) => new(
            position,
            HorizontalVector.Zero,
            0d,
            facingYawRadians,
            isGrounded: true,
            SurfaceNormal.Up,
            SupportIdentity.None);

    public CharacterKinematicState WithPosition(WorldPosition position) => new(
        position,
        HorizontalVelocity,
        VerticalVelocity,
        FacingYawRadians,
        IsGrounded,
        GroundNormal,
        Support);

    public CharacterKinematicState WithVelocity(HorizontalVector horizontal, double vertical) =>
        new(
            Position,
            horizontal,
            vertical,
            FacingYawRadians,
            IsGrounded,
            GroundNormal,
            Support);

    public CharacterKinematicState WithFacing(double facingYawRadians) => new(
        Position,
        HorizontalVelocity,
        VerticalVelocity,
        facingYawRadians,
        IsGrounded,
        GroundNormal,
        Support);

    /// <summary>
    /// Records the outcome of a ground probe. Clearing support also clears the
    /// normal, so an airborne state can never retain a stale slope that would
    /// change how the next landing is classified.
    /// </summary>
    public CharacterKinematicState WithGround(
        bool isGrounded,
        SurfaceNormal groundNormal,
        SupportIdentity support) => new(
            Position,
            HorizontalVelocity,
            VerticalVelocity,
            FacingYawRadians,
            isGrounded,
            groundNormal,
            support);
}
