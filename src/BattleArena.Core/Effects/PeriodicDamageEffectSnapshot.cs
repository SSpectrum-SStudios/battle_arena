using BattleArena.Core.Common;
using System.Collections.ObjectModel;

namespace BattleArena.Core.Effects;

public sealed class PeriodicDamageEffectSnapshot
{
    private readonly ReadOnlyCollection<EffectTag> _tags;
    private readonly ReadOnlyCollection<PeriodicDamagePortionValue> _tickDamagePortions;

    public PeriodicDamageEffectSnapshot(
        ActiveEffectSnapshot effect,
        IEnumerable<EffectTag> tags,
        IEnumerable<PeriodicDamagePortionValue> tickDamagePortions,
        SimulationDuration interval,
        PeriodicCompletionPolicy completionPolicy,
        long effectiveValuesRevision,
        int modifierCount)
    {
        Effect = effect ?? throw new ArgumentNullException(nameof(effect));
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(tickDamagePortions);

        _tags = Array.AsReadOnly(tags.ToArray());
        _tickDamagePortions = Array.AsReadOnly(tickDamagePortions.ToArray());
        Interval = interval;
        CompletionPolicy = completionPolicy ?? throw new ArgumentNullException(nameof(completionPolicy));
        EffectiveValuesRevision = effectiveValuesRevision;
        ModifierCount = modifierCount;
    }

    public ActiveEffectSnapshot Effect { get; }

    public IReadOnlyList<EffectTag> Tags => _tags;

    public IReadOnlyList<PeriodicDamagePortionValue> TickDamagePortions => _tickDamagePortions;

    public SimulationDuration Interval { get; }

    public PeriodicCompletionPolicy CompletionPolicy { get; }

    public long EffectiveValuesRevision { get; }

    public int ModifierCount { get; }
}
