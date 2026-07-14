namespace BattleArena.Core.Common;

public readonly record struct SimulationInstant : IComparable<SimulationInstant>
{
    public static readonly SimulationInstant Zero = new(0);

    public SimulationInstant(long tick)
    {
        if (tick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tick), "Simulation time cannot be negative.");
        }

        Tick = tick;
    }

    public long Tick { get; }

    public int CompareTo(SimulationInstant other) => Tick.CompareTo(other.Tick);

    public static SimulationInstant operator +(SimulationInstant instant, SimulationDuration duration) =>
        new(checked(instant.Tick + duration.Ticks));

    public static SimulationDuration operator -(SimulationInstant left, SimulationInstant right)
    {
        if (left < right)
        {
            throw new InvalidOperationException("A later instant is required to calculate elapsed time.");
        }

        return new SimulationDuration(left.Tick - right.Tick);
    }

    public static bool operator <(SimulationInstant left, SimulationInstant right) =>
        left.Tick < right.Tick;

    public static bool operator >(SimulationInstant left, SimulationInstant right) =>
        left.Tick > right.Tick;

    public static bool operator <=(SimulationInstant left, SimulationInstant right) =>
        left.Tick <= right.Tick;

    public static bool operator >=(SimulationInstant left, SimulationInstant right) =>
        left.Tick >= right.Tick;

    public override string ToString() => $"tick {Tick}";
}
