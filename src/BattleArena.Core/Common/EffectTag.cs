namespace BattleArena.Core.Common;

public readonly record struct EffectTag
{
    public EffectTag(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !value.Contains(':', StringComparison.Ordinal) ||
            value.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                "An effect tag must be a namespaced value without whitespace, such as 'base:poison'.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
