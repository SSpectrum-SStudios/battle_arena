using BattleArena.Core.Combat;
using BattleArena.Core.Common;

namespace BattleArena.Core.Effects;

public abstract record DamagePortionSelector
{
    private DamagePortionSelector()
    {
    }

    internal abstract bool Matches(PeriodicDamagePortionDefinition portion);

    public sealed record All : DamagePortionSelector
    {
        internal override bool Matches(PeriodicDamagePortionDefinition portion) => true;
    }

    public sealed record ByDamageType : DamagePortionSelector
    {
        public ByDamageType(DamageType damageType)
        {
            if (!Enum.IsDefined(damageType))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(damageType),
                    damageType,
                    "The damage type is not defined.");
            }

            DamageType = damageType;
        }

        public DamageType DamageType { get; }

        internal override bool Matches(PeriodicDamagePortionDefinition portion) =>
            portion.Type == DamageType;
    }

    public sealed record Exact : DamagePortionSelector
    {
        public Exact(DamagePortionId portionId)
        {
            PortionId = portionId;
        }

        public DamagePortionId PortionId { get; }

        internal override bool Matches(PeriodicDamagePortionDefinition portion) =>
            portion.Id == PortionId;
    }
}
