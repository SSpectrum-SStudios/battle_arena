using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Multiplayer.Connection;
using BattleArena.Multiplayer.Replication;
using BattleArena.Multiplayer.Timing;

namespace BattleArena.Multiplayer.Tests.Prediction;

public sealed class PredictionQualitySimulationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(40)]
    [InlineData(80)]
    public void StableLowLatencyPathsUseAtMostTwoPresentationTicks(double rttMilliseconds)
    {
        var policy = new AdaptivePredictionTimingPolicy();
        var path = SimulatePath(rttMilliseconds);

        var decision = policy.Evaluate(new PredictionTimingContext(60, path, 0));

        Assert.InRange(decision.PresentationDelayTicks, 1, 2);
        Assert.Equal(9, decision.NormalPredictionLimitTicks);
        Assert.InRange(path.SmoothedRttMilliseconds,
            Math.Max(0, rttMilliseconds - 0.01), rttMilliseconds + 0.01);
    }

    [Theory]
    [InlineData(120)]
    [InlineData(200)]
    public void HighLatencyStillUsesBoundedPredictionRatherThanUnboundedDrift(
        double rttMilliseconds)
    {
        var policy = new AdaptivePredictionTimingPolicy();
        var path = SimulatePath(
            rttMilliseconds,
            jitterMilliseconds: 10,
            dropEvery: 20,
            reorderSequence: 50,
            duplicateSequence: 70);

        var decision = policy.Evaluate(new PredictionTimingContext(60, path, 1));

        Assert.InRange(decision.PresentationDelayTicks, 1, 6);
        Assert.Equal(9, decision.NormalPredictionLimitTicks);
        Assert.Equal(15, decision.FreezeAfterTicks);
        Assert.True(path.MissingPackets > 0);
        Assert.True(path.ReorderedPackets > 0);
        Assert.True(path.EstimatedLossRate > 0d);
    }

    [Fact]
    public void LossDuplicationAndReorderingConvergeOnCanonicalAcceptedSequence()
    {
        var timeline = Baseline();
        timeline.ObserveDirect(Direct(sequence: 11, authorityTick: 101));
        // Sequence 12 is initially lost; 13 arrives first.
        timeline.ObserveDirect(Direct(sequence: 13, authorityTick: 103));
        timeline.ObserveDirect(Direct(sequence: 12, authorityTick: 102));
        timeline.ObserveDirect(Direct(sequence: 12, authorityTick: 102));

        var speculative = Assert.IsType<RemoteMovementSampleWindow<string>>(
            timeline.Sample(103, 9, 15));
        Assert.Equal([11UL, 12UL, 13UL], speculative.PredictionCommands
            .Select(command => command.Command.Sequence));
        Assert.Equal(1, timeline.DuplicateDirectCommands);

        timeline.ObserveAccepted(Accepted(sequence: 13, authorityTick: 103));

        Assert.Equal(0, timeline.PendingDirectCommandCount);
        Assert.Single(Assert.IsType<RemoteMovementSampleWindow<string>>(
            timeline.Sample(103, 9, 15)).PredictionCommands);
    }

    [Fact]
    public void RepeatedMaliciousMismatchesAreCorrectedAndSurfacedWithoutGameplayAuthority()
    {
        var timeline = Baseline();
        for (ulong offset = 1; offset <= 8; offset++)
        {
            var sequence = 10 + offset;
            var tick = 100 + offset;
            timeline.ObserveDirect(Direct(sequence, tick));
            timeline.ObserveAccepted(Accepted(
                sequence,
                tick,
                movement: new HorizontalVector(1, 0)));
        }

        Assert.Equal(8, timeline.DirectMismatches);
        Assert.False(timeline.DirectStateHintsAllowed);
        Assert.True(timeline.DirectRouteQuarantineRecommended);
        Assert.Equal(0, timeline.PendingDirectCommandCount);
        var sample = Assert.IsType<RemoteMovementSampleWindow<string>>(
            timeline.Sample(108, 9, 15));
        Assert.All(sample.PredictionCommands,
            command => Assert.Equal(new HorizontalVector(1, 0), command.Command.Movement));
    }

    [Fact]
    public void AuthoritativeFrameRepairsAndRecordsSpeculativeEvidence()
    {
        var timeline = Baseline();
        timeline.ObserveDirect(Direct(11, 101));
        timeline.ObserveDirect(Direct(12, 102));

        timeline.ObserveAuthority(new RemoteMovementFrame<string>(102, 1, 12, "canonical"));

        Assert.Equal(2, timeline.AuthorityPrunedDirectCommands);
        Assert.Equal(0, timeline.PendingDirectCommandCount);
        Assert.Equal("canonical", timeline.LatestAuthorityFrame?.State);
    }

    private static RemoteMovementTimeline<string> Baseline()
    {
        var timeline = new RemoteMovementTimeline<string>();
        timeline.ObserveAuthority(new RemoteMovementFrame<string>(100, 1, 10, "baseline"));
        return timeline;
    }

    private static DirectMovementCommand Direct(ulong sequence, ulong authorityTick) => new(
        new SessionPeerId(2),
        ConnectionGeneration.Initial,
        combatantId: 2,
        lifeId: 1,
        bundleSequence: sequence,
        estimatedAuthorityTick: authorityTick,
        Command(sequence, new HorizontalVector(0, -1)));

    private static AcceptedMovementCommand Accepted(
        ulong sequence,
        ulong authorityTick,
        HorizontalVector? movement = null) => new(
        new SessionPeerId(2),
        ConnectionGeneration.Initial,
        combatantId: 2,
        lifeId: 1,
        appliedAuthorityTick: authorityTick,
        Command(sequence, movement ?? new HorizontalVector(0, -1)));

    private static MovementCommand Command(
        ulong sequence,
        HorizontalVector movement) => new(
        sequence,
        new SimulationInstant(checked((long)sequence)),
        movement,
        0,
        0);

    private static NetworkPathEstimate SimulatePath(
        double rttMilliseconds,
        double jitterMilliseconds = 0,
        int? dropEvery = null,
        ulong? reorderSequence = null,
        ulong? duplicateSequence = null)
    {
        var estimator = new NetworkPathEstimator(expectedPacketsPerSecond: 60);
        var arrivals = new List<(ulong Sequence, ulong Timestamp)>();
        const ulong start = 1_000_000;
        const ulong interval = 16_667;
        var oneWayMicroseconds = checked((long)Math.Round(rttMilliseconds * 500d));
        var jitterMicroseconds = checked((long)Math.Round(jitterMilliseconds * 1_000d));
        for (ulong sequence = 1; sequence <= 120; sequence++)
        {
            if (dropEvery is not null && sequence % (ulong)dropEvery.Value == 0)
            {
                continue;
            }

            var sentAt = start + ((sequence - 1) * interval);
            var signedJitter = jitterMicroseconds == 0
                ? 0
                : sequence % 2 == 0 ? jitterMicroseconds : -jitterMicroseconds;
            var delay = Math.Max(0, oneWayMicroseconds + signedJitter);
            if (reorderSequence == sequence)
            {
                delay += 50_000;
            }

            var arrivedAt = checked((ulong)(checked((long)sentAt) + delay));
            arrivals.Add((sequence, arrivedAt));
            if (duplicateSequence == sequence)
            {
                arrivals.Add((sequence, arrivedAt + 1_000));
            }

            if (sequence % 15 == 0)
            {
                estimator.ObserveRoundTrip(Math.Max(
                    0d,
                    rttMilliseconds + (signedJitter / 1_000d)));
            }
        }

        foreach (var arrival in arrivals.OrderBy(value => value.Timestamp))
        {
            estimator.ObservePacket(arrival.Sequence, arrival.Timestamp);
        }

        return estimator.Current;
    }
}
