using BattleArena.Core.Common;

namespace BattleArena.Core.Tests.Effects;

public sealed class SimulationTimeTests
{
    [Fact]
    public void ConvertsAuthoredSecondsToIntegerPhysicsTicks()
    {
        var duration = new SimulationRate(60).DurationFromSeconds(0.5m);

        Assert.Equal(30, duration.Ticks);
    }

    [Fact]
    public void RoundsFractionalTicksDeterministically()
    {
        var rate = new SimulationRate(60);

        Assert.Equal(1, rate.DurationFromSeconds(0.01m).Ticks);
        Assert.Equal(2, rate.DurationFromSeconds(0.025m).Ticks);
    }

    [Fact]
    public void PositiveSubTickDurationClampsToOneTick()
    {
        var duration = new SimulationRate(60).DurationFromSeconds(0.000_001m);

        Assert.Equal(1, duration.Ticks);
    }

    [Fact]
    public void ZeroSecondsRemainsZeroTicks()
    {
        var duration = new SimulationRate(60).DurationFromSeconds(0m);

        Assert.Equal(SimulationDuration.Zero, duration);
    }

    [Fact]
    public void SimulationInstantUsesCheckedIntegerAddition()
    {
        var instant = new SimulationInstant(100);
        var duration = new SimulationDuration(25);

        Assert.Equal(new SimulationInstant(125), instant + duration);
    }
}
