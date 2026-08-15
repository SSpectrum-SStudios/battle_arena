namespace BattleArena.Core.Movement.Simulation;

/// <summary>
/// Authority-owned identity for one contiguous numbering of the shared match
/// simulation frame. This identity is not itself simulation time.
/// </summary>
/// <remarks>
/// Values may be ordered only within the same match/session. The owning match
/// simulation context establishes that scope before comparison.
/// </remarks>
public readonly record struct MatchFrameEpochId : IComparable<MatchFrameEpochId>
{
    public static MatchFrameEpochId Initial { get; } = new(1);

    public MatchFrameEpochId(ulong value)
    {
        if (value == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "A match-frame epoch identity must be positive.");
        }

        Value = value;
    }

    public ulong Value { get; }
    public bool IsValid => Value != 0;

    public MatchFrameEpochId Next()
    {
        RequireValid(Value);
        return new(checked(Value + 1));
    }

    public int CompareTo(MatchFrameEpochId other)
    {
        RequireValid(Value);
        RequireValid(other.Value);
        return Value.CompareTo(other.Value);
    }

    public static bool operator <(MatchFrameEpochId left, MatchFrameEpochId right) =>
        left.CompareTo(right) < 0;

    public static bool operator >(MatchFrameEpochId left, MatchFrameEpochId right) =>
        left.CompareTo(right) > 0;

    public static bool operator <=(MatchFrameEpochId left, MatchFrameEpochId right) =>
        left.CompareTo(right) <= 0;

    public static bool operator >=(MatchFrameEpochId left, MatchFrameEpochId right) =>
        left.CompareTo(right) >= 0;

    public override string ToString() => IsValid
        ? $"match-frame-epoch:{Value}"
        : "match-frame-epoch:invalid";

    private static void RequireValid(ulong value)
    {
        if (value == 0)
        {
            throw new InvalidOperationException(
                "A default/invalid match-frame epoch identity cannot be ordered or advanced.");
        }
    }
}
