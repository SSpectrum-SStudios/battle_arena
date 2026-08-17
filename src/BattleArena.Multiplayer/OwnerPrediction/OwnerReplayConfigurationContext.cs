using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Multiplayer.Replication;

namespace BattleArena.Multiplayer.OwnerPrediction;

/// <summary>
/// P06-05: resolves the authored tuning effective on a replayed frame.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why replay cannot just use "current" configuration.</b> Attributes are
/// revisioned content that can change mid-match. A frame simulated under revision
/// N and replayed under N+1 produces a different result for reasons that have
/// nothing to do with the owner being wrong — and reconciliation would report that
/// as a divergence, correct the player, and then do it again on the next replay.
/// The frame must be replayed with what was in force <em>on that frame</em>.
/// </para>
/// <para>
/// Adapts the existing <see cref="IMovementConfigurationTimeline"/> rather than
/// keeping a second store. Two timelines for one fact would be two things to keep
/// in step, and the one that drifted would be the one nobody was watching.
/// </para>
/// <para>
/// The revisions come from the retained frame's own state, not from a "latest
/// known" pointer. That is the whole mechanism: <see cref="CharacterSimulationState"/>
/// carries the revision it simulated under, so a replayed frame can ask for
/// exactly that.
/// </para>
/// </remarks>
public sealed class OwnerReplayConfigurationContext : IOwnerReplayContext
{
    private readonly IMovementConfigurationTimeline _timeline;
    private readonly Func<SimulationInstant, (ulong Movement, ulong Capability)> _revisionsForFrame;
    private readonly ulong _combatantId;
    private readonly ulong _lifeId;

    /// <param name="revisionsForFrame">
    /// Supplies the revisions a retained frame simulated under. A delegate rather
    /// than a direct history reference so this stays testable without building a
    /// history, and so the context cannot reach into history for anything else.
    /// </param>
    public OwnerReplayConfigurationContext(
        IMovementConfigurationTimeline timeline,
        CombatantAuthorityPredictionEpoch epoch,
        Func<SimulationInstant, (ulong Movement, ulong Capability)> revisionsForFrame)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(revisionsForFrame);
        if (!epoch.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(epoch));
        }

        _timeline = timeline;
        _revisionsForFrame = revisionsForFrame;
        _combatantId = checked((ulong)epoch.CombatantId.Value);
        _lifeId = checked((ulong)epoch.Life.Value);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns false rather than falling back to the newest configuration. A
    /// silent fallback is the failure this type exists to prevent: it would replay
    /// the frame under the wrong tuning and present the result as the owner's
    /// error. The caller queues a baseline and only treats repeated failure as
    /// terminal, which is
    /// <see cref="OwnerCorrectionReason.ConfigurationHistoryPolicyExhausted"/>.
    /// </remarks>
    public bool TryGetConfiguration(
        SimulationInstant frame,
        out MovementAttributeSnapshot attributes,
        out MovementCapabilitySnapshot capabilities)
    {
        attributes = null!;
        capabilities = null!;

        var (movementRevision, capabilityRevision) = _revisionsForFrame(frame);
        if (movementRevision == 0 || capabilityRevision == 0)
        {
            return false;
        }

        if (!_timeline.TryResolve(
                _combatantId,
                _lifeId,
                movementRevision,
                capabilityRevision,
                checked((ulong)Math.Max(0L, frame.Tick)),
                out var configuration))
        {
            return false;
        }

        attributes = configuration.Attributes;
        capabilities = configuration.Capabilities;
        return true;
    }
}
