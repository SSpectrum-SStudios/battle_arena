using BattleArena.Core.Combat;
using BattleArena.Core.Common;
using BattleArena.Core.Effects;

namespace BattleArena.Core.Tests.Effects;

public sealed class PeriodicDamageModifierTests
{
    [Fact]
    public void DamageModifiersTargetTypeAndExecuteInInstallationOrder()
    {
        var effect = Instance(
            Definition(
                [
                    Portion("primary_poison", DamageType.Poison, 10d),
                    Portion("bonus_fire", DamageType.Fire, 4d),
                ]));

        effect.InstallModifier(
            DamageModifier(1, 1, 1, new DamagePortionSelector.ByDamageType(DamageType.Poison),
                ValueOperationKind.Add, 5d),
            SimulationInstant.Zero);
        effect.InstallModifier(
            DamageModifier(2, 2, 2, new DamagePortionSelector.ByDamageType(DamageType.Poison),
                ValueOperationKind.Multiply, 2d),
            SimulationInstant.Zero);

        Assert.Collection(
            effect.EffectiveValues.TickDamagePortions,
            poison =>
            {
                Assert.Equal(new DamagePortionId("primary_poison"), poison.Id);
                Assert.Equal(30d, poison.Amount);
            },
            fire =>
            {
                Assert.Equal(new DamagePortionId("bonus_fire"), fire.Id);
                Assert.Equal(4d, fire.Amount);
            });
    }

    [Fact]
    public void ExactSelectorChangesOnlyOneSameTypePortion()
    {
        var effect = Instance(
            Definition(
                [
                    Portion("primary_poison", DamageType.Poison, 10d),
                    Portion("splash_poison", DamageType.Poison, 3d),
                ]));

        effect.InstallModifier(
            DamageModifier(
                1,
                1,
                1,
                new DamagePortionSelector.Exact(new DamagePortionId("splash_poison")),
                ValueOperationKind.Multiply,
                3d),
            SimulationInstant.Zero);

        Assert.Equal(10d, effect.EffectiveValues.TickDamagePortions[0].Amount);
        Assert.Equal(9d, effect.EffectiveValues.TickDamagePortions[1].Amount);
    }

    [Fact]
    public void ShortenedIntervalInterruptsTheCurrentCycleImmediately()
    {
        var effect = Instance(Definition(intervalSeconds: 2m));
        var currentTime = AtSeconds(1.5m);

        effect.InstallModifier(
            IntervalModifier(1, 1, 1, ValueOperationKind.Multiply, 0.5d),
            currentTime);

        Assert.Equal(TestSimulation.Duration(1m), effect.EffectiveValues.Interval);
        Assert.Equal(currentTime, effect.Schedule.NextActionAt);
    }

    [Fact]
    public void TickCountChangesPreserveExecutedTicks()
    {
        var effect = Instance(Definition(ticks: 5));
        effect.Schedule.Advance(AtSeconds(1m));
        effect.Schedule.Advance(AtSeconds(2m));

        effect.InstallModifier(
            CompletionModifier(1, 1, 1, ValueOperationKind.Add, 3d),
            AtSeconds(2m));

        Assert.Equal(2, effect.Schedule.ExecutedTicks);
        Assert.Equal(6, effect.Schedule.RemainingTicks);

        effect.InstallModifier(
            CompletionModifier(2, 2, 2, ValueOperationKind.Add, -5d),
            AtSeconds(2m));

        Assert.Equal(2, effect.Schedule.ExecutedTicks);
        Assert.Equal(1, effect.Schedule.RemainingTicks);
        Assert.False(effect.Schedule.IsExpired);
    }

    [Fact]
    public void ReducingTotalTicksToExecutedTicksExpiresImmediately()
    {
        var effect = Instance(Definition(ticks: 5));
        effect.Schedule.Advance(AtSeconds(1m));
        effect.Schedule.Advance(AtSeconds(2m));

        effect.InstallModifier(
            CompletionModifier(1, 1, 1, ValueOperationKind.Replace, 2d),
            AtSeconds(2m));

        Assert.Equal(2, effect.Schedule.ExecutedTicks);
        Assert.Equal(0, effect.Schedule.RemainingTicks);
        Assert.True(effect.Schedule.IsExpired);
    }

    [Fact]
    public void CompletionPolicyTypeCannotChange()
    {
        var schedule = new PeriodicEffectSchedule(
            SimulationInstant.Zero,
            TestSimulation.Duration(1m),
            FirstTickPolicy.AfterInterval,
            new PeriodicCompletionPolicy.AfterTickCount(5));

        Assert.Throws<ArgumentException>(() =>
            schedule.ChangeCompletionValue(
                new PeriodicCompletionPolicy.AfterDuration(TestSimulation.Duration(5m)),
                SimulationInstant.Zero));
    }

    [Fact]
    public void RemovingAnOwnerRestoresDefinitionAndRemainingContributions()
    {
        var effect = Instance(Definition(damagePerTick: 10d));
        effect.InstallModifier(
            DamageModifier(1, 1, 1, new DamagePortionSelector.All(), ValueOperationKind.Add, 5d),
            SimulationInstant.Zero);
        effect.InstallModifier(
            DamageModifier(2, 2, 2, new DamagePortionSelector.All(), ValueOperationKind.Multiply, 2d),
            SimulationInstant.Zero);

        var removed = effect.RemoveModifiersOwnedBy(new ModifierOwnerId(1), SimulationInstant.Zero);

        Assert.Equal(1, removed);
        Assert.Equal(20d, Assert.Single(effect.EffectiveValues.TickDamagePortions).Amount);
        Assert.Single(effect.Modifiers);
    }

    [Fact]
    public void ARemovedTemporaryModifierDoesNotAffectLaterTicks()
    {
        var effect = Instance(Definition(damagePerTick: 10d, ticks: 2));
        var target = new Combatant(new CombatantId(2), 100d);
        var executor = new PeriodicDamageEffectExecutor(new CombatResolver());
        var resistance = new ResistanceProfileCompiler().Compile([]);

        effect.InstallModifier(
            DamageModifier(1, 1, 1, new DamagePortionSelector.All(), ValueOperationKind.Multiply, 2d),
            SimulationInstant.Zero);
        executor.ExecuteDue(effect, AtSeconds(1m), target, resistance, Chain(1));
        effect.RemoveModifiersOwnedBy(new ModifierOwnerId(1), AtSeconds(1m));
        executor.ExecuteDue(effect, AtSeconds(2m), target, resistance, Chain(2));

        Assert.Equal(70d, target.CurrentHealth);
    }

    [Fact]
    public void MissingExactPortionIsRejectedWithoutInstallingContribution()
    {
        var effect = Instance(Definition());

        Assert.Throws<ArgumentException>(() =>
            effect.InstallModifier(
                DamageModifier(
                    1,
                    1,
                    1,
                    new DamagePortionSelector.Exact(new DamagePortionId("missing")),
                    ValueOperationKind.Add,
                    5d),
                SimulationInstant.Zero));

        Assert.Empty(effect.Modifiers);
        Assert.Equal(10d, Assert.Single(effect.EffectiveValues.TickDamagePortions).Amount);
    }

    [Fact]
    public void ShorteningDurationPastElapsedTimeExpiresImmediately()
    {
        var effect = Instance(
            Definition(
                durationSeconds: 10m,
                intervalSeconds: 2m));
        effect.Schedule.Advance(AtSeconds(2m));

        effect.InstallModifier(
            CompletionModifier(1, 1, 1, ValueOperationKind.Replace, 120d),
            AtSeconds(3m));

        Assert.True(effect.Schedule.IsExpired);
        Assert.Equal(1, effect.Schedule.ExecutedTicks);
    }

    private static PeriodicDamageEffectDefinition Definition(
        PeriodicDamagePortionDefinition[]? portions = null,
        double damagePerTick = 10d,
        int ticks = 5,
        decimal intervalSeconds = 1m,
        decimal? durationSeconds = null) =>
        new(
            new EffectDefinitionId("base:test_poison"),
            portions ?? [Portion("primary_poison", DamageType.Poison, damagePerTick)],
            TestSimulation.Duration(intervalSeconds),
            FirstTickPolicy.AfterInterval,
            durationSeconds is { } duration
                ? new PeriodicCompletionPolicy.AfterDuration(TestSimulation.Duration(duration))
                : new PeriodicCompletionPolicy.AfterTickCount(ticks),
            EffectLifetimeScope.PerLife,
            [new EffectTag("base:poison")]);

    private static PeriodicDamagePortionDefinition Portion(
        string id,
        DamageType type,
        double amount) =>
        new(new DamagePortionId(id), type, amount);

    private static PeriodicDamageEffectInstance Instance(PeriodicDamageEffectDefinition definition) =>
        new PeriodicDamageEffectFactory().Create(
            new ActiveEffectId(1),
            definition,
            new CombatantId(1),
            new CombatantId(2),
            new LifeGenerationId(1),
            SimulationInstant.Zero);

    private static PeriodicDamageModifierContribution.DamageAmount DamageModifier(
        long id,
        long owner,
        long sequence,
        DamagePortionSelector selector,
        ValueOperationKind operation,
        double operand) =>
        new(
            new ContributionId(id),
            new ModifierOwnerId(owner),
            new InstallationSequence(sequence),
            selector,
            new ValueOperation(operation, operand));

    private static PeriodicDamageModifierContribution.Interval IntervalModifier(
        long id,
        long owner,
        long sequence,
        ValueOperationKind operation,
        double operand) =>
        new(
            new ContributionId(id),
            new ModifierOwnerId(owner),
            new InstallationSequence(sequence),
            new ValueOperation(operation, operand));

    private static PeriodicDamageModifierContribution.CompletionValue CompletionModifier(
        long id,
        long owner,
        long sequence,
        ValueOperationKind operation,
        double operand) =>
        new(
            new ContributionId(id),
            new ModifierOwnerId(owner),
            new InstallationSequence(sequence),
            new ValueOperation(operation, operand));

    private static EffectChainContext Chain(long id) => new(new EffectChainId(id), 100);

    private static SimulationInstant AtSeconds(decimal seconds) => TestSimulation.At(seconds);
}
