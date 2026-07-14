using BattleArena.Core.Common;
using System.Collections.ObjectModel;

namespace BattleArena.Core.Influences;

public sealed class ActiveEffectInfluenceDefinition
{
    private readonly ReadOnlyCollection<PeriodicDamageModifierDefinition> _modifiers;

    public ActiveEffectInfluenceDefinition(
        ActiveEffectInfluenceDefinitionId id,
        CombatantRelationshipFilter targetFilter,
        CombatantRelationshipFilter effectSourceFilter,
        EffectTagSpecification effectTags,
        IEnumerable<PeriodicDamageModifierDefinition> modifiers,
        bool requiresActiveSourceLife = true)
    {
        targetFilter.Validate(nameof(targetFilter));
        effectSourceFilter.Validate(nameof(effectSourceFilter));
        ArgumentNullException.ThrowIfNull(modifiers);

        var materialized = modifiers.ToArray();
        if (materialized.Length == 0 || materialized.Any(static modifier => modifier is null))
        {
            throw new ArgumentException(
                "An active-effect influence must contain at least one non-null modifier.",
                nameof(modifiers));
        }

        Id = id;
        TargetFilter = targetFilter;
        EffectSourceFilter = effectSourceFilter;
        EffectTags = effectTags ?? throw new ArgumentNullException(nameof(effectTags));
        _modifiers = Array.AsReadOnly(materialized);
        RequiresActiveSourceLife = requiresActiveSourceLife;
    }

    public ActiveEffectInfluenceDefinitionId Id { get; }

    public CombatantRelationshipFilter TargetFilter { get; }

    public CombatantRelationshipFilter EffectSourceFilter { get; }

    public EffectTagSpecification EffectTags { get; }

    public IReadOnlyList<PeriodicDamageModifierDefinition> Modifiers => _modifiers;

    public bool RequiresActiveSourceLife { get; }
}
