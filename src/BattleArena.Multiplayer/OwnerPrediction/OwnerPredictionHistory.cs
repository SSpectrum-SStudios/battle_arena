using BattleArena.Core.Common;
using BattleArena.Core.Movement.Simulation;

namespace BattleArena.Multiplayer.OwnerPrediction;

/// <summary>
/// Everything the owner retained about one frame it simulated locally.
/// </summary>
/// <remarks>
/// <para>
/// Both the pre-state and the post-state are stored. Replay restores the
/// <em>pre</em>-state of the first mismatched frame and resimulates forward;
/// comparison happens against the <em>post</em>-state, because that is what
/// authority's answer describes. Storing only one of the two would force every
/// replay to start a frame earlier than necessary or make comparison impossible.
/// </para>
/// <para>
/// The command is retained because replay must feed the frame the same input it
/// originally received. Re-deriving input from current player state would
/// reproduce a different frame and call it a match.
/// </para>
/// </remarks>
public readonly record struct OwnerPredictedFrame
{
    public OwnerPredictedFrame(
        SimulationInstant frame,
        CharacterSimulationState preState,
        CharacterSimulationState postState,
        OwnerSimulationCommand command,
        CapsuleMotionOutcome motionOutcome,
        ulong canonicalHash) => throw new NotImplementedException();

    public SimulationInstant Frame { get; }

    /// <summary>State this frame started from; the restore point for replay.</summary>
    public CharacterSimulationState PreState { get; }

    /// <summary>State this frame produced; what authority's answer is compared against.</summary>
    public CharacterSimulationState PostState { get; }

    /// <summary>The exact input this frame consumed, so replay feeds it the same one.</summary>
    public OwnerSimulationCommand Command { get; }

    /// <summary>
    /// What the motor did. Retained so a correction can be explained — a
    /// divergence on a frame that hit the slide iteration cap or refused a step
    /// has a different cause than one on a frame of free travel.
    /// </summary>
    public CapsuleMotionOutcome MotionOutcome { get; }

    /// <summary>
    /// Diagnostic hash of the post-state. Never a correction trigger on its own;
    /// see <see cref="OwnerReconciliationPolicy"/>.
    /// </summary>
    public ulong CanonicalHash { get; }

    public bool IsValid => throw new NotImplementedException();
}

public enum OwnerHistoryInsertDecision : byte
{
    Inserted = 1,

    /// <summary>The frame is older than the oldest retained frame; it is gone.</summary>
    TooOld = 2,

    /// <summary>Not contiguous with the newest retained frame. History must stay gapless.</summary>
    NonContiguous = 3,

    /// <summary>The frame belongs to a different epoch and cannot join this history.</summary>
    WrongEpoch = 4,
}

public enum OwnerHistoryLookupDecision : byte
{
    Found = 1,

    /// <summary>Older than retention. The owner cannot replay it and must rebase.</summary>
    Evicted = 2,

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
/// not just a memory one.
/// </para>
/// <para>
/// History is gapless by construction. Every frame the owner simulates is
/// inserted, contiguously; a gap would mean replay silently skipped a frame and
/// produced a state no simulation ever generated.
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
    /// <remarks>
    /// Replay does not edit history in place: the frames after the mismatch are
    /// dropped and reinserted as they are resimulated. Editing in place would
    /// leave a frame's stored pre-state disagreeing with the previous frame's
    /// post-state if replay diverged.
    /// </remarks>
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
