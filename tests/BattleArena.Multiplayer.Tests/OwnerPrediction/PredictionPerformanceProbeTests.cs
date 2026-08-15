using BattleArena.Multiplayer.OwnerPrediction;

namespace BattleArena.Multiplayer.Tests.OwnerPrediction;

public sealed class PredictionPerformanceProbeTests
{
    [Fact]
    public void StandardMatrixMatchesExactGodotScenarioProfiles()
    {
        var calibration = PredictionQueryCalibration.P01_11GodotHeadless;
        var reports = new PredictionPerformanceProbe().MeasureStandardMatrix(
            calibration,
            sampleCount: 64);

        Assert.Equal(6, reports.Count);
        Assert.Equal(new[] { 1, 1, 3, 3, 8, 8 }, reports.Select(report => report.Scenario.TotalCombatants));
        Assert.Equal(new[] { 1, 1, 3, 3, 3, 8 }, reports.Select(report => report.Scenario.ReplayedCombatants));
        Assert.Equal(new[] { 8, 32, 8, 32, 8, 32 }, reports.Select(report => report.Scenario.ReplayDepthFrames));

        foreach (var report in reports)
        {
            var godot = calibration.Resolve(report.Scenario);
            Assert.Equal(godot.MeasuredReplay, report.GodotQueryReplayMeasured);
            Assert.Equal(godot.ManagedAllocatedBytes, report.GodotManagedQueryAllocatedBytes);
            Assert.Equal(report.Scenario.StaticQueriesPerReplay, godot.QueriesPerSample);
            Assert.Equal(
                report.ManagedPrototypeReplay.ComponentwiseAdd(report.GodotQueryReplayMeasured),
                report.PlanningProjection);
            AssertPercentilesOrdered(report.ManagedPrototypeReplay);
            AssertPercentilesOrdered(report.GodotQueryReplayMeasured);
            AssertPercentilesOrdered(report.PlanningProjection);
            Assert.NotEqual(0UL, report.Checksum);
        }
    }

    [Fact]
    public void ComponentwiseProjectionCannotReplaceAllPercentilesWithP99()
    {
        var managed = new PredictionReplayPercentiles(10, 20, 30, 40);
        var godot = new PredictionReplayPercentiles(100, 200, 300, 400);

        var projection = managed.ComponentwiseAdd(godot);

        Assert.Equal(new PredictionReplayPercentiles(110, 220, 330, 440), projection);
        Assert.NotEqual(managed.P50Nanoseconds + godot.P99Nanoseconds, projection.P50Nanoseconds);
        Assert.NotEqual(managed.P95Nanoseconds + godot.P99Nanoseconds, projection.P95Nanoseconds);
        Assert.NotEqual(managed.MaximumNanoseconds + godot.P99Nanoseconds, projection.MaximumNanoseconds);
    }

    [Fact]
    public void CalibrationRetainsPerScenarioWholeBatchDistributionsAndRunPolicy()
    {
        var calibration = PredictionQueryCalibration.P01_11GodotHeadless;
        var metadata = calibration.Metadata;

        Assert.Equal(6, calibration.Profiles.Count);
        Assert.Equal(4, calibration.RawRuns.Count);
        Assert.Equal(4, metadata.RetainedRuns);
        Assert.Equal(64, metadata.WarmupBatchesPerScenario);
        Assert.Contains("component-wise maximum", metadata.AggregationPolicy, StringComparison.Ordinal);
        Assert.Contains("i7-12700H", metadata.Cpu, StringComparison.Ordinal);
        Assert.Contains("4.4", metadata.GodotBuild, StringComparison.Ordinal);
        Assert.All(calibration.Profiles, profile =>
        {
            Assert.Equal(256, profile.SampleCountPerRun);
            Assert.Equal(4, profile.RetainedRunCount);
            Assert.Equal(0, profile.ManagedAllocatedBytes);
            AssertPercentilesOrdered(profile.MeasuredReplay);

            var raw = calibration.RawRuns
                .Select(run => run.Measurements.Single(value =>
                    value.TotalCombatants == profile.TotalCombatants &&
                    value.ReplayedCombatants == profile.ReplayedCombatants &&
                    value.ReplayDepthFrames == profile.ReplayDepthFrames &&
                    value.StaticSweepsPerCharacterFrame == profile.StaticSweepsPerCharacterFrame))
                .ToArray();
            Assert.Equal(raw.Max(value => value.MeasuredReplay.P50Nanoseconds), profile.MeasuredReplay.P50Nanoseconds);
            Assert.Equal(raw.Max(value => value.MeasuredReplay.P95Nanoseconds), profile.MeasuredReplay.P95Nanoseconds);
            Assert.Equal(raw.Max(value => value.MeasuredReplay.P99Nanoseconds), profile.MeasuredReplay.P99Nanoseconds);
            Assert.Equal(raw.Max(value => value.MeasuredReplay.MaximumNanoseconds), profile.MeasuredReplay.MaximumNanoseconds);
        });

        Assert.Equal(
            new PredictionReplayPercentiles(1_078_000, 1_138_000, 1_263_000, 1_565_000),
            calibration.Resolve(PredictionPerformanceScenario.Expected(8)).MeasuredReplay);
        Assert.Equal(
            new PredictionReplayPercentiles(23_242_000, 23_856_000, 28_697_000, 37_231_000),
            calibration.Resolve(PredictionPerformanceScenario.WorstBounded(8)).MeasuredReplay);
    }

    [Fact]
    public void MemoryLayoutMatchesStateCommandResultAndDependencyOwnership()
    {
        var probe = new PredictionPerformanceProbe();
        var reports = new[] { 1, 3, 8 }
            .Select(players => probe.Measure(
                PredictionPerformanceScenario.WorstBounded(players),
                PredictionQueryCalibration.P01_11GodotHeadless,
                sampleCount: 32))
            .ToArray();

        Assert.True(reports[0].HistoryPayloadBytes < reports[1].HistoryPayloadBytes);
        Assert.True(reports[1].HistoryPayloadBytes < reports[2].HistoryPayloadBytes);
        Assert.Equal(
            new[]
            {
                (State: 448, Command: 232, Result: 272, Dependency: 392, History: 464896, Total: 465344),
                (State: 448, Command: 232, Result: 272, Dependency: 392, History: 1382400, Total: 1383744),
                (State: 448, Command: 232, Result: 272, Dependency: 392, History: 3676160, Total: 3679744),
            },
            reports.Select(report => (
                State: report.Layout.CharacterStateBytes,
                Command: report.Layout.CommandBytes,
                Result: report.Layout.FrameResultBytes,
                Dependency: report.Layout.DependencyJournalBytes,
                History: report.HistoryPayloadBytes,
                Total: report.TotalPreallocatedPayloadBytes)));
        Assert.All(reports, report =>
        {
            var layout = report.Layout;
            Assert.True(layout.AllHotStructsContainNoManagedReferences);
            Assert.True(layout.CharacterStateBufferOwnershipValid);
            Assert.True(layout.CommandBufferOwnershipValid);
            Assert.True(layout.FrameResultBufferOwnershipValid);
            Assert.True(layout.DependencyJournalBufferOwnershipValid);
            Assert.Equal(PredictionPerformanceProbe.MaximumMovementSources, layout.StateMovementSourceCapacity);
            Assert.Equal(PredictionPerformanceProbe.MaximumContactFacts, layout.StateContactCapacity);
            Assert.Equal(PredictionPerformanceProbe.MaximumTransitionReferences, layout.CommandTransitionCapacity);
            Assert.Equal(PredictionPerformanceProbe.MaximumActionReferences, layout.CommandActionCapacity);
            Assert.Equal(PredictionPerformanceProbe.MaximumSimulationEvents, layout.FrameEventCapacity);
            Assert.Equal(PredictionPerformanceProbe.MaximumCollisionDependencies, layout.DependencyCapacity);
            Assert.Equal(
                layout.FrameMetadataBytes + report.Scenario.TotalCombatants *
                (2 * layout.CharacterStateBytes + layout.CommandBytes +
                 layout.FrameResultBytes + layout.DependencyJournalBytes),
                report.FramePayloadBytes);
            Assert.Equal(
                report.FramePayloadBytes * PredictionPerformanceProbe.HistoryCapacityFrames,
                report.HistoryPayloadBytes);
            Assert.Equal(
                report.HistoryPayloadBytes +
                report.Scenario.TotalCombatants * layout.CharacterStateBytes,
                report.TotalPreallocatedPayloadBytes);
            Assert.True(report.PreallocatedManagedBytes >= report.TotalPreallocatedPayloadBytes);
            Assert.Equal(
                report.PreallocatedManagedBytes - report.TotalPreallocatedPayloadBytes,
                report.PreallocatedManagedOverheadBytes);
            Assert.InRange(report.PreallocatedManagedOverheadBytes, 0, 4_096);
        });

        var inspectionCount = PredictionPerformanceProbe.LayoutInspectionCount;
        _ = probe.Measure(
            PredictionPerformanceScenario.Expected(1),
            PredictionQueryCalibration.P01_11GodotHeadless,
            sampleCount: 32);
        Assert.Equal(inspectionCount, PredictionPerformanceProbe.LayoutInspectionCount);
        Assert.Equal(1, PredictionPerformanceProbe.LayoutInspectionCount);
    }

    [Fact]
    public void WorstStorageHighWaterEqualsEveryFixedCapacity()
    {
        var report = new PredictionPerformanceProbe().Measure(
            PredictionPerformanceScenario.WorstBounded(8),
            PredictionQueryCalibration.P01_11GodotHeadless,
            sampleCount: 32);

        Assert.Equal(PredictionPerformanceProbe.HistoryCapacityFrames, report.HistoryHighWaterFrames);
        Assert.Equal(PredictionPerformanceProbe.MaximumMovementSources, report.SourceHighWater);
        Assert.Equal(PredictionPerformanceProbe.MaximumContactFacts, report.ContactHighWater);
        Assert.Equal(PredictionPerformanceProbe.MaximumSimulationEvents, report.EventHighWater);
        Assert.Equal(PredictionPerformanceProbe.MaximumTransitionReferences, report.TransitionReferenceHighWater);
        Assert.Equal(PredictionPerformanceProbe.MaximumActionReferences, report.ActionReferenceHighWater);
        Assert.Equal(PredictionPerformanceProbe.MaximumCollisionDependencies, report.DependencyHighWater);
    }

    [Fact]
    public void AllocationScopesCannotCollapseUnknownNativeMemoryToZero()
    {
        var report = new PredictionPerformanceProbe().Measure(
            PredictionPerformanceScenario.Expected(3),
            PredictionQueryCalibration.P01_11GodotHeadless,
            sampleCount: 64);

        Assert.Equal(0, report.ManagedPrototypeReplayAllocatedBytes);
        Assert.Equal(0, report.ManagedPrototypeCeilingAllocatedBytesPerReplay);
        Assert.Equal(0, report.GodotManagedQueryAllocatedBytes);
        Assert.Equal(
            PredictionNativeAllocationMeasurementStatus.NotMeasured,
            report.GodotNativeAllocationStatus);
        Assert.Equal(PredictionPoolUsagePolicy.InlinePreallocatedNoPool, report.PoolUsagePolicy);

        Assert.Equal(1, PredictionPerformanceProbe.ConservativeAllocatedBytesPerReplay(1, 64));
        Assert.Equal(2, PredictionPerformanceProbe.ConservativeAllocatedBytesPerReplay(65, 64));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PredictionPerformanceProbe.ConservativeAllocatedBytesPerReplay(-1, 64));
    }

    [Fact]
    public void RawRunAggregationRejectsMissingDuplicateAndInconsistentRuns()
    {
        var first = PredictionQueryCalibration.P01_11RawRuns[0];
        var second = PredictionQueryCalibration.P01_11RawRuns[1];
        var twoRunMetadata = TestMetadata() with { RetainedRuns = 2 };

        var missing = new PredictionGodotCalibrationRun(
            99,
            second.Measurements.Take(second.Measurements.Count - 1).ToArray());
        Assert.Throws<ArgumentException>(() => PredictionQueryCalibration.AggregateRawRuns(
            twoRunMetadata,
            [first, missing]));

        Assert.Throws<ArgumentException>(() => new PredictionGodotCalibrationRun(
            100,
            [first.Measurements[0], first.Measurements[0]]));
        Assert.Throws<ArgumentException>(() => PredictionQueryCalibration.AggregateRawRuns(
            TestMetadata() with { RetainedRuns = 3 },
            [first, second]));
        Assert.Throws<ArgumentException>(() => PredictionQueryCalibration.AggregateRawRuns(
            twoRunMetadata,
            [first, new PredictionGodotCalibrationRun(first.RunNumber, second.Measurements)]));
    }

    [Fact]
    public void CalibrationAndRawRunCollectionsAreDefensivelyImmutable()
    {
        var sourceProfile = new PredictionGodotReplayCalibration(
            1, 1, 8, 4, 256, 32, 1,
            new PredictionReplayPercentiles(1, 2, 3, 4), 0);
        var source = new[] { sourceProfile };
        var calibration = new PredictionQueryCalibration(TestMetadata(), source);
        source[0] = sourceProfile with { TotalCombatants = 8 };

        Assert.Same(sourceProfile, calibration.Profiles[0]);
        var mutableView = Assert.IsAssignableFrom<IList<PredictionGodotReplayCalibration>>(
            calibration.Profiles);
        Assert.True(mutableView.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => mutableView[0] = source[0]);

        var rawSource = new[] { PredictionQueryCalibration.P01_11RawRuns[0].Measurements[0] };
        var rawRun = new PredictionGodotCalibrationRun(50, rawSource);
        var retained = rawRun.Measurements[0];
        rawSource[0] = rawSource[0] with { TotalCombatants = 8 };
        Assert.Same(retained, rawRun.Measurements[0]);
        Assert.IsAssignableFrom<IList<PredictionGodotReplayMeasurement>>(rawRun.Measurements);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<PredictionGodotReplayMeasurement>)rawRun.Measurements)[0] = rawSource[0]);
    }

    [Fact]
    public void ScenarioCalibrationAndSampleBoundsAreValidated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PredictionPerformanceScenario.Expected(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PredictionPerformanceScenario.WorstBounded(9));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PredictionReplayPercentiles(0, 1, 2, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PredictionReplayPercentiles(2, 1, 3, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PredictionPerformanceProbe().Measure(
            PredictionPerformanceScenario.Expected(1),
            PredictionQueryCalibration.P01_11GodotHeadless,
            sampleCount: 31));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PredictionPerformanceProbe().Measure(
            PredictionPerformanceScenario.Expected(1),
            PredictionQueryCalibration.P01_11GodotHeadless,
            sampleCount: PredictionPerformanceProbe.MaximumSampleCount + 1));

        var incomplete = new PredictionQueryCalibration(TestMetadata(),
        [
            new PredictionGodotReplayCalibration(
                1,
                1,
                8,
                4,
                256,
                32,
                1,
                new PredictionReplayPercentiles(1, 2, 3, 4),
                0),
        ]);
        Assert.Throws<InvalidOperationException>(() => new PredictionPerformanceProbe().Measure(
            PredictionPerformanceScenario.Expected(3),
            incomplete,
            sampleCount: 32));

        var invalidProfile = new PredictionGodotReplayCalibration(
            1,
            1,
            8,
            4,
            199,
            31,
            1,
            new PredictionReplayPercentiles(1, 2, 3, 4),
            0);
        Assert.Throws<ArgumentException>(() => new PredictionQueryCalibration(
            TestMetadata(),
            [invalidProfile]));
    }

    [Fact]
    public void CheckedProjectionArithmeticCannotOverflowSilently()
    {
        var almostMaximum = new PredictionReplayPercentiles(
            long.MaxValue - 4,
            long.MaxValue - 3,
            long.MaxValue - 2,
            long.MaxValue - 1);

        Assert.Throws<OverflowException>(() => almostMaximum.ComponentwiseAdd(
            new PredictionReplayPercentiles(10, 20, 30, 40)));
    }

    private static PredictionCalibrationMetadata TestMetadata() => new(
        "test cpu",
        "test os",
        "test Godot",
        "test configuration",
        1,
        1,
        "test aggregation",
        "2026-08-11");

    private static void AssertPercentilesOrdered(PredictionReplayPercentiles percentiles)
    {
        Assert.InRange(percentiles.P50Nanoseconds, 1, percentiles.P95Nanoseconds);
        Assert.InRange(percentiles.P95Nanoseconds, percentiles.P50Nanoseconds, percentiles.P99Nanoseconds);
        Assert.InRange(percentiles.P99Nanoseconds, percentiles.P95Nanoseconds, percentiles.MaximumNanoseconds);
    }
}
