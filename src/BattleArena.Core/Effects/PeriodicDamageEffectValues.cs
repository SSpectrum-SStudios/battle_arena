using BattleArena.Core.Common;
using System.Collections.ObjectModel;

namespace BattleArena.Core.Effects;

public sealed class PeriodicDamageEffectValues
{
    private readonly ReadOnlyCollection<PeriodicDamagePortionValue> _tickDamagePortions;

    public PeriodicDamageEffectValues(
        IEnumerable<PeriodicDamagePortionValue> tickDamagePortions,
        SimulationDuration interval,
        PeriodicCompletionPolicy completionPolicy,
        long revision)
    {
        ArgumentNullException.ThrowIfNull(tickDamagePortions);
        ArgumentNullException.ThrowIfNull(completionPolicy);

        var portions = tickDamagePortions.ToArray();
        if (portions.Length == 0 || portions.Any(static portion => portion is null))
        {
            throw new ArgumentException(
                "Effective periodic damage must contain at least one non-null portion.",
                nameof(tickDamagePortions));
        }

        if (interval == SimulationDuration.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "A periodic interval must be positive.");
        }

        if (revision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision), "A revision cannot be negative.");
        }

        _tickDamagePortions = Array.AsReadOnly(portions);
        Interval = interval;
        CompletionPolicy = completionPolicy;
        Revision = revision;
    }

    public IReadOnlyList<PeriodicDamagePortionValue> TickDamagePortions => _tickDamagePortions;

    public SimulationDuration Interval { get; }

    public PeriodicCompletionPolicy CompletionPolicy { get; }

    public long Revision { get; }
}
