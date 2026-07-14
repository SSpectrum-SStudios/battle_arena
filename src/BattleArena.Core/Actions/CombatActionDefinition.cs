using BattleArena.Core.Common;
using System.Collections.ObjectModel;

namespace BattleArena.Core.Actions;

public sealed class CombatActionDefinition
{
    private readonly ReadOnlyCollection<CombatActionEffectDefinition> _effects;

    public CombatActionDefinition(
        CombatActionDefinitionId id,
        TargetHitPolicy hitPolicy,
        IEnumerable<CombatActionEffectDefinition> effects)
    {
        ArgumentNullException.ThrowIfNull(effects);
        var materialized = effects.ToArray();
        if (materialized.Length == 0 || materialized.Any(static effect => effect is null))
        {
            throw new ArgumentException(
                "A combat action must contain at least one non-null effect.",
                nameof(effects));
        }

        Id = id;
        HitPolicy = hitPolicy ?? throw new ArgumentNullException(nameof(hitPolicy));
        _effects = Array.AsReadOnly(materialized);
    }

    public CombatActionDefinitionId Id { get; }

    public TargetHitPolicy HitPolicy { get; }

    public IReadOnlyList<CombatActionEffectDefinition> Effects => _effects;
}
