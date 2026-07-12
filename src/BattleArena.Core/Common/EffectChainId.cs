namespace BattleArena.Core.Common;

public readonly record struct EffectChainId
{
    public EffectChainId(long value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "An effect-chain ID must be positive.");
        }

        Value = value;
    }

    public long Value { get; }

    public override string ToString() => Value.ToString();
}
