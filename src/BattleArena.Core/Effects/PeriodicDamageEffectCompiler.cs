using BattleArena.Core.Combat;
using BattleArena.Core.Common;

namespace BattleArena.Core.Effects;

public sealed class PeriodicDamageEffectCompiler
{
    public PeriodicDamageEffectValues Compile(
        PeriodicDamageEffectDefinition definition,
        IEnumerable<PeriodicDamageModifierContribution> contributions,
        long revision)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(contributions);

        var materialized = contributions.ToArray();
        if (materialized.Any(static contribution => contribution is null))
        {
            throw new ArgumentException("Modifier contributions cannot contain null.", nameof(contributions));
        }

        var duplicateId = materialized
            .GroupBy(static contribution => contribution.Id)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateId is not null)
        {
            throw new ArgumentException(
                $"Contribution ID {duplicateId.Key} appears more than once.",
                nameof(contributions));
        }

        ValidateExactSelectors(definition, materialized);

        var amounts = definition.TickDamagePortions.ToDictionary(
            static portion => portion.Id,
            static portion => portion.Amount);
        var intervalTicks = (double)definition.Interval.Ticks;
        var completionValue = GetCompletionValue(definition.CompletionPolicy);

        foreach (var contribution in materialized
                     .OrderBy(static contribution => contribution.InstallationSequence)
                     .ThenBy(static contribution => contribution.Id.Value))
        {
            switch (contribution)
            {
                case PeriodicDamageModifierContribution.DamageAmount damageAmount:
                    foreach (var portion in definition.TickDamagePortions.Where(damageAmount.Selector.Matches))
                    {
                        amounts[portion.Id] = Math.Max(0d, damageAmount.Operation.Apply(amounts[portion.Id]));
                    }

                    break;
                case PeriodicDamageModifierContribution.Interval interval:
                    intervalTicks = interval.Operation.Apply(intervalTicks);
                    break;
                case PeriodicDamageModifierContribution.CompletionValue completion:
                    completionValue = completion.Operation.Apply(completionValue);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported periodic-damage modifier type: {contribution.GetType().Name}.");
            }
        }

        var effectivePortions = definition.TickDamagePortions.Select(
            portion => new PeriodicDamagePortionValue(
                portion.Id,
                portion.Type,
                amounts[portion.Id]));
        var effectiveInterval = new SimulationDuration(ToPositiveWholeNumber(intervalTicks));
        var effectiveCompletion = CreateCompletionPolicy(
            definition.CompletionPolicy,
            ToPositiveWholeNumber(completionValue));

        return new PeriodicDamageEffectValues(
            effectivePortions,
            effectiveInterval,
            effectiveCompletion,
            revision);
    }

    private static void ValidateExactSelectors(
        PeriodicDamageEffectDefinition definition,
        IEnumerable<PeriodicDamageModifierContribution> contributions)
    {
        var portionIds = definition.TickDamagePortions
            .Select(static portion => portion.Id)
            .ToHashSet();

        var missing = contributions
            .OfType<PeriodicDamageModifierContribution.DamageAmount>()
            .Select(static contribution => contribution.Selector)
            .OfType<DamagePortionSelector.Exact>()
            .Select(static selector => selector.PortionId)
            .FirstOrDefault(portionId => !portionIds.Contains(portionId));

        if (missing != default)
        {
            throw new ArgumentException(
                $"Exact damage-portion selector {missing} does not exist in effect {definition.Id}.",
                nameof(contributions));
        }
    }

    private static double GetCompletionValue(PeriodicCompletionPolicy policy) =>
        policy switch
        {
            PeriodicCompletionPolicy.AfterTickCount tickCount => tickCount.TotalTicks,
            PeriodicCompletionPolicy.AfterDuration duration => duration.Duration.Ticks,
            _ => throw new InvalidOperationException($"Unsupported completion policy: {policy.GetType().Name}."),
        };

    private static PeriodicCompletionPolicy CreateCompletionPolicy(
        PeriodicCompletionPolicy structuralPolicy,
        long effectiveValue) =>
        structuralPolicy switch
        {
            PeriodicCompletionPolicy.AfterTickCount =>
                new PeriodicCompletionPolicy.AfterTickCount(checked((int)Math.Min(effectiveValue, int.MaxValue))),
            PeriodicCompletionPolicy.AfterDuration =>
                new PeriodicCompletionPolicy.AfterDuration(new SimulationDuration(effectiveValue)),
            _ => throw new InvalidOperationException(
                $"Unsupported completion policy: {structuralPolicy.GetType().Name}."),
        };

    private static long ToPositiveWholeNumber(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new InvalidOperationException("A modifier produced a non-finite schedule value.");
        }

        var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        if (rounded <= 1d)
        {
            return 1L;
        }

        if (rounded >= long.MaxValue)
        {
            return long.MaxValue;
        }

        return (long)rounded;
    }
}
