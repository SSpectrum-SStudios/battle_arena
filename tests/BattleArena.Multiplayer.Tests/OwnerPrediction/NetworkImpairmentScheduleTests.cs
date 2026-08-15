using BattleArena.Multiplayer.OwnerPrediction;

namespace BattleArena.Multiplayer.Tests.OwnerPrediction;

public sealed class NetworkImpairmentScheduleTests
{
    [Fact]
    public void SameSeedProducesSameDecisionsIndependentOfEvaluationOrder()
    {
        var policy = CreateMixedPolicy();
        var first = new NetworkImpairmentSchedule(123_456UL, policy);
        var second = new NetworkImpairmentSchedule(123_456UL, policy);
        var expected = Enumerable.Range(0, 200)
            .Select(index => first.Evaluate(
                index % 2 == 0
                    ? NetworkImpairmentDirection.Upstream
                    : NetworkImpairmentDirection.Downstream,
                (ulong)index))
            .ToArray();

        var actual = Enumerable.Range(0, 200)
            .Reverse()
            .Select(index => new
            {
                Index = index,
                Decision = second.Evaluate(
                    index % 2 == 0
                        ? NetworkImpairmentDirection.Upstream
                        : NetworkImpairmentDirection.Downstream,
                    (ulong)index),
            })
            .OrderBy(result => result.Index)
            .Select(result => result.Decision)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void MixedScheduleMatchesCrossPlatformGoldenVector()
    {
        var schedule = new NetworkImpairmentSchedule(123_456UL, CreateMixedPolicy());
        var inputs = new[]
        {
            (NetworkImpairmentDirection.Upstream, 0UL),
            (NetworkImpairmentDirection.Downstream, 0UL),
            (NetworkImpairmentDirection.Upstream, 1UL),
            (NetworkImpairmentDirection.Upstream, 7UL),
            (NetworkImpairmentDirection.Upstream, 12UL),
            (NetworkImpairmentDirection.Downstream, 18UL),
            (NetworkImpairmentDirection.Upstream, 37UL),
            (NetworkImpairmentDirection.Downstream, 99UL),
            (NetworkImpairmentDirection.Upstream, ulong.MaxValue),
            (NetworkImpairmentDirection.Downstream, ulong.MaxValue),
        };
        var actual = inputs
            .Select(input => GoldenLine(
                input.Item1,
                input.Item2,
                schedule.Evaluate(input.Item1, input.Item2)))
            .ToArray();

        string[] expected =
        [
            "Upstream:0:None:295140:none:False:False",
            "Downstream:0:Burst:0:none:False:False",
            "Upstream:1:None:302062:322062:False:False",
            "Upstream:7:None:743513:none:True:False",
            "Upstream:12:None:1509199:none:False:True",
            "Downstream:18:None:682288:none:False:False",
            "Upstream:37:None:300425:320425:False:False",
            "Downstream:99:None:793641:none:False:False",
            "Upstream:18446744073709551615:Burst:0:none:False:False",
            "Downstream:18446744073709551615:Independent:0:none:False:False",
        ];

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void DifferentSeedsProduceDifferentMixedSchedule()
    {
        var first = new NetworkImpairmentSchedule(1UL, CreateMixedPolicy());
        var second = new NetworkImpairmentSchedule(2UL, CreateMixedPolicy());

        var decisionsDiffer = Enumerable.Range(0, 200).Any(index =>
            first.Evaluate(NetworkImpairmentDirection.Upstream, (ulong)index) !=
            second.Evaluate(NetworkImpairmentDirection.Upstream, (ulong)index));

        Assert.True(decisionsDiffer);
    }

    [Fact]
    public void NonePolicyNeverChangesDelivery()
    {
        var schedule = new NetworkImpairmentSchedule(0UL, NetworkImpairmentPolicy.None);

        foreach (var direction in Enum.GetValues<NetworkImpairmentDirection>())
        {
            foreach (var ordinal in new[] { 0UL, 1UL, ulong.MaxValue })
            {
                Assert.Equal(
                    new NetworkImpairmentDecision(
                        NetworkLossReason.None,
                        TimeSpan.Zero,
                        null,
                        Reordered: false,
                        Stalled: false),
                    schedule.Evaluate(direction, ordinal));
            }
        }
    }

    [Fact]
    public void DirectionUsesIndependentAsymmetricBaseDelay()
    {
        var schedule = new NetworkImpairmentSchedule(
            10UL,
            new NetworkImpairmentPolicy(
                upstreamBaseDelay: TimeSpan.FromMilliseconds(20),
                downstreamBaseDelay: TimeSpan.FromMilliseconds(80)));

        Assert.Equal(
            TimeSpan.FromMilliseconds(20),
            schedule.Evaluate(NetworkImpairmentDirection.Upstream, 5).PrimaryDelay);
        Assert.Equal(
            TimeSpan.FromMilliseconds(80),
            schedule.Evaluate(NetworkImpairmentDirection.Downstream, 5).PrimaryDelay);
    }

    [Fact]
    public void JitterIsBoundedAndNeverMakesDelayNegative()
    {
        var schedule = new NetworkImpairmentSchedule(
            999UL,
            new NetworkImpairmentPolicy(
                upstreamBaseDelay: TimeSpan.FromMilliseconds(5),
                maximumJitter: TimeSpan.FromMilliseconds(10)));
        var delays = Enumerable.Range(0, 1_000)
            .Select(index => schedule.Evaluate(
                NetworkImpairmentDirection.Upstream,
                (ulong)index).PrimaryDelay)
            .ToArray();

        Assert.All(
            delays,
            delay => Assert.InRange(delay, TimeSpan.Zero, TimeSpan.FromMilliseconds(15)));
        Assert.Contains(TimeSpan.Zero, delays);
        Assert.Contains(delays, delay => delay > TimeSpan.FromMilliseconds(5));
    }

    [Fact]
    public void CertainIndependentLossDropsWithoutDeliveryArtifacts()
    {
        var schedule = new NetworkImpairmentSchedule(
            1UL,
            new NetworkImpairmentPolicy(independentLossProbability: 1d));

        var decision = schedule.Evaluate(NetworkImpairmentDirection.Upstream, 0);

        Assert.True(decision.IsDropped);
        Assert.Equal(NetworkLossReason.Independent, decision.LossReason);
        Assert.Equal(TimeSpan.Zero, decision.PrimaryDelay);
        Assert.Null(decision.DuplicateDelay);
        Assert.False(decision.Reordered);
        Assert.False(decision.Stalled);
    }

    [Fact]
    public void AuthoredBurstHasExactSeededLengthAndInterval()
    {
        const int interval = 20;
        const int burstLength = 4;
        var schedule = new NetworkImpairmentSchedule(
            44UL,
            new NetworkImpairmentPolicy(
                burstIntervalPackets: interval,
                burstLengthPackets: burstLength));
        var decisions = Enumerable.Range(0, interval * 3)
            .Select(index => schedule.Evaluate(
                NetworkImpairmentDirection.Upstream,
                (ulong)index).LossReason)
            .ToArray();

        Assert.Equal(burstLength * 3, decisions.Count(reason => reason == NetworkLossReason.Burst));
        for (var index = 0; index < interval * 2; index++)
        {
            Assert.Equal(decisions[index], decisions[index + interval]);
        }


        var burstStart = Enumerable.Range(1, interval)
            .First(index =>
                decisions[index - 1] == NetworkLossReason.None &&
                decisions[index] == NetworkLossReason.Burst);
        Assert.All(
            decisions[burstStart..(burstStart + burstLength)],
            reason => Assert.Equal(NetworkLossReason.Burst, reason));
        Assert.Equal(NetworkLossReason.None, decisions[burstStart + burstLength]);
        Assert.Equal(
            NetworkLossReason.Burst,
            decisions[burstStart + interval]);
    }

    [Fact]
    public void BurstPeriodRemainsCorrectNearMaximumPacketOrdinal()
    {
        const int interval = 37;
        var schedule = new NetworkImpairmentSchedule(
            88UL,
            new NetworkImpairmentPolicy(
                burstIntervalPackets: interval,
                burstLengthPackets: 8));

        for (ulong distanceFromMaximum = 0; distanceFromMaximum < 100; distanceFromMaximum++)
        {
            var ordinal = ulong.MaxValue - distanceFromMaximum;
            Assert.Equal(
                schedule.Evaluate(
                    NetworkImpairmentDirection.Upstream,
                    ordinal % interval).LossReason,
                schedule.Evaluate(
                    NetworkImpairmentDirection.Upstream,
                    ordinal).LossReason);
        }
    }

    [Fact]
    public void RandomStreamsAreIndependentlyKeyedByDirection()
    {
        var schedule = new NetworkImpairmentSchedule(
            55UL,
            new NetworkImpairmentPolicy(
                upstreamBaseDelay: TimeSpan.FromMilliseconds(50),
                downstreamBaseDelay: TimeSpan.FromMilliseconds(50),
                maximumJitter: TimeSpan.FromMilliseconds(20),
                independentLossProbability: 0.1d,
                burstIntervalPackets: 31,
                burstLengthPackets: 3,
                duplicateProbability: 0.2d,
                duplicateSpacing: TimeSpan.FromMilliseconds(2),
                reorderProbability: 0.2d,
                reorderAdditionalDelay: TimeSpan.FromMilliseconds(30),
                stallProbability: 0.1d,
                stallDuration: TimeSpan.FromMilliseconds(80)));

        Assert.Contains(
            Enumerable.Range(0, 256),
            index => schedule.Evaluate(NetworkImpairmentDirection.Upstream, (ulong)index) !=
                     schedule.Evaluate(NetworkImpairmentDirection.Downstream, (ulong)index));
    }

    [Fact]
    public void DuplicateReorderAndStallComposeOntoPrimaryDelay()
    {
        var schedule = new NetworkImpairmentSchedule(
            1UL,
            new NetworkImpairmentPolicy(
                upstreamBaseDelay: TimeSpan.FromMilliseconds(10),
                duplicateProbability: 1d,
                duplicateSpacing: TimeSpan.FromMilliseconds(5),
                reorderProbability: 1d,
                reorderAdditionalDelay: TimeSpan.FromMilliseconds(20),
                stallProbability: 1d,
                stallDuration: TimeSpan.FromMilliseconds(30)));

        var decision = schedule.Evaluate(NetworkImpairmentDirection.Upstream, 0);

        Assert.False(decision.IsDropped);
        Assert.True(decision.Reordered);
        Assert.True(decision.Stalled);
        Assert.Equal(TimeSpan.FromMilliseconds(60), decision.PrimaryDelay);
        Assert.Equal(TimeSpan.FromMilliseconds(65), decision.DuplicateDelay);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-0.01d)]
    [InlineData(1.01d)]
    public void ProbabilitiesMustBeFiniteUnitInterval(double probability)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new NetworkImpairmentPolicy(
                independentLossProbability: probability));
    }

    [Fact]
    public void DurationAndProbabilityMustBeEnabledTogether()
    {
        Assert.Throws<ArgumentException>(
            () => new NetworkImpairmentPolicy(duplicateProbability: 0.5d));
        Assert.Throws<ArgumentException>(
            () => new NetworkImpairmentPolicy(
                duplicateProbability: 0d,
                duplicateSpacing: TimeSpan.FromMilliseconds(1)));
        Assert.Throws<ArgumentException>(
            () => new NetworkImpairmentPolicy(reorderProbability: 0.5d));
        Assert.Throws<ArgumentException>(
            () => new NetworkImpairmentPolicy(stallProbability: 0.5d));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 1)]
    [InlineData(10, 0)]
    [InlineData(10, 11)]
    public void BurstBoundsMustBeCoherent(int interval, int length)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new NetworkImpairmentPolicy(
                burstIntervalPackets: interval,
                burstLengthPackets: length));
    }

    [Fact]
    public void DurationsMustBeNonnegativeAndBounded()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new NetworkImpairmentPolicy(
                upstreamBaseDelay: TimeSpan.FromTicks(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new NetworkImpairmentPolicy(
                downstreamBaseDelay: TimeSpan.FromHours(1) + TimeSpan.FromTicks(1)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new NetworkImpairmentPolicy(
                maximumJitter: TimeSpan.FromHours(2)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new NetworkImpairmentPolicy(
                duplicateProbability: 1d,
                duplicateSpacing: TimeSpan.FromHours(1) + TimeSpan.FromTicks(1)));
    }

    [Fact]
    public void OneHourComponentMaximumsComposeWithCheckedPositiveJitter()
    {
        var schedule = new NetworkImpairmentSchedule(
            77UL,
            new NetworkImpairmentPolicy(
                upstreamBaseDelay: TimeSpan.FromHours(1),
                maximumJitter: TimeSpan.FromHours(1),
                duplicateProbability: 1d,
                duplicateSpacing: TimeSpan.FromHours(1),
                reorderProbability: 1d,
                reorderAdditionalDelay: TimeSpan.FromHours(1),
                stallProbability: 1d,
                stallDuration: TimeSpan.FromHours(1)));
        var decision = Enumerable.Range(0, 1_000)
            .Select(index => schedule.Evaluate(
                NetworkImpairmentDirection.Upstream,
                (ulong)index))
            .First(candidate => candidate.PrimaryDelay > TimeSpan.FromHours(3));

        Assert.InRange(
            decision.PrimaryDelay,
            TimeSpan.FromHours(3),
            TimeSpan.FromHours(4));
        Assert.Equal(
            TimeSpan.FromHours(1),
            decision.DuplicateDelay!.Value - decision.PrimaryDelay);
        Assert.True(decision.Reordered);
        Assert.True(decision.Stalled);
    }

    [Fact]
    public void UndefinedDirectionIsRejected()
    {
        var schedule = new NetworkImpairmentSchedule(1UL, NetworkImpairmentPolicy.None);

        Assert.Equal(
            "direction",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => schedule.Evaluate((NetworkImpairmentDirection)99, 0)).ParamName);
    }

    [Fact]
    public void NullPolicyIsRejected()
    {
        Assert.Equal(
            "policy",
            Assert.Throws<ArgumentNullException>(
                () => new NetworkImpairmentSchedule(1UL, null!)).ParamName);
    }

    private static NetworkImpairmentPolicy CreateMixedPolicy() =>
        new(
            upstreamBaseDelay: TimeSpan.FromMilliseconds(40),
            downstreamBaseDelay: TimeSpan.FromMilliseconds(80),
            maximumJitter: TimeSpan.FromMilliseconds(12),
            independentLossProbability: 0.07d,
            burstIntervalPackets: 37,
            burstLengthPackets: 3,
            duplicateProbability: 0.08d,
            duplicateSpacing: TimeSpan.FromMilliseconds(2),
            reorderProbability: 0.12d,
            reorderAdditionalDelay: TimeSpan.FromMilliseconds(30),
            stallProbability: 0.04d,
            stallDuration: TimeSpan.FromMilliseconds(100));

    private static string GoldenLine(
        NetworkImpairmentDirection direction,
        ulong ordinal,
        NetworkImpairmentDecision decision) =>
        $"{direction}:{ordinal}:{decision.LossReason}:{decision.PrimaryDelay.Ticks}:" +
        $"{decision.DuplicateDelay?.Ticks.ToString() ?? "none"}:" +
        $"{decision.Reordered}:{decision.Stalled}";
}
