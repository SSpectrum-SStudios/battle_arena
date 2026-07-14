using BattleArena.Core.Common;

namespace BattleArena.Core.Effects;

public sealed class PeriodicDamageEffectFactory
{
    public PeriodicDamageEffectInstance Create(
        ActiveEffectId id,
        PeriodicDamageEffectDefinition definition,
        CombatantId sourceCombatantId,
        CombatantId targetCombatantId,
        LifeGenerationId sourceLifeGenerationId,
        SimulationInstant appliedAt)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var schedule = new PeriodicEffectSchedule(
            appliedAt,
            definition.Interval,
            definition.FirstTickPolicy,
            definition.CompletionPolicy);

        return new PeriodicDamageEffectInstance(
            id,
            definition,
            sourceCombatantId,
            targetCombatantId,
            sourceLifeGenerationId,
            schedule);
    }
}
