using BattleArena.Core.Common;

namespace BattleArena.Core.Application;

public sealed record BeginActionResult(
    BeginActionStatus Status,
    ActionExecutionId? ExecutionId)
{
    public bool Started => Status == BeginActionStatus.Started;
}
