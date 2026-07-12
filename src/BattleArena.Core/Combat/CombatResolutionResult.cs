using BattleArena.Core.Common;
using System.Collections.ObjectModel;

namespace BattleArena.Core.Combat;

public sealed class CombatResolutionResult
{
    private readonly ReadOnlyCollection<ResolvedDamagePortion> _portions;

    public CombatResolutionResult(
        CombatantId sourceId,
        IEnumerable<ResolvedDamagePortion> portions)
    {
        ArgumentNullException.ThrowIfNull(portions);

        var materializedPortions = portions.ToArray();
        if (materializedPortions.Length == 0)
        {
            throw new ArgumentException("A combat result must contain at least one resolved portion.", nameof(portions));
        }

        if (materializedPortions.Any(static portion => portion is null))
        {
            throw new ArgumentException("A combat result cannot contain a null portion.", nameof(portions));
        }

        SourceId = sourceId;
        _portions = Array.AsReadOnly(materializedPortions);
        TotalDamage = materializedPortions.Sum(static portion => portion.Damage);
        TotalHealing = materializedPortions.Sum(static portion => portion.Healing);
    }

    public CombatantId SourceId { get; }

    public IReadOnlyList<ResolvedDamagePortion> Portions => _portions;

    public double TotalDamage { get; }

    public double TotalHealing { get; }
}
