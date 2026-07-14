using BattleArena.Core.Combat;
using BattleArena.Core.Common;
using BattleArena.Core.Effects;

namespace BattleArena.Core.Tests.Effects;

public sealed class PeriodicDamageEffectTests
{
    private readonly PeriodicDamageEffectFactory _factory = new();
    private readonly PeriodicDamageEffectExecutor _executor = new(new CombatResolver());
    private readonly ResistanceProfileCompiler _resistanceCompiler = new();

    [Fact]
    public void DefinitionDefensivelyCopiesDamageAndTags()
    {
        var portions = new[] { new DamagePortion(DamageType.Poison, 5d) };
        var tags = new[] { new EffectTag("base:poison") };
        var definition = Definition(portions, tags: tags);

        portions[0] = new DamagePortion(DamageType.Fire, 999d);
        tags[0] = new EffectTag("base:fire");

        Assert.Equal(DamageType.Poison, definition.TickDamagePortions[0].Type);
        Assert.Equal(5d, definition.TickDamagePortions[0].Amount);
        Assert.Equal(new EffectTag("base:poison"), Assert.Single(definition.Tags));
    }

    [Fact]
    public void TwoApplicationsOfOneDefinitionHaveIndependentSchedules()
    {
        var definition = Definition();
        var first = Instance(1, definition);
        var second = Instance(2, definition);

        first.Schedule.Advance(AtSeconds(1m));

        Assert.Equal(1, first.Schedule.ExecutedTicks);
        Assert.Equal(0, second.Schedule.ExecutedTicks);
        Assert.Equal(2, first.Schedule.RemainingTicks);
        Assert.Equal(3, second.Schedule.RemainingTicks);
    }

    [Fact]
    public void EveryTickBuildsANewPacketAndUsesCurrentResistance()
    {
        var target = new Combatant(new CombatantId(2), 100d);
        var effect = Instance(1, Definition());
        var noResistance = _resistanceCompiler.Compile([]);

        var first = _executor.ExecuteDue(
            effect,
            AtSeconds(1m),
            target,
            noResistance,
            Chain(1));

        var poisonImmunity = _resistanceCompiler.Compile(
            [PoisonResistance(id: 1, sequence: 1, amount: 1d)]);
        var second = _executor.ExecuteDue(
            effect,
            AtSeconds(2m),
            target,
            poisonImmunity,
            Chain(2));

        Assert.True(first.AppliedTick);
        Assert.True(second.AppliedTick);
        Assert.NotSame(first.DamagePacket, second.DamagePacket);
        Assert.Equal(90d, target.CurrentHealth, precision: 10);
        Assert.Equal(10d, first.CombatResolution!.TotalDamage);
        Assert.Equal(0d, second.CombatResolution!.TotalDamage);
        Assert.Equal(new EffectChainId(1), first.ChainId);
        Assert.Equal(new EffectChainId(2), second.ChainId);
    }

    [Fact]
    public void CurrentOverResistanceCanTurnALaterTickIntoHealing()
    {
        var target = CombatantAt(currentHealth: 50d, maximumHealth: 100d);
        var effect = Instance(1, Definition(damagePerTick: 20d));
        var overResistance = _resistanceCompiler.Compile(
            [PoisonResistance(id: 1, sequence: 1, amount: 1.5d)]);

        var result = _executor.ExecuteDue(
            effect,
            AtSeconds(1m),
            target,
            overResistance,
            Chain(1));

        Assert.Equal(10d, result.CombatResolution!.TotalHealing, precision: 10);
        Assert.Equal(60d, target.CurrentHealth, precision: 10);
        Assert.Equal(10d, result.HealthApplication!.ActualHealthGained, precision: 10);
    }

    [Fact]
    public void CountBasedPoisonAppliesExactlyItsAuthoredNumberOfTicks()
    {
        var target = new Combatant(new CombatantId(2), 100d);
        var effect = Instance(1, Definition(damagePerTick: 5d, ticks: 5));
        var resistance = _resistanceCompiler.Compile([]);

        for (var second = 1; second <= 5; second++)
        {
            var result = _executor.ExecuteDue(
                effect,
                AtSeconds(second),
                target,
                resistance,
                Chain(second));
            Assert.True(result.AppliedTick);
        }

        Assert.Equal(75d, target.CurrentHealth, precision: 10);
        Assert.Equal(5, effect.Schedule.ExecutedTicks);
        Assert.True(effect.Schedule.IsExpired);
    }

    [Fact]
    public void ExhaustedChainBudgetDoesNotConsumeScheduledTick()
    {
        var target = new Combatant(new CombatantId(2), 100d);
        var effect = Instance(1, Definition());
        var chain = Chain(1, maximumOperations: 1);
        Assert.Equal(TriggerBudgetConsumeStatus.Consumed, chain.TryConsumeOperation());

        var result = _executor.ExecuteDue(
            effect,
            AtSeconds(1m),
            target,
            _resistanceCompiler.Compile([]),
            chain);

        Assert.Equal(PeriodicDamageTickExecutionStatus.ChainBudgetExhausted, result.Status);
        Assert.Equal(0, effect.Schedule.ExecutedTicks);
        Assert.Equal(100d, target.CurrentHealth);
    }

    [Fact]
    public void ExecutorRejectsWrongTargetCombatant()
    {
        var effect = Instance(1, Definition());
        var wrongTarget = new Combatant(new CombatantId(3), 100d);

        Assert.Throws<ArgumentException>(() =>
            _executor.ExecuteDue(
                effect,
                AtSeconds(1m),
                wrongTarget,
                _resistanceCompiler.Compile([]),
                Chain(1)));
    }

    private static PeriodicDamageEffectDefinition Definition(
        double damagePerTick = 10d,
        int ticks = 3,
        DamagePortion[]? portions = null,
        EffectTag[]? tags = null) =>
        Definition(
            portions ?? [new DamagePortion(DamageType.Poison, damagePerTick)],
            ticks,
            tags);

    private static PeriodicDamageEffectDefinition Definition(
        DamagePortion[] portions,
        int ticks = 3,
        EffectTag[]? tags = null) =>
        new(
            new EffectDefinitionId("base:lesser_poison"),
            portions,
            TestSimulation.Duration(1m),
            FirstTickPolicy.AfterInterval,
            new PeriodicCompletionPolicy.AfterTickCount(ticks),
            EffectLifetimeScope.PerLife,
            tags ?? [new EffectTag("base:poison")]);

    private PeriodicDamageEffectInstance Instance(
        long id,
        PeriodicDamageEffectDefinition definition) =>
        _factory.Create(
            new ActiveEffectId(id),
            definition,
            new CombatantId(1),
            new CombatantId(2),
            new LifeGenerationId(1),
            SimulationInstant.Zero);

    private static ResistanceContribution PoisonResistance(
        long id,
        long sequence,
        double amount) =>
        new(
            new ContributionId(id),
            new InstallationSequence(sequence),
            ResistanceStatKey.ForDamageType(DamageType.Poison, ResistanceMeasure.Percentage),
            new ValueOperation(ValueOperationKind.Add, amount));

    private static EffectChainContext Chain(long id, int maximumOperations = 100) =>
        new(new EffectChainId(id), maximumOperations);

    private static SimulationInstant AtSeconds(decimal seconds) => TestSimulation.At(seconds);

    private static Combatant CombatantAt(double currentHealth, double maximumHealth)
    {
        var combatant = new Combatant(new CombatantId(2), maximumHealth);
        var damage = new CombatResolutionResult(
            new CombatantId(99),
            [new ResolvedDamagePortion(DamageType.Physical, maximumHealth - currentHealth, 0d)]);
        combatant.Apply(damage);
        return combatant;
    }
}
