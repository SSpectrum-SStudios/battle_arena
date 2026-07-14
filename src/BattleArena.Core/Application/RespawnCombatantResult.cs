using BattleArena.Core.Combat;
using BattleArena.Core.Common;

namespace BattleArena.Core.Application;

public sealed record RespawnCombatantResult(
    RespawnStatus Status,
    CombatantId CombatantId,
    CombatantRespawnResult? Respawn)
{
    public bool Respawned => Status == RespawnStatus.Respawned;
}
