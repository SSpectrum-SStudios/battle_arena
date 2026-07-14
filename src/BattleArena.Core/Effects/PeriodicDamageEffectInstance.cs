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
        SimulationInstant currentTime)
    {
        ArgumentNullException.ThrowIfNull(contribution);

        if (Schedule.IsExpired)
        {
            throw new InvalidOperationException("An expired effect cannot be modified.");
        }

        if (!_modifiers.TryAdd(contribution.Id, contribution))
        {
            throw new ArgumentException(
                $"Modifier contribution ID {contribution.Id} is already installed.",
                nameof(contribution));
        }

        try
        {
            Recompile(currentTime);
        }
        catch
        {
            _modifiers.Remove(contribution.Id);
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
