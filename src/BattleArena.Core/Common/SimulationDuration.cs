namespace BattleArena.Core.Common;

public readonly record struct SimulationDuration : IComparable<SimulationDuration>
{
    public static readonly SimulationDuration Zero = new(0);

    public SimulationDuration(long ticks)
    {
        if (ticks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ticks), "A duration cannot be negative.");
        }

        Ticks = ticks;
    }

    public long Ticks { get; }

    public int CompareTo(SimulationDuration other) => Ticks.CompareTo(other.Ticks);

    public static SimulationDuration operator +(SimulationDuration left, SimulationDuration right) =>
        new(checked(left.Ticks + right.Ticks));

    public override string ToString() => $"{Ticks} ticks";
}
