using BattleArena.Core.Common;

namespace BattleArena.Core.Effects;

public abstract record PeriodicCompletionPolicy
{
    private PeriodicCompletionPolicy()
    {
    }

    public sealed record AfterTickCount : PeriodicCompletionPolicy
    {
        public AfterTickCount(int totalTicks)
        {
            if (totalTicks <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(totalTicks), "Tick count must be positive.");
            }

            TotalTicks = totalTicks;
        }

        public int TotalTicks { get; }
    }

    public sealed record AfterDuration : PeriodicCompletionPolicy
    {
        public AfterDuration(SimulationDuration duration)
        {
            if (duration == SimulationDuration.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(duration), "Duration must be positive.");
            }

            Duration = duration;
        }

        public SimulationDuration Duration { get; }
    }
}
