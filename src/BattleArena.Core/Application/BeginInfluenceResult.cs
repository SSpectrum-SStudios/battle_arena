using BattleArena.Core.Common;

namespace BattleArena.Core.Application;

public sealed record BeginInfluenceResult(
    BeginInfluenceStatus Status,
    ActiveEffectInfluenceId? InfluenceId)
{
    public bool Started => Status == BeginInfluenceStatus.Started;
}
