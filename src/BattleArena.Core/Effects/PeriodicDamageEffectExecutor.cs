using BattleArena.Core.Combat;
using BattleArena.Core.Common;

namespace BattleArena.Core.Effects;

public sealed class PeriodicDamageEffectExecutor
{
    private readonly CombatResolver _combatResolver;

    public PeriodicDamageEffectExecutor(CombatResolver combatResolver)
    {
        _combatResolver = combatResolver ?? throw new ArgumentNullException(nameof(combatResolver));
    }

    public PeriodicDamageTickExecutionResult ExecuteDue(
        PeriodicDamageEffectInstance effect,
        SimulationInstant currentTime,
        Combatant target,
        ResistanceProfile currentResistance,
        EffectChainContext chain)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(currentResistance);
        ArgumentNullException.ThrowIfNull(chain);

        if (effect.TargetCombatantId != target.Id)
        {
            throw new ArgumentException(
                "The supplied combatant is not the active effect's target.",
                nameof(target));
        }

        if (effect.Schedule.IsExpired)
        {
            return Empty(PeriodicDamageTickExecutionStatus.AlreadyExpired, chain.Id);
        }

        if (effect.Schedule.NextActionAt is not { } dueAt || dueAt > currentTime)
        {
            return Empty(PeriodicDamageTickExecutionStatus.NotDue, chain.Id);
        }

        if (chain.TryConsumeOperation() == TriggerBudgetConsumeStatus.GlobalChainBudgetExhausted)
        {
            return Empty(PeriodicDamageTickExecutionStatus.ChainBudgetExhausted, chain.Id);
        }

        var scheduleResult = effect.Schedule.Advance(currentTime);
        if (!scheduleResult.ExecutedTick)
        {
            return new PeriodicDamageTickExecutionResult(
                PeriodicDamageTickExecutionStatus.EffectExpired,
                chain.Id,
                scheduleResult,
                null,
                null,
                null);
        }

        var packet = new DamagePacket(
            effect.SourceCombatantId,
            effect.Definition.TickDamagePortions);
        var combatResolution = _combatResolver.Resolve(packet, currentResistance);
        var healthApplication = target.Apply(combatResolution);

        return new PeriodicDamageTickExecutionResult(
            PeriodicDamageTickExecutionStatus.TickApplied,
            chain.Id,
            scheduleResult,
            packet,
            combatResolution,
            healthApplication);
    }

    private static PeriodicDamageTickExecutionResult Empty(
        PeriodicDamageTickExecutionStatus status,
        EffectChainId chainId) =>
        new(status, chainId, null, null, null, null);
}
