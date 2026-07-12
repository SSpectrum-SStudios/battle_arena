namespace BattleArena.Core.Effects;

public enum PeriodicScheduleStatus
{
    NotDue = 0,
    Ticked = 1,
    TickedAndExpired = 2,
    ExpiredWithoutTick = 3,
    AlreadyExpired = 4,
}
