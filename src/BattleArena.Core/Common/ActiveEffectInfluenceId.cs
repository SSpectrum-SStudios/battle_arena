namespace BattleArena.Core.Common;

public readonly record struct ActiveEffectInfluenceId
{
    public ActiveEffectInfluenceId(long value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "An active-effect influence ID must be positive.");
        }

        Value = value;
    }

    public long Value { get; }

    public override string ToString() => Value.ToString();
}
