using System.Runtime.CompilerServices;
using BattleArena.Core.Common;

namespace BattleArena.Core.Movement.Simulation;

/// <summary>
/// What produced an external motion contribution. The kind is authored, not
/// inferred, so replay applies the same curve the original frame did.
/// </summary>
public enum MovementSourceKind : byte
{
    /// <summary>Attack lunge, starting on the accepted action frame.</summary>
    AttackLunge = 1,

    /// <summary>Roll displacement, distinct from the roll's own locomotion rules.</summary>
    RollBoost = 2,

    Dash = 3,

    /// <summary>Impulse from taking a hit. Authority-originated but replayable.</summary>
    Knockback = 4,

    /// <summary>Sustained pull toward a point, for future item behaviour.</summary>
    Pull = 5,
}

/// <summary>
/// How a source's contribution changes across its lifetime.
/// </summary>
/// <remarks>
/// Stored on the source rather than inferred from its kind, so the curve a frame
/// was simulated with is the curve replay reproduces. Inferring from kind would
/// mean an authoring tweak silently rewrote history.
/// </remarks>
public enum MovementSourceFalloff : byte
{
    /// <summary>Full contribution for the whole lifetime, then nothing.</summary>
    Constant = 1,

    /// <summary>Full at the start, falling linearly to nothing at the end.</summary>
    Linear = 2,

    /// <summary>Front-loaded: falls as the square of remaining progress.</summary>
    Quadratic = 3,
}

/// <summary>
/// One active external motion contribution, complete enough to be re-evaluated
/// from scratch on any frame of its lifetime.
/// </summary>
/// <remarks>
/// <para>
/// This is the replayable replacement for the current practice of adding an
/// impulse to node velocity once, at the moment the effect fires. A one-time
/// impulse cannot be replayed: rewinding to a frame before it fired and
/// resimulating forward loses it entirely, while rewinding to a frame after it
/// fired applies it twice. Storing the source and evaluating its curve per frame
/// makes both cases correct.
/// </para>
/// <para>
/// Progress is derived from the start frame and the current frame rather than
/// accumulated, so it is identical on the first run and every replay regardless
/// of how many times the frame is simulated.
/// </para>
/// </remarks>
public readonly record struct MovementSourceState
{
    public MovementSourceState(
        MovementSourceKind kind,
        ulong sourceId,
        SimulationInstant startFrame,
        SimulationDuration duration,
        HorizontalVector horizontalDirection,
        double horizontalSpeed,
        double verticalSpeed,
        MovementSourceFalloff falloff)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        if (sourceId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceId), "A movement source must have an identity.");
        }
        if (duration.Ticks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }
        if (!horizontalDirection.IsFinite ||
            !double.IsFinite(horizontalSpeed) ||
            !double.IsFinite(verticalSpeed))
        {
            throw new ArgumentOutOfRangeException(nameof(horizontalDirection));
        }
        if (!Enum.IsDefined(falloff))
        {
            throw new ArgumentOutOfRangeException(nameof(falloff));
        }

        Kind = kind;
        SourceId = sourceId;
        StartFrame = startFrame;
        Duration = duration;
        HorizontalDirection = horizontalDirection.LengthSquared > MovementMath.EpsilonSquared
            ? horizontalDirection.Normalized
            : HorizontalVector.Zero;
        HorizontalSpeed = horizontalSpeed;
        VerticalSpeed = verticalSpeed;
        Falloff = falloff;
    }

    public MovementSourceKind Kind { get; }

    /// <summary>
    /// Distinguishes two sources of the same kind active at once, and lets a
    /// source be ended or superseded by identity rather than by search.
    /// </summary>
    public ulong SourceId { get; }

    public SimulationInstant StartFrame { get; }
    public SimulationDuration Duration { get; }
    public HorizontalVector HorizontalDirection { get; }
    public double HorizontalSpeed { get; }
    public double VerticalSpeed { get; }
    public MovementSourceFalloff Falloff { get; }

    public bool IsValid =>
        Enum.IsDefined(Kind) &&
        SourceId != 0 &&
        Duration.Ticks > 0 &&
        HorizontalDirection.IsFinite &&
        double.IsFinite(HorizontalSpeed) &&
        double.IsFinite(VerticalSpeed) &&
        Enum.IsDefined(Falloff);

    /// <summary>The first frame after this source stops contributing.</summary>
    public SimulationInstant EndFrameExclusive =>
        new(StartFrame.Tick + Duration.Ticks);

    /// <summary>
    /// Whether this source still contributes on the given frame.
    /// </summary>
    /// <remarks>
    /// The window is half-open: active on [StartFrame, StartFrame + Duration),
    /// so a source with a one-frame duration contributes to exactly one frame.
    /// Returns false for a frame before the start, which is representable because
    /// a source may be started with a start frame in the future.
    /// </remarks>
    public bool IsActiveOn(SimulationInstant frame) =>
        IsValid && frame.Tick >= StartFrame.Tick && frame.Tick < EndFrameExclusive.Tick;

    /// <summary>
    /// Normalized progress in [0, 1] on the given frame, derived from the start
    /// frame rather than accumulated so replay reproduces it exactly.
    /// </summary>
    /// <remarks>
    /// Computed by subtracting ticks directly rather than through
    /// <c>SimulationInstant</c> subtraction, which throws when the left operand
    /// is earlier — and a source may legitimately be asked about a frame before
    /// it starts.
    /// </remarks>
    public double ProgressOn(SimulationInstant frame)
    {
        if (!IsValid)
        {
            return 0d;
        }

        var elapsed = frame.Tick - StartFrame.Tick;
        if (elapsed <= 0)
        {
            return 0d;
        }

        return elapsed >= Duration.Ticks ? 1d : (double)elapsed / Duration.Ticks;
    }

    /// <summary>
    /// This source's velocity contribution on the given frame, after its authored
    /// falloff curve.
    /// </summary>
    public (HorizontalVector Horizontal, double Vertical) EvaluateOn(SimulationInstant frame)
    {
        if (!IsActiveOn(frame))
        {
            return (HorizontalVector.Zero, 0d);
        }

        var remaining = 1d - ProgressOn(frame);
        var scale = Falloff switch
        {
            MovementSourceFalloff.Constant => 1d,
            MovementSourceFalloff.Linear => remaining,
            MovementSourceFalloff.Quadratic => remaining * remaining,
            _ => 0d,
        };

        return (HorizontalDirection * (HorizontalSpeed * scale), VerticalSpeed * scale);
    }
}

/// <summary>
/// Fixed-capacity store of active movement sources.
/// </summary>
/// <remarks>
/// <para>
/// Fixed capacity, inline storage, and no allocation on any path: this is copied
/// as part of the rewind unit every frame, and an unbounded list would both
/// allocate and let a hostile or buggy effect stream grow authority memory.
/// </para>
/// <para>
/// Ordering is stable by source identity rather than insertion, so aggregation
/// sums contributions in the same sequence on replay. Floating-point addition is
/// not associative, so unstable ordering would produce a different result from
/// identical inputs.
/// </para>
/// </remarks>
public struct MovementSourceBuffer : IEquatable<MovementSourceBuffer>
{
    /// <summary>
    /// Matches the sixteen concurrent movement sources the design document sets
    /// as the wire and runtime safety bound. Keeping the two the same means the
    /// protocol never has to describe a buffer the simulation cannot hold.
    /// </summary>
    public const int Capacity = 16;

    private Storage _sources;
    private int _count;

    [InlineArray(Capacity)]
    private struct Storage
    {
        private MovementSourceState _element0;
    }

    public int Count => _count;
    public bool IsFull => _count >= Capacity;

    public MovementSourceState this[int index] => (uint)index < (uint)_count
        ? _sources[index]
        : throw new ArgumentOutOfRangeException(nameof(index));

    /// <summary>
    /// Adds a source, or replaces the existing one with the same identity so a
    /// repeated authority message is idempotent.
    /// </summary>
    /// <returns>
    /// False when the buffer is full. Refusing is deliberate: silently dropping
    /// the oldest would make the result depend on arrival order, and growing
    /// would break the copy cost this type exists to bound.
    /// </returns>
    public bool TryAddOrReplace(in MovementSourceState source)
    {
        if (!source.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }

        for (var index = 0; index < _count; index++)
        {
            if (_sources[index].SourceId == source.SourceId)
            {
                _sources[index] = source;
                return true;
            }
        }

        if (IsFull)
        {
            return false;
        }

        // Inserted in ascending identity order so aggregation sums in a stable
        // sequence; floating-point addition is not associative, so insertion
        // order would otherwise leak into the result.
        var insertAt = _count;
        for (var index = 0; index < _count; index++)
        {
            if (_sources[index].SourceId > source.SourceId)
            {
                insertAt = index;
                break;
            }
        }

        for (var index = _count; index > insertAt; index--)
        {
            _sources[index] = _sources[index - 1];
        }

        _sources[insertAt] = source;
        _count++;
        return true;
    }

    public bool Remove(ulong sourceId)
    {
        for (var index = 0; index < _count; index++)
        {
            if (_sources[index].SourceId != sourceId)
            {
                continue;
            }

            RemoveAt(index);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Drops every source whose lifetime ended at or before the given frame.
    /// Called once per frame so expiry is a function of the frame number rather
    /// than of how many times the frame was simulated.
    /// </summary>
    public int RemoveExpired(SimulationInstant frame)
    {
        var removed = 0;
        for (var index = _count - 1; index >= 0; index--)
        {
            if (frame.Tick >= _sources[index].EndFrameExclusive.Tick)
            {
                RemoveAt(index);
                removed++;
            }
        }

        return removed;
    }

    /// <summary>
    /// Sums every active source's contribution on the given frame, in stable
    /// identity order.
    /// </summary>
    public (HorizontalVector Horizontal, double Vertical) Aggregate(SimulationInstant frame)
    {
        var horizontal = HorizontalVector.Zero;
        var vertical = 0d;
        for (var index = 0; index < _count; index++)
        {
            var (sourceHorizontal, sourceVertical) = _sources[index].EvaluateOn(frame);
            horizontal += sourceHorizontal;
            vertical += sourceVertical;
        }

        return (horizontal, vertical);
    }

    public void Clear()
    {
        for (var index = 0; index < _count; index++)
        {
            _sources[index] = default;
        }

        _count = 0;
    }

    /// <summary>
    /// Value equality over the occupied entries only.
    /// </summary>
    /// <remarks>
    /// Implemented explicitly because this is held by
    /// <see cref="CharacterSimulationState"/>, which is a record struct: without
    /// it the compiler-generated equality falls back to reflection-based
    /// <see cref="ValueType.Equals"/> for this member, which boxes and is slow on
    /// a path that runs per retained frame. The reference-buffer types on
    /// <see cref="CharacterSimulationInput"/> already follow this pattern.
    /// </remarks>
    public bool Equals(MovementSourceBuffer other)
    {
        if (_count != other._count)
        {
            return false;
        }

        for (var index = 0; index < _count; index++)
        {
            if (!_sources[index].Equals(other._sources[index]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) =>
        obj is MovementSourceBuffer other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_count);
        for (var index = 0; index < _count; index++)
        {
            hash.Add(_sources[index]);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(MovementSourceBuffer left, MovementSourceBuffer right) =>
        left.Equals(right);

    public static bool operator !=(MovementSourceBuffer left, MovementSourceBuffer right) =>
        !left.Equals(right);

    private void RemoveAt(int index)
    {
        for (var shift = index; shift < _count - 1; shift++)
        {
            _sources[shift] = _sources[shift + 1];
        }

        _sources[--_count] = default;
    }
}
