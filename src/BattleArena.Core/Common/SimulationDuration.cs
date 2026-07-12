namespace BattleArena.Core.Common;

public readonly record struct SimulationDuration : IComparable<SimulationDuration>
{
    public static readonly SimulationDuration Zero = new(0);

    public SimulationDuration(long microseconds)
    {
        if (microseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(microseconds), "A duration cannot be negative.");
        }

        Microseconds = microseconds;
    }

    public long Microseconds { get; }

    public static SimulationDuration FromSeconds(decimal seconds)
    {
        if (seconds < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), "A duration cannot be negative.");
        }

        var microseconds = decimal.Round(
            seconds * 1_000_000m,
            decimals: 0,
            MidpointRounding.AwayFromZero);

        if (microseconds > long.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), "The duration is too large.");
        }

        return new SimulationDuration((long)microseconds);
    }

    public int CompareTo(SimulationDuration other) => Microseconds.CompareTo(other.Microseconds);

    public static SimulationDuration operator +(SimulationDuration left, SimulationDuration right) =>
        new(checked(left.Microseconds + right.Microseconds));

    public override string ToString() => $"{Microseconds}us";
}
