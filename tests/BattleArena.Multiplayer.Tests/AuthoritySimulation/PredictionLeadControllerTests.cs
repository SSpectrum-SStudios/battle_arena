using BattleArena.Core.Common;
using BattleArena.Core.Movement.Simulation;
using BattleArena.Multiplayer.AuthoritySimulation;
using BattleArena.Multiplayer.OwnerPrediction;
using BattleArena.Multiplayer.Timing;

namespace BattleArena.Multiplayer.Tests.AuthoritySimulation;

public sealed class PredictionLeadControllerTests
{
    // The redesign's supported envelope. Each profile must settle, and the
    // settled lead must grow with RTT without the step size ever changing.
    public static TheoryData<double> RttProfiles => new() { 0d, 40d, 80d, 120d, 200d, 300d };

    [Theory]
    [MemberData(nameof(RttProfiles))]
    public void EveryRttProfileConvergesAndStopsChanging(double rttMilliseconds)
    {
        var controller = Controller();
        var settledAt = RunUntilSettled(controller, rttMilliseconds, jitter: 10d);

        Assert.True(
            settledAt > 0,
            $"lead never settled at {rttMilliseconds} ms RTT");

        // Once settled it must stay settled: no further updates at all.
        for (var i = 0; i < 600; i++)
        {
            var evaluation = controller.Evaluate(Observation(
                frame: settledAt + i,
                rttMilliseconds,
                jitter: 10d,
                buffered: controller.Policy.AuthorityBufferTargetFrames));
            Assert.Equal(PredictionLeadControllerDecision.Unchanged, evaluation.Decision);
            Assert.Null(evaluation.Update);
        }
    }

    [Theory]
    [MemberData(nameof(RttProfiles))]
    public void SettledLeadCoversOneWayTravelPlusJitterPlusTheBufferTarget(
        double rttMilliseconds)
    {
        var controller = Controller();
        RunUntilSettled(controller, rttMilliseconds, jitter: 10d);

        var stepMilliseconds = 1_000d / 60d;
        var expectedFloor =
            (int)Math.Ceiling(rttMilliseconds / 2d / stepMilliseconds) +
            controller.Policy.AuthorityBufferTargetFrames;

        Assert.True(
            controller.CurrentLeadFrames >= Math.Min(expectedFloor, controller.LeadPolicy.MaximumLeadFrames)
                - controller.Policy.MaximumOccupancyReductionFrames,
            $"settled lead {controller.CurrentLeadFrames} is below the travel+buffer floor {expectedFloor}");
        Assert.InRange(
            controller.CurrentLeadFrames,
            controller.LeadPolicy.MinimumLeadFrames,
            controller.LeadPolicy.MaximumLeadFrames);
    }

    [Fact]
    public void HigherRttSettlesToAStrictlyLongerLead()
    {
        var settled = new List<int>();
        foreach (var rtt in new[] { 0d, 40d, 80d, 120d, 200d })
        {
            var controller = Controller();
            RunUntilSettled(controller, rtt, jitter: 0d);
            settled.Add(controller.CurrentLeadFrames);
        }

        for (var i = 1; i < settled.Count; i++)
        {
            Assert.True(
                settled[i] >= settled[i - 1],
                $"lead did not grow with RTT: {string.Join(", ", settled)}");
        }

        Assert.True(settled[^1] > settled[0]);
    }

    [Fact]
    public void AStableProfileNeverOscillates()
    {
        var controller = Controller();
        var updates = new List<int>();

        for (var frame = 0; frame < 4_000; frame++)
        {
            var evaluation = controller.Evaluate(Observation(
                frame,
                rtt: 120d,
                jitter: 8d,
                buffered: Math.Max(
                    0,
                    controller.Policy.AuthorityBufferTargetFrames + (frame % 5) - 2)));
            if (evaluation.Update is { } update)
            {
                updates.Add(update.TargetLead.Value);
            }
        }

        // Convergence means a bounded number of monotone moves, not a cycle.
        Assert.True(updates.Count <= 12, $"too many lead changes: {updates.Count}");
        var reversals = 0;
        for (var i = 2; i < updates.Count; i++)
        {
            var previous = Math.Sign(updates[i - 1] - updates[i - 2]);
            var current = Math.Sign(updates[i] - updates[i - 1]);
            if (previous != 0 && current != 0 && previous != current)
            {
                reversals++;
            }
        }

        Assert.True(reversals <= 1, $"lead oscillated {reversals} times: {string.Join(", ", updates)}");
    }

    [Fact]
    public void NoisyJitterDoesNotProduceAChangePerFrame()
    {
        var controller = Controller();
        var random = new Random(99);
        var changes = 0;

        for (var frame = 0; frame < 4_000; frame++)
        {
            var evaluation = controller.Evaluate(Observation(
                frame,
                rtt: 100d + random.NextDouble() * 40d,
                jitter: random.NextDouble() * 25d,
                buffered: random.Next(0, 5)));
            if (evaluation.Decision == PredictionLeadControllerDecision.Updated)
            {
                changes++;
            }
        }

        // 4000 frames is ~67 seconds; the change interval alone caps this well
        // below one change per frame.
        Assert.True(
                changes <= 4_000 / controller.Policy.MinimumFramesBetweenStarvationChanges + 2,
                $"{changes} changes in 4000 frames");
    }

    [Fact]
    public void StarvationReactsOnAMuchShorterClockThanAnOrdinaryChange()
    {
        var controller = Controller();
        var before = controller.CurrentLeadFrames;

        var first = controller.Evaluate(Observation(
            frame: 0, rtt: 40d, jitter: 0d, buffered: 0, starved: 3));
        Assert.Equal(PredictionLeadControllerDecision.Updated, first.Decision);
        Assert.Equal(PredictionLeadAdjustmentReason.StarvationRecovery, first.Reason);
        Assert.True(controller.CurrentLeadFrames > before);

        // Still rate limited, so the very next frame holds...
        var immediate = controller.Evaluate(Observation(
            frame: 1, rtt: 40d, jitter: 0d, buffered: 0, starved: 2));
        Assert.Equal(PredictionLeadControllerDecision.Unchanged, immediate.Decision);
        Assert.Equal(
            PredictionLeadAdjustmentReason.ChangeIntervalNotElapsed,
            immediate.Reason);

        // ...but it reacts far sooner than the ordinary increase interval.
        var starvationClock = controller.Policy.MinimumFramesBetweenStarvationChanges;
        Assert.True(starvationClock < controller.Policy.MinimumFramesBetweenIncreases);

        var afterStarvationClock = controller.Evaluate(Observation(
            frame: starvationClock, rtt: 40d, jitter: 0d, buffered: 0, starved: 2));
        Assert.Equal(PredictionLeadControllerDecision.Updated, afterStarvationClock.Decision);
        Assert.Equal(
            PredictionLeadAdjustmentReason.StarvationRecovery,
            afterStarvationClock.Reason);
    }

    [Fact]
    public void ShrinkingIsSlowerAndMoreReluctantThanGrowing()
    {
        var controller = Controller();
        RunUntilSettled(controller, rtt: 250d, jitter: 20d);
        var high = controller.CurrentLeadFrames;

        var decreases = new List<int>();
        for (var frame = 5_000; frame < 40_000; frame++)
        {
            var evaluation = controller.Evaluate(Observation(
                frame, rtt: 0d, jitter: 0d, buffered: controller.Policy.AuthorityBufferTargetFrames));
            if (evaluation.Update is { } update)
            {
                decreases.Add(update.TargetLead.Value);
            }
        }

        Assert.NotEmpty(decreases);
        Assert.True(controller.CurrentLeadFrames < high);

        // Every shrink respects the cap, and the last steps are the gentle ones:
        // the step scales down as the lead approaches target.
        var previous = high;
        foreach (var value in decreases)
        {
            Assert.True(previous - value <= controller.Policy.MaximumDecreaseStepFrames);
            previous = value;
        }

        Assert.True(
            decreases[^1] - controller.CurrentLeadFrames <= 1,
            "the final approach to target should be one frame at a time");
    }

    [Fact]
    public void RecoveryFromTheMaximumLeadCompletesInAReasonableTime()
    {
        // A flat one-frame shrink took over a minute of recovered conditions to
        // walk back from the ceiling, so the owner kept paying inflated
        // authority-side latency long after the excursion ended.
        var controller = Controller();
        for (var frame = 0; frame < 4_000; frame++)
        {
            controller.Evaluate(Observation(
                frame, rtt: 900d, jitter: 200d, buffered: 0, starved: 2));
        }

        Assert.Equal(controller.LeadPolicy.MaximumLeadFrames, controller.CurrentLeadFrames);

        long recoveredAt = -1;
        var target = 0;
        for (var frame = 4_000L; frame < 200_000; frame++)
        {
            var evaluation = controller.Evaluate(Observation(frame, rtt: 40d, jitter: 5d, buffered: 2));
            target = evaluation.DesiredLeadFrames;
            if (controller.CurrentLeadFrames <= target + controller.Policy.DeadbandFrames)
            {
                recoveredAt = frame - 4_000;
                break;
            }
        }

        Assert.True(recoveredAt > 0, "never recovered from the maximum lead");

        // Under 30 seconds at 60 Hz, versus 72 s for a flat one-frame step.
        Assert.True(
            recoveredAt < 30 * 60,
            $"recovery from maximum lead took {recoveredAt} frames ({recoveredAt / 60.0:F1} s)");
    }

    [Fact]
    public void MeasuredOccupancyAndNotOnlyRttDrivesTheTarget()
    {
        // Same path evidence, different measured buffer depth. If the controller
        // only looked at RTT these would settle identically.
        var shallow = Controller();
        var deep = Controller();

        for (var frame = 0; frame < 6_000; frame++)
        {
            shallow.Evaluate(Observation(frame, rtt: 120d, jitter: 5d, buffered: 2));
            deep.Evaluate(Observation(frame, rtt: 120d, jitter: 5d, buffered: 6));
        }

        Assert.True(
            deep.CurrentLeadFrames < shallow.CurrentLeadFrames,
            $"occupancy was ignored: deep={deep.CurrentLeadFrames} shallow={shallow.CurrentLeadFrames}");
        Assert.True(deep.SmoothedBufferOccupancy > shallow.SmoothedBufferOccupancy);
    }

    [Fact]
    public void LowClockConfidenceHoldsTheAbsolutePolicyStill()
    {
        var controller = Controller();
        var before = controller.CurrentLeadFrames;

        for (var frame = 0; frame < 100; frame++)
        {
            var evaluation = controller.Evaluate(Observation(
                frame, rtt: 250d, jitter: 40d, buffered: 2, confidence: 0.1d));
            Assert.Equal(PredictionLeadControllerDecision.Unchanged, evaluation.Decision);
            Assert.Equal(PredictionLeadAdjustmentReason.ClockConfidenceTooLow, evaluation.Reason);
        }

        Assert.Equal(before, controller.CurrentLeadFrames);
    }

    [Fact]
    public void SustainedLowConfidenceDemandsAnExplicitRebase()
    {
        var controller = Controller();
        PredictionLeadEvaluation last = default;

        for (var frame = 0; frame <= controller.Policy.LowConfidenceRebaseFrames + 1; frame++)
        {
            last = controller.Evaluate(Observation(
                frame, rtt: 120d, jitter: 10d, buffered: 2, confidence: 0d));
        }

        Assert.Equal(PredictionLeadControllerDecision.RebaseRequired, last.Decision);
        Assert.Equal(PredictionLeadAdjustmentReason.RebaseRequired, last.Reason);
        Assert.Null(last.Update);
    }

    [Fact]
    public void StarvationAtTheMaximumLeadDemandsARebaseRatherThanASilentClamp()
    {
        var controller = Controller();

        PredictionLeadEvaluation last = default;
        for (var frame = 0; frame < 500; frame++)
        {
            last = controller.Evaluate(Observation(
                frame, rtt: 900d, jitter: 200d, buffered: 0, starved: 4));
            if (last.Decision == PredictionLeadControllerDecision.RebaseRequired)
            {
                break;
            }
        }

        Assert.Equal(PredictionLeadControllerDecision.RebaseRequired, last.Decision);
        Assert.Equal(controller.LeadPolicy.MaximumLeadFrames, controller.CurrentLeadFrames);
    }

    [Fact]
    public void EveryUpdateIsAbsoluteRevisionedAndSafelyInTheFuture()
    {
        var controller = Controller();
        var revisions = new List<ulong>();

        for (var frame = 0; frame < 20_000; frame++)
        {
            var scheduled = new SimulationInstant(frame + 12);
            var evaluation = controller.Evaluate(Observation(
                frame,
                rtt: frame < 10_000 ? 200d : 20d,
                jitter: 10d,
                buffered: 2,
                lastScheduled: scheduled));

            if (evaluation.Update is not { } update)
            {
                continue;
            }

            Assert.Equal(controller.Scope, update.Scope);
            Assert.True(update.IsValid);
            Assert.Equal(controller.CurrentLeadFrames, update.TargetLead.Value);
            Assert.True(update.EffectiveFrame.Tick > scheduled.Tick);
            Assert.True(new PredictionLeadSafetyContext(
                new SimulationInstant(frame),
                scheduled).IsSafe(update.EffectiveFrame, controller.LeadPolicy));
            revisions.Add(update.Revision.Value);
        }

        Assert.NotEmpty(revisions);
        for (var i = 1; i < revisions.Count; i++)
        {
            Assert.True(revisions[i] > revisions[i - 1]);
        }
    }

    [Fact]
    public void EveryEmittedUpdateIsAcceptedByTheClientSideGate()
    {
        // The controller and the P03-08 receive gate must agree, or the client
        // would reject its own authority's policy.
        var controller = Controller();
        var gate = new PredictionLeadUpdateGate(controller.Scope, controller.LeadPolicy);
        var applied = 0;

        for (var frame = 0; frame < 20_000; frame++)
        {
            var scheduled = new SimulationInstant(frame + 5);
            var evaluation = controller.Evaluate(Observation(
                frame,
                rtt: frame % 4_000 < 2_000 ? 240d : 30d,
                jitter: 15d,
                buffered: 2,
                lastScheduled: scheduled));

            if (evaluation.Update is not { } update)
            {
                continue;
            }

            var decision = gate.Observe(
                update,
                new PredictionLeadSafetyContext(new SimulationInstant(frame), scheduled));
            Assert.Equal(PredictionLeadUpdateDecision.Applied, decision);
            Assert.False(gate.RequiresTimelineRebase);
            applied++;
        }

        Assert.True(applied > 0);
        Assert.Equal(controller.CurrentLeadFrames, gate.LatestAccepted!.Value.TargetLead.Value);
    }

    [Fact]
    public void TheLeadIsAlwaysWholeFramesInsidePolicyBounds()
    {
        var controller = Controller();
        var random = new Random(7);

        for (var frame = 0; frame < 20_000; frame++)
        {
            controller.Evaluate(Observation(
                frame,
                rtt: random.NextDouble() * 600d,
                jitter: random.NextDouble() * 120d,
                buffered: random.Next(0, 10),
                starved: random.Next(0, 10) == 0 ? random.Next(1, 4) : 0,
                confidence: random.NextDouble()));

            Assert.InRange(
                controller.CurrentLeadFrames,
                controller.LeadPolicy.MinimumLeadFrames,
                controller.LeadPolicy.MaximumLeadFrames);
        }

        // The step duration is a constant of the negotiated rate and is never a
        // controller output.
        Assert.Equal(1_000d / 60d, controller.FixedStepMilliseconds, 10);
    }

    [Fact]
    public void ConstructionFailsClosed()
    {
        var scope = Scope();

        Assert.Throws<ArgumentOutOfRangeException>(() => new PredictionLeadController(
            default, SimulationRate.Default, 4, PredictionLeadPolicyRevision.Initial));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PredictionLeadController(
            scope, SimulationRate.Default, 0, PredictionLeadPolicyRevision.Initial));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PredictionLeadController(
            scope,
            SimulationRate.Default,
            PredictionLeadControllerPolicy.MaximumLeadFramesForRate(SimulationRate.Default) + 1,
            PredictionLeadPolicyRevision.Initial));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PredictionLeadController(
            scope, SimulationRate.Default, 4, default));

        Assert.Throws<ArgumentOutOfRangeException>(() => new PredictionLeadObservation(
            new SimulationInstant(-1), default, 1d, 0, 0, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PredictionLeadObservation(
            new SimulationInstant(0), default, 2d, 0, 0, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PredictionLeadObservation(
            new SimulationInstant(0), default, 1d, -1, 0, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PredictionLeadObservation(
            new SimulationInstant(0), default, 1d, 0, -1, null));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PredictionLeadControllerPolicy(2, 1d, 100d, 1, 30, 240, 6, 0, 1, 0.5d, 600, 0.1d, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PredictionLeadControllerPolicy(2, 1d, 100d, 1, 30, 240, 0, 6, 1, 0.5d, 600, 0.1d, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PredictionLeadControllerPolicy.MaximumLeadFramesForRate(default));
    }

    [Fact]
    public void ATransientDipDoesNotLatchTheLeadPermanentlyHigh()
    {
        // The failure this guards: an asymmetric magnitude deadband lets a target
        // rise through the narrow grow threshold and then be unable to fall back
        // through a wider shrink threshold, stranding the owner with permanent
        // extra authority-side latency.
        var controller = Controller();
        RunUntilSettled(controller, rtt: 0d, jitter: 0d);
        var settled = controller.CurrentLeadFrames;

        // A sustained but ordinary buffer dip, with no actual starvation.
        for (var frame = 20_000; frame < 21_000; frame++)
        {
            controller.Evaluate(Observation(frame, rtt: 0d, jitter: 0d, buffered: 0));
        }

        for (var frame = 21_000; frame < 80_000; frame++)
        {
            controller.Evaluate(Observation(
                frame,
                rtt: 0d,
                jitter: 0d,
                buffered: controller.Policy.AuthorityBufferTargetFrames));
        }

        Assert.InRange(
            controller.CurrentLeadFrames,
            settled - controller.Policy.DeadbandFrames,
            settled + controller.Policy.DeadbandFrames);
    }

    [Fact]
    public void ARoundTripInConditionsReturnsToTheOriginalLead()
    {
        var controller = Controller();
        RunUntilSettled(controller, rtt: 40d, jitter: 5d);
        var low = controller.CurrentLeadFrames;

        var frame = 30_000L;
        for (var i = 0; i < 20_000; i++, frame++)
        {
            controller.Evaluate(Observation(frame, rtt: 220d, jitter: 20d, buffered: 2));
        }

        var high = controller.CurrentLeadFrames;
        Assert.True(high > low);

        for (var i = 0; i < 200_000; i++, frame++)
        {
            controller.Evaluate(Observation(frame, rtt: 40d, jitter: 5d, buffered: 2));
        }

        // It returns to within the hysteresis band rather than to the exact
        // frame. A symmetric one-frame deadband is a bounded 16.7 ms tolerance
        // that stops chattering; the defect being guarded against here is a
        // multi-frame offset that can never recover at all.
        Assert.InRange(
            controller.CurrentLeadFrames,
            low - controller.Policy.DeadbandFrames,
            low + controller.Policy.DeadbandFrames);
        Assert.True(controller.CurrentLeadFrames < high);
    }

    [Fact]
    public void ContinuousStarvationIsRateLimitedRatherThanUpdatingEveryFrame()
    {
        // One fallback-filled frame per evaluation is an ordinary lossy link, not
        // an attack. It must not produce one wire update per evaluation.
        var controller = Controller();
        var updates = 0;
        const int frames = 600;

        for (var frame = 0; frame < frames; frame++)
        {
            var evaluation = controller.Evaluate(Observation(
                frame, rtt: 60d, jitter: 5d, buffered: 0, starved: 1));
            if (evaluation.Decision == PredictionLeadControllerDecision.Updated)
            {
                updates++;
            }
        }

        Assert.True(
            updates <= frames / controller.Policy.MinimumFramesBetweenStarvationChanges + 1,
            $"{updates} updates in {frames} frames of continuous starvation");
        Assert.True(updates > 0);
    }

    [Fact]
    public void TheLeadCeilingIsDerivedFromTheNegotiatedRateNotAFlatFrameCount()
    {
        // 24 frames at 60 Hz and 48 at 120 Hz are the same 400 ms.
        Assert.Equal(24, PredictionLeadControllerPolicy.MaximumLeadFramesForRate(new SimulationRate(60)));
        Assert.Equal(48, PredictionLeadControllerPolicy.MaximumLeadFramesForRate(new SimulationRate(120)));
        Assert.Equal(58, PredictionLeadControllerPolicy.MaximumLeadFramesForRate(new SimulationRate(144)));

        var atSixty = new PredictionLeadController(
            Scope(), new SimulationRate(60), 2, PredictionLeadPolicyRevision.Initial);
        var atOneTwenty = new PredictionLeadController(
            Scope(), new SimulationRate(120), 2, PredictionLeadPolicyRevision.Initial);

        Assert.Equal(24, atSixty.LeadPolicy.MaximumLeadFrames);
        Assert.Equal(48, atOneTwenty.LeadPolicy.MaximumLeadFrames);
        Assert.Equal(
            PredictionLeadControllerPolicy.MaximumLeadMilliseconds,
            (int)Math.Round(atSixty.LeadPolicy.MaximumLeadFrames * atSixty.FixedStepMilliseconds));
    }

    [Fact]
    public void NonFinitePathEvidenceHoldsTheLeadInsteadOfMovingIt()
    {
        var controller = Controller();
        RunUntilSettled(controller, rtt: 120d, jitter: 10d);
        var settled = controller.CurrentLeadFrames;
        var revision = controller.CurrentRevision;

        foreach (var bad in new[]
                 {
                     double.NaN,
                     double.PositiveInfinity,
                     double.NegativeInfinity,
                     -1d,
                 })
        {
            var evaluation = controller.Evaluate(Observation(
                frame: 50_000, rtt: bad, jitter: 10d, buffered: 2));
            Assert.Equal(PredictionLeadControllerDecision.Unchanged, evaluation.Decision);
            Assert.Equal(PredictionLeadAdjustmentReason.PathEvidenceUnusable, evaluation.Reason);
            Assert.Null(evaluation.Update);

            evaluation = controller.Evaluate(Observation(
                frame: 50_001, rtt: 120d, jitter: bad, buffered: 2));
            Assert.Equal(PredictionLeadAdjustmentReason.PathEvidenceUnusable, evaluation.Reason);
        }

        Assert.Equal(settled, controller.CurrentLeadFrames);
        Assert.Equal(revision, controller.CurrentRevision);
    }

    [Fact]
    public void OccupancyCorrectionShortensButNeverLengthensTheLead()
    {
        var deep = Controller();
        var shallow = Controller();
        var neutral = Controller();

        for (var frame = 0; frame < 60_000; frame++)
        {
            deep.Evaluate(Observation(frame, rtt: 120d, jitter: 0d, buffered: 8));
            shallow.Evaluate(Observation(frame, rtt: 120d, jitter: 0d, buffered: 0));
            neutral.Evaluate(Observation(frame, rtt: 120d, jitter: 0d, buffered: 2));
        }

        Assert.True(deep.CurrentLeadFrames < neutral.CurrentLeadFrames);
        Assert.Equal(neutral.CurrentLeadFrames, shallow.CurrentLeadFrames);
    }

    private static long RunUntilSettled(
        PredictionLeadController controller,
        double rtt,
        double jitter,
        int horizon = 60_000)
    {
        // Occupancy is deliberately noisy around the target rather than pinned to
        // it. Feeding the exact target every frame holds the occupancy error at
        // zero and silently excludes the correction term from every convergence
        // and oscillation assertion.
        var noise = new Random(1234);
        var stableSince = 0L;
        for (var frame = 0L; frame < horizon; frame++)
        {
            var evaluation = controller.Evaluate(Observation(
                frame,
                rtt,
                jitter,
                buffered: Math.Max(
                    0,
                    controller.Policy.AuthorityBufferTargetFrames + noise.Next(-1, 2))));
            if (evaluation.Decision == PredictionLeadControllerDecision.Updated)
            {
                stableSince = frame + 1;
                continue;
            }

            // Settled means no change for well beyond the change interval.
            if (frame - stableSince > controller.Policy.MinimumFramesBetweenDecreases * 2L)
            {
                return frame;
            }
        }

        return -1;
    }

    private static PredictionLeadController Controller() => new(
        Scope(),
        SimulationRate.Default,
        initialLeadFrames: PredictionLeadUpdatePolicy.Default.MinimumLeadFrames,
        PredictionLeadPolicyRevision.Initial,
        PredictionLeadUpdatePolicy.Default,
        PredictionLeadControllerPolicy.Default);

    private static OwnerIntentScope Scope() => new(
        10,
        new LifeEpoch(new CombatantId(4), new LifeGenerationId(1)),
        new OwnerControlEpoch(3));

    private static PredictionLeadObservation Observation(
        long frame,
        double rtt,
        double jitter,
        int buffered,
        int starved = 0,
        double confidence = 1d,
        SimulationInstant? lastScheduled = null) => new(
        new SimulationInstant(frame),
        new NetworkPathEstimate(
            SmoothedRttMilliseconds: rtt,
            RttJitterMilliseconds: jitter,
            ArrivalJitterMilliseconds: jitter,
            EstimatedLossRate: 0d,
            MissingPackets: 0,
            ReorderedPackets: 0,
            LatestSequence: (ulong)frame),
        confidence,
        buffered,
        starved,
        lastScheduled);
}
