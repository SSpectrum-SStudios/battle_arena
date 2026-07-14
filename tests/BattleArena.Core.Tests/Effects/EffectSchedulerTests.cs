using BattleArena.Core.Common;
using BattleArena.Core.Effects;
using BattleArena.Core.Combat;

namespace BattleArena.Core.Tests.Effects;

public sealed class EffectSchedulerTests
{
    [Fact]
    public void ReturnsOnlyDueEffectsInDueTimeThenIdentityOrder()
    {
        var scheduler = new EffectScheduler();
        var effects = new[]
        {
            Effect(id: 3, intervalSeconds: 1m),
            Effect(id: 2, intervalSeconds: 2m),
            Effect(id: 1, intervalSeconds: 1m),
            Effect(id: 4, intervalSeconds: 5m),
        };

        var due = scheduler.GetDueEffects(effects, AtSeconds(2m));

        Assert.Equal(
            new long[] { 1, 3, 2 },
            due.Select(static effect => effect.Id.Value));
    }

    private static ActiveEffectInstance Effect(long id, decimal intervalSeconds)
    {
        var definition = new PeriodicDamageEffectDefinition(
            new EffectDefinitionId("base:test_effect"),
            [new PeriodicDamagePortionDefinition(
                new DamagePortionId("primary_physical"),
                DamageType.Physical,
                1d)],
            TestSimulation.Duration(intervalSeconds),
            FirstTickPolicy.AfterInterval,
            new PeriodicCompletionPolicy.AfterTickCount(1));

        return new PeriodicDamageEffectFactory().Create(
            new ActiveEffectId(id),
            definition,
            new CombatantId(1),
            new CombatantId(2),
            new LifeGenerationId(1),
            SimulationInstant.Zero);
    }

    private static SimulationInstant AtSeconds(decimal seconds) => TestSimulation.At(seconds);
}
