namespace BattleArena.Core.Common;

public readonly record struct ActiveEffectInfluenceDefinitionId
{
    public ActiveEffectInfluenceDefinitionId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !value.Contains(':', StringComparison.Ordinal) ||
            value.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                "An influence-definition ID must be namespaced and contain no whitespace, such as 'base:poison_aura'.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
