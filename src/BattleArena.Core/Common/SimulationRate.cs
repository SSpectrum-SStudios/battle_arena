namespace BattleArena.Core.Common;

public readonly record struct SimulationRate
{
    public static readonly SimulationRate Default = new(60);

    public SimulationRate(int ticksPerSecond)
    {
        if (ticksPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ticksPerSecond),
                "Simulation ticks per second must be positive.");
        }

        TicksPerSecond = ticksPerSecond;
    }

    public int TicksPerSecond { get; }

    public SimulationDuration DurationFromSeconds(decimal seconds)
    {
        if (seconds < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), "A duration cannot be negative.");
        }

        if (seconds == 0m)
        {
            return SimulationDuration.Zero;
        }

        var roundedTicks = decimal.Round(
            seconds * TicksPerSecond,
            decimals: 0,
            MidpointRounding.AwayFromZero);

        if (roundedTicks > long.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), "The duration is too large.");
        }

        return new SimulationDuration(Math.Max(1L, (long)roundedTicks));
    }

    public decimal SecondsFromDuration(SimulationDuration duration) =>
        (decimal)duration.Ticks / TicksPerSecond;
}
