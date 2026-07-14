using BattleArena.Core.Common;
using System.Collections.ObjectModel;

namespace BattleArena.Core.Influences;

public sealed class ActiveEffectInfluenceCatalog
{
    private readonly ReadOnlyDictionary<ActiveEffectInfluenceDefinitionId, ActiveEffectInfluenceDefinition> _definitions;

    public ActiveEffectInfluenceCatalog(IEnumerable<ActiveEffectInfluenceDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var materialized = definitions.ToArray();
        if (materialized.Any(static definition => definition is null))
        {
            throw new ArgumentException("Influence definitions cannot contain null.", nameof(definitions));
        }

        var duplicateId = materialized
            .GroupBy(static definition => definition.Id)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateId is not null)
        {
            throw new ArgumentException(
                $"Influence definition ID {duplicateId.Key} appears more than once.",
                nameof(definitions));
        }

        _definitions = new ReadOnlyDictionary<ActiveEffectInfluenceDefinitionId, ActiveEffectInfluenceDefinition>(
            materialized.ToDictionary(static definition => definition.Id));
    }

    public bool TryGet(
        ActiveEffectInfluenceDefinitionId id,
        out ActiveEffectInfluenceDefinition? definition) =>
        _definitions.TryGetValue(id, out definition);
}
