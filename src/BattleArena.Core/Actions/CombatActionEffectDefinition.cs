using BattleArena.Core.Combat;
using BattleArena.Core.Effects;
using System.Collections.ObjectModel;

namespace BattleArena.Core.Actions;

public abstract record CombatActionEffectDefinition
{
    private CombatActionEffectDefinition()
    {
    }

    public sealed record ImmediateDamage : CombatActionEffectDefinition
    {
        private readonly ReadOnlyCollection<DamagePortion> _portions;

        public ImmediateDamage(IEnumerable<DamagePortion> portions)
        {
            ArgumentNullException.ThrowIfNull(portions);
            var materialized = portions.ToArray();
            if (materialized.Length == 0 || materialized.Any(static portion => portion is null))
            {
                throw new ArgumentException(
                    "Immediate damage must contain at least one non-null portion.",
                    nameof(portions));
            }

            _portions = Array.AsReadOnly(materialized);
        }

        public IReadOnlyList<DamagePortion> Portions => _portions;
    }

    public sealed record ApplyPeriodicDamage : CombatActionEffectDefinition
    {
        public ApplyPeriodicDamage(PeriodicDamageEffectDefinition effect)
        {
            Effect = effect ?? throw new ArgumentNullException(nameof(effect));
        }

        public PeriodicDamageEffectDefinition Effect { get; }
    }
}
