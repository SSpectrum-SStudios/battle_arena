using BattleArena.Core.Common;
using System.Collections.ObjectModel;

namespace BattleArena.Core.Effects;

public sealed class ActiveEffectContainer
{
    private readonly Dictionary<ActiveEffectId, ActiveEffectInstance> _effects = [];

    public ActiveEffectContainer(
        CombatantId ownerCombatantId,
        LifeGenerationId currentLifeGenerationId)
    {
        OwnerCombatantId = ownerCombatantId;
        CurrentLifeGenerationId = currentLifeGenerationId;
    }

    public CombatantId OwnerCombatantId { get; }

    public LifeGenerationId CurrentLifeGenerationId { get; private set; }

    public IReadOnlyCollection<ActiveEffectInstance> Effects =>
        new ReadOnlyCollection<ActiveEffectInstance>(_effects.Values.ToArray());

    public void Add(ActiveEffectInstance effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        if (effect.TargetCombatantId != OwnerCombatantId)
        {
            throw new ArgumentException(
                "The active effect targets a different combatant.",
                nameof(effect));
        }

        if (!_effects.TryAdd(effect.Id, effect))
        {
            throw new ArgumentException(
                $"Active effect ID {effect.Id} is already installed.",
                nameof(effect));
        }
    }

    public bool TryGet(ActiveEffectId id, out ActiveEffectInstance? effect) =>
        _effects.TryGetValue(id, out effect);

    public bool Remove(ActiveEffectId id) => _effects.Remove(id);

    public IReadOnlyList<ActiveEffectId> EndCurrentLife()
    {
        var removedIds = _effects.Values
            .Where(static effect => effect.LifetimeScope == EffectLifetimeScope.PerLife)
            .Select(static effect => effect.Id)
            .OrderBy(static id => id.Value)
            .ToArray();

        foreach (var id in removedIds)
        {
            _effects.Remove(id);
        }

        return Array.AsReadOnly(removedIds);
    }

    public IReadOnlyList<ActiveEffectId> BeginNewLife(LifeGenerationId newLifeGenerationId)
    {
        if (newLifeGenerationId.CompareTo(CurrentLifeGenerationId) <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(newLifeGenerationId),
                "A new life generation must be greater than the current generation.");
        }

        var removedIds = EndCurrentLife();

        CurrentLifeGenerationId = newLifeGenerationId;
        return removedIds;
    }
}
