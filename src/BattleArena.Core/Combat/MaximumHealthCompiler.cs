namespace BattleArena.Core.Combat;

public sealed class MaximumHealthCompiler
{
    public const double MinimumMaximumHealth = 1d;

    public MaximumHealthSnapshot Compile(
        double baseMaximumHealth,
        IEnumerable<MaximumHealthContribution> equipmentContributions,
        IEnumerable<MaximumHealthContribution> runtimeContributions)
    {
        if (!double.IsFinite(baseMaximumHealth) || baseMaximumHealth <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(baseMaximumHealth),
                "Base maximum health must be finite and positive.");
        }

        var equipmentMaximum = CompileLayer(
            baseMaximumHealth,
            equipmentContributions,
            nameof(equipmentContributions));
        var effectiveMaximum = CompileLayer(
            equipmentMaximum,
            runtimeContributions,
            nameof(runtimeContributions));

        return new MaximumHealthSnapshot(
            baseMaximumHealth,
            equipmentMaximum,
            effectiveMaximum);
    }

    private static double CompileLayer(
        double startingValue,
        IEnumerable<MaximumHealthContribution> contributions,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(contributions, parameterName);

        var materialized = contributions.ToArray();
        if (materialized.Any(static contribution => contribution is null))
        {
            throw new ArgumentException(
                "Maximum-health contributions cannot contain null.",
                parameterName);
        }

        var duplicateId = materialized
            .GroupBy(static contribution => contribution.Id)
            .FirstOrDefault(static group => group.Count() > 1);

        if (duplicateId is not null)
        {
            throw new ArgumentException(
                $"Contribution ID {duplicateId.Key} appears more than once.",
                parameterName);
        }

        var effectiveMaximum = startingValue;
        foreach (var contribution in materialized
                     .OrderBy(static contribution => contribution.InstallationSequence)
                     .ThenBy(static contribution => contribution.Id.Value))
        {
            effectiveMaximum = contribution.Operation.Apply(effectiveMaximum);
        }

        return Math.Max(MinimumMaximumHealth, effectiveMaximum);
    }
}
