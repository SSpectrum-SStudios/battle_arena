using BattleArena.Core.Actions;
using BattleArena.Core.Application;
using BattleArena.Core.Combat;
using BattleArena.Core.Common;
using BattleArena.Core.Effects;
using BattleArena.Core.Influences;
using BattleArena.Core.Tests.Effects;

namespace BattleArena.Core.Tests.Application;

public sealed class ActiveEffectInfluenceTests
{
    private static readonly CombatantId AuraOwnerId = new(1);
    private static readonly CombatantId PoisonSourceId = new(2);
    private static readonly CombatantId TargetId = new(3);

    [Fact]
    public void EnteringModifiesExistingMatchingEffectsAndExitingRestoresThem()
    {
        var setup = Setup(PoisonAura());
        ApplyPoison(setup.Facade, setup.PoisonAction, PoisonSourceId, TargetId);

        var influence = BeginAura(setup.Facade, setup.Influence);
        var entered = setup.Facade.EnterInfluence(influence, TargetId);
        var modified = Assert.Single(setup.Facade.GetPeriodicDamageEffectSnapshots());

        Assert.Equal(InfluenceMembershipStatus.Entered, entered.Status);
        Assert.Equal(20d, Assert.Single(modified.TickDamagePortions).Amount);
        Assert.Equal(1, modified.ModifierCount);

        var exited = setup.Facade.ExitInfluence(influence, TargetId);
        var restored = Assert.Single(setup.Facade.GetPeriodicDamageEffectSnapshots());

        Assert.Equal(InfluenceMembershipStatus.Exited, exited.Status);
        Assert.Equal(10d, Assert.Single(restored.TickDamagePortions).Amount);
        Assert.Equal(0, restored.ModifierCount);
    }

    [Fact]
    public void NewMatchingEffectsAreModifiedWhileMembershipRemainsActive()
    {
        var setup = Setup(PoisonAura());
        var influence = BeginAura(setup.Facade, setup.Influence);
        setup.Facade.EnterInfluence(influence, TargetId);

        ApplyPoison(setup.Facade, setup.PoisonAction, PoisonSourceId, TargetId);

        var effect = Assert.Single(setup.Facade.GetPeriodicDamageEffectSnapshots());
        Assert.Equal(20d, Assert.Single(effect.TickDamagePortions).Amount);
        Assert.Equal(1, effect.ModifierCount);
    }

    [Fact]
    public void OwnerOnlySourceFilterDoesNotModifyAnotherSourcesPoison()
    {
        var influenceDefinition = PoisonAura(
            sourceFilter: CombatantRelationshipFilter.Self);
        var setup = Setup(influenceDefinition);
        ApplyPoison(setup.Facade, setup.PoisonAction, AuraOwnerId, TargetId);
        ApplyPoison(setup.Facade, setup.PoisonAction, PoisonSourceId, TargetId);

        var influence = BeginAura(setup.Facade, influenceDefinition);
        setup.Facade.EnterInfluence(influence, TargetId);

        var effects = setup.Facade.GetPeriodicDamageEffectSnapshots();
        var ownerPoison = effects.Single(effect => effect.Effect.SourceCombatantId == AuraOwnerId);
        var otherPoison = effects.Single(effect => effect.Effect.SourceCombatantId == PoisonSourceId);
        Assert.Equal(20d, Assert.Single(ownerPoison.TickDamagePortions).Amount);
        Assert.Equal(10d, Assert.Single(otherPoison.TickDamagePortions).Amount);
    }

    [Fact]
    public void AllyTargetFilterUsesRegisteredTeams()
    {
        var influenceDefinition = PoisonAura(
            targetFilter: CombatantRelationshipFilter.Allies);
        var setup = Setup(
            influenceDefinition,
            auraOwnerTeam: new TeamId(1),
            poisonSourceTeam: new TeamId(2),
            targetTeam: new TeamId(1));
        var enemyId = new CombatantId(4);
        Register(setup.Facade, enemyId, teamId: new TeamId(2));
        var influence = BeginAura(setup.Facade, influenceDefinition);

        Assert.Equal(
            InfluenceMembershipStatus.Entered,
            setup.Facade.EnterInfluence(influence, TargetId).Status);
        Assert.Equal(
            InfluenceMembershipStatus.TargetRejected,
            setup.Facade.EnterInfluence(influence, enemyId).Status);
    }

    [Fact]
    public void NonMatchingEffectTagsAreUnaffected()
    {
        var setup = Setup(PoisonAura(), periodicTag: new EffectTag("base:fire"));
        ApplyPoison(setup.Facade, setup.PoisonAction, PoisonSourceId, TargetId);
        var influence = BeginAura(setup.Facade, setup.Influence);

        setup.Facade.EnterInfluence(influence, TargetId);

        var effect = Assert.Single(setup.Facade.GetPeriodicDamageEffectSnapshots());
        Assert.Equal(10d, Assert.Single(effect.TickDamagePortions).Amount);
        Assert.Equal(0, effect.ModifierCount);
    }

    [Fact]
    public void ToggleOffRemovesContributionsFromEveryMember()
    {
        var setup = Setup(PoisonAura());
        ApplyPoison(setup.Facade, setup.PoisonAction, PoisonSourceId, TargetId);
        var influence = BeginAura(setup.Facade, setup.Influence);
        setup.Facade.EnterInfluence(influence, TargetId);

        Assert.True(setup.Facade.EndInfluence(influence));

        var effect = Assert.Single(setup.Facade.GetPeriodicDamageEffectSnapshots());
        Assert.Equal(10d, Assert.Single(effect.TickDamagePortions).Amount);
        Assert.Equal(0, effect.ModifierCount);
        Assert.Contains(setup.Facade.DrainFacts(), fact =>
            fact is CombatFact.InfluenceEnded ended && ended.InfluenceId == influence);
    }

    [Fact]
    public void SourceDeathEndsAuraButDoesNotRemoveTargetPoison()
    {
        var lethalAction = ImmediateAction();
        var setup = Setup(PoisonAura(), additionalActions: [lethalAction]);
        ApplyPoison(setup.Facade, setup.PoisonAction, PoisonSourceId, TargetId);
        var influence = BeginAura(setup.Facade, setup.Influence);
        setup.Facade.EnterInfluence(influence, TargetId);

        var lethalExecution = setup.Facade
            .BeginAction(PoisonSourceId, lethalAction.Id)
            .ExecutionId!.Value;
        setup.Facade.RegisterHit(lethalExecution, AuraOwnerId);

        var poison = Assert.Single(setup.Facade.GetPeriodicDamageEffectSnapshots());
        Assert.Equal(10d, Assert.Single(poison.TickDamagePortions).Amount);
        Assert.Equal(0, poison.ModifierCount);
        Assert.False(setup.Facade.EndInfluence(influence));
    }

    [Fact]
    public void ShorterAuraIntervalCanMakeTickDueImmediately()
    {
        var influenceDefinition = PoisonAura(
            modifiers:
            [new PeriodicDamageModifierDefinition.Interval(
                new ValueOperation(ValueOperationKind.Multiply, 0.5d))]);
        var setup = Setup(influenceDefinition, intervalSeconds: 2m);
        ApplyPoison(setup.Facade, setup.PoisonAction, PoisonSourceId, TargetId);
        Advance(setup.Facade, 90);
        var influence = BeginAura(setup.Facade, influenceDefinition);

        setup.Facade.EnterInfluence(influence, TargetId);

        Assert.True(setup.Facade.TryGetCombatantSnapshot(TargetId, out var target));
        Assert.Equal(90d, target!.CurrentHealth);
        var effect = Assert.Single(setup.Facade.GetPeriodicDamageEffectSnapshots());
        Assert.Equal(TestSimulation.Duration(1m), effect.Interval);
        Assert.Equal(new SimulationInstant(150), effect.Effect.NextActionAt);
    }

    private static SetupResult Setup(
        ActiveEffectInfluenceDefinition influence,
        EffectTag? periodicTag = null,
        decimal intervalSeconds = 1m,
        TeamId? auraOwnerTeam = null,
        TeamId? poisonSourceTeam = null,
        TeamId? targetTeam = null,
        CombatActionDefinition[]? additionalActions = null)
    {
        var poisonAction = PoisonAction(periodicTag ?? new EffectTag("base:poison"), intervalSeconds);
        var actions = new[] { poisonAction }.Concat(additionalActions ?? []).ToArray();
        var facade = new CombatApplicationFacade(
            new CombatActionCatalog(actions),
            new CombatResolver(),
            new ActiveEffectInfluenceCatalog([influence]));
        Register(facade, AuraOwnerId, auraOwnerTeam);
        Register(facade, PoisonSourceId, poisonSourceTeam);
        Register(facade, TargetId, targetTeam);
        return new SetupResult(facade, poisonAction, influence);
    }

    private static ActiveEffectInfluenceDefinition PoisonAura(
        CombatantRelationshipFilter targetFilter = CombatantRelationshipFilter.Everyone,
        CombatantRelationshipFilter sourceFilter = CombatantRelationshipFilter.Everyone,
        PeriodicDamageModifierDefinition[]? modifiers = null) =>
        new(
            new ActiveEffectInfluenceDefinitionId("base:test_poison_aura"),
            targetFilter,
            sourceFilter,
            new EffectTagSpecification(requiredAll: [new EffectTag("base:poison")]),
            modifiers ??
            [
                new PeriodicDamageModifierDefinition.DamageAmount(
                    new DamagePortionSelector.ByDamageType(DamageType.Poison),
                    new ValueOperation(ValueOperationKind.Multiply, 2d)),
            ]);

    private static CombatActionDefinition PoisonAction(EffectTag tag, decimal intervalSeconds) =>
        new(
            new CombatActionDefinitionId("base:test_poison_hit"),
            new TargetHitPolicy.Limited(1),
            [new CombatActionEffectDefinition.ApplyPeriodicDamage(
                new PeriodicDamageEffectDefinition(
                    new EffectDefinitionId("base:test_periodic"),
                    [new PeriodicDamagePortionDefinition(
                        new DamagePortionId("primary_poison"),
                        DamageType.Poison,
                        10d)],
                    TestSimulation.Duration(intervalSeconds),
                    FirstTickPolicy.AfterInterval,
                    new PeriodicCompletionPolicy.AfterTickCount(5),
                    EffectLifetimeScope.PerLife,
                    [tag]))]);

    private static CombatActionDefinition ImmediateAction() =>
        new(
            new CombatActionDefinitionId("base:test_lethal"),
            new TargetHitPolicy.Limited(1),
            [new CombatActionEffectDefinition.ImmediateDamage(
                [new DamagePortion(DamageType.Physical, 100d)])]);

    private static void ApplyPoison(
        CombatApplicationFacade facade,
        CombatActionDefinition action,
        CombatantId source,
        CombatantId target)
    {
        var execution = facade.BeginAction(source, action.Id).ExecutionId!.Value;
        Assert.True(facade.RegisterHit(execution, target).Accepted);
        facade.EndAction(execution);
    }

    private static ActiveEffectInfluenceId BeginAura(
        CombatApplicationFacade facade,
        ActiveEffectInfluenceDefinition definition) =>
        facade.BeginInfluence(AuraOwnerId, definition.Id).InfluenceId!.Value;

    private static void Register(
        CombatApplicationFacade facade,
        CombatantId id,
        TeamId? teamId = null) =>
        facade.RegisterCombatant(
            new Combatant(id, 100d),
            new ResistanceProfileCompiler().Compile([]),
            teamId);

    private static void Advance(CombatApplicationFacade facade, int ticks)
    {
        for (var index = 0; index < ticks; index++)
        {
            facade.AdvanceOneTick();
        }
    }

    private sealed record SetupResult(
        CombatApplicationFacade Facade,
        CombatActionDefinition PoisonAction,
        ActiveEffectInfluenceDefinition Influence);
}
