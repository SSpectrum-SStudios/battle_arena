using BattleArena.Core.Common;

namespace BattleArena.Core.Effects;

public sealed class PeriodicDamageEffectInstance : ActiveEffectInstance
{
    internal PeriodicDamageEffectInstance(
        ActiveEffectId id,
        PeriodicDamageEffectDefinition definition,
        CombatantId sourceCombatantId,
        CombatantId targetCombatantId,
        LifeGenerationId sourceLifeGenerationId,
        PeriodicEffectSchedule schedule)
        : base(
            id,
            definition?.Id ?? throw new ArgumentNullException(nameof(definition)),
            sourceCombatantId,
            targetCombatantId,
            sourceLifeGenerationId,
            definition.LifetimeScope,
            schedule)
    {
        Definition = definition;
    }

    public PeriodicDamageEffectDefinition Definition { get; }
}
