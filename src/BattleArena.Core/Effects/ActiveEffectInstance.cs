using BattleArena.Core.Common;

namespace BattleArena.Core.Effects;

public abstract class ActiveEffectInstance
{
    protected ActiveEffectInstance(
        ActiveEffectId id,
        EffectDefinitionId definitionId,
        CombatantId sourceCombatantId,
        CombatantId targetCombatantId,
        LifeGenerationId sourceLifeGenerationId,
        EffectLifetimeScope lifetimeScope,
        PeriodicEffectSchedule schedule)
    {
        if (!Enum.IsDefined(lifetimeScope))
        {
            throw new ArgumentOutOfRangeException(
                nameof(lifetimeScope),
                lifetimeScope,
                "The lifetime scope is not defined.");
        }

        Id = id;
        DefinitionId = definitionId;
        SourceCombatantId = sourceCombatantId;
        TargetCombatantId = targetCombatantId;
        SourceLifeGenerationId = sourceLifeGenerationId;
        LifetimeScope = lifetimeScope;
        Schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
    }

    public ActiveEffectId Id { get; }

    public EffectDefinitionId DefinitionId { get; }

    public CombatantId SourceCombatantId { get; }

    public CombatantId TargetCombatantId { get; }

    public LifeGenerationId SourceLifeGenerationId { get; }

    public EffectLifetimeScope LifetimeScope { get; }

    public PeriodicEffectSchedule Schedule { get; }

    public ActiveEffectSnapshot CreateSnapshot() =>
        new(
            Id,
            DefinitionId,
            SourceCombatantId,
            TargetCombatantId,
            SourceLifeGenerationId,
            LifetimeScope,
            Schedule.NextActionAt,
            Schedule.ExecutedTicks,
            Schedule.RemainingTicks,
            Schedule.IsExpired,
            Schedule.Revision);
}
