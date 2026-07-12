using System.Collections.ObjectModel;

namespace BattleArena.Core.Combat;

public sealed class ResistanceProfile
{
    private readonly ReadOnlyDictionary<ResistanceStatKey, double> _values;
    private readonly HashSet<ResistanceStatKey> _contributedStats;

    internal ResistanceProfile(
        IDictionary<ResistanceStatKey, double> values,
        IEnumerable<ResistanceStatKey> contributedStats)
    {
        _values = new ReadOnlyDictionary<ResistanceStatKey, double>(
            new Dictionary<ResistanceStatKey, double>(values));
        _contributedStats = new HashSet<ResistanceStatKey>(contributedStats);
    }

    public double Get(ResistanceStatKey stat) =>
        _values.TryGetValue(stat, out var value) ? value : 0d;

    public bool HasContribution(ResistanceStatKey stat) => _contributedStats.Contains(stat);
}
