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
        double verticalSpeed) => throw new NotImplementedException();

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
    public bool IsValid => throw new NotImplementedException();

    /// <summary>
    /// Whether this source still contributes on the given frame.
    /// </summary>
    /// <remarks>
    /// The window is half-open: active on [StartFrame, StartFrame + Duration),
    /// so a source with a one-frame duration contributes to exactly one frame.
    /// Returns false for a frame before the start, which is representable because
    /// a source may be started with a start frame in the future.
    /// </remarks>
    public bool IsActiveOn(SimulationInstant frame) => throw new NotImplementedException();

    /// <summary>
    /// Normalized progress in [0, 1] on the given frame, derived from the start
    /// frame rather than accumulated so replay reproduces it exactly.
    /// </summary>
    public double ProgressOn(SimulationInstant frame) => throw new NotImplementedException();

    /// <summary>
    /// This source's velocity contribution on the given frame, after its authored
    /// falloff curve.
    /// </summary>
    public (HorizontalVector Horizontal, double Vertical) EvaluateOn(SimulationInstant frame) =>
        throw new NotImplementedException();
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

    public int Count => throw new NotImplementedException();
    public bool IsFull => throw new NotImplementedException();

    public MovementSourceState this[int index] => throw new NotImplementedException();

    /// <summary>
    /// Adds a source, or replaces the existing one with the same identity so a
    /// repeated authority message is idempotent.
    /// </summary>
    /// <returns>
    /// False when the buffer is full. Refusing is deliberate: silently dropping
    /// the oldest would make the result depend on arrival order, and growing
    /// would break the copy cost this type exists to bound.
    /// </returns>
    public bool TryAddOrReplace(in MovementSourceState source) =>
        throw new NotImplementedException();

    public bool Remove(ulong sourceId) => throw new NotImplementedException();

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
    public bool Equals(MovementSourceBuffer other) => throw new NotImplementedException();

    public override bool Equals(object? obj) => throw new NotImplementedException();

    public override int GetHashCode() => throw new NotImplementedException();

    public static bool operator ==(MovementSourceBuffer left, MovementSourceBuffer right) =>
        throw new NotImplementedException();

    public static bool operator !=(MovementSourceBuffer left, MovementSourceBuffer right) =>
        throw new NotImplementedException();

    /// <summary>
    /// Drops every source whose lifetime ended at or before the given frame.
    /// Called once per frame so expiry is a function of the frame number rather
    /// than of how many times the frame was simulated.
    /// </summary>
    public int RemoveExpired(SimulationInstant frame) => throw new NotImplementedException();

    /// <summary>
    /// Sums every active source's contribution on the given frame, in stable
    /// identity order.
    /// </summary>
    public (HorizontalVector Horizontal, double Vertical) Aggregate(SimulationInstant frame) =>
        throw new NotImplementedException();

    public void Clear() => throw new NotImplementedException();
}
