using BattleArena.Core.Common;
using BattleArena.Core.Movement.Simulation;
using BattleArena.Multiplayer.OwnerPrediction;
using Godot;

namespace BattleArena.GodotNetworking;

/// <summary>
/// Commits the owner's reconciled simulation to the engine exactly once per real
/// frame.
/// </summary>
/// <remarks>
/// <para>
/// This is the boundary that makes replay invisible. A correction may resimulate
/// thirty frames, and every one of those frames produces a state — but the
/// player must see one pose, hear one set of cues, and observe one camera
/// update, all describing the final frame. Committing per replayed frame would
/// turn every correction into a visible stutter and an audible burst
/// proportional to how far back it reached.
/// </para>
/// <para>
/// Phase 2 already established this rule for the legacy path: historical replay
/// runs state-only inside a frame transaction and a single outer authorization
/// publishes afterwards. This is the same rule for the V2 path, and it is the
/// only place V2 is allowed to touch a node at all.
/// </para>
/// <para>
/// The adapter also decides how a correction is presented, which is not the same
/// as whether it is applied. An ordinary replay may be smoothed toward its
/// result; a contact replay and a hard rebase may not, because both mean the
/// simulation disagreed about collision or the timeline, and smoothing those
/// would render the character somewhere the world says it cannot be.
/// </para>
/// </remarks>
public sealed partial class GodotOwnerPredictionAdapter : Node
{
    /// <summary>
    /// Binds the adapter to the avatar it commits to.
    /// </summary>
    public void Bind(NetworkAvatar avatar) => throw new NotImplementedException();

    /// <summary>
    /// Commits one reconciled frame: one collision pose, one presentation sample.
    /// </summary>
    /// <param name="result">
    /// What reconciliation did this frame, so presentation can distinguish an
    /// ordinary correction from one it must not smooth.
    /// </param>
    /// <remarks>
    /// Called once per engine frame regardless of how many simulation frames were
    /// replayed to produce <paramref name="state"/>.
    /// </remarks>
    public void CommitFrame(
        in CharacterSimulationState state,
        in OwnerReconciliationResult result) => throw new NotImplementedException();

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

    /// <summary>
    /// Number of body commits performed. Diagnostics for the replay gate, which
    /// asserts this is exactly one per engine frame regardless of replay depth.
    /// </summary>
    public long BodyCommitCount => throw new NotImplementedException();

    /// <summary>
    /// Number of presentation samples published, asserted the same way.
    /// </summary>
    public long PresentationSampleCount => throw new NotImplementedException();
}
