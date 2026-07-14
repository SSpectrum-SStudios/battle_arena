using BattleArena.Core.Common;
using System.Collections.ObjectModel;

namespace BattleArena.Core.Actions;

public sealed class CombatActionCatalog
{
    private readonly ReadOnlyDictionary<CombatActionDefinitionId, CombatActionDefinition> _definitions;

    public CombatActionCatalog(IEnumerable<CombatActionDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var materialized = definitions.ToArray();
        if (materialized.Any(static definition => definition is null))
        {
            throw new ArgumentException("Combat-action definitions cannot contain null.", nameof(definitions));
        }

        var duplicateId = materialized
            .GroupBy(static definition => definition.Id)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateId is not null)
        {
            throw new ArgumentException(
                $"Combat-action definition ID {duplicateId.Key} appears more than once.",
                nameof(definitions));
        }

        _definitions = new ReadOnlyDictionary<CombatActionDefinitionId, CombatActionDefinition>(
            materialized.ToDictionary(static definition => definition.Id));
    }

    public IReadOnlyCollection<CombatActionDefinition> Definitions => _definitions.Values;

    public bool TryGet(
        CombatActionDefinitionId id,
        out CombatActionDefinition? definition) =>
        _definitions.TryGetValue(id, out definition);
}
