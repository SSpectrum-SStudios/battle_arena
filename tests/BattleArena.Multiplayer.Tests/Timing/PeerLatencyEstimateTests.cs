using BattleArena.Multiplayer.Timing;

namespace BattleArena.Multiplayer.Tests.Timing;

public sealed class PeerLatencyEstimateTests
{
    [Fact]
    public void FirstSampleGrantsHalfRttPlusMinimumMargin()
    {
        var estimate = new PeerLatencyEstimate();

        estimate.Observe(120d);

        Assert.Equal(120d, estimate.SmoothedRttMilliseconds);
        Assert.Equal(0d, estimate.JitterMilliseconds);
        Assert.Equal(65d, estimate.RewindAllowanceMilliseconds);
    }

    [Fact]
    public void AllowanceNeverExceedsHardCap()
    {
        var estimate = new PeerLatencyEstimate();
        estimate.Observe(800d);
        estimate.Observe(1000d);

        Assert.Equal(
            PeerLatencyEstimate.MaximumRewindMilliseconds,
            estimate.RewindAllowanceMilliseconds);
    }

    [Theory]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidSamplesAreRejected(double sample)
    {
        var estimate = new PeerLatencyEstimate();

        Assert.Throws<ArgumentOutOfRangeException>(() => estimate.Observe(sample));
    }
}
