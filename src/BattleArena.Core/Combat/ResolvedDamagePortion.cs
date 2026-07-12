namespace BattleArena.Core.Combat;

public sealed record ResolvedDamagePortion
{
    public ResolvedDamagePortion(DamageType type, double damage, double healing)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "The damage type is not defined.");
        }

        if (!double.IsFinite(damage) || damage < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(damage), "Resolved damage must be finite and non-negative.");
        }

        if (!double.IsFinite(healing) || healing < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(healing), "Resolved healing must be finite and non-negative.");
        }

        if (damage > 0d && healing > 0d)
        {
            throw new ArgumentException("One typed portion cannot resolve to both damage and healing.");
        }

        Type = type;
        Damage = damage;
        Healing = healing;
    }

    public DamageType Type { get; }

    public double Damage { get; }

    public double Healing { get; }
}
