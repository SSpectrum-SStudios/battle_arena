using BattleArena.Core.Common;
using BattleArena.Core.Effects;

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

    private static ActiveEffectInstance Effect(long id, decimal intervalSeconds) =>
        new(
            new ActiveEffectId(id),
            new EffectDefinitionId("base:test_effect"),
            new CombatantId(1),
            new CombatantId(2),
            new LifeGenerationId(1),
            EffectLifetimeScope.PerLife,
            new PeriodicEffectSchedule(
                SimulationInstant.Zero,
                SimulationDuration.FromSeconds(intervalSeconds),
                FirstTickPolicy.AfterInterval,
                new PeriodicCompletionPolicy.AfterTickCount(1)));

    private static SimulationInstant AtSeconds(decimal seconds) =>
        new(SimulationDuration.FromSeconds(seconds).Microseconds);
}
