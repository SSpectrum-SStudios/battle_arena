namespace BattleArena.Core.Effects;

public enum PeriodicDamageTickExecutionStatus
{
    NotDue = 0,
    TickApplied = 1,
    EffectExpired = 2,
    AlreadyExpired = 3,
    ChainBudgetExhausted = 4,
}
