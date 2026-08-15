using BattleArena.Core.Actions;
using BattleArena.Core.Application;
using BattleArena.Core.Combat;
using BattleArena.Core.Common;
using BattleArena.Core.Effects;
using BattleArena.Core.Tests.Effects;

namespace BattleArena.Core.Tests.Application;

public sealed class CombatApplicationFacadeTests
{
    private static readonly CombatantId AttackerId = new(1);
    private static readonly CombatantId TargetId = new(2);

    [Fact]
    public void SimultaneousBatchPreservesMutualElimination()
    {
        var lethal = ImmediateAction("base:mutual_lethal", 100d);
        var facade = Facade(lethal);
        Register(facade, AttackerId, 100d);
        Register(facade, TargetId, 100d);
        var attackerExecution =
            facade.BeginAction(AttackerId, lethal.Id).ExecutionId!.Value;
        var targetExecution =
            facade.BeginAction(TargetId, lethal.Id).ExecutionId!.Value;

        var results = facade.RegisterHitsSimultaneously(
        [
            new SimultaneousHitRequest(attackerExecution, TargetId),
            new SimultaneousHitRequest(targetExecution, AttackerId),
        ]);

        Assert.All(results, result => Assert.True(result.Accepted));
        AssertHealth(facade, AttackerId, 0d, eliminated: true);
        AssertHealth(facade, TargetId, 0d, eliminated: true);
    }

    [Fact]
    public void BasicSwordAcceptsOneHitAndRunsImmediateAndPeriodicDamage()
    {
        var action = SwordAction(physicalDamage: 15d, poisonDamage: 5d, poisonTicks: 2);
        var facade = Facade(action);
        Register(facade, AttackerId, 100d);
        Register(facade, TargetId, 100d);

        var execution = facade.BeginAction(AttackerId, action.Id).ExecutionId!.Value;
        var firstHit = facade.RegisterHit(execution, TargetId);
        var duplicateHit = facade.RegisterHit(execution, TargetId);
        Advance(facade, 60);

        Assert.True(firstHit.Accepted);
        Assert.Equal(RegisterHitStatus.RejectedByHitPolicy, duplicateHit.Status);
        AssertHealth(facade, TargetId, 80d, eliminated: false);
        Assert.Single(facade.GetActiveEffectSnapshots());
    }

    [Fact]
    public void SeparateSwordExecutionsApplyIndependentPoisons()
    {
        var action = SwordAction(physicalDamage: 0d, poisonDamage: 5d, poisonTicks: 2);
        var facade = Facade(action);
        Register(facade, AttackerId, 100d);
        Register(facade, TargetId, 100d);

        var first = facade.BeginAction(AttackerId, action.Id).ExecutionId!.Value;
        facade.RegisterHit(first, TargetId);
        facade.EndAction(first);
        var second = facade.BeginAction(AttackerId, action.Id).ExecutionId!.Value;
        facade.RegisterHit(second, TargetId);

        Assert.Equal(2, facade.GetActiveEffectSnapshots().Count);
    }

    [Fact]
    public void RepeatingAreaAcceptsHitsOnlyAtItsAuthoredInterval()
    {
        var action = new CombatActionDefinition(
            new CombatActionDefinitionId("base:test_fire_circle"),
            new TargetHitPolicy.Repeating(TestSimulation.Duration(2m)),
            [new CombatActionEffectDefinition.ImmediateDamage(
                [new DamagePortion(DamageType.Fire, 3d)])]);
        var facade = Facade(action);
        Register(facade, AttackerId, 100d);
        Register(facade, TargetId, 100d);

        var execution = facade.BeginAction(AttackerId, action.Id).ExecutionId!.Value;
        Assert.True(facade.RegisterHit(execution, TargetId).Accepted);
        Advance(facade, 119);
        Assert.Equal(
            RegisterHitStatus.RejectedByHitPolicy,
            facade.RegisterHit(execution, TargetId).Status);
        Advance(facade, 1);
        Assert.True(facade.RegisterHit(execution, TargetId).Accepted);

        AssertHealth(facade, TargetId, 94d, eliminated: false);
    }

    [Fact]
    public void EliminationImmediatelyClearsPerLifeEffectsAndEndsSourceActions()
    {
        var poisonAction = SwordAction(physicalDamage: 0d, poisonDamage: 5d, poisonTicks: 5);
        var lethalAction = ImmediateAction("base:lethal", 200d);
        var facade = Facade(poisonAction, lethalAction);
        Register(facade, AttackerId, 100d);
        Register(facade, TargetId, 100d);

        var poisonExecution = facade.BeginAction(AttackerId, poisonAction.Id).ExecutionId!.Value;
        facade.RegisterHit(poisonExecution, TargetId);
        var lethalExecution = facade.BeginAction(TargetId, lethalAction.Id).ExecutionId!.Value;
        facade.RegisterHit(lethalExecution, AttackerId);

        AssertHealth(facade, AttackerId, 0d, eliminated: true);
        Assert.Equal(
            RegisterHitStatus.ExecutionEnded,
            facade.RegisterHit(poisonExecution, TargetId).Status);
        Assert.Single(facade.GetActiveEffectSnapshots());

        var reverseLethalExecution = facade.BeginAction(TargetId, lethalAction.Id).ExecutionId!.Value;
        facade.RegisterHit(reverseLethalExecution, TargetId);

        Assert.Empty(facade.GetActiveEffectSnapshots());
    }

    [Fact]
    public void PoisonSurvivesSourceDeathButOldSourceLifeCannotReceiveBenefits()
    {
        var poisonAction = SwordAction(physicalDamage: 0d, poisonDamage: 5d, poisonTicks: 2);
        var lethalAction = ImmediateAction("base:lethal", 100d);
        var facade = Facade(poisonAction, lethalAction);
        Register(facade, AttackerId, 100d);
        Register(facade, TargetId, 100d);

        var sourceLife = new LifeGenerationId(1);
        var poisonExecution = facade.BeginAction(AttackerId, poisonAction.Id).ExecutionId!.Value;
        facade.RegisterHit(poisonExecution, TargetId);
        var lethalExecution = facade.BeginAction(TargetId, lethalAction.Id).ExecutionId!.Value;
        facade.RegisterHit(lethalExecution, AttackerId);
        facade.Respawn(AttackerId);
        Advance(facade, 60);

        AssertHealth(facade, TargetId, 95d, eliminated: false);
        Assert.False(facade.IsSourceLifeActive(AttackerId, sourceLife));

        var poisonFact = facade.DrainFacts()
            .OfType<CombatFact.DamageResolved>()
            .Last(fact => fact.ActiveEffectId is not null);
        Assert.Equal(sourceLife, poisonFact.SourceLifeGenerationId);
        Assert.Equal(AttackerId, poisonFact.SourceCombatantId);
    }

    [Fact]
    public void RespawnBeginsNewLifeAtFullEffectiveHealth()
    {
        var lethalAction = ImmediateAction("base:lethal", 200d);
        var facade = Facade(lethalAction);
        Register(facade, AttackerId, 100d);
        Register(facade, TargetId, 125d);

        var execution = facade.BeginAction(AttackerId, lethalAction.Id).ExecutionId!.Value;
        facade.RegisterHit(execution, TargetId);
        var result = facade.Respawn(TargetId);

        Assert.True(result.Respawned);
        var respawnFact = Assert.Single(
            facade.DrainFacts().OfType<CombatFact.CombatantRespawned>());
        Assert.Equal(new LifeGenerationId(2), respawnFact.LifeGenerationId);
        AssertHealth(facade, TargetId, 125d, eliminated: false);
    }

    [Fact]
    public void EveryPeriodicTickUsesTheCurrentResistanceProfile()
    {
        var poisonAction = SwordAction(physicalDamage: 0d, poisonDamage: 10d, poisonTicks: 2);
        var facade = Facade(poisonAction);
        Register(facade, AttackerId, 100d);
        Register(facade, TargetId, 100d);

        var execution = facade.BeginAction(AttackerId, poisonAction.Id).ExecutionId!.Value;
        facade.RegisterHit(execution, TargetId);
        Advance(facade, 60);

        var immune = new ResistanceProfileCompiler().Compile(
            [new ResistanceContribution(
                new ContributionId(1),
                new InstallationSequence(1),
                ResistanceStatKey.ForDamageType(DamageType.Poison, ResistanceMeasure.Percentage),
                new ValueOperation(ValueOperationKind.Add, 1d))]);
        Assert.True(facade.UpdateResistanceProfile(TargetId, immune));
        Advance(facade, 60);

        AssertHealth(facade, TargetId, 90d, eliminated: false);
    }

    [Fact]
    public void LethalOperationStopsLaterOperationsFromAttachingToCorpse()
    {
        var poison = PeriodicPoison(damage: 5d, ticks: 2);
        var action = new CombatActionDefinition(
            new CombatActionDefinitionId("base:lethal_then_poison"),
            new TargetHitPolicy.Limited(1),
            [
                new CombatActionEffectDefinition.ImmediateDamage(
                    [new DamagePortion(DamageType.Physical, 100d)]),
                new CombatActionEffectDefinition.ApplyPeriodicDamage(poison),
            ]);
        var facade = Facade(action);
        Register(facade, AttackerId, 100d);
        Register(facade, TargetId, 100d);

        var execution = facade.BeginAction(AttackerId, action.Id).ExecutionId!.Value;
        facade.RegisterHit(execution, TargetId);

        Assert.Empty(facade.GetActiveEffectSnapshots());
    }

    private static CombatApplicationFacade Facade(params CombatActionDefinition[] definitions) =>
        new(new CombatActionCatalog(definitions), new CombatResolver());

    private static void Register(
        CombatApplicationFacade facade,
        CombatantId id,
        double maximumHealth) =>
        facade.RegisterCombatant(
            new Combatant(id, maximumHealth),
            new ResistanceProfileCompiler().Compile([]));

    private static CombatActionDefinition SwordAction(
        double physicalDamage,
        double poisonDamage,
        int poisonTicks) =>
        new(
            new CombatActionDefinitionId("base:test_sword"),
            new TargetHitPolicy.Limited(1),
            [
                new CombatActionEffectDefinition.ImmediateDamage(
                    [new DamagePortion(DamageType.Physical, physicalDamage)]),
                new CombatActionEffectDefinition.ApplyPeriodicDamage(
                    PeriodicPoison(poisonDamage, poisonTicks)),
            ]);

    private static CombatActionDefinition ImmediateAction(string id, double damage) =>
        new(
            new CombatActionDefinitionId(id),
            new TargetHitPolicy.Limited(1),
            [new CombatActionEffectDefinition.ImmediateDamage(
                [new DamagePortion(DamageType.Physical, damage)])]);

    private static PeriodicDamageEffectDefinition PeriodicPoison(double damage, int ticks) =>
        new(
            new EffectDefinitionId("base:test_poison"),
            [new PeriodicDamagePortionDefinition(
                new DamagePortionId("primary_poison"),
                DamageType.Poison,
                damage)],
            TestSimulation.Duration(1m),
            FirstTickPolicy.AfterInterval,
            new PeriodicCompletionPolicy.AfterTickCount(ticks),
            EffectLifetimeScope.PerLife,
            [new EffectTag("base:poison")]);

    private static void Advance(CombatApplicationFacade facade, int ticks)
    {
        for (var index = 0; index < ticks; index++)
        {
            facade.AdvanceOneTick();
        }
    }

    private static void AssertHealth(
        CombatApplicationFacade facade,
        CombatantId id,
        double expectedHealth,
        bool eliminated)
    {
        Assert.True(facade.TryGetCombatantSnapshot(id, out var snapshot));
        Assert.Equal(expectedHealth, snapshot!.CurrentHealth);
        Assert.Equal(eliminated, snapshot.IsEliminated);
    }
}
