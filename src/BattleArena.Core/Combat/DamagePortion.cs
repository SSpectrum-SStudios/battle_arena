namespace BattleArena.Core.Combat;

public sealed record DamagePortion
{
    public DamagePortion(DamageType type, double amount)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "The damage type is not defined.");
        }

        if (!double.IsFinite(amount))
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Damage must be a finite number.");
        }

        Type = type;
        Amount = Math.Max(0d, amount);
    }

    public DamageType Type { get; }

    public double Amount { get; }
}
