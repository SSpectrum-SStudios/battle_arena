using BattleArena.Core.Common;

namespace BattleArena.Core.Application;

public sealed record RegisterHitResult(
    RegisterHitStatus Status,
    ActionExecutionId ExecutionId,
    CombatantId TargetCombatantId)
{
    public bool Accepted => Status == RegisterHitStatus.Accepted;
}
