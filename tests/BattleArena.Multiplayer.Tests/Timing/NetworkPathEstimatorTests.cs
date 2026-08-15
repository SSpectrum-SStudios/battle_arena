using BattleArena.Multiplayer.Timing;

namespace BattleArena.Multiplayer.Tests.Timing;

public sealed class NetworkPathEstimatorTests
{
    [Fact]
    public void TracksRttJitterLossAndReorderingPerStream()
    {
        var estimator = new NetworkPathEstimator(60);
        estimator.ObserveRoundTrip(80);
        estimator.ObserveRoundTrip(100);
        estimator.ObservePacket(1, 1_000_000);
        estimator.ObservePacket(3, 1_033_334);
        estimator.ObservePacket(2, 1_034_000);

        var estimate = estimator.Current;

        Assert.InRange(estimate.SmoothedRttMilliseconds, 82d, 83d);
        Assert.True(estimate.RttJitterMilliseconds > 0d);
        Assert.Equal(1, estimate.MissingPackets);
        Assert.Equal(1, estimate.ReorderedPackets);
        Assert.Equal(3UL, estimate.LatestSequence);
        Assert.True(estimate.EstimatedLossRate > 0d);
    }

    [Fact]
    public void StableCadenceKeepsArrivalJitterNearZero()
    {
        var estimator = new NetworkPathEstimator(60);
        estimator.ObservePacket(1, 1_000_000);
        estimator.ObservePacket(2, 1_016_667);
        estimator.ObservePacket(3, 1_033_334);

        Assert.InRange(estimator.Current.ArrivalJitterMilliseconds, 0d, 0.001d);
    }
}
