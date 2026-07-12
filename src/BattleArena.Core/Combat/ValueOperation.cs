namespace BattleArena.Core.Combat;

public enum ValueOperationKind
{
    Add = 0,
    Multiply = 1,
    Replace = 2,
    Minimum = 3,
    Maximum = 4,
}

public readonly record struct ValueOperation
{
    public ValueOperation(ValueOperationKind kind, double operand)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "The value operation is not defined.");
        }

        if (!double.IsFinite(operand))
        {
            throw new ArgumentOutOfRangeException(nameof(operand), "The operation operand must be finite.");
        }

        Kind = kind;
        Operand = operand;
    }

    public ValueOperationKind Kind { get; }

    public double Operand { get; }

    public double Apply(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "The input value must be finite.");
        }

        var result = Kind switch
        {
            ValueOperationKind.Add => value + Operand,
            ValueOperationKind.Multiply => value * Operand,
            ValueOperationKind.Replace => Operand,
            ValueOperationKind.Minimum => Math.Min(value, Operand),
            ValueOperationKind.Maximum => Math.Max(value, Operand),
            _ => throw new InvalidOperationException($"Unsupported value operation: {Kind}."),
        };

        if (!double.IsFinite(result))
        {
            throw new InvalidOperationException("The value operation produced a non-finite result.");
        }

        return result;
    }
}
