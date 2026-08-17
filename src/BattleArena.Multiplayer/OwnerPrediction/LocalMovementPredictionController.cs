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
    /// must never be serialized or presented as authority state. Sourced from the
    /// injected <see cref="CombatantPredictionEpochGate"/> rather than minted
    /// here, so one rebase means one thing across the client.
    /// </summary>
    public LocalPredictionRebaseId? RebaseId { get; }
}

/// <summary>
/// Supplies the authored configuration one replayed frame needs.
/// </summary>
/// <remarks>
/// <para>
/// A seam rather than a direct dependency because the controller must run in
/// unit tests with no engine and no configuration service. This is where the
/// tick-effective lookup lands: a replayed frame must be simulated under the
/// revision in force <em>on that frame</em>, not the current one, or replay
/// silently rewrites history whenever configuration changed.
/// </para>
/// <para>
/// Applied transitions are deliberately <em>not</em> here. They are history, not
/// context: the set that applied on a frame is a fact recorded when the frame was
/// first simulated, and authority can remap a transition to a later frame after
/// the fact. Re-deriving them at replay time is how first run and replay come to
/// disagree, so they live in <see cref="OwnerPredictedFrame"/>.
/// </para>
/// </remarks>
public interface IOwnerReplayContext
{
    /// <summary>The authored tuning effective on a specific frame.</summary>
    /// <returns>
    /// False when no revision is resolvable yet. The caller queues a baseline and
    /// only treats it as terminal once the bounded policy is exhausted, which is
    /// <see cref="OwnerCorrectionReason.ConfigurationHistoryPolicyExhausted"/>.
    /// </returns>
    bool TryGetConfiguration(
        SimulationInstant frame,
        out MovementAttributeSnapshot attributes,
        out MovementCapabilitySnapshot capabilities);
}

/// <summary>Bounds on how much work one authority answer may cause.</summary>
/// <remarks>
/// Retention decides how far back a correction can reach; this decides how much
/// of that reach may be spent in a single engine frame. They are separate
/// numbers because one malformed or extremely late packet must not be able to
/// convert the whole retention window into one frame's work — at the measured
/// per-frame query cost, replaying the full window would blow a 16 ms budget
/// many times over.
/// </remarks>
public readonly record struct OwnerPredictionWorkPolicy
{
    /// <summary>Most frames that may be resimulated for one authority answer.</summary>
    public int MaximumReplayFrames { get; init; }

    /// <summary>
    /// Most authority states that may be held for frames not yet simulated.
    /// Beyond this the oldest is dropped, because an unbounded queue is a memory
    /// vector and the newest answer is the one that matters.
    /// </summary>
    public int MaximumQueuedFutureStates { get; init; }

    public bool IsValid => throw new NotImplementedException();
    public static OwnerPredictionWorkPolicy Default => throw new NotImplementedException();
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
/// nodes, and publishes no presentation. That is structural rather than a rule to
/// remember: this controller holds no reference to the Godot adapter, so replay
/// physically cannot commit. Presentation happens once afterwards, through the
/// commit-once adapter.
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
        CombatantPredictionEpochGate epochGate,
        CharacterMovementSimulator simulator,
        OwnerPredictionHistory history,
        OwnerReconciliationComparer comparer,
        OwnerReconciliationPolicy policy,
        IOwnerReplayContext replayContext,
        PredictedCueLedger cueLedger,
        SimulationRate rate,
        OwnerPredictionWorkPolicy workPolicy) => throw new NotImplementedException();

    /// <summary>
    /// The epoch gate, which owns lifecycle identity and the local rebase
    /// counter. Injected rather than duplicated: three gates per client is how
    /// one rebase comes to mean three different things.
    /// </summary>
    public CombatantPredictionEpochGate EpochGate => throw new NotImplementedException();

    /// <summary>The owner's current predicted state, ahead of authority by the lead.</summary>
    public CharacterSimulationState Current => throw new NotImplementedException();

    /// <summary>
    /// The newest frame authority has answered for and the owner has reconciled.
    /// </summary>
    public SimulationInstant? ReconciledThroughFrame => throw new NotImplementedException();

    /// <summary>Authority states received for frames not yet simulated locally.</summary>
    public int QueuedFutureCount => throw new NotImplementedException();

    /// <summary>
    /// Correction telemetry accumulated for this combatant.
    /// </summary>
    /// <remarks>
    /// The Phase 1 taxonomy, recorded here rather than reinvented. Replay depth
    /// is derived by telemetry from the frames involved, so it has one source
    /// rather than being reported independently and disagreeing.
    /// </remarks>
    public OwnerCorrectionTelemetry Telemetry => throw new NotImplementedException();

    /// <summary>
    /// Simulates one new predicted frame from local input and records it.
    /// </summary>
    /// <remarks>
    /// The owner runs ahead of authority by the prediction lead, so this is the
    /// frame the player is actually seeing the results of. The applied
    /// transitions are recorded into history here, which is what makes replay
    /// able to reproduce the same edges later.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The context is a historical-replay pass. Predicting a new frame is always
    /// a first run; replay goes through <see cref="ReplayFrom"/>.
    /// </exception>
    public CharacterFrameResult PredictFrame(
        in CharacterSimulationInput input,
        ReadOnlySpan<MovementTransitionKindTag> appliedTransitions,
        in OwnerSimulationCommand command,
        in SimulationStepContext context) => throw new NotImplementedException();

    /// <summary>
    /// Applies one authority answer: compare, then confirm, replay, or rebase.
    /// </summary>
    /// <param name="epoch">
    /// The epoch the answer belongs to, so a lifecycle change is detected here
    /// rather than being invisible. Each of the four lifecycle identities yields
    /// its own reason; collapsing them into one would lose the diagnosis.
    /// </param>
    /// <remarks>
    /// The frame is taken from <paramref name="authoritative"/> rather than
    /// passed separately, so the two can never disagree.
    /// </remarks>
    public OwnerReconciliationResult ApplyAuthorityState(
        in CharacterSimulationState authoritative,
        in CombatantAuthorityPredictionEpoch epoch,
        CanonicalMovementStateHash? authoritativeHash) => throw new NotImplementedException();

    /// <summary>
    /// Snaps to an authoritative state and restarts prediction from it.
    /// </summary>
    /// <remarks>
    /// Clears history, queued futures, and pending replay together, atomically. A
    /// partial clear would leave a frame from the discarded timeline able to
    /// reconcile against the new one. The rebase identity comes from the injected
    /// epoch gate, which advances it exactly once per applied reset.
    /// </remarks>
    public OwnerReconciliationResult HardRebase(
        in CharacterSimulationState authoritative,
        OwnerCorrectionReason reason) => throw new NotImplementedException();

    /// <summary>
    /// Rebuilds for a new epoch, discarding everything retained.
    /// </summary>
    public OwnerReconciliationResult ResetForEpoch(
        in CombatantAuthorityPredictionEpoch epoch,
        in CharacterSimulationState baseline) => throw new NotImplementedException();

    /// <summary>
    /// Replays from a restored frame forward to the newest predicted frame.
    /// </summary>
    /// <remarks>
    /// Each replayed frame is resimulated with the input and applied transitions
    /// it originally consumed, under the configuration revision in force on it,
    /// then its post-state is replaced in history. History is truncated and
    /// rewritten rather than edited ad hoc, so a stored frame can never disagree
    /// with its own predecessor.
    /// </remarks>
    /// <returns>
    /// Frames resimulated, or -1 when the span exceeded
    /// <see cref="OwnerPredictionWorkPolicy.MaximumReplayFrames"/> and the caller
    /// must rebase instead.
    /// </returns>
    internal int ReplayFrom(
        SimulationInstant restoredFrame,
        in CharacterSimulationState restoredState) => throw new NotImplementedException();
}
