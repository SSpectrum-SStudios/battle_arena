using BattleArena.Core.Common;

namespace BattleArena.Core.Tests.Effects;

public sealed class SimulationTimeTests
{
    [Fact]
    public void ConvertsAuthoredSecondsToIntegerMicroseconds()
    {
        var duration = SimulationDuration.FromSeconds(0.5m);

        Assert.Equal(500_000, duration.Microseconds);
    }

    [Fact]
    public void RoundsSubMicrosecondValuesDeterministically()
    {
        var duration = SimulationDuration.FromSeconds(0.000_000_5m);

        Assert.Equal(1, duration.Microseconds);
    }

    [Fact]
    public void SimulationInstantUsesCheckedIntegerAddition()
    {
        var instant = new SimulationInstant(100);
        var duration = new SimulationDuration(25);

        Assert.Equal(new SimulationInstant(125), instant + duration);
    }
}
