namespace BattleArena.Core.Combat;

public sealed class ResistanceProfileCompiler
{
    public ResistanceProfile Compile(IEnumerable<ResistanceContribution> contributions)
    {
        ArgumentNullException.ThrowIfNull(contributions);

        var materialized = contributions.ToArray();
        if (materialized.Any(static contribution => contribution is null))
        {
            throw new ArgumentException("Resistance contributions cannot contain null.", nameof(contributions));
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

        var values = new Dictionary<ResistanceStatKey, double>();
        var contributedStats = new HashSet<ResistanceStatKey>();

        foreach (var contribution in materialized
                     .OrderBy(static contribution => contribution.InstallationSequence)
                     .ThenBy(static contribution => contribution.Id.Value))
        {
            var currentValue = values.GetValueOrDefault(contribution.Stat);
            values[contribution.Stat] = contribution.Operation.Apply(currentValue);
            contributedStats.Add(contribution.Stat);
        }

        return new ResistanceProfile(values, contributedStats);
    }
}
