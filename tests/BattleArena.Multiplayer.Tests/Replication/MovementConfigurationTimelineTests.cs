using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Multiplayer.Replication;

namespace BattleArena.Multiplayer.Tests.Replication;

public sealed class MovementConfigurationTimelineTests
{
    [Fact]
    public void ConfigurationIsUnavailableBeforeEffectiveTickAndExactRevision()
    {
        var timeline = new MovementConfigurationTimeline();
        var configuration = Configuration(life: 2, profile: 3, capabilities: 4, tick: 100);
        Assert.True(timeline.Apply(configuration));

        Assert.False(timeline.TryResolve(1, 2, 3, 4, 99, out _));
        Assert.False(timeline.TryResolve(1, 2, 2, 4, 100, out _));
        Assert.True(timeline.TryResolve(1, 2, 3, 4, 100, out var resolved));
        Assert.Same(configuration, resolved);
    }

    [Fact]
    public void NewLifeClearsOldConfigurationsAndRejectsDelayedLife()
    {
        var timeline = new MovementConfigurationTimeline();
        timeline.Apply(Configuration(2, 3, 4, 100));
        Assert.True(timeline.Apply(Configuration(3, 1, 1, 200)));

        Assert.False(timeline.TryResolve(1, 2, 3, 4, 201, out _));
        Assert.False(timeline.Apply(Configuration(2, 5, 5, 202)));
    }

    private static NetworkMovementConfiguration Configuration(
        ulong life,
        ulong profile,
        ulong capabilities,
        long tick) => new(
        1,
        life,
        new SimulationInstant(tick),
        new MovementAttributeSnapshot(
            profile,
            new GroundMovementAttributes(6, 13, 8, 10, 12, 20, 7, 2, -0.4)),
        MovementCapabilitySnapshot.CreateBaseFighter(capabilities));
}
