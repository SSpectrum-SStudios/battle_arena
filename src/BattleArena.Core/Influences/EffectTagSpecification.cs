using BattleArena.Core.Common;
using System.Collections.ObjectModel;

namespace BattleArena.Core.Influences;

public sealed class EffectTagSpecification
{
    private readonly ReadOnlyCollection<EffectTag> _requiredAll;
    private readonly ReadOnlyCollection<EffectTag> _requiredAny;
    private readonly ReadOnlyCollection<EffectTag> _excluded;

    public EffectTagSpecification(
        IEnumerable<EffectTag>? requiredAll = null,
        IEnumerable<EffectTag>? requiredAny = null,
        IEnumerable<EffectTag>? excluded = null)
    {
        _requiredAll = Array.AsReadOnly(requiredAll?.Distinct().ToArray() ?? []);
        _requiredAny = Array.AsReadOnly(requiredAny?.Distinct().ToArray() ?? []);
        _excluded = Array.AsReadOnly(excluded?.Distinct().ToArray() ?? []);
    }

    public IReadOnlyList<EffectTag> RequiredAll => _requiredAll;

    public IReadOnlyList<EffectTag> RequiredAny => _requiredAny;

    public IReadOnlyList<EffectTag> Excluded => _excluded;

    public bool IsSatisfiedBy(IEnumerable<EffectTag> candidateTags)
    {
        ArgumentNullException.ThrowIfNull(candidateTags);
        var candidates = candidateTags.ToHashSet();

        return _requiredAll.All(candidates.Contains) &&
               (_requiredAny.Count == 0 || _requiredAny.Any(candidates.Contains)) &&
               !_excluded.Any(candidates.Contains);
    }
}
