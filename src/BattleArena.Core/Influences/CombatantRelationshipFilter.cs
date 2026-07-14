namespace BattleArena.Core.Influences;

[Flags]
public enum CombatantRelationshipFilter
{
    None = 0,
    Self = 1 << 0,
    Allies = 1 << 1,
    Enemies = 1 << 2,
    Everyone = Self | Allies | Enemies,
}

public static class CombatantRelationshipFilterExtensions
{
    public static bool Includes(
        this CombatantRelationshipFilter filter,
        CombatantRelationship relationship)
    {
        var required = relationship switch
        {
            CombatantRelationship.Self => CombatantRelationshipFilter.Self,
            CombatantRelationship.Ally => CombatantRelationshipFilter.Allies,
            CombatantRelationship.Enemy => CombatantRelationshipFilter.Enemies,
            _ => throw new ArgumentOutOfRangeException(nameof(relationship), relationship, null),
        };

        return (filter & required) != 0;
    }

    public static void Validate(this CombatantRelationshipFilter filter, string parameterName)
    {
        if (filter == CombatantRelationshipFilter.None ||
            (filter & ~CombatantRelationshipFilter.Everyone) != 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                filter,
                "A relationship filter must contain at least one supported relationship.");
        }
    }
}
