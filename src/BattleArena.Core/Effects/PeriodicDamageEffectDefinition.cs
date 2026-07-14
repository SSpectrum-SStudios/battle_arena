using BattleArena.Core.Combat;
using BattleArena.Core.Common;
using System.Collections.ObjectModel;

namespace BattleArena.Core.Effects;

public sealed class PeriodicDamageEffectDefinition
{
    private readonly ReadOnlyCollection<DamagePortion> _tickDamagePortions;
    private readonly ReadOnlyCollection<EffectTag> _tags;

    public PeriodicDamageEffectDefinition(
        EffectDefinitionId id,
        IEnumerable<DamagePortion> tickDamagePortions,
        SimulationDuration interval,
        FirstTickPolicy firstTickPolicy,
        PeriodicCompletionPolicy completionPolicy,
        EffectLifetimeScope lifetimeScope = EffectLifetimeScope.PerLife,
        IEnumerable<EffectTag>? tags = null)
    {
        ArgumentNullException.ThrowIfNull(tickDamagePortions);
        ArgumentNullException.ThrowIfNull(completionPolicy);

        var portions = tickDamagePortions.ToArray();
        if (portions.Length == 0 || portions.Any(static portion => portion is null))
        {
            throw new ArgumentException(
                "Periodic damage must contain at least one non-null damage portion.",
                nameof(tickDamagePortions));
        }

        if (interval == SimulationDuration.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "A periodic interval must be positive.");
        }

        if (!Enum.IsDefined(firstTickPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(firstTickPolicy));
        }

        if (!Enum.IsDefined(lifetimeScope))
        {
            throw new ArgumentOutOfRangeException(nameof(lifetimeScope));
        }

        var materializedTags = tags?.Distinct().ToArray() ?? [];

        Id = id;
        _tickDamagePortions = Array.AsReadOnly(portions);
        Interval = interval;
        FirstTickPolicy = firstTickPolicy;
        CompletionPolicy = completionPolicy;
        LifetimeScope = lifetimeScope;
        _tags = Array.AsReadOnly(materializedTags);
    }

    public EffectDefinitionId Id { get; }

    public IReadOnlyList<DamagePortion> TickDamagePortions => _tickDamagePortions;

    public SimulationDuration Interval { get; }

    public FirstTickPolicy FirstTickPolicy { get; }

    public PeriodicCompletionPolicy CompletionPolicy { get; }

    public EffectLifetimeScope LifetimeScope { get; }

    public IReadOnlyList<EffectTag> Tags => _tags;
}
