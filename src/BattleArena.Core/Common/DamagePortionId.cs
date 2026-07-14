namespace BattleArena.Core.Common;

public readonly record struct DamagePortionId
{
    public DamagePortionId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                "A damage-portion ID must be a non-empty value without whitespace, such as 'primary_poison'.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
