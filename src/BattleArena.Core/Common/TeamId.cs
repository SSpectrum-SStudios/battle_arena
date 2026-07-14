namespace BattleArena.Core.Common;

public readonly record struct TeamId
{
    public TeamId(long value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "A team ID must be positive.");
        }

        Value = value;
    }

    public long Value { get; }

    public override string ToString() => Value.ToString();
}
