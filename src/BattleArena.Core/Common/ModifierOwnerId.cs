namespace BattleArena.Core.Common;

public readonly record struct ModifierOwnerId
{
    public ModifierOwnerId(long value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "A modifier-owner ID must be positive.");
        }

        Value = value;
    }

    public long Value { get; }

    public override string ToString() => Value.ToString();
}
