namespace BattleArena.Core.Common;

public readonly record struct ActiveEffectId
{
    public ActiveEffectId(long value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "An active-effect ID must be positive.");
        }

        Value = value;
    }

    public long Value { get; }

    public override string ToString() => Value.ToString();
}
