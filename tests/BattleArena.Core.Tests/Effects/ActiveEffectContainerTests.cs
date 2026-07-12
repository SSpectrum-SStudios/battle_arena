using BattleArena.Core.Combat;
using BattleArena.Core.Common;
using BattleArena.Core.Effects;

namespace BattleArena.Core.Tests.Effects;

public sealed class ActiveEffectContainerTests
{
    [Fact]
    public void CombatantOwnsAnActiveEffectContainerForItsCurrentLife()
    {
        var combatant = new Combatant(new CombatantId(7), 100d);

        Assert.Equal(new CombatantId(7), combatant.ActiveEffects.OwnerCombatantId);
        Assert.Equal(new LifeGenerationId(1), combatant.ActiveEffects.CurrentLifeGenerationId);
    }

    [Fact]
    public void NewLifeRemovesPerLifeEffectsButPreservesLongerScopes()
    {
        var owner = new CombatantId(1);
        var container = new ActiveEffectContainer(owner, new LifeGenerationId(1));
        container.Add(Effect(1, owner, EffectLifetimeScope.PerLife));
        container.Add(Effect(2, owner, EffectLifetimeScope.PerRound));

        var removed = container.BeginNewLife(new LifeGenerationId(2));

        Assert.Equal([new ActiveEffectId(1)], removed);
        Assert.False(container.TryGet(new ActiveEffectId(1), out _));
        Assert.True(container.TryGet(new ActiveEffectId(2), out _));
        Assert.Equal(new LifeGenerationId(2), container.CurrentLifeGenerationId);
    }

    [Fact]
    public void OldWorldEffectQuotaDoesNotLiveInAttachedEffectContainer()
    {
        var owner = new CombatantId(1);
        var container = new ActiveEffectContainer(owner, new LifeGenerationId(1));
        container.Add(Effect(1, owner, EffectLifetimeScope.PerLife));

        container.BeginNewLife(new LifeGenerationId(2));

        Assert.Empty(container.Effects);
    }

    [Fact]
    public void RejectsEffectTargetingAnotherCombatant()
    {
        var container = new ActiveEffectContainer(
            new CombatantId(1),
            new LifeGenerationId(1));

        Assert.Throws<ArgumentException>(
            () => container.Add(Effect(1, new CombatantId(2), EffectLifetimeScope.PerLife)));
    }

    [Fact]
    public void RemovalIsIdempotent()
    {
        var owner = new CombatantId(1);
        var container = new ActiveEffectContainer(owner, new LifeGenerationId(1));
        container.Add(Effect(1, owner, EffectLifetimeScope.PerLife));

        Assert.True(container.Remove(new ActiveEffectId(1)));
        Assert.False(container.Remove(new ActiveEffectId(1)));
    }

    private static ActiveEffectInstance Effect(
        long id,
        CombatantId target,
        EffectLifetimeScope scope) =>
        new(
            new ActiveEffectId(id),
            new EffectDefinitionId("base:test_effect"),
            new CombatantId(99),
            target,
            new LifeGenerationId(1),
            scope,
            new PeriodicEffectSchedule(
                SimulationInstant.Zero,
                SimulationDuration.FromSeconds(1m),
                FirstTickPolicy.AfterInterval,
                new PeriodicCompletionPolicy.AfterTickCount(1)));
}
