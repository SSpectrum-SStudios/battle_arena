using BattleArena.Multiplayer.Timing;

namespace BattleArena.Multiplayer.Tests.Timing;

public sealed class AuthorityClockSynchronizerTests
{
    [Fact]
    public void EstimatesAuthorityTickFromFourTimestampExchange()
    {
        var synchronizer = new AuthorityClockSynchronizer();
        synchronizer.Observe(new AuthorityClockExchange(
            ClientSendTimestampMicroseconds: 1_000_000,
            AuthorityReceiveTimestampMicroseconds: 2_040_000,
            AuthoritySendTimestampMicroseconds: 2_041_000,
            ClientReceiveTimestampMicroseconds: 1_081_000,
            AuthorityTick: 120,
            SimulationTicksPerSecond: 60));

        var estimate = synchronizer.Estimate(1_097_667);

        Assert.True(synchronizer.HasEstimate);
        Assert.InRange(estimate.ClockOffsetMilliseconds, 999.9d, 1_000.1d);
        Assert.InRange(estimate.SmoothedRttMilliseconds, 79.9d, 80.1d);
        // The estimate advances beyond the reply's authority tick by the
        // one-way transit time plus the additional local elapsed time.
        Assert.InRange(estimate.SimulationTick, 123.39d, 123.41d);
    }

    [Fact]
    public void SmoothsRttAndBoundsAbruptOffsetAdjustment()
    {
        var synchronizer = new AuthorityClockSynchronizer();
        synchronizer.Observe(Exchange(authorityOffsetMicroseconds: 1_000_000));
        synchronizer.Observe(Exchange(authorityOffsetMicroseconds: 1_100_000));

        var estimate = synchronizer.Current;

        Assert.InRange(estimate.ClockOffsetMilliseconds, 1_000d, 1_001d);
        Assert.True(estimate.Confidence > 0d);
    }

    [Fact]
    public void RejectsInvalidTimestampOrdering()
    {
        var synchronizer = new AuthorityClockSynchronizer();

        Assert.Throws<ArgumentOutOfRangeException>(() => synchronizer.Observe(
            new AuthorityClockExchange(100, 200, 199, 300, 1, 60)));
    }

    private static AuthorityClockExchange Exchange(ulong authorityOffsetMicroseconds) =>
        new(
            ClientSendTimestampMicroseconds: 1_000_000,
            AuthorityReceiveTimestampMicroseconds: 1_040_000 + authorityOffsetMicroseconds,
            AuthoritySendTimestampMicroseconds: 1_041_000 + authorityOffsetMicroseconds,
            ClientReceiveTimestampMicroseconds: 1_081_000,
            AuthorityTick: 60,
            SimulationTicksPerSecond: 60);
}
