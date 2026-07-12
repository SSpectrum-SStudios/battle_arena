using BattleArena.Core.Common;

namespace BattleArena.Core.Effects;

public sealed record PeriodicScheduleAdvanceResult(
    PeriodicScheduleStatus Status,
    SimulationInstant? TickScheduledAt,
    SimulationInstant? NextActionAt,
    int ExecutedTicks,
    int? RemainingTicks,
    bool IsExpired)
{
    public bool ExecutedTick =>
        Status is PeriodicScheduleStatus.Ticked or PeriodicScheduleStatus.TickedAndExpired;
}
