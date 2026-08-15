using BattleArena.Core.Common;
using BattleArena.Core.Movement.Simulation;
using BattleArena.Multiplayer.AuthoritySimulation;

namespace BattleArena.Multiplayer.Tests.AuthoritySimulation;

public sealed class AuthoritySimulationClockTests
{
    private const double Step = 1_000d / 60d;

    [Fact]
    public void AnExactMultipleOfTheStepYieldsExactlyThatManyFrames()
    {
        // 1000/rate is not exactly representable, so N * step can land a
        // fraction below N frames. The caller must still get N.
        foreach (var rate in new[] { 30, 60, 120, 144 })
        {
            for (var frames = 1; frames <= 4; frames++)
            {
                var clock = Clock(rate: rate);
                var advance = clock.Advance(frames * (1_000d / rate));
                Assert.Equal(frames, advance.StepsToRun);
            }
        }
    }

    [Fact]
    public void ATrueShortfallIsNotAbsorbedByTheTolerance()
    {
        var clock = Clock();
        Assert.Equal(0, clock.Advance(Step * 0.999d).StepsToRun);
        Assert.Equal(1, clock.Advance(Step * 0.001d).StepsToRun);
    }

    [Fact]
    public void SteadyPacingRunsOneFramePerCallbackAndStampsEachBoundary()
    {
        var clock = Clock();
        var simulated = new List<long>();

        for (var i = 0; i < 600; i++)
        {
            var advance = clock.Advance(Step);
            Assert.Equal(AuthorityClockAdvanceDecision.OnSchedule, advance.Decision);
            Assert.Equal(1, advance.StepsToRun);

            for (var step = 0; step < advance.StepsToRun; step++)
            {
                simulated.Add(clock.CompleteFrame(1_000 + i).Tick);
            }
        }

        Assert.Equal(600, simulated.Count);
        Assert.Equal(Enumerable.Range(100, 600).Select(i => (long)i), simulated);
        Assert.Equal(600, clock.TotalFramesSimulated);
        Assert.False(clock.IsFrozen);
    }

    [Fact]
    public void SimulationTimeNeverAdvancesWithoutAFrameActuallyRunning()
    {
        var clock = Clock();

        // Planning alone moves nothing.
        var advance = clock.Advance(Step * 3d);
        Assert.Equal(3, advance.StepsToRun);
        Assert.Equal(new SimulationInstant(100), clock.NextFrame);
        Assert.Equal(0, clock.TotalFramesSimulated);

        clock.CompleteFrame(1);
        Assert.Equal(new SimulationInstant(101), clock.NextFrame);

        clock.CompleteFrame(2);
        clock.CompleteFrame(3);
        Assert.Equal(new SimulationInstant(103), clock.NextFrame);

        // A fourth completion was never planned and must be refused.
        Assert.Throws<InvalidOperationException>(() => clock.CompleteFrame(4));
        Assert.Equal(new SimulationInstant(103), clock.NextFrame);
    }

    [Fact]
    public void AdvancingBeforeCompletingThePlannedFramesFailsClosed()
    {
        var clock = Clock();
        var advance = clock.Advance(Step * 3d);
        Assert.Equal(3, advance.StepsToRun);
        clock.CompleteFrame(1);

        Assert.Throws<InvalidOperationException>(() => clock.Advance(Step));
        Assert.Equal(2, clock.PendingSteps);
    }

    [Fact]
    public void AHitchIsRecoveredFrameByFrameWithNoneSkipped()
    {
        // A stall worth exactly six frames. The cap runs four now and the rest
        // stay owed; every one of the six eventually runs, in order.
        var clock = Clock();
        var simulated = new List<long>();

        var advance = clock.Advance(Step * 6d);
        Assert.Equal(AuthorityClockAdvanceDecision.CatchUpCapped, advance.Decision);
        Assert.Equal(clock.Policy.MaximumCatchUpStepsPerCallback, advance.StepsToRun);
        Run(clock, advance, simulated);

        while (simulated.Count < 6)
        {
            advance = clock.Advance(0d);
            Run(clock, advance, simulated);
            if (advance.StepsToRun == 0)
            {
                break;
            }
        }

        Assert.Equal(6, simulated.Count);
        Assert.Equal(Enumerable.Range(100, 6).Select(i => (long)i), simulated);
    }

    [Fact]
    public void OwedTimeIsRetainedRatherThanDiscardedSoNoFrameIsCompressedAway()
    {
        var clock = Clock();
        var simulated = new List<long>();

        // Twenty callbacks each owing six frames, against a cap of four, so the
        // deficit genuinely builds rather than being absorbed each time.
        for (var i = 0; i < 20; i++)
        {
            Run(clock, clock.Advance(Step * 6d), simulated);
        }

        // Drain whatever is still owed.
        for (var i = 0; i < 100; i++)
        {
            var advance = clock.Advance(0d);
            if (advance.StepsToRun == 0)
            {
                break;
            }

            Run(clock, advance, simulated);
        }

        // A hundred and twenty frames were owed and a hundred and twenty ran,
        // contiguously.
        Assert.Equal(120, simulated.Count);
        Assert.Equal(Enumerable.Range(100, 120).Select(i => (long)i), simulated);
        Assert.True(clock.OwedLagMilliseconds < Step);
    }

    [Fact]
    public void SubFrameCallbacksAccumulateInsteadOfBeingLost()
    {
        var clock = Clock();
        var simulated = new List<long>();

        // Four callbacks at a quarter frame each owe exactly one frame.
        for (var i = 0; i < 4; i++)
        {
            var advance = clock.Advance(Step / 4d);
            if (i < 3)
            {
                Assert.Equal(AuthorityClockAdvanceDecision.NoStepDue, advance.Decision);
                Assert.Equal(0, advance.StepsToRun);
            }

            Run(clock, advance, simulated);
        }

        Assert.Single(simulated);
    }

    [Fact]
    public void SlowWallTimeRecoveryDrainsTheDeficitWithoutAReset()
    {
        var clock = Clock();
        var simulated = new List<long>();

        // A burst of hitches deep enough to exceed the catch-up cap, then a long
        // stretch of healthy callbacks.
        for (var i = 0; i < 5; i++)
        {
            Run(clock, clock.Advance(Step * 8d), simulated);
        }

        var behind = clock.OwedLagMilliseconds;
        Assert.True(behind > 0d);

        for (var i = 0; i < 600; i++)
        {
            var advance = clock.Advance(Step);
            Assert.NotEqual(AuthorityClockAdvanceDecision.TimelineResetRequired, advance.Decision);
            Run(clock, advance, simulated);
        }

        Assert.False(clock.IsFrozen);
        Assert.True(clock.OwedLagMilliseconds < Step);
        Assert.Equal(
            Enumerable.Range(100, simulated.Count).Select(i => (long)i),
            simulated);
    }

    [Fact]
    public void UnrecoverableLagFreezesAndEmitsExactlyOneReset()
    {
        var clock = Clock();
        Run(clock, clock.Advance(Step), new List<long>());

        // One callback far past the recoverable bound.
        var advance = clock.Advance(clock.Policy.MaximumRecoverableLagMilliseconds + 500d);

        Assert.Equal(AuthorityClockAdvanceDecision.TimelineResetRequired, advance.Decision);
        Assert.Equal(0, advance.StepsToRun);
        Assert.True(clock.IsFrozen);

        Assert.NotNull(advance.Reset);
        var reset = advance.Reset!.Value;
        Assert.Equal(AuthorityTimelineResetReason.UnrecoverableLag, reset.Reason);
        Assert.Equal(new SimulationInstant(100), reset.LastSimulatedFrame);
        Assert.True(reset.ObservedLagMilliseconds > clock.Policy.MaximumRecoverableLagMilliseconds);
        Assert.Equal(new SimulationInstant(101), reset.ResumeFrame);
        Assert.True(reset.NewEpoch > reset.PreviousEpoch);

        // The heavy message is latched: repeating it every callback while the
        // condition persists would be its own amplification problem.
        for (var i = 0; i < 100; i++)
        {
            var repeated = clock.Advance(1_000d);
            Assert.Equal(AuthorityClockAdvanceDecision.Frozen, repeated.Decision);
            Assert.Null(repeated.Reset);
            Assert.Equal(0, repeated.StepsToRun);
        }
    }

    [Fact]
    public void NoFrameRunsWhileFrozen()
    {
        var clock = Clock();
        clock.DemandTimelineReset();

        Assert.True(clock.IsFrozen);
        Assert.Throws<InvalidOperationException>(() => clock.CompleteFrame(1));
        Assert.Equal(0, clock.TotalFramesSimulated);
    }

    [Fact]
    public void SustainedCatchUpDeficitEventuallyDemandsAReset()
    {
        var clock = Clock(new AuthoritySimulationClockPolicy(
            maximumCatchUpStepsPerCallback: 2,
            maximumRecoverableLagMilliseconds: 1_000_000d,
            sustainedDeficitCallbackLimit: 20,
            retainedFrameBoundaries: 64,
            maximumElapsedMillisecondsPerCallback: 1_000_000d));
        var simulated = new List<long>();

        AuthorityClockAdvance advance = default;
        for (var i = 0; i < 100; i++)
        {
            advance = clock.Advance(Step * 5d);
            if (advance.Decision == AuthorityClockAdvanceDecision.TimelineResetRequired)
            {
                break;
            }

            Run(clock, advance, simulated);
        }

        Assert.Equal(AuthorityClockAdvanceDecision.TimelineResetRequired, advance.Decision);
        Assert.Equal(
            AuthorityTimelineResetReason.SustainedCatchUpDeficit,
            advance.Reset!.Value.Reason);

        // Everything that ran before the reset is still contiguous.
        Assert.Equal(
            Enumerable.Range(100, simulated.Count).Select(i => (long)i),
            simulated);
    }

    [Fact]
    public void AnAbsurdCallbackIntervalResetsInsteadOfBeingSilentlyDiscarded()
    {
        var clock = Clock(new AuthoritySimulationClockPolicy(
            maximumCatchUpStepsPerCallback: 4,
            maximumRecoverableLagMilliseconds: 250d,
            sustainedDeficitCallbackLimit: 1_000_000,
            retainedFrameBoundaries: 64,
            maximumElapsedMillisecondsPerCallback: 250d));

        var stall = TimeSpan.FromHours(3).TotalMilliseconds;
        var advance = clock.Advance(stall);

        // A three-hour gap is a suspended process. Truncating it and carrying on
        // would discard the time silently; the honest response is a reset that
        // reports the interval as it was actually observed.
        Assert.Equal(AuthorityClockAdvanceDecision.TimelineResetRequired, advance.Decision);
        Assert.Equal(0, advance.StepsToRun);
        Assert.Equal(stall, advance.Reset!.Value.ObservedLagMilliseconds, 3);
        Assert.True(clock.IsFrozen);
    }

    [Fact]
    public void ResumeStartsANewEpochAndClearsTheDeficit()
    {
        var clock = Clock();
        Run(clock, clock.Advance(Step), new List<long>());
        var advance = clock.Advance(clock.Policy.MaximumRecoverableLagMilliseconds + 500d);
        var reset = advance.Reset!.Value;

        clock.ResumeAfterReset(reset.NewEpoch, new SimulationInstant(5_000));

        Assert.False(clock.IsFrozen);
        Assert.Equal(reset.NewEpoch, clock.Epoch);
        Assert.Equal(new SimulationInstant(5_000), clock.NextFrame);
        Assert.Equal(0d, clock.OwedLagMilliseconds);

        var resumed = clock.Advance(Step);
        Assert.Equal(AuthorityClockAdvanceDecision.OnSchedule, resumed.Decision);
        Assert.Equal(new SimulationInstant(5_000), clock.CompleteFrame(9));

        // A second unrecoverable event in the new epoch raises its own reset.
        var second = clock.Advance(clock.Policy.MaximumRecoverableLagMilliseconds + 500d);
        Assert.NotNull(second.Reset);
        Assert.Equal(reset.NewEpoch, second.Reset!.Value.PreviousEpoch);
    }

    [Fact]
    public void ResumeFailsClosedOnANonAdvancingEpochOrWhenNotFrozen()
    {
        var clock = Clock();
        Assert.Throws<InvalidOperationException>(() =>
            clock.ResumeAfterReset(new MatchFrameEpochId(9), new SimulationInstant(0)));

        clock.DemandTimelineReset();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            clock.ResumeAfterReset(clock.Epoch, new SimulationInstant(0)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            clock.ResumeAfterReset(default, new SimulationInstant(0)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            clock.ResumeAfterReset(clock.Epoch.Next(), new SimulationInstant(-1)));
    }

    [Fact]
    public void FrameBoundariesAreRecordedForClockSynchronisation()
    {
        var clock = Clock();
        var simulated = new List<long>();

        for (var i = 0; i < 10; i++)
        {
            Run(clock, clock.Advance(Step), simulated, monotonic: 7_000 + i * 17);
        }

        Assert.True(clock.TryGetFrameBoundary(new SimulationInstant(103), out var boundary));
        Assert.Equal(new SimulationInstant(103), boundary.Frame);
        Assert.Equal(clock.Epoch, boundary.Epoch);
        Assert.Equal(7_000 + 3 * 17, boundary.MonotonicTimestamp);

        Assert.True(clock.TryGetLatestFrameBoundary(out var latest));
        Assert.Equal(new SimulationInstant(109), latest.Frame);

        Assert.False(clock.TryGetFrameBoundary(new SimulationInstant(9_999), out _));
    }

    [Fact]
    public void BoundariesFromASupersededEpochAreNotReturned()
    {
        var clock = Clock();
        Run(clock, clock.Advance(Step), new List<long>(), monotonic: 42);
        Assert.True(clock.TryGetFrameBoundary(new SimulationInstant(100), out _));

        clock.DemandTimelineReset();
        clock.ResumeAfterReset(clock.Epoch.Next(), new SimulationInstant(400));

        Assert.False(clock.TryGetFrameBoundary(new SimulationInstant(100), out _));
    }

    [Fact]
    public void TimelineExhaustionResetsRatherThanWrapping()
    {
        var clock = new AuthoritySimulationClock(
            new MatchFrameEpochId(1),
            SimulationRate.Default,
            new SimulationInstant(long.MaxValue - 2));

        var advance = clock.Advance(Step * 10d);

        Assert.Equal(AuthorityClockAdvanceDecision.TimelineResetRequired, advance.Decision);
        Assert.NotNull(advance.Reset);
        Assert.True(clock.IsFrozen);
    }

    [Fact]
    public void ConstructionAndInputsFailClosed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AuthoritySimulationClock(
            default, SimulationRate.Default, new SimulationInstant(0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AuthoritySimulationClock(
            new MatchFrameEpochId(1), SimulationRate.Default, new SimulationInstant(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AuthoritySimulationClockPolicy(
            0, 1_000d, 10, 16, 1_000d));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AuthoritySimulationClockPolicy(
            4, 0d, 10, 16, 1_000d));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AuthoritySimulationClockPolicy(
            4, 1_000d, 10, 0, 1_000d));

        // A clamp below the recoverable bound could discard an unrecoverable
        // stall without ever resetting.
        Assert.Throws<ArgumentOutOfRangeException>(() => new AuthoritySimulationClockPolicy(
            4, 1_000d, 10, 16, 999d));

        var clock = Clock();
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(-1d));
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(double.PositiveInfinity));

        // The step is a constant of the negotiated rate.
        Assert.Equal(Step, clock.FixedStepMilliseconds, 10);
        Assert.Equal(1_000d / 120d, Clock(rate: 120).FixedStepMilliseconds, 10);
    }

    [Fact]
    public void AResetBeforeAnyFrameRanNamesNoLastSimulatedFrame()
    {
        var clock = Clock();
        var advance = clock.DemandTimelineReset();

        Assert.NotNull(advance.Reset);
        Assert.Null(advance.Reset!.Value.LastSimulatedFrame);
        Assert.Equal(new SimulationInstant(100), advance.Reset!.Value.ResumeFrame);
        Assert.Equal(0, clock.TotalFramesSimulated);

        // And after a resume that simulates nothing, still none.
        clock.ResumeAfterReset(clock.Epoch.Next(), new SimulationInstant(700));
        var second = clock.DemandTimelineReset();
        Assert.Null(second.Reset!.Value.LastSimulatedFrame);
    }

    [Fact]
    public void AFreshEpochThatRunsNothingNamesNoLastSimulatedFrameEvenAfterEarlierEpochsRan()
    {
        // The lifetime frame count is the wrong question. Epoch 1 really ran a
        // frame; epoch 2 has not, so epoch 2's reset must name none — otherwise
        // it reports a tick derived from its resume cursor that no epoch ever
        // simulated.
        var clock = Clock();
        Run(clock, clock.Advance(Step), new List<long>());

        var first = clock.DemandTimelineReset();
        Assert.Equal(new SimulationInstant(100), first.Reset!.Value.LastSimulatedFrame);

        clock.ResumeAfterReset(first.Reset!.Value.NewEpoch, new SimulationInstant(700));
        Assert.Equal(1, clock.TotalFramesSimulated);
        Assert.Equal(0, clock.FramesSimulatedInEpoch);

        var second = clock.DemandTimelineReset();
        Assert.Null(second.Reset!.Value.LastSimulatedFrame);
        Assert.Equal(new SimulationInstant(700), second.Reset!.Value.ResumeFrame);

        // Once the new epoch does run a frame, it names that one.
        clock.ResumeAfterReset(second.Reset!.Value.NewEpoch, new SimulationInstant(900));
        Run(clock, clock.Advance(Step), new List<long>());
        var third = clock.DemandTimelineReset();
        Assert.Equal(new SimulationInstant(900), third.Reset!.Value.LastSimulatedFrame);
        Assert.Equal(1, clock.FramesSimulatedInEpoch);
        Assert.Equal(2, clock.TotalFramesSimulated);
    }

    [Fact]
    public void AZeroMonotonicTimestampIsAValidRecordedBoundary()
    {
        // A monotonic clock may legitimately read zero. Treating that as an
        // empty slot would make a real boundary permanently unqueryable.
        var clock = Clock();
        Run(clock, clock.Advance(Step), new List<long>(), monotonic: 0);

        Assert.True(clock.TryGetFrameBoundary(new SimulationInstant(100), out var boundary));
        Assert.Equal(0, boundary.MonotonicTimestamp);
        Assert.Equal(new SimulationInstant(100), boundary.Frame);

        Assert.True(clock.TryGetLatestFrameBoundary(out var latest));
        Assert.Equal(boundary, latest);
    }

    [Fact]
    public void ADemandedResetDiscardsUnrunPlannedFramesAndRecoversOnResume()
    {
        var clock = Clock();
        var advance = clock.Advance(Step * 4d);
        Assert.Equal(4, advance.StepsToRun);
        clock.CompleteFrame(1);
        Assert.Equal(3, clock.PendingSteps);

        var reset = clock.DemandTimelineReset();
        Assert.NotNull(reset.Reset);
        Assert.Equal(new SimulationInstant(100), reset.Reset!.Value.LastSimulatedFrame);
        Assert.Equal(0, clock.PendingSteps);
        Assert.Throws<InvalidOperationException>(() => clock.CompleteFrame(2));

        clock.ResumeAfterReset(reset.Reset!.Value.NewEpoch, new SimulationInstant(900));
        var resumed = clock.Advance(Step);
        Assert.Equal(1, resumed.StepsToRun);
        Assert.Equal(new SimulationInstant(900), clock.CompleteFrame(5));
    }

    [Fact]
    public void FreezingConsumesNoOwedTimeSoTheReportedLagIsTheRealOne()
    {
        var clock = Clock();
        Run(clock, clock.Advance(Step), new List<long>());

        var stall = clock.Policy.MaximumRecoverableLagMilliseconds + 500d;
        var advance = clock.Advance(stall);

        Assert.Equal(0, advance.StepsToRun);
        Assert.Equal(stall, advance.OwedLagMilliseconds, 6);
        Assert.Equal(stall, advance.Reset!.Value.ObservedLagMilliseconds, 6);
    }

    private static void Run(
        AuthoritySimulationClock clock,
        AuthorityClockAdvance advance,
        List<long> simulated,
        long monotonic = 1)
    {
        for (var step = 0; step < advance.StepsToRun; step++)
        {
            simulated.Add(clock.CompleteFrame(monotonic).Tick);
        }
    }

    private static AuthoritySimulationClock Clock(
        AuthoritySimulationClockPolicy? policy = null,
        int rate = 60) => new(
        new MatchFrameEpochId(1),
        new SimulationRate(rate),
        new SimulationInstant(100),
        policy ?? AuthoritySimulationClockPolicy.Default);
}
