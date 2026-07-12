namespace BattleArena.Core.Common;

public readonly record struct TriggerInstanceId
{
    public TriggerInstanceId(long value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "A trigger-instance ID must be positive.");
        }

        Value = value;
    }

    public long Value { get; }

    public override string ToString() => Value.ToString();
}
