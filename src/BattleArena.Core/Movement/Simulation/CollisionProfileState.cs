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
    public CollisionProfileDimensions(double radius, double height)
    {
        if (!double.IsFinite(radius) || radius <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(radius));
        }
        if (!double.IsFinite(height) || height <= 0d || height < radius * 2d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(height),
                "A capsule height must accommodate both hemispheres.");
        }

        Radius = radius;
        Height = height;
    }

    public double Radius { get; }
    public double Height { get; }

    /// <summary>
    /// Half the full height. Note the capsule origin is the FOOT, so the distance
    /// from the origin to the top is <see cref="Height"/>, not this.
    /// </summary>
    public double HalfHeight => Height * 0.5d;

    public bool IsValid =>
        double.IsFinite(Radius) && Radius > 0d &&
        double.IsFinite(Height) && Height > 0d && Height >= Radius * 2d;
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
    public CollisionProfileState(CollisionProfileKind current, CollisionProfileKind desired)
    {
        if (!Enum.IsDefined(current))
        {
            throw new ArgumentOutOfRangeException(nameof(current));
        }
        if (!Enum.IsDefined(desired))
        {
            throw new ArgumentOutOfRangeException(nameof(desired));
        }

        Current = current;
        Desired = desired;
    }

    public CollisionProfileKind Current { get; }

    /// <summary>
    /// What the character would occupy if clearance allowed. Equal to
    /// <see cref="Current"/> whenever no expansion is pending.
    /// </summary>
    public CollisionProfileKind Desired { get; }

    /// <summary>
    /// True when the character is held in a smaller profile than it wants. Takes
    /// the table because enum order is not size order — Standing is the smallest
    /// enum value and the largest capsule.
    /// </summary>
    public bool HasPendingExpansion(CollisionProfileTable profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        return Current != Desired && profiles.IsExpansion(Current, Desired);
    }

    public bool IsValid => Enum.IsDefined(Current) && Enum.IsDefined(Desired);

    public static CollisionProfileState Standing =>
        new(CollisionProfileKind.Standing, CollisionProfileKind.Standing);

    /// <summary>
    /// Requests a profile. Shrinking applies immediately; expanding records the
    /// intent and leaves <see cref="Current"/> alone for the clearance stage to
    /// resolve.
    /// </summary>
    public CollisionProfileState WithDesired(
        CollisionProfileKind desired,
        CollisionProfileTable profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        if (!Enum.IsDefined(desired))
        {
            throw new ArgumentOutOfRangeException(nameof(desired));
        }

        // Shrinking never needs clearance: a smaller capsule always fits where a
        // larger one already did.
        return profiles.IsExpansion(Current, desired)
            ? new CollisionProfileState(Current, desired)
            : new CollisionProfileState(desired, desired);
    }

    /// <summary>
    /// Applies a pending expansion the clearance stage has approved.
    /// </summary>
    public CollisionProfileState ExpandToDesired() => new(Desired, Desired);
}

/// <summary>
/// The authored dimension table, resolved by profile identity.
/// </summary>
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
    public static CollisionProfileTable FromAttributes(MovementAttributeSnapshot attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        var crouchRoll = attributes.CrouchRoll;
        var radius = crouchRoll.CapsuleRadius;
        return new CollisionProfileTable(
            new CollisionProfileDimensions(radius, crouchRoll.StandingCapsuleHeight),
            new CollisionProfileDimensions(radius, crouchRoll.CrouchingCapsuleHeight),
            new CollisionProfileDimensions(radius, crouchRoll.RollingCapsuleHeight));
    }

    public CollisionProfileTable(
        CollisionProfileDimensions standing,
        CollisionProfileDimensions crouching,
        CollisionProfileDimensions rolling)
    {
        if (!standing.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(standing));
        }
        if (!crouching.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(crouching));
        }
        if (!rolling.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(rolling));
        }

        Standing = standing;
        Crouching = crouching;
        Rolling = rolling;
    }

    public CollisionProfileDimensions Standing { get; }
    public CollisionProfileDimensions Crouching { get; }
    public CollisionProfileDimensions Rolling { get; }

    public CollisionProfileDimensions For(CollisionProfileKind kind) => kind switch
    {
        CollisionProfileKind.Standing => Standing,
        CollisionProfileKind.Crouching => Crouching,
        CollisionProfileKind.Rolling => Rolling,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// Whether moving between two profiles grows the capsule, and therefore
    /// needs a clearance check before it may be applied.
    /// </summary>
    /// <remarks>
    /// Decided from authored dimensions rather than from enum order, which is
    /// inverse to size: <see cref="CollisionProfileKind.Standing"/> is the
    /// smallest enum value and the tallest capsule.
    /// </remarks>
    public bool IsExpansion(CollisionProfileKind from, CollisionProfileKind to)
    {
        var source = For(from);
        var target = For(to);
        return target.Height > source.Height || target.Radius > source.Radius;
    }
}
