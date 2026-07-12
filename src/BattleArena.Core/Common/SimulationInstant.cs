namespace BattleArena.Core.Common;

public readonly record struct SimulationInstant : IComparable<SimulationInstant>
{
    public static readonly SimulationInstant Zero = new(0);

    public SimulationInstant(long microseconds)
    {
        if (microseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(microseconds), "Simulation time cannot be negative.");
        }

        Microseconds = microseconds;
    }

    public long Microseconds { get; }

    public int CompareTo(SimulationInstant other) => Microseconds.CompareTo(other.Microseconds);

    public static SimulationInstant operator +(SimulationInstant instant, SimulationDuration duration) =>
        new(checked(instant.Microseconds + duration.Microseconds));

    public static SimulationDuration operator -(SimulationInstant left, SimulationInstant right)
    {
        if (left < right)
        {
            throw new InvalidOperationException("A later instant is required to calculate elapsed time.");
        }

        return new SimulationDuration(left.Microseconds - right.Microseconds);
    }

    public static bool operator <(SimulationInstant left, SimulationInstant right) =>
        left.Microseconds < right.Microseconds;

    public static bool operator >(SimulationInstant left, SimulationInstant right) =>
        left.Microseconds > right.Microseconds;

    public static bool operator <=(SimulationInstant left, SimulationInstant right) =>
        left.Microseconds <= right.Microseconds;

    public static bool operator >=(SimulationInstant left, SimulationInstant right) =>
        left.Microseconds >= right.Microseconds;

    public override string ToString() => $"{Microseconds}us";
}
