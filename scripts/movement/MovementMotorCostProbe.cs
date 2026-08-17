using System.Diagnostics;
using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Core.Movement.Simulation;
using Godot;

namespace BattleArena.Movement;

/// <summary>
/// Measures the explicit motor's real per-frame query cost in-engine.
/// </summary>
/// <remarks>
/// <para>
/// P01-11 measured a single query at about 10.8 microseconds, but the motor
/// issues several per frame — overlap recovery, the initial sweep, a re-sweep
/// per slide iteration, a ground probe, and up to three more when a step is
/// solved. The real per-frame budget is therefore a multiple that has never been
/// measured, and replay multiplies it again by history depth.
/// </para>
/// <para>
/// This is what turns the replay-depth cap from a guess into a number. Without
/// it, the cap is chosen by intuition and discovered to be wrong during
/// acceptance, with far more built on top.
/// </para>
/// <para>
/// Both geometries are measured because the interesting number is the bad one. A
/// character crossing open ground issues the fewest queries the motor ever
/// issues; a character wedged into a corner while stepping issues the most, and
/// that is the frame that decides whether a replay depth is affordable.
/// </para>
/// </remarks>
public sealed partial class MovementMotorCostProbe : Node3D
{
    /// <summary>
    /// Replay depths measured.
    /// </summary>
    /// <remarks>
    /// Sampled densely enough to locate where the budget is actually crossed, not
    /// just to bracket it. The first version measured 1, 8 and 32 only and then
    /// declared the supported depth to be 8 — which was the largest sampled depth
    /// that passed, not the largest affordable one. The curve is close to linear at
    /// roughly 240 microseconds per depth, so 8 was conservative by about half and
    /// nobody could have seen that from the samples taken.
    /// </remarks>
    private static readonly int[] ReplayDepths = [1, 4, 8, 12, 16, 20, 24, 32];

    /// <summary>
    /// The replay depth the project intends to support, derived from this probe.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Eight, and this is a deliberately conservative choice rather than the limit.
    /// The measurement is clear about where the limit is: over the hostile
    /// geometry, p95 crosses the quarter-frame budget between depth 12 (3476 us,
    /// 83% of budget) and depth 16 (4260 us, 102%). Depth 12 is affordable on this
    /// machine.
    /// </para>
    /// <para>
    /// The cap is held at 8 because 8 costs 51% of the budget and 12 costs 83%, and
    /// the machine that produced these numbers is a development machine rather than
    /// the slowest that will run the game. Leaving nearly half the budget spare is
    /// worth more than four frames of replay depth.
    /// </para>
    /// <para>
    /// The first version of this constant claimed 8 "because that is what the
    /// measurement supports and 32 is not". That was wrong in a way worth recording:
    /// depths 1, 8 and 32 were the only ones sampled, so 8 was simply the largest
    /// sampled depth that passed. "32 is unaffordable" is an argument against 32, not
    /// evidence for 8. The intermediate depths are now measured, which is what turns
    /// this from a guess dressed as a measurement into a choice with a number behind
    /// it.
    /// </para>
    /// <para>
    /// This constant is the whole output of P5B-03: Phase 6's replay-depth cap in
    /// <c>OwnerPredictionWorkPolicy</c> must not exceed it. Depths above it are
    /// measured and reported but not gated — a gate that fails by design is a gate
    /// everyone learns to ignore — and the probe fails if this constant drifts
    /// either above what is affordable or to less than half of it.
    /// </para>
    /// </remarks>
    private const int IntendedReplayDepth = 8;

    /// <summary>
    /// Percentile enforced instead of the single worst frame.
    /// </summary>
    /// <remarks>
    /// The worst frame in a headless probe is dominated by GC and scheduler noise
    /// rather than query cost: at depth 1 over open ground the worst frame measured
    /// 2677 microseconds against a 129 microsecond mean, which is a collection
    /// pause and not twenty times the work. Gating on that would make the gate
    /// flaky, and a flaky gate gets disabled. The 95th percentile still catches a
    /// real per-frame regression while tolerating a handful of pauses, and the mean
    /// and true worst are both still reported so a genuine spike stays visible.
    /// </remarks>
    private const double EnforcedPercentile = 0.95d;

    /// <summary>
    /// Share of one 60 Hz frame that movement replay may consume.
    /// </summary>
    /// <remarks>
    /// A quarter of the frame. Movement is one system among many — animation,
    /// rendering, networking, and combat all need the rest — so a replay budget
    /// that consumed the whole frame would be no budget at all. A hard failure
    /// past this is deliberate: an unaffordable replay is a design problem, and
    /// the cap that bounds it has to come from this number rather than from
    /// intuition.
    /// </remarks>
    private const double FrameBudgetShare = 0.25d;

    private const uint StaticWorldLayer = 1 << 0;
    private const int MeasuredFrames = 240;
    private const int WarmupFrames = 60;

    /// <summary>
    /// Fewest queries a genuinely simulated frame can issue.
    /// </summary>
    /// <remarks>
    /// Overlap recovery plus a sweep plus a ground probe is three, and every frame
    /// pays all three. Anything less means the motor returned early — an
    /// unrecoverable penetration, say — and the timing is measuring a bail-out
    /// rather than movement work.
    /// </remarks>
    private const int MinimumQueriesPerSimulatedFrame = 3;

    private readonly List<string> _failures = [];
    private readonly List<string> _report = [];
    private readonly Dictionary<int, double> _perDepthWorstPercentile = [];

    private MovementAttributeSnapshot _attributes = null!;
    private MovementCapabilitySnapshot _capabilities = null!;
    private CollisionProfileTable _profiles = null!;
    private SimulationRate _rate;
    private CountingCollisionWorld _world = null!;
    private int _settleFrames;

    [Export]
    public string MovementProfilePath { get; set; } = "res://assets/classes/fighter/movement.json";

    public override void _Ready()
    {
        _rate = new SimulationRate(Engine.PhysicsTicksPerSecond);
        _attributes = MovementProfileLoader.Load(MovementProfilePath, simulationRate: _rate).Attributes;
        _capabilities = MovementCapabilitySnapshot.CreateBaseFighter(1);
        _profiles = CollisionProfileTable.FromAttributes(_attributes);

        BuildGeometry();

        var inner = new GodotKinematicCollisionWorld();
        AddChild(inner);
        inner.Initialize(_profiles, StaticWorldLayer);
        _world = new CountingCollisionWorld(inner);
    }

    /// <summary>
    /// Waits for the static bodies to exist before timing anything.
    /// </summary>
    /// <remarks>
    /// Querying an empty space is fast and meaningless. A cost probe that ran
    /// early would report an encouraging number and a budget nobody can trust.
    /// </remarks>
    public override void _PhysicsProcess(double delta)
    {
        if (++_settleFrames < 3)
        {
            return;
        }

        SetPhysicsProcess(false);
        Measure();
        EnforceBudget();
    }

    /// <summary>
    /// Measures queries and microseconds per frame at replay depths of 1, 8, and
    /// 32, over both typical and deliberately hostile geometry.
    /// </summary>
    private void Measure()
    {
        if (!VerifyGeometryIsPresent())
        {
            return;
        }

        var budgetMicroseconds = FrameBudgetShare * 1_000_000d / Engine.PhysicsTicksPerSecond;
        _report.Add(
            $"budget: {budgetMicroseconds:0.#} us per frame " +
            $"({FrameBudgetShare:P0} of a {Engine.PhysicsTicksPerSecond} Hz frame)");

        // The deepest depth whose p95 stayed inside budget across every geometry.
        // Reported so the cap is a readable consequence of the measurement rather
        // than a constant someone has to trust.
        var deepestAffordable = 0;

        foreach (var scenario in Scenarios())
        {
            foreach (var depth in ReplayDepths)
            {
                var measurement = MeasureScenario(scenario, depth);
                _perDepthWorstPercentile[depth] = Math.Max(
                    _perDepthWorstPercentile.GetValueOrDefault(depth),
                    measurement.PercentileMicroseconds);

                // A scenario that issued suspiciously few queries measured nothing.
                // The hostile spawn sits close to the recoverable-penetration
                // threshold, and if it ever crossed it the motor would bail out of
                // RecoverPenetration after one query per frame and this gate would
                // go green having timed almost no work.
                var minimumExpected = depth * MinimumQueriesPerSimulatedFrame;
                if (measurement.QueriesPerFrame < minimumExpected)
                {
                    _failures.Add(
                        $"{scenario.Name} at depth {depth} issued only " +
                        $"{measurement.QueriesPerFrame:0.#} queries per frame where at least " +
                        $"{minimumExpected} were expected. The motor is bailing out early, so this " +
                        "timing measures almost no work.");
                }

                var gated = depth <= IntendedReplayDepth;
                _report.Add(
                    $"{scenario.Name} @ depth {depth}{(gated ? "" : " (reported, not gated)")}: " +
                    $"{measurement.QueriesPerFrame:0.#} queries/frame, mean " +
                    $"{measurement.MeanMicroseconds:0.#} us, p95 {measurement.PercentileMicroseconds:0.#} us, " +
                    $"worst {measurement.WorstMicroseconds:0.#} us on frame {measurement.WorstFrame}");

                if (gated && measurement.PercentileMicroseconds > budgetMicroseconds)
                {
                    _failures.Add(
                        $"{scenario.Name} at replay depth {depth} spent " +
                        $"{measurement.PercentileMicroseconds:0.#} us at the " +
                        $"{EnforcedPercentile:P0} percentile, past the {budgetMicroseconds:0.#} us " +
                        "budget. Replay at the depth this project intends to support is not " +
                        "affordable over this geometry.");
                }

                if (measurement.PercentileMicroseconds > budgetMicroseconds)
                {
                    _report.Add(
                        $"  ^ depth {depth} exceeds the budget by " +
                        $"{measurement.PercentileMicroseconds / budgetMicroseconds:0.00}x.");
                }
            }
        }

        // Recomputed across all geometries: a depth counts as affordable only if
        // the hostile geometry can afford it too.
        foreach (var depth in ReplayDepths)
        {
            if (_perDepthWorstPercentile.TryGetValue(depth, out var p95) &&
                p95 <= budgetMicroseconds &&
                depth > deepestAffordable)
            {
                deepestAffordable = depth;
            }
        }

        _report.Add(
            $"deepest affordable measured depth across all geometry: {deepestAffordable} " +
            $"(supported cap in use: {IntendedReplayDepth})");

        // The cap must be justified by the measurement, in both directions. Too
        // high is a frame budget overrun; too low, and Phase 6 quietly gives up
        // replay depth it could have had, which costs correction quality for no
        // reason. Either way the constant and the evidence must agree.
        if (IntendedReplayDepth > deepestAffordable)
        {
            _failures.Add(
                $"the supported replay depth of {IntendedReplayDepth} is not affordable: the deepest " +
                $"measured depth inside budget across all geometry is {deepestAffordable}.");
        }
        else if (deepestAffordable >= IntendedReplayDepth * 2)
        {
            _failures.Add(
                $"the supported replay depth of {IntendedReplayDepth} is more than twice as " +
                $"conservative as the measurement requires — depth {deepestAffordable} is affordable. " +
                "Raise the cap or record why it is being held down, rather than leaving the constant " +
                "and the evidence disagreeing.");
        }
    }

    /// <summary>
    /// Runs one scenario at one replay depth and reports its cost.
    /// </summary>
    /// <remarks>
    /// Replay is modelled the way reconciliation actually performs it: restore a
    /// retained frame's pre-state and resimulate forward to the present. Timing a
    /// single step and multiplying by the depth would miss that a resimulated
    /// frame visits different geometry than the frame that first ran it.
    /// </remarks>
    private CostMeasurement MeasureScenario(Scenario scenario, int replayDepth)
    {
        var simulator = new CharacterMovementSimulator(
            new CapsuleMovementSimulator(_world), new MovementSourceSimulator());
        var history = new List<CharacterSimulationState>(MeasuredFrames + WarmupFrames + 1);
        var state = CharacterSimulationState.CreateGrounded(
            scenario.Spawn,
            0d,
            SimulationInstant.Zero,
            new MovementConfigurationRevision(1),
            new MovementCapabilityRevision(1));
        history.Add(state);

        // Warm up outside the measurement: the first frames pay JIT and
        // first-touch costs that no steady-state frame pays, and letting them into
        // the sample would inflate the budget and hide a real regression later.
        for (var frame = 0; frame < WarmupFrames; frame++)
        {
            state = Step(simulator, state, scenario);
            history.Add(state);
        }

        var stopwatch = new Stopwatch();
        var samples = new List<double>(MeasuredFrames);
        var worstMicroseconds = 0d;
        var worstFrame = -1;
        var totalMicroseconds = 0d;
        var totalQueries = 0L;

        for (var frame = 0; frame < MeasuredFrames; frame++)
        {
            // Restore the pre-state of the frame replay would rewind to, then
            // resimulate forward through the present frame.
            var restoreIndex = Math.Max(0, history.Count - replayDepth);
            var replayed = history[restoreIndex];

            _world.ResetCount();
            stopwatch.Restart();
            for (var step = restoreIndex; step < history.Count; step++)
            {
                replayed = Step(simulator, replayed, scenario);
            }

            stopwatch.Stop();
            var microseconds = stopwatch.Elapsed.TotalMicroseconds;
            samples.Add(microseconds);
            totalMicroseconds += microseconds;
            totalQueries += _world.QueryCount;
            if (microseconds > worstMicroseconds)
            {
                worstMicroseconds = microseconds;
                worstFrame = frame;
            }

            // The replayed result becomes the authoritative present, which is what
            // reconciliation does after a correction.
            state = replayed;
            history.Add(state);
        }

        samples.Sort();
        var percentileIndex = Math.Clamp(
            (int)Math.Ceiling(EnforcedPercentile * samples.Count) - 1, 0, samples.Count - 1);

        return new CostMeasurement(
            (double)totalQueries / MeasuredFrames,
            totalMicroseconds / MeasuredFrames,
            samples[percentileIndex],
            worstMicroseconds,
            worstFrame);
    }

    private CharacterSimulationState Step(
        CharacterMovementSimulator simulator,
        CharacterSimulationState state,
        Scenario scenario)
    {
        var input = new CharacterSimulationInput(
            MovementAxes.FromUnitVector(scenario.Move),
            ViewOrientation.FromRadians(0d, 0d),
            new MovementHeldState(MovementHeldButtons.None),
            default,
            default,
            default,
            new MovementConfigurationRevision(1),
            new MovementCapabilityRevision(1));

        return simulator.Simulate(
            state,
            input,
            [],
            SimulationStepContext.Current(new SimulationInstant(state.Frame.Tick + 1), _rate),
            _attributes,
            _capabilities).State;
    }

    /// <summary>
    /// Confirms the geometry exists, so a cheap measurement cannot be a measure of
    /// nothing.
    /// </summary>
    private bool VerifyGeometryIsPresent()
    {
        var ground = _world.ProbeGround(new GroundProbeRequest(
            new WorldPosition(0d, 1d, 0d), 3d, CollisionProfileKind.Standing));
        if (ground.FoundGround)
        {
            return true;
        }

        _failures.Add(
            "geometry presence: no floor was found, so every timing below would be the cost of " +
            "querying an empty space.");
        return false;
    }

    /// <summary>
    /// Exits zero when every measured frame is inside the authored budget, one
    /// otherwise, naming the depth and geometry that exceeded it.
    /// </summary>
    /// <remarks>
    /// A hard failure rather than a warning: an unaffordable replay is a design
    /// problem, and the cap that bounds it has to be derived from this number.
    /// </remarks>
    private void EnforceBudget()
    {
        foreach (var line in _report)
        {
            GD.Print($"[MotorCostProbe] {line}");
        }

        if (_failures.Count == 0)
        {
            GD.Print(
                $"[MotorCostProbe] PASSED: replay is affordable up to the supported depth of " +
                $"{IntendedReplayDepth}. Deeper replay is measured above and is not affordable, " +
                "which is the point of the cap.");
            GetTree().Quit(0);
            return;
        }

        foreach (var failure in _failures)
        {
            GD.PrintErr($"[MotorCostProbe] FAILED: {failure}");
        }

        GetTree().Quit(1);
    }

    /// <summary>Typical and deliberately hostile geometry.</summary>
    private static IEnumerable<Scenario> Scenarios() =>
    [
        new("open ground", new WorldPosition(0d, 0d, 0d), new HorizontalVector(0d, 1d)),

        // Driving into the inside of a corner while a step is in reach: overlap
        // recovery, several slide iterations, a ground probe, and the step
        // solver's three probes all in the same frame.
        new("corner with step", new WorldPosition(9.2d, 0d, 9.2d), new HorizontalVector(1d, 1d)),
    ];

    private void BuildGeometry()
    {
        AddBox("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(80f, 1f, 80f));

        // A right-angled inside corner with a climbable lip on both faces.
        AddBox("CornerA", new Vector3(10.5f, 2.5f, 5f), new Vector3(1f, 5f, 12f));
        AddBox("CornerB", new Vector3(5f, 2.5f, 10.5f), new Vector3(12f, 5f, 1f));
        AddBox("LipA", new Vector3(9.7f, 0.12f, 5f), new Vector3(0.8f, 0.24f, 12f));
        AddBox("LipB", new Vector3(5f, 0.12f, 9.7f), new Vector3(12f, 0.24f, 0.8f));
    }

    private void AddBox(string name, Vector3 position, Vector3 size)
    {
        var body = new StaticBody3D
        {
            Name = name,
            Position = position,
            CollisionLayer = StaticWorldLayer,
            CollisionMask = 0,
        };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        AddChild(body);
    }

    private readonly record struct Scenario(string Name, WorldPosition Spawn, HorizontalVector Move);

    private readonly record struct CostMeasurement(
        double QueriesPerFrame,
        double MeanMicroseconds,
        double PercentileMicroseconds,
        double WorstMicroseconds,
        int WorstFrame);

    /// <summary>
    /// Counts queries without changing the motor.
    /// </summary>
    /// <remarks>
    /// A decorator rather than a counter inside
    /// <see cref="GodotKinematicCollisionWorld"/>: the production adapter should
    /// not carry instrumentation that only a probe reads, and counting here means
    /// the measured motor is byte-for-byte the shipped one.
    /// </remarks>
    private sealed class CountingCollisionWorld(ICharacterCollisionWorld inner)
        : ICharacterCollisionWorld
    {
        public long QueryCount { get; private set; }

        public void ResetCount() => QueryCount = 0;

        public CapsuleSweepResult Sweep(
            in CapsuleSweepRequest request,
            Span<CollisionContactState> contacts)
        {
            QueryCount++;
            return inner.Sweep(request, contacts);
        }

        public GroundProbeResult ProbeGround(in GroundProbeRequest request)
        {
            QueryCount++;
            return inner.ProbeGround(request);
        }

        public OverlapResolution ResolveOverlap(in ClearanceRequest request)
        {
            QueryCount++;
            return inner.ResolveOverlap(request);
        }

        public bool HasClearance(in ClearanceRequest request)
        {
            QueryCount++;
            return inner.HasClearance(request);
        }

        public bool TryGetSupportMotion(
            SupportIdentity support,
            SimulationInstant fromFrame,
            SimulationInstant toFrame,
            out HorizontalVector horizontal,
            out double vertical) =>
            inner.TryGetSupportMotion(support, fromFrame, toFrame, out horizontal, out vertical);
    }
}
