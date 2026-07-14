using BattleArena.Core.Common;
using System.Collections.ObjectModel;

namespace BattleArena.Core.Combat;

public sealed class CombatantRespawnResult
{
    private readonly ReadOnlyCollection<ActiveEffectId> _removedEffectIds;

    public CombatantRespawnResult(
        HealthSnapshot before,
        HealthSnapshot after,
        IEnumerable<ActiveEffectId> removedEffectIds)
    {
        Before = before ?? throw new ArgumentNullException(nameof(before));
        After = after ?? throw new ArgumentNullException(nameof(after));
        ArgumentNullException.ThrowIfNull(removedEffectIds);
        _removedEffectIds = Array.AsReadOnly(removedEffectIds.ToArray());
    }

    public HealthSnapshot Before { get; }

    public HealthSnapshot After { get; }

    public IReadOnlyList<ActiveEffectId> RemovedEffectIds => _removedEffectIds;
}
