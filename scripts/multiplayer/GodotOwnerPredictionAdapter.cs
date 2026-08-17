using BattleArena.Core.Common;
using BattleArena.Core.Movement.Simulation;
using BattleArena.Multiplayer.OwnerPrediction;
using BattleArena.Multiplayer.Presentation;
using Godot;

namespace BattleArena.GodotNetworking;

/// <summary>
/// Commits the owner's reconciled simulation to the engine exactly once per real
/// frame.
/// </summary>
/// <remarks>
/// <para>
/// This is the boundary that makes replay invisible. A correction may resimulate
/// thirty frames, and every one of those produces a state — but the player must
/// see one pose, hear one set of cues, and observe one camera update, all
/// describing the final frame. Committing per replayed frame would turn every
/// correction into a visible stutter and an audible burst proportional to how
/// far back it reached.
/// </para>
/// <para>
/// The commit-once property is already structural rather than enforced here:
/// <see cref="LocalMovementPredictionController"/> holds no reference to this
/// adapter, so replay physically cannot commit. The counters below exist to
/// prove that in a gate, not to create the guarantee.
/// </para>
/// <para>
/// Presentation goes through <see cref="CharacterPresentationController"/>
/// rather than being reimplemented: that type already refuses a
/// historical-replay pass structurally, and Phase 7 builds correction-debt
/// smoothing on it. Body writes go through the avatar's fixed-physics commit
/// guard for the same reason — Phase 2 established that gameplay blocking bodies
/// are written only at physics boundaries, and V2 does not get an exception.
/// </para>
/// <para>
/// Cue deduplication is <see cref="PredictedCueLedger"/>'s job. Replay offers it
/// the same event identities the first run did, so a jump that was predicted,
/// replayed nine times, and then confirmed still fires one sound.
/// </para>
/// </remarks>
public sealed partial class GodotOwnerPredictionAdapter : Node
{
    /// <summary>
    /// Binds the adapter to the avatar it commits to and the presentation and cue
    /// seams it publishes through.
    /// </summary>
    public void Bind(
        NetworkAvatar avatar,
        CharacterPresentationController presentation,
        PredictedCueLedger cueLedger) => throw new NotImplementedException();

    /// <summary>
    /// Opens one engine frame, resetting the per-frame commit counters.
    /// </summary>
    /// <remarks>
    /// The gate asserts exactly one body commit and one presentation sample
    /// <em>per engine frame</em>, which a monotone lifetime counter cannot
    /// express. This is the frame boundary those counts are measured against.
    /// </remarks>
    public void BeginEngineFrame(SimulationInstant frame) => throw new NotImplementedException();

    /// <summary>
    /// Commits one reconciled frame: one collision pose, one presentation sample,
    /// and any cues the ledger has not already retired.
    /// </summary>
    /// <param name="result">
    /// What reconciliation did this frame, so presentation can distinguish an
    /// ordinary correction from one it must not smooth.
    /// </param>
    public void CommitFrame(
        in CharacterSimulationState state,
        in OwnerReconciliationResult result,
        double deltaSeconds) => throw new NotImplementedException();

    /// <summary>
    /// Whether a correction of this kind may be visually smoothed.
    /// </summary>
    /// <remarks>
    /// Ordinary numeric replay may be. Contact replay and hard rebase may not:
    /// smoothing a contact correction renders the character inside geometry the
    /// authority says it is outside of, and smoothing a rebase hides a timeline
    /// discontinuity the player needs to see resolve immediately.
    /// </remarks>
    public static bool MaySmooth(in OwnerReconciliationResult result) =>
        throw new NotImplementedException();

    /// <summary>Body commits performed in the current engine frame. Must be one.</summary>
    public int BodyCommitsThisFrame => throw new NotImplementedException();

    /// <summary>Presentation samples published in the current engine frame. Must be one.</summary>
    public int PresentationSamplesThisFrame => throw new NotImplementedException();

    /// <summary>Lifetime totals, for the soak gates.</summary>
    public long TotalBodyCommits => throw new NotImplementedException();
    public long TotalPresentationSamples => throw new NotImplementedException();
}
