using BattleArena.Core.Common;

namespace BattleArena.Core.Effects;

public sealed class PeriodicDamageEffectInstance : ActiveEffectInstance
{
    private readonly Dictionary<ContributionId, PeriodicDamageModifierContribution> _modifiers = [];
    private readonly PeriodicDamageEffectCompiler _compiler = new();

    internal PeriodicDamageEffectInstance(
        ActiveEffectId id,
        PeriodicDamageEffectDefinition definition,
        CombatantId sourceCombatantId,
        CombatantId targetCombatantId,
        LifeGenerationId sourceLifeGenerationId,
        PeriodicEffectSchedule schedule)
        : base(
            id,
            definition?.Id ?? throw new ArgumentNullException(nameof(definition)),
            sourceCombatantId,
            targetCombatantId,
            sourceLifeGenerationId,
            definition.LifetimeScope,
            schedule)
    {
        Definition = definition;
        EffectiveValues = _compiler.Compile(definition, [], revision: 0);
    }

    public PeriodicDamageEffectDefinition Definition { get; }

    public PeriodicDamageEffectValues EffectiveValues { get; private set; }

    public IReadOnlyCollection<PeriodicDamageModifierContribution> Modifiers =>
        Array.AsReadOnly(_modifiers.Values.ToArray());

    public void InstallModifier(
        PeriodicDamageModifierContribution contribution,
        SimulationInstant currentTime) =>
        InstallModifiers([contribution], currentTime);

    public void InstallModifiers(
        IEnumerable<PeriodicDamageModifierContribution> contributions,
        SimulationInstant currentTime)
    {
        ArgumentNullException.ThrowIfNull(contributions);
        var materialized = contributions.ToArray();
        if (materialized.Length == 0)
        {
            return;
        }

        if (materialized.Any(static contribution => contribution is null))
        {
            throw new ArgumentException("Modifier contributions cannot contain null.", nameof(contributions));
        }

        if (Schedule.IsExpired)
        {
            throw new InvalidOperationException("An expired effect cannot be modified.");
        }

        var duplicateIncoming = materialized
            .GroupBy(static contribution => contribution.Id)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateIncoming is not null)
        {
            throw new ArgumentException(
                $"Modifier contribution ID {duplicateIncoming.Key} appears more than once.",
                nameof(contributions));
        }

        var existingId = materialized
            .Select(static contribution => contribution.Id)
            .FirstOrDefault(_modifiers.ContainsKey);
        if (existingId != default)
        {
            throw new ArgumentException(
                $"Modifier contribution ID {existingId} is already installed.",
                nameof(contributions));
        }

        foreach (var contribution in materialized)
        {
            _modifiers.Add(contribution.Id, contribution);
        }

        try
        {
            Recompile(currentTime);
        }
        catch
        {
            foreach (var contribution in materialized)
            {
                _modifiers.Remove(contribution.Id);
            }

            throw;
        }
    }

    public int RemoveModifiersOwnedBy(
        ModifierOwnerId ownerId,
        SimulationInstant currentTime)
    {
        if (Schedule.IsExpired)
        {
            return 0;
        }

        var removedIds = _modifiers.Values
            .Where(contribution => contribution.OwnerId == ownerId)
            .Select(static contribution => contribution.Id)
            .ToArray();

        foreach (var id in removedIds)
        {
            _modifiers.Remove(id);
        }

        if (removedIds.Length > 0)
        {
            Recompile(currentTime);
        }

        return removedIds.Length;
    }

    public PeriodicDamageEffectSnapshot CreatePeriodicSnapshot() =>
        new(
            CreateSnapshot(),
            Definition.Tags,
            EffectiveValues.TickDamagePortions,
            EffectiveValues.Interval,
            EffectiveValues.CompletionPolicy,
            EffectiveValues.Revision,
            _modifiers.Count);

    private void Recompile(SimulationInstant currentTime)
    {
        var compiled = _compiler.Compile(
            Definition,
            _modifiers.Values,
            EffectiveValues.Revision + 1);

        if (compiled.Interval != EffectiveValues.Interval ||
            compiled.CompletionPolicy != EffectiveValues.CompletionPolicy)
        {
            Schedule.Reconfigure(
                compiled.Interval,
                compiled.CompletionPolicy,
                currentTime);
        }

        EffectiveValues = compiled;
    }
}
