using BattleArena.Core.Common;

namespace BattleArena.Core.Combat.Attacks;

public sealed record AttackRuntimeState(
    ulong ExecutionId,
    AttackContext Context,
    int StepIndex,
    SimulationInstant StartedAt,
    bool ContinuationQueued,
    bool AttackReleasedDuringStep,
    IReadOnlySet<ulong> HitCombatants);
