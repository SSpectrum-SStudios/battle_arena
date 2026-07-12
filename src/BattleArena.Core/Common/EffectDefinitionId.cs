namespace BattleArena.Core.Common;

public readonly record struct EffectDefinitionId
{
    public EffectDefinitionId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "An effect-definition ID must be a non-empty namespaced value such as 'base:poison'.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
