using BattleArena.Core.Common;
using System.Collections.ObjectModel;

namespace BattleArena.Core.Combat;

public sealed class DamagePacket
{
    private readonly ReadOnlyCollection<DamagePortion> _portions;

    public DamagePacket(CombatantId sourceId, IEnumerable<DamagePortion> portions)
    {
        ArgumentNullException.ThrowIfNull(portions);

        var materializedPortions = portions.ToArray();
        if (materializedPortions.Length == 0)
        {
            throw new ArgumentException("A damage packet must contain at least one portion.", nameof(portions));
        }

        if (materializedPortions.Any(static portion => portion is null))
        {
            throw new ArgumentException("A damage packet cannot contain a null portion.", nameof(portions));
        }

        SourceId = sourceId;
        _portions = Array.AsReadOnly(materializedPortions);
    }

    public CombatantId SourceId { get; }

    public IReadOnlyList<DamagePortion> Portions => _portions;
}
