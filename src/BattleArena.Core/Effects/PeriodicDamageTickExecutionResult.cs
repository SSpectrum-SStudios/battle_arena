using BattleArena.Core.Combat;
using BattleArena.Core.Common;

namespace BattleArena.Core.Effects;

public sealed record PeriodicDamageTickExecutionResult(
    PeriodicDamageTickExecutionStatus Status,
    EffectChainId ChainId,
    PeriodicScheduleAdvanceResult? ScheduleResult,
    DamagePacket? DamagePacket,
    CombatResolutionResult? CombatResolution,
    HealthApplicationResult? HealthApplication)
{
    public bool AppliedTick => Status == PeriodicDamageTickExecutionStatus.TickApplied;
}
