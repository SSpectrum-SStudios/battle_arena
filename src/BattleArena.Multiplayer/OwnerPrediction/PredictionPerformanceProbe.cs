using System.Diagnostics;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Reflection;

namespace BattleArena.Multiplayer.OwnerPrediction;

/// <summary>
/// Phase-1 value-history performance prototype. Managed replay and Godot query
/// distributions remain separate measured products. Their component-wise sum is
/// labelled a planning projection, never a measured end-to-end percentile.
/// </summary>
public sealed class PredictionPerformanceProbe
{
    public const int HistoryCapacityFrames = 256;
    public const int MaximumMovementSources = 16;
    public const int MaximumContactFacts = 4;
    public const int MaximumSimulationEvents = 16;
    public const int MaximumTransitionReferences =
        BattleArena.Core.Movement.Simulation.OwnerSimulationLimits.MaximumTransitionReferences;
    public const int MaximumActionReferences =
        BattleArena.Core.Movement.Simulation.OwnerSimulationLimits.MaximumActionReferences;
    public const int MaximumCollisionDependencies = 16;
    public const int DefaultSampleCount = 256;
    public const int MaximumSampleCount = 4_096;

    public static int LayoutInspectionCount => ProbeLayoutInspector.InspectionCount;

    public IReadOnlyList<PredictionPerformanceReport> MeasureStandardMatrix(
        PredictionQueryCalibration calibration,
        int sampleCount = DefaultSampleCount)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        var reports = new List<PredictionPerformanceReport>(6);
        foreach (var players in new[] { 1, 3, 8 })
        {
            reports.Add(Measure(PredictionPerformanceScenario.Expected(players), calibration, sampleCount));
            reports.Add(Measure(PredictionPerformanceScenario.WorstBounded(players), calibration, sampleCount));
        }

        return reports;
    }

    public PredictionPerformanceReport Measure(
        PredictionPerformanceScenario scenario,
        PredictionQueryCalibration calibration,
        int sampleCount = DefaultSampleCount)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(calibration);
        if (sampleCount is < 32 or > MaximumSampleCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleCount),
                sampleCount,
                $"Timing sample count must be between 32 and {MaximumSampleCount}.");
        }

        var godot = calibration.Resolve(scenario);
        if (godot.QueriesPerSample != scenario.StaticQueriesPerReplay)
        {
            throw new InvalidOperationException(
                "Godot calibration query count does not match the replay scenario.");
        }

        // Resolve diagnostic layout metadata before the measured allocation scope.
        // Reflection is a one-time inspection concern, not history-storage cost.
        var layout = ProbeLayoutInspector.Layout;
        GC.KeepAlive(new ProbeHistoryStore(scenario, layout));
        var preallocationStarted = GC.GetAllocatedBytesForCurrentThread();
        var store = new ProbeHistoryStore(scenario, layout);
        var preallocatedManagedBytes = checked(
            GC.GetAllocatedBytesForCurrentThread() - preallocationStarted);

        var warmupCount = Math.Max(64, sampleCount / 2);
        ulong checksum = 0;
        for (var index = 0; index < warmupCount; index++)
        {
            checksum ^= store.Replay(index, scenario);
        }

        // Prime Stopwatch and the conversion path outside the allocation scope.
        // Their first process use may initialize runtime timing infrastructure;
        // that cost is not a replay-loop allocation.
        var timingPrimeStarted = Stopwatch.GetTimestamp();
        checksum ^= store.Replay(warmupCount, scenario);
        _ = TicksToNanoseconds(Stopwatch.GetTimestamp() - timingPrimeStarted);

        var samples = new long[sampleCount];
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < sampleCount; index++)
        {
            var started = Stopwatch.GetTimestamp();
            checksum ^= store.Replay(index + warmupCount, scenario);
            samples[index] = TicksToNanoseconds(Stopwatch.GetTimestamp() - started);
        }

        var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
        var managedReplayAllocatedBytes = checked(allocatedAfter - allocatedBefore);
        Array.Sort(samples);
        var managed = new PredictionReplayPercentiles(
            Percentile(samples, 0.50),
            Percentile(samples, 0.95),
            Percentile(samples, 0.99),
            samples[^1]);

        return new PredictionPerformanceReport(
            scenario,
            sampleCount,
            store.Layout,
            store.FramePayloadBytes,
            store.HistoryPayloadBytes,
            store.TotalPreallocatedPayloadBytes,
            preallocatedManagedBytes,
            checked(preallocatedManagedBytes - store.TotalPreallocatedPayloadBytes),
            store.HistoryHighWaterFrames,
            store.SourceHighWater,
            store.ContactHighWater,
            store.EventHighWater,
            store.TransitionReferenceHighWater,
            store.ActionReferenceHighWater,
            store.DependencyHighWater,
            PredictionPoolUsagePolicy.InlinePreallocatedNoPool,
            managedReplayAllocatedBytes,
            ConservativeAllocatedBytesPerReplay(managedReplayAllocatedBytes, sampleCount),
            godot.ManagedAllocatedBytes,
            PredictionNativeAllocationMeasurementStatus.NotMeasured,
            managed,
            godot.MeasuredReplay,
            managed.ComponentwiseAdd(godot.MeasuredReplay),
            calibration.Metadata,
            checksum);
    }

    public static long ConservativeAllocatedBytesPerReplay(long allocatedBytes, int replayCount)
    {
        if (allocatedBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(allocatedBytes));
        }

        if (replayCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(replayCount));
        }

        return allocatedBytes == 0
            ? 0
            : checked(1 + (allocatedBytes - 1) / replayCount);
    }

    private static long TicksToNanoseconds(long ticks) => checked(
        (long)Math.Ceiling(ticks * 1_000_000_000d / Stopwatch.Frequency));

    private static long Percentile(long[] sorted, double percentile)
    {
        var rank = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Length - 1)];
    }

    private sealed class ProbeHistoryStore
    {
        private readonly ProbeFrameMetadata[] _metadata;
        private readonly ProbeCharacterState[] _preAndPostStates;
        private readonly ProbeCommand[] _commands;
        private readonly ProbeFrameResult[] _frameResults;
        private readonly ProbeDependencyJournal[] _dependencyJournals;
        private readonly ProbeCharacterState[] _scratch;

        public ProbeHistoryStore(
            PredictionPerformanceScenario scenario,
            PredictionPerformanceLayout layout)
        {
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            var frameCharacters = checked(HistoryCapacityFrames * scenario.TotalCombatants);
            _metadata = new ProbeFrameMetadata[HistoryCapacityFrames];
            _preAndPostStates = new ProbeCharacterState[checked(frameCharacters * 2)];
            _commands = new ProbeCommand[frameCharacters];
            _frameResults = new ProbeFrameResult[frameCharacters];
            _dependencyJournals = new ProbeDependencyJournal[frameCharacters];
            _scratch = new ProbeCharacterState[scenario.TotalCombatants];

            for (var frame = 0; frame < HistoryCapacityFrames; frame++)
            {
                _metadata[frame] = new ProbeFrameMetadata
                {
                    MatchFrame = checked((ulong)(10_000 + frame)),
                    Epoch = 7,
                    CharacterCount = scenario.TotalCombatants,
                };
                for (var combatant = 0; combatant < scenario.TotalCombatants; combatant++)
                {
                    var character = frame * scenario.TotalCombatants + combatant;
                    var state = CreateState(frame, combatant, scenario);
                    _preAndPostStates[character * 2] = state;
                    state.PositionX += 17;
                    _preAndPostStates[character * 2 + 1] = state;
                    _commands[character] = CreateCommand(frame, combatant, scenario);
                    _frameResults[character] = CreateFrameResult(frame, scenario);
                    _dependencyJournals[character] = CreateDependencyJournal(
                        combatant,
                        scenario);
                }
            }

            FramePayloadBytes = checked(
                Layout.FrameMetadataBytes +
                scenario.TotalCombatants *
                (2 * Layout.CharacterStateBytes +
                 Layout.CommandBytes +
                 Layout.FrameResultBytes +
                 Layout.DependencyJournalBytes));
            HistoryPayloadBytes = checked(FramePayloadBytes * HistoryCapacityFrames);
            TotalPreallocatedPayloadBytes = checked(
                HistoryPayloadBytes +
                scenario.TotalCombatants * Layout.CharacterStateBytes);
            HistoryHighWaterFrames = HistoryCapacityFrames;
            SourceHighWater = scenario.ActiveMovementSources;
            ContactHighWater = scenario.ActiveContactFacts;
            EventHighWater = scenario.ActiveSimulationEvents;
            TransitionReferenceHighWater = scenario.ActiveTransitionReferences;
            ActionReferenceHighWater = scenario.ActiveActionReferences;
            DependencyHighWater = scenario.ActiveCollisionDependencies;
        }

        public PredictionPerformanceLayout Layout { get; }
        public int FramePayloadBytes { get; }
        public int HistoryPayloadBytes { get; }
        public int TotalPreallocatedPayloadBytes { get; }
        public int HistoryHighWaterFrames { get; }
        public int SourceHighWater { get; }
        public int ContactHighWater { get; }
        public int EventHighWater { get; }
        public int TransitionReferenceHighWater { get; }
        public int ActionReferenceHighWater { get; }
        public int DependencyHighWater { get; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public ulong Replay(int sample, PredictionPerformanceScenario scenario)
        {
            var firstFrame = Math.Abs(sample * 17) %
                (HistoryCapacityFrames - scenario.ReplayDepthFrames);
            for (var combatant = 0; combatant < scenario.ReplayedCombatants; combatant++)
            {
                var character = firstFrame * scenario.TotalCombatants + combatant;
                _scratch[combatant] = _preAndPostStates[character * 2];
            }

            ulong checksum = 0;
            for (var offset = 0; offset < scenario.ReplayDepthFrames; offset++)
            {
                var frame = firstFrame + offset;
                ref readonly var metadata = ref _metadata[frame];
                for (var combatant = 0; combatant < scenario.ReplayedCombatants; combatant++)
                {
                    var character = frame * scenario.TotalCombatants + combatant;
                    ref readonly var command = ref _commands[character];
                    ref readonly var result = ref _frameResults[character];
                    ref readonly var dependencies = ref _dependencyJournals[character];
                    ref var state = ref _scratch[combatant];

                    state.VelocityX += command.AxisX >> 7;
                    state.VelocityZ += command.AxisZ >> 7;
                    state.PositionX += state.VelocityX;
                    state.PositionY += state.VelocityY;
                    state.PositionZ += state.VelocityZ;
                    state.ButtonState = command.Buttons;
                    state.MovementRevision = command.MovementRevision;
                    state.CapabilityRevision = command.CapabilityRevision;

                    for (var source = 0; source < scenario.ActiveMovementSources; source++)
                    {
                        ref var active = ref state.Sources[source];
                        state.VelocityX += active.DirectionX * active.MagnitudeQ15 >> 20;
                        state.VelocityZ += active.DirectionZ * active.MagnitudeQ15 >> 20;
                        active.ElapsedFrames++;
                    }

                    for (var iteration = 0;
                         iteration < scenario.StaticSweepsPerCharacterFrame;
                         iteration++)
                    {
                        var limit = 4_000_000 - iteration * 31;
                        state.PositionX = Math.Clamp(state.PositionX, -limit, limit);
                        state.PositionY = Math.Max(state.PositionY - 27, 0);
                        state.PositionZ = Math.Clamp(state.PositionZ, -limit, limit);
                        state.Grounded = state.PositionY == 0 ? (byte)1 : (byte)0;
                    }

                    for (var contact = 0; contact < scenario.ActiveContactFacts; contact++)
                    {
                        ref readonly var fact = ref state.Contacts[contact];
                        state.DeterministicChecksum ^= fact.ColliderId + fact.NormalOct;
                    }

                    for (var transition = 0;
                         transition < scenario.ActiveTransitionReferences;
                         transition++)
                    {
                        state.DeterministicChecksum ^= command.Transitions[transition];
                    }

                    for (var action = 0; action < scenario.ActiveActionReferences; action++)
                    {
                        state.DeterministicChecksum ^= command.Actions[action];
                    }

                    for (var simulationEvent = 0;
                         simulationEvent < scenario.ActiveSimulationEvents;
                         simulationEvent++)
                    {
                        ref readonly var identified = ref result.Events[simulationEvent];
                        state.DeterministicChecksum ^= identified.EventId + identified.PolicyId;
                    }

                    for (var dependency = 0;
                         dependency < scenario.ActiveCollisionDependencies;
                         dependency++)
                    {
                        ref readonly var edge = ref dependencies.Dependencies[dependency];
                        state.DeterministicChecksum ^= edge.OtherCombatantId + edge.ColliderId;
                    }

                    state.LastMatchFrame = metadata.MatchFrame;
                    checksum ^= unchecked(
                        (ulong)(state.PositionX + state.PositionY + state.PositionZ) +
                        state.DeterministicChecksum);
                }
            }

            return checksum;
        }

        private static ProbeCharacterState CreateState(
            int frame,
            int combatant,
            PredictionPerformanceScenario scenario)
        {
            var state = new ProbeCharacterState
            {
                PositionX = frame * 13 + combatant * 101,
                PositionY = 2_000 + combatant * 17,
                PositionZ = -frame * 7 + combatant * 53,
                VelocityX = 80 + combatant,
                VelocityY = -12,
                VelocityZ = 70 - combatant,
                FacingQ15 = (short)(1_000 + combatant),
                CollisionProfile = 1,
                MovementRevision = 3,
                CapabilityRevision = 5,
                LastMatchFrame = checked((ulong)(10_000 + frame)),
            };

            for (var source = 0; source < scenario.ActiveMovementSources; source++)
            {
                state.Sources[source] = new ProbeMovementSource
                {
                    InstanceId = checked((uint)(combatant * 1_000 + source + 1)),
                    PolicyId = checked((ushort)(source + 1)),
                    ElapsedFrames = checked((ushort)(frame & 0xffff)),
                    DirectionX = (short)(source % 2 == 0 ? 24_000 : -24_000),
                    DirectionZ = (short)(source % 3 == 0 ? 18_000 : -18_000),
                    MagnitudeQ15 = checked((short)(1_000 + source)),
                    Priority = (byte)source,
                    Flags = (byte)(source & 0x0f),
                    AuthorityCorrelation = checked((uint)(50_000 + source)),
                };
            }

            for (var contact = 0; contact < scenario.ActiveContactFacts; contact++)
            {
                state.Contacts[contact] = new ProbeContactFact
                {
                    ColliderId = checked((ulong)(1_000 + combatant * 10 + contact)),
                    ShapeId = checked((ushort)(contact + 1)),
                    NormalOct = checked((ushort)(30_000 + contact)),
                    SeparationMillimeters = checked((short)(contact * 2)),
                    Kind = (byte)(contact + 1),
                };
            }

            return state;
        }

        private static ProbeCommand CreateCommand(
            int frame,
            int combatant,
            PredictionPerformanceScenario scenario)
        {
            var command = new ProbeCommand
            {
                Sequence = checked((ulong)(frame * scenario.TotalCombatants + combatant + 1)),
                TargetMatchFrame = checked((ulong)(10_000 + frame)),
                AxisX = (short)(combatant % 2 == 0 ? 24_000 : -24_000),
                AxisZ = (short)(frame % 2 == 0 ? 18_000 : -18_000),
                Buttons = (ushort)((frame + combatant) & 0x3f),
                MovementRevision = 3,
                CapabilityRevision = 5,
                TransitionCount = (byte)scenario.ActiveTransitionReferences,
                ActionCount = (byte)scenario.ActiveActionReferences,
            };
            for (var index = 0; index < scenario.ActiveTransitionReferences; index++)
            {
                command.Transitions[index] = checked((ulong)(70_000 + index));
            }

            for (var index = 0; index < scenario.ActiveActionReferences; index++)
            {
                command.Actions[index] = checked((ulong)(80_000 + index));
            }

            return command;
        }

        private static ProbeFrameResult CreateFrameResult(
            int frame,
            PredictionPerformanceScenario scenario)
        {
            var result = new ProbeFrameResult
            {
                EventCount = (byte)scenario.ActiveSimulationEvents,
                CanonicalStateHash = checked((ulong)(90_000 + frame)),
            };
            for (var index = 0; index < scenario.ActiveSimulationEvents; index++)
            {
                result.Events[index] = new ProbeSimulationEvent
                {
                    EventId = checked((uint)(frame * MaximumSimulationEvents + index + 1)),
                    PolicyId = checked((ushort)(index + 1)),
                    FrameOffset = checked((ushort)index),
                    Magnitude = 1_000 + index,
                    Flags = (uint)(index & 0x0f),
                };
            }

            return result;
        }

        private static ProbeDependencyJournal CreateDependencyJournal(
            int combatant,
            PredictionPerformanceScenario scenario)
        {
            var journal = new ProbeDependencyJournal
            {
                DependencyCount = (byte)scenario.ActiveCollisionDependencies,
            };
            for (var index = 0; index < scenario.ActiveCollisionDependencies; index++)
            {
                journal.Dependencies[index] = new ProbeCollisionDependency
                {
                    OtherCombatantId = checked((ulong)(200 + (combatant + index + 1) % 8)),
                    ColliderId = checked((ulong)(1_000 + index)),
                    Kind = (uint)(index % 3 + 1),
                    FirstFrameOffset = checked((ushort)index),
                    LastFrameOffset = checked((ushort)(index + 1)),
                };
            }

            return journal;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProbeFrameMetadata
    {
        public ulong MatchFrame;
        public ulong Epoch;
        public int CharacterCount;
        public int Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProbeCharacterState
    {
        public int PositionX;
        public int PositionY;
        public int PositionZ;
        public int VelocityX;
        public int VelocityY;
        public int VelocityZ;
        public short FacingQ15;
        public byte Grounded;
        public byte CollisionProfile;
        public ushort ButtonState;
        public ushort Reserved;
        public ulong MovementRevision;
        public ulong CapabilityRevision;
        public ulong LastMatchFrame;
        public ulong DeterministicChecksum;
        public ProbeMovementSourceBuffer Sources;
        public ProbeContactFactBuffer Contacts;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProbeCommand
    {
        public ulong Sequence;
        public ulong TargetMatchFrame;
        public short AxisX;
        public short AxisZ;
        public ushort Buttons;
        public byte TransitionCount;
        public byte ActionCount;
        public ulong MovementRevision;
        public ulong CapabilityRevision;
        public ProbeTransitionReferenceBuffer Transitions;
        public ProbeActionReferenceBuffer Actions;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProbeFrameResult
    {
        public ulong CanonicalStateHash;
        public byte EventCount;
        public ProbeSimulationEventBuffer Events;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProbeDependencyJournal
    {
        public byte DependencyCount;
        public ProbeCollisionDependencyBuffer Dependencies;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProbeMovementSource
    {
        public uint InstanceId;
        public ushort PolicyId;
        public ushort ElapsedFrames;
        public short DirectionX;
        public short DirectionZ;
        public short MagnitudeQ15;
        public byte Priority;
        public byte Flags;
        public uint AuthorityCorrelation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProbeContactFact
    {
        public ulong ColliderId;
        public ushort ShapeId;
        public ushort NormalOct;
        public short SeparationMillimeters;
        public byte Kind;
        public byte Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProbeSimulationEvent
    {
        public uint EventId;
        public ushort PolicyId;
        public ushort FrameOffset;
        public int Magnitude;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProbeCollisionDependency
    {
        public ulong OtherCombatantId;
        public ulong ColliderId;
        public uint Kind;
        public ushort FirstFrameOffset;
        public ushort LastFrameOffset;
    }

    [InlineArray(MaximumMovementSources)]
    private struct ProbeMovementSourceBuffer { private ProbeMovementSource _element0; }
    [InlineArray(MaximumContactFacts)]
    private struct ProbeContactFactBuffer { private ProbeContactFact _element0; }
    [InlineArray(MaximumSimulationEvents)]
    private struct ProbeSimulationEventBuffer { private ProbeSimulationEvent _element0; }
    [InlineArray(MaximumTransitionReferences)]
    private struct ProbeTransitionReferenceBuffer { private ulong _element0; }
    [InlineArray(MaximumActionReferences)]
    private struct ProbeActionReferenceBuffer { private ulong _element0; }
    [InlineArray(MaximumCollisionDependencies)]
    private struct ProbeCollisionDependencyBuffer { private ProbeCollisionDependency _element0; }

    private static class ProbeLayoutInspector
    {
        private static readonly Lazy<PredictionPerformanceLayout> Cached = new(
            CreateUncached,
            LazyThreadSafetyMode.ExecutionAndPublication);
        private static int _inspectionCount;

        public static PredictionPerformanceLayout Layout => Cached.Value;
        public static int InspectionCount => Volatile.Read(ref _inspectionCount);

        private static PredictionPerformanceLayout CreateUncached()
        {
            Interlocked.Increment(ref _inspectionCount);
            return new(
            Unsafe.SizeOf<ProbeCharacterState>(),
            Unsafe.SizeOf<ProbeCommand>(),
            Unsafe.SizeOf<ProbeFrameResult>(),
            Unsafe.SizeOf<ProbeDependencyJournal>(),
            Unsafe.SizeOf<ProbeFrameMetadata>(),
            !RuntimeHelpers.IsReferenceOrContainsReferences<ProbeCharacterState>() &&
            !RuntimeHelpers.IsReferenceOrContainsReferences<ProbeCommand>() &&
            !RuntimeHelpers.IsReferenceOrContainsReferences<ProbeFrameResult>() &&
            !RuntimeHelpers.IsReferenceOrContainsReferences<ProbeDependencyJournal>() &&
            !RuntimeHelpers.IsReferenceOrContainsReferences<ProbeFrameMetadata>(),
            HasExactBuffers<ProbeCharacterState>(
                typeof(ProbeMovementSourceBuffer),
                typeof(ProbeContactFactBuffer)),
            HasExactBuffers<ProbeCommand>(
                typeof(ProbeTransitionReferenceBuffer),
                typeof(ProbeActionReferenceBuffer)),
            HasExactBuffers<ProbeFrameResult>(typeof(ProbeSimulationEventBuffer)),
            HasExactBuffers<ProbeDependencyJournal>(typeof(ProbeCollisionDependencyBuffer)),
            MaximumMovementSources,
            MaximumContactFacts,
            MaximumTransitionReferences,
            MaximumActionReferences,
            MaximumSimulationEvents,
            MaximumCollisionDependencies);
        }

        private static bool HasExactBuffers<T>(params Type[] expected)
        {
            var actual = typeof(T)
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(field => field.FieldType)
                .Where(type => type.Name.EndsWith("Buffer", StringComparison.Ordinal))
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .ToArray();
            return actual.SequenceEqual(
                expected.OrderBy(type => type.FullName, StringComparer.Ordinal));
        }
    }
}

public enum PredictionPerformanceLoad { ExpectedCausalIsland, WorstAllCombatants }
public enum PredictionPoolUsagePolicy { InlinePreallocatedNoPool }
public enum PredictionNativeAllocationMeasurementStatus { NotMeasured }

public sealed record PredictionPerformanceScenario
{
    private PredictionPerformanceScenario(
        string name,
        PredictionPerformanceLoad load,
        int totalCombatants,
        int replayedCombatants,
        int replayDepthFrames,
        int staticSweepsPerCharacterFrame,
        int activeMovementSources,
        int activeContactFacts,
        int activeSimulationEvents,
        int activeTransitionReferences,
        int activeActionReferences,
        int activeCollisionDependencies)
    {
        Name = name;
        Load = load;
        TotalCombatants = totalCombatants;
        ReplayedCombatants = replayedCombatants;
        ReplayDepthFrames = replayDepthFrames;
        StaticSweepsPerCharacterFrame = staticSweepsPerCharacterFrame;
        ActiveMovementSources = activeMovementSources;
        ActiveContactFacts = activeContactFacts;
        ActiveSimulationEvents = activeSimulationEvents;
        ActiveTransitionReferences = activeTransitionReferences;
        ActiveActionReferences = activeActionReferences;
        ActiveCollisionDependencies = activeCollisionDependencies;
    }

    public string Name { get; }
    public PredictionPerformanceLoad Load { get; }
    public int TotalCombatants { get; }
    public int ReplayedCombatants { get; }
    public int ReplayDepthFrames { get; }
    public int StaticSweepsPerCharacterFrame { get; }
    public int ActiveMovementSources { get; }
    public int ActiveContactFacts { get; }
    public int ActiveSimulationEvents { get; }
    public int ActiveTransitionReferences { get; }
    public int ActiveActionReferences { get; }
    public int ActiveCollisionDependencies { get; }
    public int StaticQueriesPerReplay => checked(
        ReplayedCombatants * ReplayDepthFrames * StaticSweepsPerCharacterFrame);

    public static PredictionPerformanceScenario Expected(int totalCombatants)
    {
        ValidateCombatants(totalCombatants);
        return new(
            $"expected-{totalCombatants}",
            PredictionPerformanceLoad.ExpectedCausalIsland,
            totalCombatants,
            Math.Min(totalCombatants, 3),
            8,
            4,
            4,
            2,
            4,
            4,
            2,
            4);
    }

    public static PredictionPerformanceScenario WorstBounded(int totalCombatants)
    {
        ValidateCombatants(totalCombatants);
        return new(
            $"worst-{totalCombatants}",
            PredictionPerformanceLoad.WorstAllCombatants,
            totalCombatants,
            totalCombatants,
            32,
            8,
            PredictionPerformanceProbe.MaximumMovementSources,
            PredictionPerformanceProbe.MaximumContactFacts,
            PredictionPerformanceProbe.MaximumSimulationEvents,
            PredictionPerformanceProbe.MaximumTransitionReferences,
            PredictionPerformanceProbe.MaximumActionReferences,
            PredictionPerformanceProbe.MaximumCollisionDependencies);
    }

    private static void ValidateCombatants(int value)
    {
        if (value is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Expected one to eight combatants.");
        }
    }
}

public sealed record PredictionPerformanceLayout(
    int CharacterStateBytes,
    int CommandBytes,
    int FrameResultBytes,
    int DependencyJournalBytes,
    int FrameMetadataBytes,
    bool AllHotStructsContainNoManagedReferences,
    bool CharacterStateBufferOwnershipValid,
    bool CommandBufferOwnershipValid,
    bool FrameResultBufferOwnershipValid,
    bool DependencyJournalBufferOwnershipValid,
    int StateMovementSourceCapacity,
    int StateContactCapacity,
    int CommandTransitionCapacity,
    int CommandActionCapacity,
    int FrameEventCapacity,
    int DependencyCapacity);

public sealed record PredictionReplayPercentiles
{
    public PredictionReplayPercentiles(
        long p50Nanoseconds,
        long p95Nanoseconds,
        long p99Nanoseconds,
        long maximumNanoseconds)
    {
        if (p50Nanoseconds <= 0 ||
            p50Nanoseconds > p95Nanoseconds ||
            p95Nanoseconds > p99Nanoseconds ||
            p99Nanoseconds > maximumNanoseconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(p50Nanoseconds),
                "Replay percentiles must be positive and monotonically ordered.");
        }

        P50Nanoseconds = p50Nanoseconds;
        P95Nanoseconds = p95Nanoseconds;
        P99Nanoseconds = p99Nanoseconds;
        MaximumNanoseconds = maximumNanoseconds;
    }

    public long P50Nanoseconds { get; }
    public long P95Nanoseconds { get; }
    public long P99Nanoseconds { get; }
    public long MaximumNanoseconds { get; }

    public PredictionReplayPercentiles ComponentwiseAdd(PredictionReplayPercentiles other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new(
            checked(P50Nanoseconds + other.P50Nanoseconds),
            checked(P95Nanoseconds + other.P95Nanoseconds),
            checked(P99Nanoseconds + other.P99Nanoseconds),
            checked(MaximumNanoseconds + other.MaximumNanoseconds));
    }
}

public sealed record PredictionGodotReplayCalibration(
    int TotalCombatants,
    int ReplayedCombatants,
    int ReplayDepthFrames,
    int StaticSweepsPerCharacterFrame,
    int SampleCountPerRun,
    int QueriesPerSample,
    int RetainedRunCount,
    PredictionReplayPercentiles MeasuredReplay,
    long ManagedAllocatedBytes)
{
    public bool Matches(PredictionPerformanceScenario scenario) =>
        TotalCombatants == scenario.TotalCombatants &&
        ReplayedCombatants == scenario.ReplayedCombatants &&
        ReplayDepthFrames == scenario.ReplayDepthFrames &&
        StaticSweepsPerCharacterFrame == scenario.StaticSweepsPerCharacterFrame;
}

public sealed record PredictionGodotReplayMeasurement(
    int TotalCombatants,
    int ReplayedCombatants,
    int ReplayDepthFrames,
    int StaticSweepsPerCharacterFrame,
    int SampleCount,
    int QueriesPerSample,
    PredictionReplayPercentiles MeasuredReplay,
    long ManagedAllocatedBytes)
{
    public (int Total, int Replayed, int Depth, int Sweeps) Key =>
        (TotalCombatants, ReplayedCombatants, ReplayDepthFrames, StaticSweepsPerCharacterFrame);
}

public sealed class PredictionGodotCalibrationRun
{
    private readonly ReadOnlyCollection<PredictionGodotReplayMeasurement> _measurements;

    public PredictionGodotCalibrationRun(
        int runNumber,
        IReadOnlyList<PredictionGodotReplayMeasurement> measurements)
    {
        ArgumentNullException.ThrowIfNull(measurements);
        if (runNumber <= 0 || measurements.Count == 0 || measurements.Any(value => value is null))
        {
            throw new ArgumentException("A raw Godot run must have an identity and measurements.");
        }

        var copy = measurements.ToArray();
        if (copy.GroupBy(value => value.Key).Any(group => group.Count() != 1))
        {
            throw new ArgumentException("Raw Godot run scenario keys must be unique.", nameof(measurements));
        }

        foreach (var value in copy)
        {
            ArgumentNullException.ThrowIfNull(value.MeasuredReplay);
            if (value.TotalCombatants is < 1 or > 8 ||
                value.ReplayedCombatants is < 1 ||
                value.ReplayedCombatants > value.TotalCombatants ||
                value.ReplayDepthFrames <= 0 ||
                value.StaticSweepsPerCharacterFrame <= 0 ||
                value.SampleCount < 200 ||
                value.QueriesPerSample != checked(
                    value.ReplayedCombatants * value.ReplayDepthFrames *
                    value.StaticSweepsPerCharacterFrame) ||
                value.ManagedAllocatedBytes < 0)
            {
                throw new ArgumentException("A raw Godot measurement is incomplete.", nameof(measurements));
            }
        }

        RunNumber = runNumber;
        _measurements = Array.AsReadOnly(copy);
    }

    public int RunNumber { get; }
    public IReadOnlyList<PredictionGodotReplayMeasurement> Measurements => _measurements;
}

public sealed record PredictionCalibrationMetadata(
    string Cpu,
    string OperatingSystem,
    string GodotBuild,
    string BuildConfiguration,
    int WarmupBatchesPerScenario,
    int RetainedRuns,
    string AggregationPolicy,
    string CapturedOn);

public sealed class PredictionQueryCalibration
{
    private readonly ReadOnlyCollection<PredictionGodotReplayCalibration> _profiles;
    private readonly ReadOnlyCollection<PredictionGodotCalibrationRun> _rawRuns;

    public static IReadOnlyList<PredictionGodotCalibrationRun> P01_11RawRuns { get; } =
        Array.AsReadOnly(new[]
        {
            RawRun(1,
                Raw(1, 1, 8, 4, 32, 340, 353, 369, 374),
                Raw(1, 1, 32, 8, 256, 2_799, 2_893, 3_232, 3_417),
                Raw(3, 3, 8, 4, 96, 1_033, 1_105, 1_202, 1_318),
                Raw(3, 3, 32, 8, 768, 8_427, 8_640, 8_982, 10_197),
                Raw(8, 3, 8, 4, 96, 1_075, 1_099, 1_148, 1_565),
                Raw(8, 8, 32, 8, 2_048, 22_469, 23_095, 23_340, 24_202)),
            RawRun(2,
                Raw(1, 1, 8, 4, 32, 347, 367, 519, 618),
                Raw(1, 1, 32, 8, 256, 2_854, 2_942, 3_157, 4_120),
                Raw(3, 3, 8, 4, 96, 1_074, 1_098, 1_116, 1_135),
                Raw(3, 3, 32, 8, 768, 8_612, 8_706, 8_934, 11_577),
                Raw(8, 3, 8, 4, 96, 1_075, 1_106, 1_139, 1_162),
                Raw(8, 8, 32, 8, 2_048, 23_003, 23_343, 24_444, 34_916)),
            RawRun(3,
                Raw(1, 1, 8, 4, 32, 355, 377, 455, 521),
                Raw(1, 1, 32, 8, 256, 2_869, 3_022, 3_104, 3_510),
                Raw(3, 3, 8, 4, 96, 1_075, 1_128, 1_239, 1_242),
                Raw(3, 3, 32, 8, 768, 8_654, 8_852, 9_059, 11_684),
                Raw(8, 3, 8, 4, 96, 1_077, 1_136, 1_236, 1_244),
                Raw(8, 8, 32, 8, 2_048, 23_086, 23_339, 23_575, 24_385)),
            RawRun(4,
                Raw(1, 1, 8, 4, 32, 356, 377, 431, 519),
                Raw(1, 1, 32, 8, 256, 2_867, 3_023, 3_146, 3_240),
                Raw(3, 3, 8, 4, 96, 1_076, 1_126, 1_246, 1_263),
                Raw(3, 3, 32, 8, 768, 8_683, 8_895, 9_017, 9_777),
                Raw(8, 3, 8, 4, 96, 1_078, 1_138, 1_263, 1_295),
                Raw(8, 8, 32, 8, 2_048, 23_242, 23_856, 28_697, 37_231)),
        });

    public static PredictionQueryCalibration P01_11GodotHeadless { get; } = AggregateRawRuns(
        new PredictionCalibrationMetadata(
            "12th Gen Intel(R) Core(TM) i7-12700H",
            "Microsoft Windows 10.0.26200",
            "Godot 4.4.stable.mono custom_build.4c311cbee",
            "Debug C# / headless custom editor",
            64,
            4,
            "Conservative component-wise maximum for p50/p95/p99/max across four retained runs",
            "2026-08-11"),
        P01_11RawRuns);

    public PredictionQueryCalibration(
        PredictionCalibrationMetadata metadata,
        IReadOnlyList<PredictionGodotReplayCalibration> profiles)
        : this(metadata, profiles, Array.Empty<PredictionGodotCalibrationRun>())
    {
    }

    private PredictionQueryCalibration(
        PredictionCalibrationMetadata metadata,
        IReadOnlyList<PredictionGodotReplayCalibration> profiles,
        IReadOnlyList<PredictionGodotCalibrationRun> rawRuns)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(profiles);
        if (profiles.Count == 0 || profiles.Any(profile => profile is null))
        {
            throw new ArgumentException("At least one complete Godot profile is required.", nameof(profiles));
        }

        if (string.IsNullOrWhiteSpace(metadata.Cpu) ||
            string.IsNullOrWhiteSpace(metadata.OperatingSystem) ||
            string.IsNullOrWhiteSpace(metadata.GodotBuild) ||
            string.IsNullOrWhiteSpace(metadata.BuildConfiguration) ||
            string.IsNullOrWhiteSpace(metadata.AggregationPolicy) ||
            string.IsNullOrWhiteSpace(metadata.CapturedOn) ||
            metadata.WarmupBatchesPerScenario <= 0 ||
            metadata.RetainedRuns <= 0)
        {
            throw new ArgumentException(
                "Calibration metadata must completely identify the measurement environment and run policy.",
                nameof(metadata));
        }

        foreach (var profile in profiles)
        {
            ArgumentNullException.ThrowIfNull(profile.MeasuredReplay);
            if (profile.TotalCombatants is < 1 or > 8 ||
                profile.ReplayedCombatants < 1 ||
                profile.ReplayedCombatants > profile.TotalCombatants ||
                profile.ReplayDepthFrames <= 0 ||
                profile.StaticSweepsPerCharacterFrame <= 0 ||
                profile.SampleCountPerRun < 200 ||
                profile.QueriesPerSample != checked(
                    profile.ReplayedCombatants *
                    profile.ReplayDepthFrames *
                    profile.StaticSweepsPerCharacterFrame) ||
                profile.RetainedRunCount != metadata.RetainedRuns ||
                profile.ManagedAllocatedBytes < 0)
            {
                throw new ArgumentException(
                    "A Godot replay calibration profile is incomplete or internally inconsistent.",
                    nameof(profiles));
            }
        }

        if (profiles
            .GroupBy(profile => (
                profile.TotalCombatants,
                profile.ReplayedCombatants,
                profile.ReplayDepthFrames,
                profile.StaticSweepsPerCharacterFrame))
            .Any(group => group.Count() != 1))
        {
            throw new ArgumentException(
                "Godot replay calibration keys must be unique.",
                nameof(profiles));
        }

        Metadata = metadata;
        _profiles = Array.AsReadOnly(profiles.ToArray());
        _rawRuns = Array.AsReadOnly(rawRuns.ToArray());
    }

    public PredictionCalibrationMetadata Metadata { get; }
    public IReadOnlyList<PredictionGodotReplayCalibration> Profiles => _profiles;
    public IReadOnlyList<PredictionGodotCalibrationRun> RawRuns => _rawRuns;

    public PredictionGodotReplayCalibration Resolve(PredictionPerformanceScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        var matches = _profiles.Where(profile => profile.Matches(scenario)).ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException(
                $"Expected exactly one Godot calibration for {scenario.Name}, found {matches.Length}.");
    }

    public static PredictionQueryCalibration AggregateRawRuns(
        PredictionCalibrationMetadata metadata,
        IReadOnlyList<PredictionGodotCalibrationRun> runs)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(runs);
        if (runs.Count == 0 || runs.Any(run => run is null) ||
            runs.Select(run => run.RunNumber).Distinct().Count() != runs.Count ||
            metadata.RetainedRuns != runs.Count)
        {
            throw new ArgumentException("Retained raw runs must be present, uniquely identified, and match metadata.", nameof(runs));
        }

        var runCopies = runs.ToArray();
        var expectedKeys = runCopies[0].Measurements.Select(value => value.Key).ToHashSet();
        foreach (var run in runCopies)
        {
            var keys = run.Measurements.Select(value => value.Key).ToHashSet();
            if (!keys.SetEquals(expectedKeys) || run.Measurements.Count != expectedKeys.Count)
            {
                throw new ArgumentException("Every retained run must contain exactly the same scenario keys.", nameof(runs));
            }
        }

        var aggregated = expectedKeys
            .OrderBy(key => key.Total)
            .ThenBy(key => key.Replayed)
            .ThenBy(key => key.Depth)
            .ThenBy(key => key.Sweeps)
            .Select(key =>
            {
                var values = runCopies
                    .Select(run => run.Measurements.Single(value => value.Key == key))
                    .ToArray();
                var first = values[0];
                if (values.Any(value =>
                    value.SampleCount != first.SampleCount ||
                    value.QueriesPerSample != first.QueriesPerSample))
                {
                    throw new ArgumentException("Raw scenario sample and query counts must agree across runs.", nameof(runs));
                }

                return new PredictionGodotReplayCalibration(
                    key.Total,
                    key.Replayed,
                    key.Depth,
                    key.Sweeps,
                    first.SampleCount,
                    first.QueriesPerSample,
                    runCopies.Length,
                    new PredictionReplayPercentiles(
                        values.Max(value => value.MeasuredReplay.P50Nanoseconds),
                        values.Max(value => value.MeasuredReplay.P95Nanoseconds),
                        values.Max(value => value.MeasuredReplay.P99Nanoseconds),
                        values.Max(value => value.MeasuredReplay.MaximumNanoseconds)),
                    values.Max(value => value.ManagedAllocatedBytes));
            })
            .ToArray();

        return new PredictionQueryCalibration(metadata, aggregated, runCopies);
    }

    private static PredictionGodotCalibrationRun RawRun(
        int runNumber,
        params PredictionGodotReplayMeasurement[] values) => new(runNumber, values);

    private static PredictionGodotReplayMeasurement Raw(
        int total,
        int replayed,
        int depth,
        int sweeps,
        int queries,
        long p50Microseconds,
        long p95Microseconds,
        long p99Microseconds,
        long maximumMicroseconds) => new(
            total,
            replayed,
            depth,
            sweeps,
            256,
            queries,
            new PredictionReplayPercentiles(
                checked(p50Microseconds * 1_000),
                checked(p95Microseconds * 1_000),
                checked(p99Microseconds * 1_000),
                checked(maximumMicroseconds * 1_000)),
            ManagedAllocatedBytes: 0);
}

public sealed record PredictionPerformanceReport(
    PredictionPerformanceScenario Scenario,
    int ManagedSampleCount,
    PredictionPerformanceLayout Layout,
    int FramePayloadBytes,
    int HistoryPayloadBytes,
    int TotalPreallocatedPayloadBytes,
    long PreallocatedManagedBytes,
    long PreallocatedManagedOverheadBytes,
    int HistoryHighWaterFrames,
    int SourceHighWater,
    int ContactHighWater,
    int EventHighWater,
    int TransitionReferenceHighWater,
    int ActionReferenceHighWater,
    int DependencyHighWater,
    PredictionPoolUsagePolicy PoolUsagePolicy,
    long ManagedPrototypeReplayAllocatedBytes,
    long ManagedPrototypeCeilingAllocatedBytesPerReplay,
    long GodotManagedQueryAllocatedBytes,
    PredictionNativeAllocationMeasurementStatus GodotNativeAllocationStatus,
    PredictionReplayPercentiles ManagedPrototypeReplay,
    PredictionReplayPercentiles GodotQueryReplayMeasured,
    PredictionReplayPercentiles PlanningProjection,
    PredictionCalibrationMetadata CalibrationMetadata,
    ulong Checksum);
