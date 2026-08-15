using BattleArena.Core.Common;

namespace BattleArena.Core.Application;

public sealed record SimultaneousHitRequest(
    ActionExecutionId ExecutionId,
    CombatantId TargetCombatantId);
