using BattleArena.Core.Combat;
using BattleArena.Core.Common;

namespace BattleArena.Core.Effects;

public abstract record PeriodicDamageModifierContribution
{
    private PeriodicDamageModifierContribution(
        ContributionId id,
        ModifierOwnerId ownerId,
        InstallationSequence installationSequence,
        ValueOperation operation)
    {
        Id = id;
        OwnerId = ownerId;
        InstallationSequence = installationSequence;
        Operation = operation;
    }

    public ContributionId Id { get; }

    public ModifierOwnerId OwnerId { get; }

    public InstallationSequence InstallationSequence { get; }

    public ValueOperation Operation { get; }

    public sealed record DamageAmount : PeriodicDamageModifierContribution
    {
        public DamageAmount(
            ContributionId id,
            ModifierOwnerId ownerId,
            InstallationSequence installationSequence,
            DamagePortionSelector selector,
            ValueOperation operation)
            : base(id, ownerId, installationSequence, operation)
        {
            Selector = selector ?? throw new ArgumentNullException(nameof(selector));
        }

        public DamagePortionSelector Selector { get; }
    }

    public sealed record Interval : PeriodicDamageModifierContribution
    {
        public Interval(
            ContributionId id,
            ModifierOwnerId ownerId,
            InstallationSequence installationSequence,
            ValueOperation operation)
            : base(id, ownerId, installationSequence, operation)
        {
        }
    }

    public sealed record CompletionValue : PeriodicDamageModifierContribution
    {
        public CompletionValue(
            ContributionId id,
            ModifierOwnerId ownerId,
            InstallationSequence installationSequence,
            ValueOperation operation)
            : base(id, ownerId, installationSequence, operation)
        {
        }
    }
}
