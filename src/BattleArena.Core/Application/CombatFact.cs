using BattleArena.Core.Combat;
using BattleArena.Core.Common;
using BattleArena.Core.Effects;

namespace BattleArena.Core.Application;

public abstract record CombatFact(SimulationInstant OccurredAt)
{
    public sealed record ActionStarted(
        SimulationInstant OccurredAt,
        ActionExecutionId ExecutionId,
        CombatActionDefinitionId DefinitionId,
        CombatantId SourceCombatantId,
        LifeGenerationId SourceLifeGenerationId)
        : CombatFact(OccurredAt);

    public sealed record DamageResolved(
        SimulationInstant OccurredAt,
        CombatantId SourceCombatantId,
        CombatantId TargetCombatantId,
        ActionExecutionId? ActionExecutionId,
        ActiveEffectId? ActiveEffectId,
        LifeGenerationId SourceLifeGenerationId,
        CombatResolutionResult Resolution,
        HealthApplicationResult HealthApplication)
        : CombatFact(OccurredAt);

    public sealed record ActiveEffectApplied(
        SimulationInstant OccurredAt,
        ActiveEffectSnapshot Effect)
        : CombatFact(OccurredAt);

    public sealed record ActiveEffectRemoved(
        SimulationInstant OccurredAt,
        CombatantId TargetCombatantId,
        ActiveEffectId EffectId,
        ActiveEffectRemovalReason Reason)
        : CombatFact(OccurredAt);

    public sealed record ActiveEffectModified(
        SimulationInstant OccurredAt,
        PeriodicDamageEffectSnapshot Effect)
        : CombatFact(OccurredAt);

    public sealed record InfluenceStarted(
        SimulationInstant OccurredAt,
        ActiveEffectInfluenceId InfluenceId,
        ActiveEffectInfluenceDefinitionId DefinitionId,
        CombatantId SourceCombatantId,
        LifeGenerationId SourceLifeGenerationId)
        : CombatFact(OccurredAt);

    public sealed record InfluenceEnded(
        SimulationInstant OccurredAt,
        ActiveEffectInfluenceId InfluenceId)
        : CombatFact(OccurredAt);

    public sealed record CombatantEliminated(
        SimulationInstant OccurredAt,
        CombatantId CombatantId,
        CombatantId CreditedSourceCombatantId,
        LifeGenerationId CreditedSourceLifeGenerationId,
        double Overkill)
        : CombatFact(OccurredAt);

    public sealed record CombatantRespawned(
        SimulationInstant OccurredAt,
        HealthSnapshot Health,
        LifeGenerationId LifeGenerationId)
        : CombatFact(OccurredAt);
}
