using BattleArena.Core.Combat;
using BattleArena.Core.Common;

namespace BattleArena.Core.Effects;

public sealed record PeriodicDamagePortionValue(
    DamagePortionId Id,
    DamageType Type,
    double Amount)
{
    public DamagePortion ToDamagePortion() => new(Type, Amount);
}
