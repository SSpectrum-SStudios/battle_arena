using System.Runtime.CompilerServices;
using BattleArena.Core.Common;
using BattleArena.Core.Movement.Simulation;

namespace BattleArena.Multiplayer.OwnerPrediction;

/// <summary>
/// The durable transitions that applied on one frame, stored inline.
/// </summary>
/// <remarks>
/// <para>
/// This lives in history rather than being re-derived at replay time, and that
/// is the whole point. Authority may remap a transition to a later frame than
/// the owner predicted, so "which transitions applied on frame F" is a fact that
/// changes as resolutions arrive. Re-deriving it during replay would reproduce a
/// different frame than the one originally simulated and then report the
/// difference as a divergence the owner caused.
/// </para>
/// <para>
/// Capacity matches <see cref="TransitionReferenceBuffer"/> so a frame can never
/// carry more transitions than a command could reference.
/// </para>
/// </remarks>
// Stub-only: the inline storage is declared for the shape review but not yet
// read or written, because every member still throws. Both suppressions come off
// with the implementation.
#pragma warning disable CS0169, CS0649
public struct AppliedTransitionBuffer : IEquatable<AppliedTransitionBuffer>
{
    public const int Capacity = 16;

    private Storage _tags;
    private int _count;

    [InlineArray(Capacity)]
    private struct Storage
    {
        private MovementTransitionKindTag _element0;
    }

    public int Count => _count;
    public MovementTransitionKindTag this[int index] => throw new NotImplementedException();
    public bool TryAdd(MovementTransitionKindTag tag) => throw new NotImplementedException();
    public int CopyTo(Span<MovementTransitionKindTag> destination) =>
        throw new NotImplementedException();
    public void Clear() => throw new NotImplementedException();
    public bool Equals(AppliedTransitionBuffer other) => throw new NotImplementedException();
    public override bool Equals(object? obj) => throw new NotImplementedException();
    public override int GetHashCode() => throw new NotImplementedException();
    public static bool operator ==(AppliedTransitionBuffer l, AppliedTransitionBuffer r) =>
        throw new NotImplementedException();
    public static bool operator !=(AppliedTransitionBuffer l, AppliedTransitionBuffer r) =>
        throw new NotImplementedException();
}
#pragma warning restore CS0169, CS0649

/// <summary>
/// A deterministic simulation event a frame produced, identified stably enough
/// to be recognised again on replay.
/// </summary>
/// <remarks>
/// Cues — a jump, a landing, a roll start — must fire once even though their
/// frame may be simulated many times. <see cref="PredictedCueLedger"/> already
/// solves that deduplication; it needs the per-frame identities, and this is
/// where they are retained so a replayed frame offers the ledger the same ones
/// the first run did.
/// </remarks>
public readonly record struct OwnerSimulationEvent
{
    public OwnerSimulationEvent(ulong eventId, PredictedCueKind kind) =>
        throw new NotImplementedException();

    public ulong EventId { get; }
    public PredictedCueKind Kind { get; }
    public bool IsValid => throw new NotImplementedException();
}

/// <summary>Events produced by one frame. Bounded; a frame cannot emit unboundedly.</summary>
#pragma warning disable CS0169, CS0649
public struct OwnerSimulationEventBuffer : IEquatable<OwnerSimulationEventBuffer>
{
    public const int Capacity = 8;

    private Storage _events;
    private int _count;

    [InlineArray(Capacity)]
    private struct Storage
    {
        private OwnerSimulationEvent _element0;
    }

    public int Count => _count;
    public OwnerSimulationEvent this[int index] => throw new NotImplementedException();
    public bool TryAdd(in OwnerSimulationEvent simulationEvent) =>
        throw new NotImplementedException();
    public void Clear() => throw new NotImplementedException();
    public bool Equals(OwnerSimulationEventBuffer other) => throw new NotImplementedException();
    public override bool Equals(object? obj) => throw new NotImplementedException();
    public override int GetHashCode() => throw new NotImplementedException();
    public static bool operator ==(OwnerSimulationEventBuffer l, OwnerSimulationEventBuffer r) =>
        throw new NotImplementedException();
    public static bool operator !=(OwnerSimulationEventBuffer l, OwnerSimulationEventBuffer r) =>
        throw new NotImplementedException();
}
#pragma warning restore CS0169, CS0649

/// <summary>
/// Everything the owner retained about one frame it simulated locally.
/// </summary>
/// <remarks>
/// <para>
/// Both the pre-state and the post-state are stored. Replay restores the
/// <em>pre</em>-state of the first mismatched frame and resimulates forward;
/// comparison happens against the <em>post</em>-state, because that is what
/// authority's answer describes. Storing only one would force every replay to
/// start a frame earlier than necessary, or make comparison impossible.
/// </para>
/// <para>
/// Command, applied transitions, and events are all retained because replay must
/// feed the frame exactly what the first run consumed and must offer the cue
/// ledger exactly what it offered before. Anything re-derived from current state
/// instead would reproduce a different frame and then report the difference as a
/// divergence.
/// </para>
/// </remarks>
public readonly record struct OwnerPredictedFrame
{
    public OwnerPredictedFrame(
        SimulationInstant frame,
        CharacterSimulationState preState,
        CharacterSimulationState postState,
        OwnerSimulationCommand command,
        AppliedTransitionBuffer appliedTransitions,
        OwnerSimulationEventBuffer events,
        CapsuleMotionOutcome motionOutcome,
        int slideIterations,
        bool profileExpansionBlocked,
        CanonicalMovementStateHash canonicalHash) => throw new NotImplementedException();

    public SimulationInstant Frame { get; }

    /// <summary>State this frame started from; the restore point for replay.</summary>
    public CharacterSimulationState PreState { get; }

    /// <summary>State this frame produced; what authority's answer is compared against.</summary>
    public CharacterSimulationState PostState { get; }

    /// <summary>The exact input this frame consumed, so replay feeds it the same one.</summary>
    public OwnerSimulationCommand Command { get; }

    /// <summary>The durable transitions that applied, so a remap cannot silently move an edge.</summary>
    public AppliedTransitionBuffer AppliedTransitions { get; }

    /// <summary>Cue identities this frame produced, so replay does not re-fire them.</summary>
    public OwnerSimulationEventBuffer Events { get; }

    /// <summary>
    /// What the motor did. Retained so a correction can be explained — a
    /// divergence on a frame that hit the slide iteration cap or refused a step
    /// has a different cause than one on a frame of free travel.
    /// </summary>
    public CapsuleMotionOutcome MotionOutcome { get; }

    public int SlideIterations { get; }
    public bool ProfileExpansionBlocked { get; }

    /// <summary>
    /// Diagnostic hash of the post-state, with its schema version. Never a
    /// correction trigger; <see cref="OwnerReconciliationPolicy"/> is
    /// structurally unable to read it.
    /// </summary>
    public CanonicalMovementStateHash CanonicalHash { get; }

    public bool IsValid => throw new NotImplementedException();

    /// <summary>Replaces the post-state after replay resimulated this frame.</summary>
    /// <remarks>
    /// Required because after a correction the stored post-state is the owner's
    /// wrong answer. Leaving it would make a duplicate authority state for the
    /// same frame mismatch a second time and correct again.
    /// </remarks>
    public OwnerPredictedFrame WithPostState(
        CharacterSimulationState postState,
        CanonicalMovementStateHash hash) => throw new NotImplementedException();
}

public enum OwnerHistoryInsertDecision : byte
{
    Inserted = 1,

    /// <summary>Older than the oldest retained frame; the window has moved past it.</summary>
    OlderThanRetention = 2,

    /// <summary>Not contiguous with the newest retained frame. History must stay gapless.</summary>
    NonContiguous = 3,
}

public enum OwnerHistoryLookupDecision : byte
{
    Found = 1,

    /// <summary>Older than retention. The owner cannot replay it and must rebase.</summary>
    OlderThanRetention = 2,

    /// <summary>Newer than anything simulated. Authority is ahead; the frame is queued.</summary>
    NotYetSimulated = 3,

    /// <summary>Nothing retained at all, as after a reset.</summary>
    Empty = 4,
}

/// <summary>
/// The owner's bounded record of frames it simulated, kept so an authority
/// answer about a past frame can be checked and, if wrong, replayed from.
/// </summary>
/// <remarks>
/// <para>
/// Preallocated and frame-indexed: a fixed ring sized to the retention window,
/// addressed by frame number rather than searched. Retention is what bounds
/// memory and also what defines the failure mode — an authority answer older
/// than the window cannot be replayed from and must become a hard rebase, which
/// is a visible correction. Sizing the window is therefore a gameplay decision,
/// not only a memory one.
/// </para>
/// <para>
/// History is gapless by construction. Every frame the owner simulates is
/// inserted contiguously; a gap would mean replay silently skipped a frame and
/// produced a state no simulation ever generated.
/// </para>
/// <para>
/// The history is bound to one epoch for its whole life and is reset wholesale
/// when the epoch changes, rather than storing an epoch per frame and filtering.
/// Frame numbers may be reused across a timeline reset, so a per-frame check
/// would have to compare identities the owner command cannot fully reconstruct;
/// binding the container is both simpler and impossible to get subtly wrong.
/// </para>
/// </remarks>
public sealed class OwnerPredictionHistory
{
    /// <summary>
    /// Frames retained. At 60 Hz this is the round-trip window a correction can
    /// still be replayed from rather than rebased.
    /// </summary>
    public const int DefaultCapacityFrames = 256;
    public const int MaximumCapacityFrames = 1024;

    public OwnerPredictionHistory(
        CombatantAuthorityPredictionEpoch epoch,
        int capacityFrames = DefaultCapacityFrames) => throw new NotImplementedException();

    public CombatantAuthorityPredictionEpoch Epoch => throw new NotImplementedException();
    public int CapacityFrames => throw new NotImplementedException();
    public int Count => throw new NotImplementedException();

    /// <summary>Oldest frame still replayable, or null when empty.</summary>
    public SimulationInstant? OldestFrame => throw new NotImplementedException();

    /// <summary>Newest frame simulated, or null when empty.</summary>
    public SimulationInstant? NewestFrame => throw new NotImplementedException();

    /// <summary>
    /// Records one simulated frame. Refuses rather than accepts a gap, because a
    /// gap in history is indistinguishable later from a frame that simulated
    /// differently.
    /// </summary>
    public OwnerHistoryInsertDecision TryInsert(in OwnerPredictedFrame frame) =>
        throw new NotImplementedException();

    public OwnerHistoryLookupDecision TryGet(
        SimulationInstant frame,
        out OwnerPredictedFrame predicted) => throw new NotImplementedException();

    /// <summary>
    /// Replaces a frame's post-state after replay resimulated it.
    /// </summary>
    /// <remarks>
    /// Without this the corrected frame keeps the owner's original wrong answer,
    /// and a duplicate authority state for that frame mismatches again and
    /// corrects again.
    /// </remarks>
    public bool TryReplacePostState(
        SimulationInstant frame,
        in CharacterSimulationState postState,
        CanonicalMovementStateHash hash) => throw new NotImplementedException();

    /// <summary>
    /// Drops every frame at or below <paramref name="throughFrame"/>.
    /// </summary>
    /// <remarks>
    /// Called when authority confirms a frame matched. Pruning on confirmation
    /// rather than only on overflow is what keeps the window available for the
    /// frames that might still need replaying.
    /// </remarks>
    public int PruneThrough(SimulationInstant throughFrame) =>
        throw new NotImplementedException();

    /// <summary>
    /// Discards frames after <paramref name="fromFrame"/> so replay can rewrite
    /// them.
    /// </summary>
    public int TruncateAfter(SimulationInstant fromFrame) =>
        throw new NotImplementedException();

    /// <summary>
    /// Clears everything and rebinds to a new epoch.
    /// </summary>
    /// <remarks>
    /// An epoch change invalidates every retained frame: frame numbers may be
    /// reused and the scope the commands belonged to is gone. Keeping any of it
    /// would let a frame from the old timeline be replayed into the new one.
    /// </remarks>
    public void Reset(CombatantAuthorityPredictionEpoch epoch) =>
        throw new NotImplementedException();
}
