using BattleArena.Core.Common;

namespace BattleArena.Core.Application;

public sealed record InfluenceMembershipResult(
    InfluenceMembershipStatus Status,
    ActiveEffectInfluenceId InfluenceId,
    CombatantId TargetCombatantId);
