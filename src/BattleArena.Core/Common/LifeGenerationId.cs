namespace BattleArena.Core.Common;

public readonly record struct LifeGenerationId : IComparable<LifeGenerationId>
{
    public LifeGenerationId(long value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "A life-generation ID must be positive.");
        }

        Value = value;
    }

    public long Value { get; }

    public int CompareTo(LifeGenerationId other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString();
}
