namespace BattleArena.Core.Common;

public readonly record struct ActionExecutionId
{
    public ActionExecutionId(long value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "An action-execution ID must be positive.");
        }

        Value = value;
    }

    public long Value { get; }

    public override string ToString() => Value.ToString();
}
