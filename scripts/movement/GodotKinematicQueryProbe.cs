#nullable enable

using Godot;
using Godot.Collections;

namespace BattleArena.Movement;

/// <summary>
/// Feasibility gate for an explicit, replay-safe capsule query boundary.
/// Queries precreated profile bodies at supplied historical transforms without
/// moving a live CharacterBody3D or the parked query bodies.
/// </summary>
public partial class GodotKinematicQueryProbe : Node3D
{
    private const uint StaticWorldLayer = 1;
    private const uint QueryLayer = 2;
    private const int StabilityIterations = 128;
    private const int PerformanceSampleCount = 256;
    private const int PerformanceWarmupCount = 64;
    private readonly List<QueryProfile> _profiles = [];
    private readonly List<string> _failures = [];
    private readonly List<Rid> _stairRids = [];
    private readonly Array<Rid> _staticExclusions = [];
    private readonly Array<Rid> _unfilteredExclusions = [];
    private readonly PhysicsTestMotionParameters3D _staticParameters =
        CreateParameters();
    private readonly PhysicsTestMotionParameters3D _unfilteredParameters =
        CreateParameters();
    private readonly PhysicsTestMotionResult3D _result = new();
    private CharacterBody3D _liveBody = null!;
    private CharacterBody3D _dynamicDistractor = null!;
    private Rid _groundRid;
    private Rid _wallRid;
    private Rid _ceilingRid;
    private Rid _lowPassageCeilingRid;
    private Rid _rampARid;
    private Rid _rampBRid;
    private int _physicsTicks;
    private bool _finished;

    public override void _Ready()
    {
        BuildGeometry();
        _liveBody = AddCapsuleNode(
            "LiveCharacterMustNotMove",
            new Vector3(0f, 0.05f, 20f),
            0.42f,
            1.8f,
            collisionLayer: 4,
            collisionMask: StaticWorldLayer);
        _dynamicDistractor = AddBoxBody<CharacterBody3D>(
            "ExcludedDynamicBody",
            new Vector3(0f, 0.5f, 16f),
            new Vector3(1f, 1f, 1f),
            collisionLayer: StaticWorldLayer,
            collisionMask: QueryLayer);
        _staticExclusions.Add(_liveBody.GetRid());
        _staticExclusions.Add(_dynamicDistractor.GetRid());
        _staticParameters.ExcludeBodies = _staticExclusions;
        _unfilteredParameters.ExcludeBodies = _unfilteredExclusions;

        _profiles.Add(CreateProfile("standing", radius: 0.42f, height: 1.8f));
        _profiles.Add(CreateProfile("crouched", radius: 0.38f, height: 1.25f));
        _profiles.Add(CreateProfile("rolling", radius: 0.42f, height: 0.95f));
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_finished || ++_physicsTicks < 2)
        {
            return;
        }

        _finished = true;
        try
        {
            RunProbe();
        }
        catch (Exception exception)
        {
            _failures.Add($"Unexpected {exception.GetType().Name}: {exception.Message}");
        }

        if (_failures.Count > 0)
        {
            foreach (var failure in _failures)
            {
                GD.PushError($"[KinematicQueryProbe] {failure}");
            }

            GetTree().Quit(1);
            return;
        }

        GetTree().Quit(0);
    }

    public override void _ExitTree()
    {
        foreach (var profile in _profiles)
        {
            if (profile.BodyRid.IsValid)
            {
                PhysicsServer3D.FreeRid(profile.BodyRid);
            }

            profile.Shape.Dispose();
        }

        _profiles.Clear();
        GD.Print("[KinematicQueryProbe] CLEANUP COMPLETE: query bodies and shapes released.");
    }

    private void RunProbe()
    {
        var standing = _profiles[0];
        var crouched = _profiles[1];
        var rolling = _profiles[2];
        Require(
            standing.BodyRid != crouched.BodyRid &&
            standing.BodyRid != rolling.BodyRid &&
            crouched.BodyRid != rolling.BodyRid,
            "standing, crouched, and rolling profiles did not receive distinct RIDs");

        var liveBefore = _liveBody.GlobalTransform;
        var parkedBefore = _profiles.ToDictionary(
            profile => profile.BodyRid,
            profile => ReadBodyTransform(profile.BodyRid));

        VerifyGroundWallAndCeiling(standing);
        VerifyStairs(standing);
        VerifyRampSeam(standing);
        VerifyProfileSwap(standing, crouched, rolling);
        VerifyDynamicExclusion(standing);
        VerifyReusableBuffers(standing);
        var performance = VerifyRepeatedQueries();

        Require(
            TransformApproximatelyEqual(liveBefore, _liveBody.GlobalTransform),
            "historical queries moved the live CharacterBody3D");
        foreach (var profile in _profiles)
        {
            Require(
                TransformApproximatelyEqual(
                    parkedBefore[profile.BodyRid],
                    ReadBodyTransform(profile.BodyRid)),
                $"{profile.Name} query RID moved from its parked transform");
        }

        Require(
            performance.ElapsedMicroseconds <= 30_000_000UL,
            $"{performance.QueryCount} repeated queries took " +
            $"{performance.ElapsedMicroseconds / 1_000d:0.0} ms " +
            "(30,000 ms safety ceiling)");

        if (_failures.Count == 0)
        {
            GD.Print(
                "[KinematicQueryProbe] PERF_META " +
                $"cpu=\"{OS.GetProcessorName()}\" " +
                $"os=\"{OS.GetName()} {OS.GetVersion()}\" " +
                "configuration=\"Debug C# / headless custom editor\" " +
                $"warmups={PerformanceWarmupCount} samples={PerformanceSampleCount} " +
                "run_kind=\"raw_single_run\" aggregation=\"none\"");
            foreach (var scenario in performance.Scenarios)
            {
                GD.Print(
                    "[KinematicQueryProbe] PERF " +
                    $"total={scenario.TotalCombatants} " +
                    $"replayed={scenario.ReplayedCombatants} " +
                    $"depth={scenario.ReplayDepth} " +
                    $"sweeps={scenario.SweepsPerStep} " +
                    $"samples={scenario.SampleCount} " +
                    $"queries_per_sample={scenario.QueriesPerSample} " +
                    $"p50_us={scenario.P50Microseconds} " +
                    $"p95_us={scenario.P95Microseconds} " +
                    $"p99_us={scenario.P99Microseconds} " +
                    $"max_us={scenario.MaximumMicroseconds} " +
                    $"managed_allocated_bytes={scenario.ManagedAllocatedBytes}");
            }

            GD.Print(
                "[KinematicQueryProbe] PASS: explicit historical capsule sweeps covered " +
                "ground, walls, ceilings, stairs, ramp seams, dynamic exclusions, and " +
                $"three profile RIDs; {performance.QueryCount} repeated queries took " +
                $"{performance.ElapsedMicroseconds / 1_000d:0.0} ms " +
                "of summed batch time across parseable 1/3/8-combatant " +
                "causal-island/all-player distributions.");
        }
    }

    private void VerifyGroundWallAndCeiling(QueryProfile standing)
    {
        var ground = Query(
            standing,
            new Vector3(0f, 1f, 0f),
            Vector3.Down * 2f);
        Require(ground.Collided, "downward ground sweep did not collide");
        Require(ground.ColliderRid == _groundRid, "ground sweep hit the wrong collider");
        Require(ground.Normal.Y > 0.9f, $"ground normal was {ground.Normal}");

        var wall = Query(
            standing,
            new Vector3(0f, 0.05f, 0f),
            Vector3.Right * 4f);
        Require(wall.Collided, "historical wall sweep did not collide");
        Require(wall.ColliderRid == _wallRid, "wall sweep hit the wrong collider");
        Require(wall.Normal.X < -0.8f, $"wall normal was {wall.Normal}");
        Require(wall.Travel.X < 2f, $"wall sweep traveled through geometry: {wall.Travel}");

        var ceiling = Query(
            standing,
            new Vector3(0f, 0.05f, 8f),
            Vector3.Up * 3f);
        Require(ceiling.Collided, "upward ceiling sweep did not collide");
        Require(ceiling.ColliderRid == _ceilingRid, "ceiling sweep hit the wrong collider");
        Require(ceiling.Normal.Y < -0.8f, $"ceiling normal was {ceiling.Normal}");
    }

    private void VerifyStairs(QueryProfile standing)
    {
        var riser = Query(
            standing,
            new Vector3(-12f, 0.05f, -8f),
            Vector3.Right * 4f);
        Require(riser.Collided, "stair-riser sweep did not collide");
        Require(riser.ColliderRid == _stairRids[0], "stair-riser hit the wrong collider");
        Require(riser.Normal.X < -0.7f, $"stair-riser normal was {riser.Normal}");

        var tread = Query(
            standing,
            new Vector3(-8f, 2f, -8f),
            Vector3.Down * 3f);
        Require(tread.Collided, "stair-tread downward sweep did not collide");
        Require(tread.ColliderRid == _stairRids[2], "stair-tread hit the wrong collider");
        Require(tread.Normal.Y > 0.9f, $"stair-tread normal was {tread.Normal}");
        Require(
            2f + tread.Travel.Y is >= 0.55f and <= 0.65f,
            $"stair-tread landing height was {2f + tread.Travel.Y:0.000} m");
    }

    private void VerifyRampSeam(QueryProfile standing)
    {
        foreach (var x in new[] { 3.99f, 4f, 4.01f })
        {
            var seam = Query(
                standing,
                new Vector3(x, 4f, -16f),
                Vector3.Down * 5f);
            Require(seam.Collided, $"ramp seam query at x={x:0.00} missed");
            Require(
                seam.ColliderRid == _rampARid || seam.ColliderRid == _rampBRid,
                $"ramp seam query at x={x:0.00} hit a non-ramp collider");
            Require(
                seam.Normal.Y > 0.7f,
                $"ramp seam query at x={x:0.00} returned {seam.Normal}");
            Require(
                4f + seam.Travel.Y is >= 1.85f and <= 2.15f,
                $"ramp seam landing at x={x:0.00} was " +
                $"{4f + seam.Travel.Y:0.000} m");
        }
    }

    private void VerifyProfileSwap(
        QueryProfile standing,
        QueryProfile crouched,
        QueryProfile rolling)
    {
        var from = new Vector3(6f, 0.05f, 8f);
        var motion = Vector3.Right * 8f;
        var standingResult = Query(standing, from, motion);
        var crouchedResult = Query(crouched, from, motion);
        var rollingResult = Query(rolling, from, motion);

        Require(standingResult.Collided, "standing profile passed through low clearance");
        Require(
            standingResult.ColliderRid == _lowPassageCeilingRid,
            "standing low-clearance query hit the wrong collider");
        Require(
            !crouchedResult.Collided || crouchedResult.Travel.X > 7.9f,
            $"crouched profile failed clear low passage: {crouchedResult.Travel}");
        Require(
            !rollingResult.Collided || rollingResult.Travel.X > 7.9f,
            $"rolling profile failed clear low passage: {rollingResult.Travel}");

        foreach (var profile in _profiles)
        {
            var ground = Query(profile, new Vector3(0f, 1f, 0f), Vector3.Down * 2f);
            var wall = Query(profile, new Vector3(0f, 0.05f, 0f), Vector3.Right * 4f);
            var ceiling = Query(profile, new Vector3(0f, 0.05f, 8f), Vector3.Up * 3f);
            Require(
                ground.ColliderRid == _groundRid && ground.Normal.Y > 0.9f,
                $"{profile.Name} profile did not agree on ground geometry");
            Require(
                wall.ColliderRid == _wallRid && wall.Normal.X < -0.8f,
                $"{profile.Name} profile did not agree on wall geometry");
            Require(
                ceiling.ColliderRid == _ceilingRid && ceiling.Normal.Y < -0.8f,
                $"{profile.Name} profile did not agree on ceiling geometry");
        }
    }

    private void VerifyDynamicExclusion(QueryProfile standing)
    {
        var from = new Vector3(-2f, 0.05f, 16f);
        var motion = Vector3.Right * 4f;
        var included = Query(
            standing,
            from,
            motion,
            applyStaticExclusions: false);
        var excluded = Query(standing, from, motion);

        Require(included.Collided, "dynamic-body control query did not collide");
        Require(
            included.ColliderRid == _dynamicDistractor.GetRid(),
            "dynamic-body control query hit an unexpected collider");
        Require(
            !excluded.Collided || excluded.Travel.X > 3.9f,
            $"explicit dynamic RID exclusion still blocked travel: {excluded.Travel}");

        var offMask = Query(
            standing,
            new Vector3(-2f, 0.05f, 20f),
            Vector3.Right * 4f,
            applyStaticExclusions: false);
        Require(
            !offMask.Collided || offMask.Travel.X > 3.9f,
            $"layer-zero static query saw the off-mask live player: {offMask.Travel}");
    }

    private void VerifyReusableBuffers(QueryProfile standing)
    {
        var hit = Query(
            standing,
            new Vector3(0f, 0.05f, 0f),
            Vector3.Right * 4f);
        var missMotion = Vector3.Right;
        var miss = Query(
            standing,
            new Vector3(-30f, 5f, -20f),
            missMotion);
        var hitAgain = Query(
            standing,
            new Vector3(0f, 0.05f, 0f),
            Vector3.Right * 4f);

        Require(hit.Collided, "reusable-result hit control missed");
        Require(!miss.Collided, "reusable result retained a stale collision boolean");
        Require(miss.CollisionCount == 0, "reusable result retained stale collision count");
        Require(!miss.ColliderRid.IsValid, "reusable result retained stale collider RID");
        Require(
            miss.Travel.IsEqualApprox(missMotion),
            $"reusable miss travel was stale: {miss.Travel}");
        RequireEquivalent(hit, hitAgain, "hit/miss/hit reusable request-result sequence");
    }

    private PerformanceResult VerifyRepeatedQueries()
    {
        var traces = CanonicalTraces();
        var baselines = new QuerySnapshot[_profiles.Count, traces.Length];
        for (var profileIndex = 0; profileIndex < _profiles.Count; profileIndex++)
        {
            for (var traceIndex = 0; traceIndex < traces.Length; traceIndex++)
            {
                var baseline = Query(
                    _profiles[profileIndex],
                    traces[traceIndex].From,
                    traces[traceIndex].Motion);
                baselines[profileIndex, traceIndex] = baseline;
                Require(
                    baseline.Collided &&
                    traces[traceIndex].ExpectedColliderRids.Contains(
                        baseline.ColliderRid),
                    $"{_profiles[profileIndex].Name} canonical " +
                    $"{traces[traceIndex].Name} hit the wrong collider");
            }
        }

        for (var iteration = 0; iteration < StabilityIterations; iteration++)
        {
            for (var profileIndex = 0; profileIndex < _profiles.Count; profileIndex++)
            {
                for (var traceIndex = 0; traceIndex < traces.Length; traceIndex++)
                {
                    var actual = Query(
                        _profiles[profileIndex],
                        traces[traceIndex].From,
                        traces[traceIndex].Motion);
                    if (!SnapshotsEquivalent(
                            baselines[profileIndex, traceIndex],
                            actual))
                    {
                        Require(
                            false,
                            $"{_profiles[profileIndex].Name} " +
                            $"{traces[traceIndex].Name} stability iteration " +
                            $"{iteration} diverged");
                    }

                }
            }
        }

        var scenarios = new List<ReplayScenarioWork>();
        foreach (var playerCount in new[] { 1, 3, 8 })
        {
            scenarios.Add(new ReplayScenarioWork(
                playerCount,
                Math.Min(playerCount, 3),
                replayDepth: 8,
                sweepsPerStep: 4,
                PerformanceSampleCount));
            scenarios.Add(new ReplayScenarioWork(
                playerCount,
                playerCount,
                replayDepth: 32,
                sweepsPerStep: 8,
                PerformanceSampleCount));
        }

        foreach (var scenario in scenarios)
        {
            for (var warmup = 0; warmup < PerformanceWarmupCount; warmup++)
            {
                var warmupQueries = 0;
                RunReplayBatch(
                    scenario.ReplayedCombatants,
                    scenario.ReplayDepth,
                    scenario.SweepsPerStep,
                    traces,
                    baselines,
                    ref warmupQueries);
                Require(
                    warmupQueries == scenario.QueriesPerSample,
                    "replay batch query accounting changed during warm-up");
            }
        }

        // Prime runtime bookkeeping so first-use allocation from the allocation
        // counter itself is not charged to the first replay scenario.
        _ = GC.GetAllocatedBytesForCurrentThread();
        _ = Time.GetTicksUsec();
        var primingQueries = 0;
        var primingStarted = Time.GetTicksUsec();
        RunReplayBatch(
            scenarios[0].ReplayedCombatants,
            scenarios[0].ReplayDepth,
            scenarios[0].SweepsPerStep,
            traces,
            baselines,
            ref primingQueries);
        _ = Time.GetTicksUsec() - primingStarted;
        Require(
            primingQueries == scenarios[0].QueriesPerSample,
            "replay batch query accounting changed during timing priming");
        var queryCount = 0;
        ulong elapsedMicroseconds = 0;
        foreach (var scenario in scenarios)
        {
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (var sample = 0; sample < scenario.ElapsedSamples.Length; sample++)
            {
                var sampleQueries = 0;
                var sampleStarted = Time.GetTicksUsec();
                RunReplayBatch(
                    scenario.ReplayedCombatants,
                    scenario.ReplayDepth,
                    scenario.SweepsPerStep,
                    traces,
                    baselines,
                    ref sampleQueries);
                var elapsed = Time.GetTicksUsec() - sampleStarted;
                scenario.ElapsedSamples[sample] = elapsed;
                elapsedMicroseconds += elapsed;
                Require(
                    sampleQueries == scenario.QueriesPerSample,
                    "replay batch query accounting changed during sampling");
                queryCount += sampleQueries;
            }

            scenario.ManagedAllocatedBytes =
                GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        }

        var results = new List<ReplayScenarioResult>(scenarios.Count);
        foreach (var scenario in scenarios)
        {
            System.Array.Sort(scenario.ElapsedSamples);
            results.Add(new ReplayScenarioResult(
                scenario.TotalCombatants,
                scenario.ReplayedCombatants,
                scenario.ReplayDepth,
                scenario.SweepsPerStep,
                scenario.ElapsedSamples.Length,
                scenario.QueriesPerSample,
                Percentile(scenario.ElapsedSamples, 0.50),
                Percentile(scenario.ElapsedSamples, 0.95),
                Percentile(scenario.ElapsedSamples, 0.99),
                scenario.ElapsedSamples[^1],
                scenario.ManagedAllocatedBytes));
        }

        Require(
            queryCount == results.Sum(result => result.SampleCount * result.QueriesPerSample),
            "published query count does not equal the sum of timed scenario queries");
        return new PerformanceResult(queryCount, elapsedMicroseconds, results);
    }

    private static ulong Percentile(ulong[] sorted, double percentile)
    {
        var index = Math.Clamp(
            (int)Math.Ceiling(sorted.Length * percentile) - 1,
            0,
            sorted.Length - 1);
        return sorted[index];
    }

    private CanonicalTrace[] CanonicalTraces() =>
    [
        new(
            "ground snap",
            new Vector3(0f, 0.5f, -4f),
            Vector3.Down,
            [_groundRid]),
        new(
            "wall",
            new Vector3(0f, 0.05f, 0f),
            Vector3.Right * 4f,
            [_wallRid]),
        new(
            "ceiling",
            new Vector3(0f, 0.05f, 8f),
            Vector3.Up * 3f,
            [_ceilingRid]),
        new(
            "stair riser",
            new Vector3(-12f, 0.05f, -8f),
            Vector3.Right * 4f,
            [_stairRids[0]]),
        new(
            "stair landing",
            new Vector3(-8f, 2f, -8f),
            Vector3.Down * 3f,
            [_stairRids[2]]),
        new(
            "ramp",
            new Vector3(2f, 4f, -16f),
            Vector3.Down * 5f,
            [_rampARid]),
        new(
            "ramp seam",
            new Vector3(4f, 4f, -16f),
            Vector3.Down * 5f,
            [_rampARid, _rampBRid]),
        new(
            "fall landing",
            new Vector3(20f, 5f, -4f),
            Vector3.Down * 6f,
            [_groundRid]),
    ];

    private void RunReplayBatch(
        int playerCount,
        int replayDepth,
        int sweepsPerStep,
        CanonicalTrace[] traces,
        QuerySnapshot[,] baselines,
        ref int queryCount)
    {
        for (var frame = 0; frame < replayDepth; frame++)
        {
            for (var player = 0; player < playerCount; player++)
            {
                var profileIndex = (frame + player) % _profiles.Count;
                for (var sweep = 0; sweep < sweepsPerStep; sweep++)
                {
                    var traceIndex = (frame + player + sweep) % traces.Length;
                    var trace = traces[traceIndex];
                    var actual = Query(
                        _profiles[profileIndex],
                        trace.From,
                        trace.Motion);
                    if (!SnapshotsEquivalent(
                            baselines[profileIndex, traceIndex],
                            actual))
                    {
                        Require(
                            false,
                            $"{playerCount}-player depth-{replayDepth} " +
                            $"{trace.Name} frame {frame} player {player} diverged");
                    }

                    queryCount++;
                }
            }
        }
    }

    private QuerySnapshot Query(
        QueryProfile profile,
        Vector3 footPosition,
        Vector3 motion,
        bool applyStaticExclusions = true)
    {
        var parameters = applyStaticExclusions
            ? _staticParameters
            : _unfilteredParameters;
        parameters.From = new Transform3D(Basis.Identity, footPosition);
        parameters.Motion = motion;
        var collided = PhysicsServer3D.BodyTestMotion(
            profile.BodyRid,
            parameters,
            _result);
        return collided
            ? new QuerySnapshot(
                true,
                _result.GetTravel(),
                _result.GetRemainder(),
                _result.GetCollisionNormal(),
                _result.GetColliderRid(),
                _result.GetColliderShape(),
                _result.GetCollisionLocalShape(),
                _result.GetCollisionCount())
            : new QuerySnapshot(
                false,
                _result.GetTravel(),
                _result.GetRemainder(),
                Vector3.Zero,
                default,
                -1,
                -1,
                _result.GetCollisionCount());
    }

    private QueryProfile CreateProfile(string name, float radius, float height)
    {
        var shape = new CapsuleShape3D { Radius = radius, Height = height };
        var bodyRid = PhysicsServer3D.BodyCreate();
        PhysicsServer3D.BodySetMode(bodyRid, PhysicsServer3D.BodyMode.Kinematic);
        PhysicsServer3D.BodySetCollisionLayer(bodyRid, 0);
        PhysicsServer3D.BodySetCollisionMask(bodyRid, StaticWorldLayer);
        PhysicsServer3D.BodyAddShape(
            bodyRid,
            shape.GetRid(),
            new Transform3D(
                Basis.Identity,
                new Vector3(0f, height * 0.5f, 0f)));
        PhysicsServer3D.BodySetSpace(bodyRid, GetWorld3D().Space);
        PhysicsServer3D.BodySetState(
            bodyRid,
            PhysicsServer3D.BodyState.Transform,
            new Transform3D(
                Basis.Identity,
                new Vector3(1_000f + _profiles.Count * 10f, 1_000f, 1_000f)));
        return new QueryProfile(name, bodyRid, shape);
    }

    private void BuildGeometry()
    {
        _groundRid = AddBoxBody<StaticBody3D>(
            "Ground",
            new Vector3(0f, -0.5f, 0f),
            new Vector3(80f, 1f, 50f)).GetRid();
        _wallRid = AddBoxBody<StaticBody3D>(
            "Wall",
            new Vector3(2f, 2f, 0f),
            new Vector3(0.2f, 4f, 4f)).GetRid();
        _ceilingRid = AddBoxBody<StaticBody3D>(
            "Ceiling",
            new Vector3(0f, 2f, 8f),
            new Vector3(4f, 0.2f, 4f)).GetRid();
        _lowPassageCeilingRid = AddBoxBody<StaticBody3D>(
            "LowPassageCeiling",
            new Vector3(10f, 1.6f, 8f),
            new Vector3(4f, 0.2f, 4f)).GetRid();

        for (var index = 0; index < 3; index++)
        {
            var height = 0.2f * (index + 1);
            _stairRids.Add(AddBoxBody<StaticBody3D>(
                $"Stair{index}",
                new Vector3(-10f + index, height * 0.5f, -8f),
                new Vector3(1f, height, 3f)).GetRid());
        }

        _rampARid = AddRamp("RampA", startX: 0f, baseY: 0f, z: -16f).GetRid();
        _rampBRid = AddRamp("RampB", startX: 4f, baseY: 2f, z: -16f).GetRid();
    }

    private StaticBody3D AddRamp(string name, float startX, float baseY, float z)
    {
        const float length = 4f;
        const float width = 4f;
        const float rise = 2f;
        var halfWidth = width * 0.5f;
        var points = new[]
        {
            new Vector3(0f, 0f, -halfWidth),
            new Vector3(0f, 0f, halfWidth),
            new Vector3(length, 0f, -halfWidth),
            new Vector3(length, 0f, halfWidth),
            new Vector3(length, rise, -halfWidth),
            new Vector3(length, rise, halfWidth),
        };
        var body = new StaticBody3D
        {
            Name = name,
            Position = new Vector3(startX, baseY, z),
            CollisionLayer = StaticWorldLayer,
            CollisionMask = QueryLayer,
        };
        body.AddChild(new CollisionShape3D
        {
            Shape = new ConvexPolygonShape3D { Points = points },
        });
        AddChild(body);
        return body;
    }

    private T AddBoxBody<T>(
        string name,
        Vector3 position,
        Vector3 size,
        uint collisionLayer = StaticWorldLayer,
        uint collisionMask = QueryLayer)
        where T : PhysicsBody3D, new()
    {
        var body = new T
        {
            Name = name,
            Position = position,
            CollisionLayer = collisionLayer,
            CollisionMask = collisionMask,
        };
        body.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = size },
        });
        AddChild(body);
        return body;
    }

    private CharacterBody3D AddCapsuleNode(
        string name,
        Vector3 position,
        float radius,
        float height,
        uint collisionLayer,
        uint collisionMask)
    {
        var body = new CharacterBody3D
        {
            Name = name,
            Position = position,
            CollisionLayer = collisionLayer,
            CollisionMask = collisionMask,
        };
        body.AddChild(new CollisionShape3D
        {
            Position = new Vector3(0f, height * 0.5f, 0f),
            Shape = new CapsuleShape3D { Radius = radius, Height = height },
        });
        AddChild(body);
        return body;
    }

    private void RequireEquivalent(
        QuerySnapshot expected,
        QuerySnapshot actual,
        string context)
    {
        if (!SnapshotsEquivalent(expected, actual))
        {
            _failures.Add($"{context} diverged: expected={expected}, actual={actual}");
        }
    }

    private static bool SnapshotsEquivalent(
        QuerySnapshot expected,
        QuerySnapshot actual) =>
        expected.Collided == actual.Collided &&
        expected.ColliderRid == actual.ColliderRid &&
        expected.ColliderShape == actual.ColliderShape &&
        expected.LocalShape == actual.LocalShape &&
        expected.CollisionCount == actual.CollisionCount &&
        expected.Travel.IsEqualApprox(actual.Travel) &&
        expected.Remainder.IsEqualApprox(actual.Remainder) &&
        expected.Normal.IsEqualApprox(actual.Normal);

    private void Require(bool condition, string message)
    {
        if (!condition && _failures.Count < 100)
        {
            _failures.Add(message);
        }
    }

    private static Transform3D ReadBodyTransform(Rid bodyRid) =>
        PhysicsServer3D.BodyGetState(
            bodyRid,
            PhysicsServer3D.BodyState.Transform).AsTransform3D();

    private static bool TransformApproximatelyEqual(Transform3D left, Transform3D right) =>
        left.Origin.IsEqualApprox(right.Origin) && left.Basis.IsEqualApprox(right.Basis);

    private static PhysicsTestMotionParameters3D CreateParameters() => new()
    {
        Margin = 0.001f,
        MaxCollisions = 8,
        RecoveryAsCollision = false,
        CollideSeparationRay = false,
    };

    private sealed record QueryProfile(
        string Name,
        Rid BodyRid,
        CapsuleShape3D Shape);

    private sealed record CanonicalTrace(
        string Name,
        Vector3 From,
        Vector3 Motion,
        Rid[] ExpectedColliderRids);

    private readonly record struct PerformanceResult(
        int QueryCount,
        ulong ElapsedMicroseconds,
        IReadOnlyList<ReplayScenarioResult> Scenarios);

    private sealed class ReplayScenarioWork
    {
        public ReplayScenarioWork(
            int totalCombatants,
            int replayedCombatants,
            int replayDepth,
            int sweepsPerStep,
            int sampleCount)
        {
            TotalCombatants = totalCombatants;
            ReplayedCombatants = replayedCombatants;
            ReplayDepth = replayDepth;
            SweepsPerStep = sweepsPerStep;
            ElapsedSamples = new ulong[sampleCount];
        }

        public int TotalCombatants { get; }
        public int ReplayedCombatants { get; }
        public int ReplayDepth { get; }
        public int SweepsPerStep { get; }
        public int QueriesPerSample => ReplayedCombatants * ReplayDepth * SweepsPerStep;
        public ulong[] ElapsedSamples { get; }
        public long ManagedAllocatedBytes { get; set; }
    }

    private sealed record ReplayScenarioResult(
        int TotalCombatants,
        int ReplayedCombatants,
        int ReplayDepth,
        int SweepsPerStep,
        int SampleCount,
        int QueriesPerSample,
        ulong P50Microseconds,
        ulong P95Microseconds,
        ulong P99Microseconds,
        ulong MaximumMicroseconds,
        long ManagedAllocatedBytes);

    private readonly record struct QuerySnapshot(
        bool Collided,
        Vector3 Travel,
        Vector3 Remainder,
        Vector3 Normal,
        Rid ColliderRid,
        int ColliderShape,
        int LocalShape,
        int CollisionCount);
}
