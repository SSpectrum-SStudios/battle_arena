using BattleArena.Core.Combat;
using BattleArena.Core.Common;

namespace BattleArena.Core.Effects;

public sealed record PeriodicDamagePortionDefinition
{
    public PeriodicDamagePortionDefinition(
        DamagePortionId id,
        DamageType type,
        double amount)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "The damage type is not defined.");
        }

        if (!double.IsFinite(amount))
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Damage must be finite.");
        }

        Id = id;
        Type = type;
        Amount = Math.Max(0d, amount);
    }

    public DamagePortionId Id { get; }

    public DamageType Type { get; }

    public double Amount { get; }
}
