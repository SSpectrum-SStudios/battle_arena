namespace BattleArena.Core.Common;

public readonly record struct CombatActionDefinitionId
{
    public CombatActionDefinitionId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !value.Contains(':', StringComparison.Ordinal) ||
            value.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                "A combat-action definition ID must be namespaced and contain no whitespace, such as 'base:basic_sword_swing'.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
