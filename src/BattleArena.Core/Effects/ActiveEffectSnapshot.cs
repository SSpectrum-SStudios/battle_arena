using BattleArena.Core.Common;

namespace BattleArena.Core.Effects;

public sealed record ActiveEffectSnapshot(
    ActiveEffectId Id,
    EffectDefinitionId DefinitionId,
    CombatantId SourceCombatantId,
    CombatantId TargetCombatantId,
    LifeGenerationId SourceLifeGenerationId,
    EffectLifetimeScope LifetimeScope,
    SimulationInstant? NextActionAt,
    int ExecutedTicks,
    int? RemainingTicks,
    bool IsExpired,
    long Revision);
