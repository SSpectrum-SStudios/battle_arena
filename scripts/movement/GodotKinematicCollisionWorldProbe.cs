using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Core.Movement.Simulation;
using Godot;

namespace BattleArena.Movement;

/// <summary>
/// Headless verification that <see cref="GodotKinematicCollisionWorld"/> behaves
/// against the real engine the way the motor assumes it does.
/// </summary>
/// <remarks>
/// <para>
/// P01-09 proved the query <em>approach</em> — precreated kinematic body RIDs,
/// reusable parameters, explicit-transform <c>BodyTestMotion</c> — but it
/// exercised a purpose-built probe class, not the adapter the motor actually
/// calls. Everything the explicit motor does in-engine is otherwise unverified.
/// </para>
/// <para>
/// The specific risk this probe exists for: the adapter's
/// <see cref="GodotKinematicCollisionWorld.ResolveOverlap"/> sets
/// <c>RecoveryAsCollision = true</c>, which the P01-09 probe deliberately kept
/// disabled throughout, and it runs before <em>every</em> frame's motion. If a
/// capsule resting normally on the floor reports margin-scale overlap, recovery
/// pushes it up and ground snap pulls it back down — a per-frame oscillation
/// that the deterministic world cannot reproduce, because its geometry is exact
/// and its overlap test is strict.
/// </para>
/// </remarks>
public sealed partial class GodotKinematicCollisionWorldProbe : Node3D
{
    private const uint StaticWorldLayer = 1 << 0;
    private const uint DynamicLayer = 1 << 3;
    private const int TicksPerSecond = 60;

    private static readonly CollisionProfileTable Profiles = new(
        new CollisionProfileDimensions(0.4d, 1.8d),
        new CollisionProfileDimensions(0.4d, 1.2d),
        new CollisionProfileDimensions(0.4d, 0.9d));

    private static readonly CapsuleMotorPolicy Policy = CapsuleMotorPolicy.TestDefault;

    private readonly List<string> _failures = [];
    private GodotKinematicCollisionWorld _world = null!;
    private int _settleFrames;

    public override void _Ready()
    {
        BuildGeometry();
        _world = new GodotKinematicCollisionWorld();
        AddChild(_world);
        _world.Initialize(Profiles, StaticWorldLayer);
    }

    /// <summary>
    /// Waits for the physics server to actually contain the static bodies before
    /// querying.
    /// </summary>
    /// <remarks>
    /// Querying during <c>_Ready</c> sees an empty space and every case would
    /// pass vacuously by finding nothing — the most dangerous way for a probe to
    /// be green.
    /// </remarks>
    public override void _PhysicsProcess(double delta)
    {
        if (++_settleFrames < 3)
        {
            return;
        }

        SetPhysicsProcess(false);
        RunProbe();
    }

    /// <summary>
    /// Runs every case and exits with the probe contract every runner keys on:
    /// zero on success, one on failure, with each failing case named.
    /// </summary>
    private void RunProbe()
    {
        VerifyGeometryIsActuallyPresent();
        VerifyFootOriginOffset();
        VerifyRestingCapsuleIsStable();
        VerifyNoLiveNodeMovement();
        VerifyStaticOnlyMasking();
        VerifyStableColliderIdentity();
        VerifyAgreesWithDeterministicWorld();
        ReportQuerySettings();

        if (_failures.Count == 0)
        {
            GD.Print("[CollisionWorldProbe] PASSED: adapter matches motor expectations in-engine.");
            GetTree().Quit(0);
            return;
        }

        foreach (var failure in _failures)
        {
            GD.PrintErr($"[CollisionWorldProbe] FAILED: {failure}");
        }

        GetTree().Quit(1);
    }

    /// <summary>
    /// Proves the world is not empty before anything else asserts against it.
    /// </summary>
    /// <remarks>
    /// Without this, every "found nothing" result reads as a pass. A probe that
    /// is green because it queried an empty space is worse than no probe.
    /// </remarks>
    private void VerifyGeometryIsActuallyPresent()
    {
        var probe = _world.ProbeGround(new GroundProbeRequest(
            new WorldPosition(0d, 1d, 0d), 3d, CollisionProfileKind.Standing));
        if (!probe.FoundGround)
        {
            _failures.Add(
                "geometry presence: no floor found beneath the origin, so every other case " +
                "would pass vacuously against an empty space.");
        }
    }

    /// <summary>
    /// The capsule sits on its feet, not through them.
    /// </summary>
    /// <remarks>
    /// Simulation treats a position as the foot; the engine centres a capsule
    /// shape on its origin. The adapter reconciles the two with a half-height
    /// offset, and getting it wrong sinks the character into the floor by half
    /// its height — obvious in play, invisible in a unit test against a fake.
    /// </remarks>
    private void VerifyFootOriginOffset()
    {
        var probe = _world.ProbeGround(new GroundProbeRequest(
            new WorldPosition(0d, 0d, 0d), 0.5d, CollisionProfileKind.Standing));

        if (!probe.FoundGround)
        {
            _failures.Add("foot origin: a capsule at floor level found no ground beneath it.");
            return;
        }
        if (probe.Distance > 0.05d)
        {
            _failures.Add(
                $"foot origin: ground is {probe.Distance:0.###} m below a capsule that should be " +
                "resting on it, which is a capsule-origin offset error.");
        }
    }

    /// <summary>
    /// A resting capsule does not oscillate between recovery and ground snap.
    /// </summary>
    /// <remarks>
    /// The headline case. The engine's default query margin is large enough that
    /// a capsule which is simply standing there can report overlap; recovering it
    /// every frame would fight ground snap and jitter the character.
    /// </remarks>
    private void VerifyRestingCapsuleIsStable()
    {
        var motor = new CapsuleMovementSimulator(_world);
        var state = CharacterKinematicState.AtRest(new WorldPosition(0d, 0d, 0d), 0d);
        var startY = state.Position.Y;
        var lowest = startY;
        var highest = startY;

        for (var frame = 0; frame < 240; frame++)
        {
            state = motor.Move(
                state,
                CollisionProfileState.Standing,
                HorizontalVector.Zero,
                -9.81d / TicksPerSecond,
                new SimulationInstant(frame),
                new SimulationInstant(frame + 1),
                Profiles,
                Policy).State;
            lowest = Math.Min(lowest, state.Position.Y);
            highest = Math.Max(highest, state.Position.Y);
        }

        var span = highest - lowest;
        if (span > 0.02d)
        {
            _failures.Add(
                $"resting stability: a stationary capsule moved through {span:0.####} m over 240 " +
                "frames, which is penetration recovery fighting ground snap.");
        }
        if (!state.IsGrounded)
        {
            _failures.Add("resting stability: a stationary capsule stopped being grounded.");
        }
    }

    /// <summary>
    /// Queries never move a live node.
    /// </summary>
    /// <remarks>
    /// This is the property that makes replay invisible, and the one an
    /// innocent-looking refactor is most likely to break.
    /// </remarks>
    private void VerifyNoLiveNodeMovement()
    {
        var before = new Dictionary<string, Transform3D>();
        foreach (var child in GetChildren())
        {
            if (child is Node3D node)
            {
                before[node.Name] = node.GlobalTransform;
            }
        }

        Span<CollisionContactState> contacts = stackalloc CollisionContactState[8];
        for (var i = 0; i < 300; i++)
        {
            _world.Sweep(
                new CapsuleSweepRequest(
                    new WorldPosition(-5d + (i * 0.03d), 0.5d, 0d),
                    new HorizontalVector(0.2d, 0.1d),
                    -0.05d,
                    CollisionProfileKind.Standing,
                    SupportIdentity.None,
                    Policy.WalkableSlopeRadians),
                contacts);
        }

        foreach (var child in GetChildren())
        {
            if (child is not Node3D node || !before.TryGetValue(node.Name, out var original))
            {
                continue;
            }
            if (!original.IsEqualApprox(node.GlobalTransform))
            {
                _failures.Add($"live node movement: '{node.Name}' moved during queries.");
            }
        }
    }

    /// <summary>
    /// Dynamic bodies and other characters are never reported.
    /// </summary>
    /// <remarks>
    /// Player-versus-player collision is frame-aligned and belongs to Phase 9. If
    /// it leaked in here, one character's replay would depend on another
    /// character's current position, which is not replayable.
    /// </remarks>
    private void VerifyStaticOnlyMasking()
    {
        Span<CollisionContactState> contacts = stackalloc CollisionContactState[8];
        var result = _world.Sweep(
            new CapsuleSweepRequest(
                new WorldPosition(0d, 0.2d, 17d),
                new HorizontalVector(0d, 4d),
                0d,
                CollisionProfileKind.Standing,
                SupportIdentity.None,
                Policy.WalkableSlopeRadians),
            contacts);

        for (var index = 0; index < result.ContactCount; index++)
        {
            if (contacts[index].Collider.ColliderId == _dynamicColliderId)
            {
                _failures.Add(
                    "static-only masking: the dynamic body was reported, so one character's " +
                    "replay could depend on another's current position.");
                return;
            }
        }
    }

    /// <summary>
    /// Collider identity is stable across repeated queries of the same surface.
    /// </summary>
    /// <remarks>
    /// Support recognition and contact ordering both key on it. An identity that
    /// changed between queries would make a character appear to re-land every
    /// frame and would make slide ordering non-deterministic.
    /// </remarks>
    private void VerifyStableColliderIdentity()
    {
        SupportIdentity? first = null;
        for (var i = 0; i < 50; i++)
        {
            var probe = _world.ProbeGround(new GroundProbeRequest(
                new WorldPosition(0d, 0.1d, 0d), 0.5d, CollisionProfileKind.Standing));
            if (!probe.FoundGround)
            {
                _failures.Add("collider identity: the floor stopped being found mid-run.");
                return;
            }

            first ??= probe.Support;
            if (probe.Support != first.Value)
            {
                _failures.Add(
                    $"collider identity: the same floor reported {probe.Support.ColliderId} " +
                    $"after {first.Value.ColliderId}, so support cannot be recognised across frames.");
                return;
            }
        }

        // Stability within one process is what a raw physics-server RID already
        // gave us, and it is why this defect survived four phases: the identity was
        // stable for the wrong reason. What must hold is that the value is derived
        // from authored content, so a second process derives the same one. Checked
        // by deriving it independently from the node's own path.
        var ground = GetNodeOrNull<StaticBody3D>("Ground");
        if (ground is null)
        {
            _failures.Add("collider identity: the ground body is missing, so nothing was verified.");
            return;
        }

        var expected = SceneColliderIdentity.FromBodyPath(ground.GetPath().ToString(), 0);
        if (first!.Value != expected)
        {
            _failures.Add(
                $"collider identity: the floor reported {first.Value.ColliderId}/" +
                $"{first.Value.ShapeIndex} but its authored path derives " +
                $"{expected.ColliderId}/{expected.ShapeIndex}. The identity is not scene-derived, " +
                "so a second process will not agree with this one and every grounded frame will " +
                "report a discrete mismatch.");
        }
    }

    /// <summary>
    /// The adapter and the deterministic reference world agree about behaviour.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The load-bearing case: every motor rule is unit tested against the
    /// reference world, and this is what makes those tests evidence about the
    /// real engine. It compares against the same type the tests use, not a copy,
    /// because a copy could drift and the agreement would then prove nothing.
    /// </para>
    /// <para>
    /// It deliberately does not demand matching positions frame by frame. The
    /// reference world approximates the capsule as an axis-aligned box, so at a
    /// step edge or a corner the two differ by construction and chasing exact
    /// parity would mean tuning the assertion until it passed rather than
    /// learning anything. What must agree is behaviour: comparable travel on
    /// open ground, both stopping at a wall, and both climbing a step. Those are
    /// the properties the motor's unit tests actually assert, so those are the
    /// properties that have to carry over.
    /// </para>
    /// </remarks>
    private void VerifyAgreesWithDeterministicWorld()
    {
        var reference = new DeterministicCollisionWorld(Profiles)
            .AddGround()
            .AddBox(2, new WorldPosition(6d, 0d, -10d), new WorldPosition(7d, 5d, 10d))
            .AddBox(3, new WorldPosition(2d, 0d, -10d), new WorldPosition(4d, 0.25d, 10d));

        var engineMotor = new CapsuleMovementSimulator(_world);
        var referenceMotor = new CapsuleMovementSimulator(reference);

        // 1. Open ground: travel rate must match closely, because this is where
        // box and capsule genuinely agree and where a per-frame loss shows up.
        var engineOpen = TravelOnOpenGround(engineMotor, new WorldPosition(-4d, 0d, 0d));
        var referenceOpen = TravelOnOpenGround(referenceMotor, new WorldPosition(-4d, 0d, 0d));
        var expected = 60 * 0.06d;
        if (Math.Abs(engineOpen - referenceOpen) > expected * 0.05d)
        {
            _failures.Add(
                $"open-ground travel: the engine covered {engineOpen:0.###} m where the reference " +
                $"covered {referenceOpen:0.###} m over 60 frames, so the engine loses travel the " +
                "unit tests never see.");
        }
        if (engineOpen < expected * 0.9d)
        {
            _failures.Add(
                $"open-ground travel: the engine covered {engineOpen:0.###} m of an expected " +
                $"{expected:0.###} m, so a walking character moves slower in-engine than in simulation.");
        }

        // 2. A wall stops both.
        var engineWall = RunTo(engineMotor, new WorldPosition(3d, 0.3d, 0d), 200);
        var referenceWall = RunTo(referenceMotor, new WorldPosition(3d, 0.3d, 0d), 200);
        if (engineWall.Position.X > 6.2d || referenceWall.Position.X > 6.2d)
        {
            _failures.Add(
                $"wall stop: engine reached X={engineWall.Position.X:0.###} and reference reached " +
                $"X={referenceWall.Position.X:0.###}; neither may pass the wall at X=6.");
        }

        // 3. A step is climbed by both.
        var engineStep = RunTo(engineMotor, new WorldPosition(0d, 0d, 0d), 60);
        var referenceStep = RunTo(referenceMotor, new WorldPosition(0d, 0d, 0d), 60);
        if (engineStep.Position.Y < 0.2d || referenceStep.Position.Y < 0.2d)
        {
            _failures.Add(
                $"step climb: engine ended at Y={engineStep.Position.Y:0.###} and reference at " +
                $"Y={referenceStep.Position.Y:0.###}; both must climb the 0.25 m step.");
        }

        // 4. And the whole course, through the driver gameplay actually runs.
        VerifyFullDriverCrossesTheCourse(reference);
    }

    /// <summary>
    /// The character walks the course under the real
    /// <see cref="CharacterMovementSimulator"/>, in-engine and in the reference.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The strongest case here, and the one that settled the step-climb defect.
    /// Every other case drives <see cref="CapsuleMovementSimulator"/> directly
    /// with a hand-supplied vertical motion, which cannot distinguish "the
    /// capsule cannot climb" from "the harness pulled it back down each frame".
    /// The driver integrates gravity into velocity and owns grounding, so it is
    /// the only harness whose answer is about gameplay.
    /// </para>
    /// <para>
    /// It asserts the engine gets as far as the reference does rather than
    /// matching positions: the two model the capsule differently and will always
    /// differ in the millimetres. Being stuck against a step is not a
    /// millimetre-scale difference, which is the point.
    /// </para>
    /// </remarks>
    private void VerifyFullDriverCrossesTheCourse(ICharacterCollisionWorld reference)
    {
        var engineEnd = RunFullDriverCourse(_world);
        var referenceEnd = RunFullDriverCourse(reference);

        // The step spans X in [2, 4] and the wall stands at X = 6. A run that
        // ends short of the step never climbed it.
        if (engineEnd.Position.X < 4.5d)
        {
            _failures.Add(
                $"full driver: in-engine the character stopped at X={engineEnd.Position.X:0.###} " +
                $"where the reference reached X={referenceEnd.Position.X:0.###}. It is stuck on " +
                "the 0.25 m step, which is a character that cannot walk up a stair in play.");
        }

        if (Math.Abs(engineEnd.Position.X - referenceEnd.Position.X) > 0.25d)
        {
            _failures.Add(
                $"full driver: engine reached X={engineEnd.Position.X:0.###} and reference " +
                $"X={referenceEnd.Position.X:0.###}; the two disagree about the course by more " +
                "than a capsule-versus-box difference explains.");
        }

        if (!engineEnd.IsGrounded)
        {
            _failures.Add("full driver: the character ended the course airborne in-engine.");
        }
    }

    /// <summary>Walks the course for two seconds under the real driver.</summary>
    private static CharacterKinematicState RunFullDriverCourse(ICharacterCollisionWorld world)
    {
        var attributes = new MovementAttributeSnapshot(
            1,
            new GroundMovementAttributes(6, 13, 8, 10, 12, 20, 7, 2, -0.4));
        var capabilities = MovementCapabilitySnapshot.CreateBaseFighter(1);
        var driver = new CharacterMovementSimulator(
            new CapsuleMovementSimulator(world), new MovementSourceSimulator());

        var revision = new MovementConfigurationRevision(1);
        var capabilityRevision = new MovementCapabilityRevision(1);
        var state = CharacterSimulationState.CreateGrounded(
            new WorldPosition(0d, 0d, 0d), 0d, new SimulationInstant(100), revision, capabilityRevision);

        var input = new CharacterSimulationInput(
            MovementAxes.FromUnitVector(new HorizontalVector(1d, 0d)),
            ViewOrientation.FromRadians(0d, 0d),
            new MovementHeldState(MovementHeldButtons.None),
            default,
            default,
            default,
            revision,
            capabilityRevision);

        for (var frame = 0; frame < 120; frame++)
        {
            var context = SimulationStepContext.Current(
                new SimulationInstant(state.Frame.Tick + 1), new SimulationRate(TicksPerSecond));
            state = driver.Simulate(state, input, [], context, attributes, capabilities).State;
        }

        return state.Kinematic;
    }

    /// <summary>Horizontal distance covered over 60 frames of unobstructed walking.</summary>
    private static double TravelOnOpenGround(
        CapsuleMovementSimulator motor,
        WorldPosition start)
    {
        var state = CharacterKinematicState.AtRest(start, 0d);
        for (var frame = 0; frame < 60; frame++)
        {
            state = motor.Move(
                state, CollisionProfileState.Standing,
                new HorizontalVector(0d, 0.06d), -0.05d,
                new SimulationInstant(frame), new SimulationInstant(frame + 1),
                Profiles, Policy).State;
        }

        return Math.Abs(state.Position.Z - start.Z);
    }

    private static CharacterKinematicState RunTo(
        CapsuleMovementSimulator motor,
        WorldPosition start,
        int frames)
    {
        var state = CharacterKinematicState.AtRest(start, 0d);
        for (var frame = 0; frame < frames; frame++)
        {
            state = motor.Move(
                state, CollisionProfileState.Standing,
                new HorizontalVector(0.06d, 0d), -0.05d,
                new SimulationInstant(frame), new SimulationInstant(frame + 1),
                Profiles, Policy).State;
        }

        return state;
    }

    /// <summary>
    /// Reports margin and separation-ray settings explicitly.
    /// </summary>
    /// <remarks>
    /// Both are left at engine defaults today. Recording them makes the defaults
    /// a decision rather than an accident, and makes a future engine upgrade that
    /// changes them visible in a diff.
    /// </remarks>
    private void ReportQuerySettings()
    {
        var parameters = new PhysicsTestMotionParameters3D();
        GD.Print(
            $"[CollisionWorldProbe] Query settings: Margin={parameters.Margin:0.####}; " +
            $"RecoveryAsCollision={parameters.RecoveryAsCollision}; " +
            $"MaxCollisions={parameters.MaxCollisions}; " +
            $"IgnoredPenetrationDepth={Policy.IgnoredPenetrationDepth:0.####}");
        parameters.Dispose();
    }

    private ulong _dynamicColliderId;

    private void BuildGeometry()
    {
        AddBox("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f), StaticWorldLayer);
        AddBox("Wall", new Vector3(6.5f, 2.5f, 0f), new Vector3(1f, 5f, 20f), StaticWorldLayer);
        AddBox("Step", new Vector3(3f, 0.125f, 0f), new Vector3(2f, 0.25f, 20f), StaticWorldLayer);
        _dynamicColliderId =
            AddBox("DynamicBlocker", new Vector3(0f, 1f, 20f), new Vector3(2f, 2f, 2f), DynamicLayer);
    }

    private ulong AddBox(string name, Vector3 position, Vector3 size, uint layer)
    {
        var body = new StaticBody3D
        {
            Name = name,
            Position = position,
            CollisionLayer = layer,
            CollisionMask = 0,
        };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        AddChild(body);
        return body.GetRid().Id;
    }
}
