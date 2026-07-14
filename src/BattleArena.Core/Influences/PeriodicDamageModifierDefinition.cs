using BattleArena.Core.Combat;
using BattleArena.Core.Common;
using BattleArena.Core.Effects;

namespace BattleArena.Core.Influences;

public abstract record PeriodicDamageModifierDefinition
{
    private PeriodicDamageModifierDefinition(ValueOperation operation)
    {
        Operation = operation;
    }

    public ValueOperation Operation { get; }

    internal abstract PeriodicDamageModifierContribution CreateContribution(
        ContributionId contributionId,
        ModifierOwnerId ownerId,
        InstallationSequence installationSequence);

    public sealed record DamageAmount : PeriodicDamageModifierDefinition
    {
        public DamageAmount(DamagePortionSelector selector, ValueOperation operation)
            : base(operation)
        {
            Selector = selector ?? throw new ArgumentNullException(nameof(selector));
        }

        public DamagePortionSelector Selector { get; }

        internal override PeriodicDamageModifierContribution CreateContribution(
            ContributionId contributionId,
            ModifierOwnerId ownerId,
            InstallationSequence installationSequence) =>
            new PeriodicDamageModifierContribution.DamageAmount(
                contributionId,
                ownerId,
                installationSequence,
                Selector,
                Operation);
    }

    public sealed record Interval : PeriodicDamageModifierDefinition
    {
        public Interval(ValueOperation operation)
            : base(operation)
        {
        }

        internal override PeriodicDamageModifierContribution CreateContribution(
            ContributionId contributionId,
            ModifierOwnerId ownerId,
            InstallationSequence installationSequence) =>
            new PeriodicDamageModifierContribution.Interval(
                contributionId,
                ownerId,
                installationSequence,
                Operation);
    }

    public sealed record CompletionValue : PeriodicDamageModifierDefinition
    {
        public CompletionValue(ValueOperation operation)
            : base(operation)
        {
        }

        internal override PeriodicDamageModifierContribution CreateContribution(
            ContributionId contributionId,
            ModifierOwnerId ownerId,
            InstallationSequence installationSequence) =>
            new PeriodicDamageModifierContribution.CompletionValue(
                contributionId,
                ownerId,
                installationSequence,
                Operation);
    }
}
