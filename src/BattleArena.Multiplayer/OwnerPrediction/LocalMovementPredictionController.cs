using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Core.Movement.Simulation;

namespace BattleArena.Multiplayer.OwnerPrediction;

/// <summary>
/// What one authority answer did to the owner's prediction.
/// </summary>
public readonly record struct OwnerReconciliationResult
{
    internal OwnerReconciliationResult(
        OwnerCorrectionDecision decision,
        int framesReplayed,
        SimulationInstant? newestFrame,
        LocalPredictionRebaseId? rebaseId) => throw new NotImplementedException();

    public OwnerCorrectionDecision Decision { get; }

    /// <summary>
    /// Frames resimulated. Zero on confirmation, which is the common case and the
    /// one worth keeping cheap.
    /// </summary>
    public int FramesReplayed { get; }

    public SimulationInstant? NewestFrame { get; }

    /// <summary>
    /// The local repair identity, when this was a hard rebase. Client-only: it
    /// must never be serialized or presented as authority state.
    /// </summary>
    public LocalPredictionRebaseId? RebaseId { get; }
}

/// <summary>
/// Supplies the pieces one replayed frame needs that the controller does not own.
/// </summary>
/// <remarks>
/// A seam rather than a direct dependency because the controller must run in
/// unit tests with no engine and no configuration service. It is also where the
/// tick-effective configuration lookup lands: a replayed frame must be simulated
/// under the revision in force <em>on that frame</em>, not the current one, or
/// replay silently rewrites history whenever configuration changed.
/// </remarks>
public interface IOwnerReplayContext
{
    /// <summary>The authored tuning effective on a specific frame.</summary>
    bool TryGetConfiguration(
        SimulationInstant frame,
        out MovementAttributeSnapshot attributes,
        out MovementCapabilitySnapshot capabilities);

    /// <summary>The durable transitions that applied on a specific frame.</summary>
    int GetAppliedTransitions(
        SimulationInstant frame,
        Span<MovementTransitionKindTag> destination);
}

/// <summary>
/// The owner's local prediction loop: simulate ahead, compare against authority
/// when it answers, and replay only what must be replayed.
/// </summary>
/// <remarks>
/// <para>
/// This is where Phases 4 and 5 finally meet. Phase 4 makes the authority answer
/// exactly which input it applied on which frame; Phase 5 makes a frame
/// reproducible from stored state. Neither is useful alone — together they let
/// the owner check its own work and correct only where it was actually wrong.
/// </para>
/// <para>
/// Replay is simulation only. It advances no cues, plays no sounds, moves no
/// nodes, and publishes no presentation; those happen once, after the final
/// frame, through the commit-once adapter. A replay that touched presentation
/// would make every correction audible and visible as a stutter proportional to
/// its depth.
/// </para>
/// <para>
/// Ordering rule: authority states are applied in frame order and never regress.
/// A state for a frame already reconciled is idempotent; a state for a frame not
/// yet simulated is queued rather than applied, because there is nothing to
/// compare it against and discarding it would lose a confirmation.
/// </para>
/// </remarks>
public sealed class LocalMovementPredictionController
{
    public LocalMovementPredictionController(
        CombatantAuthorityPredictionEpoch epoch,
        CharacterMovementSimulator simulator,
        OwnerPredictionHistory history,
        OwnerReconciliationPolicy policy,
        IOwnerReplayContext replayContext) => throw new NotImplementedException();

    public CombatantAuthorityPredictionEpoch Epoch => throw new NotImplementedException();

    /// <summary>The owner's current predicted state, ahead of authority by the lead.</summary>
    public CharacterSimulationState Current => throw new NotImplementedException();

    /// <summary>
    /// The newest frame authority has answered for and the owner has reconciled.
    /// </summary>
    public SimulationInstant? ReconciledThroughFrame => throw new NotImplementedException();

    /// <summary>Authority states received for frames not yet simulated locally.</summary>
    public int QueuedFutureCount => throw new NotImplementedException();

    /// <summary>
    /// Simulates one new predicted frame from local input and records it.
    /// </summary>
    /// <remarks>
    /// The owner runs ahead of authority by the prediction lead, so this is the
    /// frame the player is actually seeing the results of.
    /// </remarks>
    public CharacterFrameResult PredictFrame(
        in CharacterSimulationInput input,
        ReadOnlySpan<MovementTransitionKindTag> appliedTransitions,
        in OwnerSimulationCommand command,
        in SimulationStepContext context) => throw new NotImplementedException();

    /// <summary>
    /// Applies one authority answer: compare, then confirm, replay, or rebase.
    /// </summary>
    /// <remarks>
    /// The whole point is that confirmation is cheap and common. A correction
    /// only happens when the comparer named a field that actually mattered.
    /// </remarks>
    public OwnerReconciliationResult ApplyAuthorityState(
        in CharacterSimulationState authoritative,
        SimulationInstant frame) => throw new NotImplementedException();

    /// <summary>
    /// Snaps to an authoritative state and restarts prediction from it.
    /// </summary>
    /// <remarks>
    /// Clears history, queued futures, and pending replay together, atomically.
    /// A partial clear would leave a frame from the discarded timeline able to
    /// reconcile against the new one. Advances the client-only rebase identity
    /// exactly once so presentation can tell a rebase from an ordinary
    /// correction and refuse to smooth it.
    /// </remarks>
    public OwnerReconciliationResult HardRebase(
        in CharacterSimulationState authoritative,
        SimulationInstant frame,
        OwnerRebaseReason reason) => throw new NotImplementedException();

    /// <summary>
    /// Rebuilds for a new epoch, discarding everything retained.
    /// </summary>
    public void ResetForEpoch(
        CombatantAuthorityPredictionEpoch epoch,
        in CharacterSimulationState baseline,
        SimulationInstant frame) => throw new NotImplementedException();

    /// <summary>
    /// Replays from a restored frame forward to the newest predicted frame.
    /// </summary>
    /// <remarks>
    /// Each replayed frame is resimulated with the input it originally consumed
    /// and the configuration revision in force on it, then reinserted. History
    /// is truncated first rather than overwritten in place, so a stored frame
    /// can never disagree with its own predecessor.
    /// </remarks>
    internal int ReplayFrom(
        SimulationInstant restoredFrame,
        in CharacterSimulationState restoredState) => throw new NotImplementedException();
}
