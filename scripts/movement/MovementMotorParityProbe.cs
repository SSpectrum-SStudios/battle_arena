using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Core.Movement.Simulation;
using Godot;

namespace BattleArena.Movement;

/// <summary>
/// Runs one scripted input trace through both motors and reports where they
/// diverge.
/// </summary>
/// <remarks>
/// <para>
/// P05-16 supplies the motor switch but nothing compares the two, so "does the
/// explicit motor still feel right" has stayed an opinion. Both motors read the
/// same authored <c>movement.json</c>, so any divergence is the motor's doing
/// and not a difference in tuning.
/// </para>
/// <para>
/// This must run before reconciliation is trusted. Once corrections are live, a
/// motor that drifts from accepted feel stops looking like a motor bug and
/// starts looking like a network correction, which is a far more expensive thing
/// to debug.
/// </para>
/// <para>
/// <b>What "matches" means here, and what it deliberately does not.</b> Both
/// motors are closed loops over different collision algorithms, so any
/// difference on one frame changes the next frame's input state and divergence
/// compounds. Over a trace of several hundred frames the two separate by metres
/// however correct both are, and a per-frame position tolerance wide enough to
/// pass that is wide enough to hide a real regression. So this probe asserts
/// <em>feel-level quantities</em> — top speed, acceleration, braking, jump apex
/// and airtime, stair climb, crouch clearance, roll distance — which are what a
/// player perceives and what a tuning regression actually moves. Per-frame
/// position and velocity are compared only over a short shared-spawn window,
/// where divergence has not yet compounded and a gross immediate difference is
/// still visible.
/// </para>
/// <para>
/// Every measurement is reported as a measured pair rather than a pass bit, so
/// a number drifting toward its tolerance is visible before it crosses it.
/// </para>
/// <para>
/// Scope: the trace contains no attack, because attack movement influence is
/// unauthored until P05-18. The result therefore means "the motors match with no
/// attack step active", and must not be read as "the motors match".
/// </para>
/// </remarks>
public sealed partial class MovementMotorParityProbe : Node3D
{
    /// <summary>
    /// The same authored profile the arena loads.
    /// </summary>
    /// <remarks>
    /// Both motors read this one file. That is what makes a divergence
    /// attributable to the motor rather than to tuning, so it is deliberately a
    /// single source rather than a value per motor.
    /// </remarks>
    [Export]
    public string MovementProfilePath { get; set; } = "res://assets/classes/fighter/movement.json";

    /// <summary>Frames compared per frame from an identical spawn.</summary>
    private const int SharedWindowFrames = 30;

    /// <summary>Position agreement required inside the shared-spawn window.</summary>
    private const double SharedWindowPositionTolerance = 0.05d;

    /// <summary>Velocity agreement required inside the shared-spawn window.</summary>
    private const double SharedWindowVelocityTolerance = 0.35d;

    /// <summary>
    /// Phase difference between the two motors that is accepted rather than gated.
    /// </summary>
    /// <remarks>
    /// One frame, measured rather than chosen: the motors integrate in a different
    /// order, so the explicit motor's trace matches legacy's shifted by exactly one
    /// frame. Two would not be accepted — see
    /// <see cref="GateAtBestAlignment"/> for why the limit exists at all.
    /// </remarks>
    private const int MaximumAcceptedPhaseFrames = 1;

    private const uint StaticWorldLayer = 1 << 0;
    private const uint ProbeBodyLayer = 1 << 4;

    private readonly List<string> _failures = [];
    private readonly List<string> _measurements = [];
    private readonly List<Vector3> _legacyVelocities = [];
    private readonly List<Vector3> _explicitVelocities = [];

    private MovementAttributeSnapshot _attributes = null!;
    private MovementCapabilitySnapshot _capabilities = null!;
    private CollisionProfileTable _profiles = null!;
    private SimulationRate _rate;

    private CharacterBody3D _legacyBody = null!;
    private GodotKinematicCollisionWorld _world = null!;
    private int _settleFrames;

    public override void _Ready()
    {
        _rate = new SimulationRate(Engine.PhysicsTicksPerSecond);
        _attributes = MovementProfileLoader.Load(MovementProfilePath, simulationRate: _rate).Attributes;
        _capabilities = MovementCapabilitySnapshot.CreateBaseFighter(1);
        _profiles = CollisionProfileTable.FromAttributes(_attributes);

        // The real arena course, not purpose-built geometry: this probe exists to
        // say the motors agree where movement feel is actually judged.
        AddChild(new MovementTestCourse());

        _legacyBody = BuildLegacyBody();
        AddChild(_legacyBody);

        _world = new GodotKinematicCollisionWorld();
        AddChild(_world);
        _world.Initialize(_profiles, StaticWorldLayer);
    }

    /// <summary>
    /// Waits for the course's static bodies to exist in the physics space before
    /// measuring anything.
    /// </summary>
    /// <remarks>
    /// The legacy motor is <c>MoveAndSlide</c> on a live body, so it needs real
    /// physics frames; querying before the course is present would measure both
    /// motors falling through an empty world and agreeing perfectly about it.
    /// </remarks>
    public override void _PhysicsProcess(double delta)
    {
        if (++_settleFrames < 3)
        {
            return;
        }

        SetPhysicsProcess(false);
        RunProbe(delta);
    }

    private void RunProbe(double delta)
    {
        VerifyCourseIsPresent();
        if (_failures.Count == 0)
        {
            CompareSharedSpawnWindow(delta);
            CompareFeelMetrics(delta);
        }

        foreach (var measurement in _measurements)
        {
            GD.Print($"[MotorParityProbe] {measurement}");
        }

        if (_failures.Count == 0)
        {
            GD.Print("[MotorParityProbe] PASSED: the explicit motor matches accepted feel.");
            GetTree().Quit(0);
            return;
        }

        foreach (var failure in _failures)
        {
            GD.PrintErr($"[MotorParityProbe] FAILED: {failure}");
        }

        GetTree().Quit(1);
    }

    /// <summary>
    /// Proves the course exists before comparing anything against it.
    /// </summary>
    /// <remarks>
    /// Two motors in an empty world agree exactly, so without this the most
    /// broken possible run is also the greenest.
    /// </remarks>
    private void VerifyCourseIsPresent()
    {
        var ground = _world.ProbeGround(new GroundProbeRequest(
            new WorldPosition(0d, 1d, 27d), 3d, CollisionProfileKind.Standing));
        if (!ground.FoundGround)
        {
            _failures.Add(
                "course presence: no ground found at the start station, so both motors would " +
                "fall through an empty world and agree about it.");
        }
    }

    /// <summary>
    /// Per-frame position and velocity agreement over a short window from an
    /// identical spawn.
    /// </summary>
    /// <remarks>
    /// Bounded to <see cref="SharedWindowFrames"/> on purpose. This is the window
    /// where a per-frame comparison still means something; beyond it the two
    /// closed loops have compounded any difference and the comparison degrades
    /// into measuring chaos.
    /// </remarks>
    private void CompareSharedSpawnWindow(double delta)
    {
        var spawn = new Vector3(-20f, 0.05f, 27f);
        var legacy = new LegacyRun(this, spawn);
        var explicitRun = new ExplicitRun(this, spawn);

        // Tracked per field, not as one distance. Collapsing four fields into a
        // magnitude is the averaging-away this probe exists to avoid: it cannot
        // tell a vertical settling difference at spawn apart from a horizontal
        // speed difference, and those need different answers.
        var horizontalPosition = new WorstGap("horizontal position", "m");
        var verticalPosition = new WorstGap("vertical position", "m");
        var horizontalVelocity = new WorstGap("horizontal velocity", "m/s");
        var verticalVelocity = new WorstGap("vertical velocity", "m/s");

        // Retained so the gap can be re-measured at a one-frame offset. A
        // constant lag and genuinely different motion look identical in a
        // same-frame comparison, and they mean completely different things: the
        // first is a frame-semantics difference between the two motors, the second
        // is a motor that moves the character differently.
        var legacyTrace = new List<Vector3>(SharedWindowFrames);
        var explicitTrace = new List<Vector3>(SharedWindowFrames);
        _legacyVelocities.Clear();
        _explicitVelocities.Clear();

        for (var frame = 0; frame < SharedWindowFrames; frame++)
        {
            var command = ScriptedInput(frame, new SimulationInstant(frame));
            legacy.Step(command, delta);
            explicitRun.Step(command, delta);
            legacyTrace.Add(legacy.Position);
            explicitTrace.Add(explicitRun.Position);
            _legacyVelocities.Add(legacy.Velocity);
            _explicitVelocities.Add(explicitRun.Velocity);

            horizontalPosition.Observe(
                HorizontalDistance(legacy.Position, explicitRun.Position), frame);
            verticalPosition.Observe(
                Math.Abs(legacy.Position.Y - explicitRun.Position.Y), frame);
            horizontalVelocity.Observe(
                HorizontalDistance(legacy.Velocity, explicitRun.Velocity), frame);
            verticalVelocity.Observe(
                Math.Abs(legacy.Velocity.Y - explicitRun.Velocity.Y), frame);
        }

        GateAtBestAlignment(legacyTrace, explicitTrace);

        // Reported, not gated. Vertical settling at spawn differs by
        // construction: the legacy body falls the spawn gap and MoveAndSlide stops
        // it, while the explicit motor snaps to its skin distance. The same-frame
        // horizontal numbers are reported too, because they are what makes the
        // one-frame phase offset visible instead of hidden behind the aligned
        // comparison that follows.
        foreach (var gap in new[] { horizontalPosition, verticalPosition, horizontalVelocity, verticalVelocity })
        {
            _measurements.Add(
                $"shared window {gap.Field} (same frame): worst gap {gap.Worst:0.####} {gap.Unit} at " +
                $"frame {gap.WorstFrame}");
        }
    }

    /// <summary>
    /// Gates the path the motors take, separately from the frame they take it on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured, not assumed: the two motors are one frame out of phase, and the
    /// same-frame gap of 0.126 m collapses to 0.026 m at a one-frame offset. Those
    /// are two different questions and they need separate answers, because only
    /// one of them is a defect. "The character follows a different path" would be
    /// a motor bug. "The character follows the same path, one frame apart" is an
    /// integration-order difference between an accepted motor that is being
    /// retired and its replacement, and forcing the replacement to match a
    /// motor about to be deleted would be the wrong repair.
    /// </para>
    /// <para>
    /// So the path is gated at the best alignment, and the phase offset is gated
    /// at one frame. A two-frame drift, or a path that diverges however it is
    /// aligned, both still fail. What no longer fails is the phase difference
    /// itself, which is recorded in P5B-02 as a named accepted difference.
    /// </para>
    /// <para>
    /// This matters to Phase 6 beyond feel: reconciliation compares an owner's
    /// predicted frame against authority's answer for the same frame, so a
    /// one-frame phase error anywhere in that path reads as a divergence on every
    /// single frame and would correct the player continuously.
    /// </para>
    /// </remarks>
    private void GateAtBestAlignment(List<Vector3> legacy, List<Vector3> explicitTrace)
    {
        var bestOffset = 0;
        var bestWorst = double.MaxValue;
        var bestFrame = -1;

        for (var offset = -MaximumAcceptedPhaseFrames - 1;
             offset <= MaximumAcceptedPhaseFrames + 1;
             offset++)
        {
            var worst = 0d;
            var worstFrame = -1;
            var compared = 0;
            for (var frame = 0; frame < legacy.Count; frame++)
            {
                var other = frame + offset;
                if (other < 0 || other >= explicitTrace.Count)
                {
                    continue;
                }

                var gap = HorizontalDistance(legacy[frame], explicitTrace[other]);
                if (gap > worst)
                {
                    worst = gap;
                    worstFrame = frame;
                }

                compared++;
            }

            if (compared > 0 && worst < bestWorst)
            {
                bestWorst = worst;
                bestOffset = offset;
                bestFrame = worstFrame;
            }
        }

        _measurements.Add(
            $"shared window path: agrees best at a {bestOffset:+0;-0;0}-frame offset, worst " +
            $"horizontal gap {bestWorst:0.####} m there on frame {bestFrame} " +
            $"(tolerance {SharedWindowPositionTolerance:0.###})");

        // Velocity is gated at the same alignment. Leaving it merely reported —
        // which the first version did, with the tolerance constant declared and
        // never read — meant a motor producing correct positions with wrong
        // velocities passed silently. That is precisely what a broken velocity
        // projection produces, and the projection exists because a character
        // pressed into a wall that keeps full speed corrupts the jump speed bonus
        // and the roll entry gate.
        var worstVelocityGap = 0d;
        var worstVelocityFrame = -1;
        for (var frame = 0; frame < _legacyVelocities.Count; frame++)
        {
            var other = frame + bestOffset;
            if (other < 0 || other >= _explicitVelocities.Count)
            {
                continue;
            }

            var gap = HorizontalDistance(_legacyVelocities[frame], _explicitVelocities[other]);
            if (gap > worstVelocityGap)
            {
                worstVelocityGap = gap;
                worstVelocityFrame = frame;
            }
        }

        _measurements.Add(
            $"shared window velocity: worst horizontal gap {worstVelocityGap:0.####} m/s at the same " +
            $"alignment, on frame {worstVelocityFrame} " +
            $"(tolerance {SharedWindowVelocityTolerance:0.###})");

        if (worstVelocityGap > SharedWindowVelocityTolerance)
        {
            _failures.Add(
                $"shared window velocity: at its best alignment the explicit motor's horizontal " +
                $"velocity differs from legacy by {worstVelocityGap:0.####} m/s on frame " +
                $"{worstVelocityFrame}, past the {SharedWindowVelocityTolerance:0.###} m/s tolerance.");
        }

        if (bestWorst > SharedWindowPositionTolerance)
        {
            _failures.Add(
                $"shared window path: at its best alignment the explicit motor still departs from " +
                $"the legacy path by {bestWorst:0.####} m on frame {bestFrame}, past the " +
                $"{SharedWindowPositionTolerance:0.###} m tolerance. A gap that survives alignment is " +
                "a different path, not a phase difference.");
        }

        if (Math.Abs(bestOffset) > MaximumAcceptedPhaseFrames)
        {
            _failures.Add(
                $"shared window phase: the motors are {Math.Abs(bestOffset)} frames out of phase, past " +
                $"the {MaximumAcceptedPhaseFrames}-frame limit. Reconciliation compares an owner's " +
                "frame against authority's answer for the same frame, so a phase error larger than " +
                "this would read as a divergence on every frame and correct the player continuously.");
        }
    }

    /// <summary>The largest gap seen in one field, and the frame it happened on.</summary>
    private sealed class WorstGap(string field, string unit)
    {
        public string Field => field;
        public string Unit => unit;
        public double Worst { get; private set; }
        public int WorstFrame { get; private set; } = -1;

        public void Observe(double gap, int frame)
        {
            if (gap <= Worst)
            {
                return;
            }

            Worst = gap;
            WorstFrame = frame;
        }
    }

    /// <summary>
    /// Measures the quantities a player perceives under both motors.
    /// </summary>
    private void CompareFeelMetrics(double delta)
    {
        MeasureTopSpeedAndAcceleration(delta);
        MeasureBraking(delta);
        MeasureJumpArc(delta);
        MeasureStepClimb(delta);
        MeasureCrouchClearance(delta);
        MeasureRoll(delta);
        MeasureWallSlide(delta);
    }

    /// <summary>Top run speed, top sprint speed, and time to reach run speed.</summary>
    private void MeasureTopSpeedAndAcceleration(double delta)
    {
        var spawn = new Vector3(-20f, 0.05f, 27f);

        var legacyRun = MeasureLane(new LegacyRun(this, spawn), delta, sprint: false);
        var explicitRunLane = MeasureLane(new ExplicitRun(this, spawn), delta, sprint: false);
        Compare("top run speed", legacyRun.TopSpeed, explicitRunLane.TopSpeed, 0.35d, "m/s");
        Compare(
            "frames to 95% run speed",
            legacyRun.FramesToTopSpeed,
            explicitRunLane.FramesToTopSpeed,
            6d,
            "frames");

        var legacySprint = MeasureLane(new LegacyRun(this, spawn), delta, sprint: true);
        var explicitSprint = MeasureLane(new ExplicitRun(this, spawn), delta, sprint: true);
        Compare("top sprint speed", legacySprint.TopSpeed, explicitSprint.TopSpeed, 0.35d, "m/s");
    }

    private LaneResult MeasureLane(IMotorRun run, double delta, bool sprint)
    {
        var target = sprint
            ? _attributes.Ground.MaximumSprintSpeed
            : _attributes.Ground.MaximumRunSpeed;
        var topSpeed = 0d;
        var framesToTopSpeed = -1;
        var held = sprint ? MovementButtons.Sprint : MovementButtons.None;

        for (var frame = 0; frame < 240; frame++)
        {
            run.Step(
                Command(frame, new HorizontalVector(0d, -1d), held, MovementButtons.None),
                delta);
            var speed = HorizontalLength(run.Velocity);
            topSpeed = Math.Max(topSpeed, speed);
            if (framesToTopSpeed < 0 && speed >= target * 0.95d)
            {
                framesToTopSpeed = frame;
            }
        }

        return new LaneResult(topSpeed, framesToTopSpeed);
    }

    /// <summary>Distance covered between releasing input and coming to rest.</summary>
    private void MeasureBraking(double delta)
    {
        var spawn = new Vector3(-20f, 0.05f, 27f);
        Compare(
            "braking distance",
            MeasureBrakingDistance(new LegacyRun(this, spawn), delta),
            MeasureBrakingDistance(new ExplicitRun(this, spawn), delta),
            0.35d,
            "m");
    }

    private double MeasureBrakingDistance(IMotorRun run, double delta)
    {
        for (var frame = 0; frame < 120; frame++)
        {
            run.Step(
                Command(frame, new HorizontalVector(0d, -1d), MovementButtons.None, MovementButtons.None),
                delta);
        }

        var releasedAt = run.Position;
        for (var frame = 120; frame < 240; frame++)
        {
            run.Step(
                Command(frame, HorizontalVector.Zero, MovementButtons.None, MovementButtons.None),
                delta);
            if (HorizontalLength(run.Velocity) < 0.05d)
            {
                break;
            }
        }

        return Distance(releasedAt, run.Position);
    }

    /// <summary>Jump apex above spawn, and frames spent off the ground.</summary>
    private void MeasureJumpArc(double delta)
    {
        var spawn = new Vector3(0f, 0.05f, 27f);
        var legacy = MeasureJump(new LegacyRun(this, spawn), delta);
        var explicitJump = MeasureJump(new ExplicitRun(this, spawn), delta);

        Compare("jump apex", legacy.Apex, explicitJump.Apex, 0.06d, "m");
        // Four frames. The measured difference is three (64 legacy, 61 explicit)
        // and it is real rather than definitional — both sides read LocomotionMode,
        // and the apex agrees to 5 mm, so the two motors leave and regain the
        // ground at slightly different moments while flying the same arc. That is
        // consistent with the one-frame integration phase difference plus a
        // grounding-threshold difference at each end. Recorded as a named accepted
        // difference in P5B-02 rather than tuned away.
        Compare("jump airtime", legacy.AirborneFrames, explicitJump.AirborneFrames, 4d, "frames");
    }

    private static JumpResult MeasureJump(IMotorRun run, double delta)
    {
        var startY = run.Position.Y;
        var apex = 0d;
        var airborneFrames = 0;

        for (var frame = 0; frame < 180; frame++)
        {
            var pressed = frame == 5 ? MovementButtons.Jump : MovementButtons.None;
            var held = frame >= 5 && frame < 20 ? MovementButtons.Jump : MovementButtons.None;
            run.Step(Command(frame, HorizontalVector.Zero, held, pressed), delta);
            apex = Math.Max(apex, run.Position.Y - startY);
            if (!run.IsGrounded)
            {
                airborneFrames++;
            }
        }

        return new JumpResult(apex, airborneFrames);
    }

    /// <summary>
    /// Height gained walking into the arena's authored step ledges.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The ledges at Z = 32, not the staircase. The staircase looks like the right
    /// station and is not: every visible stair in
    /// <see cref="MovementTestCourse"/> is built with collision disabled, and the
    /// only collider on that path is a smooth 15-degree ramp. A climb measured
    /// there exercises no step solver at all — it is a walk up a slope, and it
    /// stays green with the step solver completely broken. That made this gate
    /// decoration in its first version, which is exactly the failure it was
    /// written to prevent.
    /// </para>
    /// <para>
    /// The 0.35 m ledge is the useful one: under the 0.4 m step height so it must
    /// be climbed, and tall enough that a capsule contacts its top edge rather
    /// than its face, which is the geometry that produced the in-engine defect
    /// P5B-01 found.
    /// </para>
    /// </remarks>
    private void MeasureStepClimb(double delta)
    {
        // Approaching the 0.35 m ledge at (0, 0.175, 32), size 2.5 cubed, from +Z.
        var spawn = new Vector3(0f, 0.05f, 35f);
        var legacy = MeasureClimb(new LegacyRun(this, spawn), delta);
        var explicitClimb = MeasureClimb(new ExplicitRun(this, spawn), delta);

        Compare("ledge height gained", legacy, explicitClimb, 0.1d, "m");

        // An absolute floor as well as a comparison. A motor that cannot climb at
        // all is not a tuning difference, and if BOTH motors regressed the
        // comparison alone would stay green.
        if (explicitClimb < 0.3d)
        {
            _failures.Add(
                $"ledge climb: the explicit motor gained only {explicitClimb:0.###} m against a " +
                "0.35 m ledge, so it cannot climb a step at all. This is the defect class P5B-01 " +
                "found in-engine and it must not regress.");
        }

        if (legacy < 0.3d)
        {
            _failures.Add(
                $"ledge climb: the legacy motor gained only {legacy:0.###} m, so this station is not " +
                "exercising a climbable step and the comparison proves nothing.");
        }
    }

    private static double MeasureClimb(IMotorRun run, double delta)
    {
        var startY = run.Position.Y;
        var highest = startY;
        for (var frame = 0; frame < 180; frame++)
        {
            run.Step(
                Command(frame, new HorizontalVector(0d, -1d), MovementButtons.None, MovementButtons.None),
                delta);
            highest = Math.Max(highest, run.Position.Y);
        }

        return highest - startY;
    }

    /// <summary>
    /// A low ceiling stops a standing character and passes a crouched one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Run at the arena's low tunnel, and asserted as a pair. The first version of
    /// this measured crouched travel across flat open ground seven metres from the
    /// nearest ceiling, so it compared crouched top speed and called it clearance —
    /// it would have passed with profile switching entirely broken.
    /// </para>
    /// <para>
    /// The standing run is the half that gives the crouched run meaning. Without
    /// it, a motor that ignored the ceiling for both postures would show identical
    /// travel and pass.
    /// </para>
    /// </remarks>
    private void MeasureCrouchClearance(double delta)
    {
        // LowTunnelLeft sits at (7, 1.55, 22) with size (3.5, 0.35, 6), so its
        // underside is at Y = 1.375: below the 1.8 m standing profile and above
        // the 1.2 m crouching one.
        var spawn = new Vector3(7f, 0.05f, 27f);

        var legacyCrouched = MeasureTunnelTravel(new LegacyRun(this, spawn), delta, crouched: true);
        var explicitCrouched = MeasureTunnelTravel(new ExplicitRun(this, spawn), delta, crouched: true);
        Compare("crouched tunnel travel", legacyCrouched, explicitCrouched, 0.4d, "m");

        var legacyStanding = MeasureTunnelTravel(new LegacyRun(this, spawn), delta, crouched: false);
        var explicitStanding = MeasureTunnelTravel(new ExplicitRun(this, spawn), delta, crouched: false);
        Compare("standing tunnel travel", legacyStanding, explicitStanding, 0.5d, "m");

        // The tunnel's near face is about 3 m from the spawn, so a standing
        // character must stop well short of a crouched one clearing it.
        if (explicitStanding >= explicitCrouched - 1d)
        {
            _failures.Add(
                $"crouch clearance: standing travel ({explicitStanding:0.###} m) was not materially " +
                $"shorter than crouched travel ({explicitCrouched:0.###} m), so the low ceiling is " +
                "not stopping a standing character and this station proves nothing about profiles.");
        }
    }

    private static double MeasureTunnelTravel(IMotorRun run, double delta, bool crouched)
    {
        var start = run.Position;
        for (var frame = 0; frame < 150; frame++)
        {
            // Crouch is entered once and held, so what is measured is crouched
            // travel rather than a roll.
            var held = crouched ? MovementButtons.CrouchOrRoll : MovementButtons.None;
            var pressed = crouched && frame == 2
                ? MovementButtons.CrouchOrRoll
                : MovementButtons.None;
            run.Step(Command(frame, new HorizontalVector(0d, -1d), held, pressed), delta);
        }

        return Distance(start, run.Position);
    }

    /// <summary>Distance covered by a roll from a run.</summary>
    private void MeasureRoll(double delta)
    {
        var spawn = new Vector3(0f, 0.05f, 27f);
        Compare(
            "roll distance",
            MeasureRollDistance(new LegacyRun(this, spawn), delta),
            MeasureRollDistance(new ExplicitRun(this, spawn), delta),
            0.5d,
            "m");
    }

    private static double MeasureRollDistance(IMotorRun run, double delta)
    {
        // Rolling requires speed, so run first and then tap crouch/roll.
        for (var frame = 0; frame < 60; frame++)
        {
            run.Step(
                Command(frame, new HorizontalVector(0d, -1d), MovementButtons.None, MovementButtons.None),
                delta);
        }

        var rollStart = run.Position;
        for (var frame = 60; frame < 140; frame++)
        {
            var pressed = frame == 60 ? MovementButtons.CrouchOrRoll : MovementButtons.None;
            run.Step(
                Command(frame, new HorizontalVector(0d, -1d), MovementButtons.None, pressed),
                delta);
        }

        return Distance(rollStart, run.Position);
    }

    /// <summary>
    /// Both motors are deflected by an obstacle rather than passing through it or
    /// sticking to it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Spawned on the obstacle line, which the first version was not: it started at
    /// X = -10.55, and with a 0.42 m capsule against 0.55 m cylinders at X = -12 and
    /// X = -8 that left a 0.48 m clear corridor down the whole slalom. It measured
    /// three seconds of unobstructed running and called it a slide, and it would
    /// have been *greener* if both motors had tunnelled through the cylinders,
    /// because the two would then have agreed exactly.
    /// </para>
    /// <para>
    /// Three properties are asserted, and the lateral deflection is the one that
    /// makes the others meaningful: it is the only evidence the obstacle was
    /// touched at all.
    /// </para>
    /// </remarks>
    private void MeasureWallSlide(double delta)
    {
        // Offset from the cylinder's axis, not aimed at its centre. Dead-centre at
        // (-12, 1, 15) the contact normal points straight back along the travel
        // direction, so the character stops head-on with zero lateral deflection —
        // measured, and it made the deflection assertion fail for the right reason.
        // Off-centre is what produces a slide.
        var spawn = new Vector3(-11.6f, 0.05f, 19f);
        var legacy = MeasureSlide(new LegacyRun(this, spawn), delta);
        var explicitSlide = MeasureSlide(new ExplicitRun(this, spawn), delta);

        Compare("obstacle forward travel", legacy.Forward, explicitSlide.Forward, 0.6d, "m");
        Compare("obstacle lateral deflection", legacy.Lateral, explicitSlide.Lateral, 0.35d, "m");

        if (explicitSlide.Lateral < 0.15d)
        {
            _failures.Add(
                $"obstacle slide: the explicit motor was deflected only " +
                $"{explicitSlide.Lateral:0.###} m sideways, so it either never touched the obstacle " +
                "or passed straight through it. Either way this station measures open ground.");
        }

        if (legacy.Lateral < 0.15d)
        {
            _failures.Add(
                $"obstacle slide: the legacy motor was deflected only {legacy.Lateral:0.###} m, so " +
                "the spawn is not aimed at an obstacle and the comparison proves nothing.");
        }
    }

    private static SlideResult MeasureSlide(IMotorRun run, double delta)
    {
        var start = run.Position;
        for (var frame = 0; frame < 180; frame++)
        {
            run.Step(
                Command(frame, new HorizontalVector(0d, -1d), MovementButtons.None, MovementButtons.None),
                delta);
        }

        // Forward is -Z, so lateral displacement is the sideways push the obstacle
        // applied. A character that never touched anything has none.
        return new SlideResult(
            Math.Abs(run.Position.Z - start.Z),
            Math.Abs(run.Position.X - start.X));
    }

    /// <summary>
    /// The shared-window input: a plain forward walk, sprinting from frame 20.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately short and simple, because this only feeds the
    /// <see cref="SharedWindowFrames"/>-frame window. Its first version scripted a
    /// jump at frame 90, a crouch at 130 and a roll at 200 — every branch past
    /// frame 30 was unreachable, which read as far broader coverage than the probe
    /// had.
    /// </para>
    /// <para>
    /// The plan's list of behaviours — sprint, wall slide, step climb, jump arc,
    /// crouch passage, roll — is covered by the targeted stations instead, each
    /// spawned at the geometry it needs. One long continuous trace cannot deliver
    /// it: divergence compounds, so by the time such a trace reached its roll the
    /// two motors would be metres apart for reasons having nothing to do with
    /// rolling.
    /// </para>
    /// <para>
    /// A pure function of frame index, so both motors receive byte-identical input
    /// and the comparison is about the motors alone.
    /// </para>
    /// </remarks>
    private static MovementCommand ScriptedInput(int frameIndex, SimulationInstant frame) =>
        new(
            (ulong)frameIndex + 1,
            frame,
            new HorizontalVector(0d, -1d),
            0d,
            0d,
            frameIndex >= 20 ? MovementButtons.Sprint : MovementButtons.None,
            MovementButtons.None,
            MovementButtons.None);

    private static MovementCommand Command(
        int frameIndex,
        HorizontalVector move,
        MovementButtons held,
        MovementButtons pressed) =>
        new(
            (ulong)frameIndex + 1,
            new SimulationInstant(frameIndex),
            move,
            0d,
            0d,
            held,
            pressed,
            MovementButtons.None);

    /// <summary>
    /// Records a measured pair and fails when they disagree past the tolerance.
    /// </summary>
    /// <remarks>
    /// Always records, pass or fail. A gate that prints only its failures cannot
    /// show a number drifting toward its tolerance, which is the warning that
    /// arrives before the break.
    /// </remarks>
    private void Compare(string metric, double legacy, double explicitValue, double tolerance, string unit)
    {
        var difference = Math.Abs(legacy - explicitValue);
        _measurements.Add(
            $"{metric}: legacy {legacy:0.###} {unit}, explicit {explicitValue:0.###} {unit}, " +
            $"difference {difference:0.###} (tolerance {tolerance:0.###})");

        if (difference > tolerance)
        {
            _failures.Add(
                $"{metric}: legacy measured {legacy:0.###} {unit} and the explicit motor " +
                $"{explicitValue:0.###} {unit}, a difference of {difference:0.###} past the " +
                $"authored {tolerance:0.###} tolerance.");
        }
    }

    private CharacterBody3D BuildLegacyBody()
    {
        var body = new CharacterBody3D
        {
            Name = "LegacyProbeBody",

            // Its own layer, and it collides only with static world geometry. On
            // the static layer it would become an obstacle the explicit motor's
            // queries could hit, and the two motors would be measured against
            // different worlds.
            CollisionLayer = ProbeBodyLayer,
            CollisionMask = StaticWorldLayer,
        };
        var standing = _profiles.For(CollisionProfileKind.Standing);
        body.AddChild(new CollisionShape3D
        {
            Name = "BodyCollision",
            Shape = new CapsuleShape3D
            {
                Radius = (float)standing.Radius,
                Height = (float)standing.Height,
            },
            Position = new Vector3(0f, (float)standing.HalfHeight, 0f),
        });
        return body;
    }

    private readonly record struct LaneResult(double TopSpeed, double FramesToTopSpeed);

    private readonly record struct JumpResult(double Apex, double AirborneFrames);

    private readonly record struct SlideResult(double Forward, double Lateral);

    private static double HorizontalLength(Vector3 velocity) =>
        Math.Sqrt((velocity.X * velocity.X) + (velocity.Z * velocity.Z));

    private static double Distance(Vector3 left, Vector3 right) =>
        (left - right).Length();

    private static double HorizontalDistance(Vector3 left, Vector3 right) =>
        Math.Sqrt(
            ((left.X - right.X) * (left.X - right.X)) +
            ((left.Z - right.Z) * (left.Z - right.Z)));

    /// <summary>One motor under test, driven a frame at a time from a spawn.</summary>
    /// <remarks>
    /// The two motors have genuinely different shapes — one moves a live body
    /// through <c>MoveAndSlide</c>, the other advances a value state — so this
    /// interface exists to let every measurement above be written once and run
    /// against both. Writing each measurement twice is how the two copies drift
    /// apart and the comparison quietly stops comparing the same thing.
    /// </remarks>
    private interface IMotorRun
    {
        Vector3 Position { get; }
        Vector3 Velocity { get; }
        bool IsGrounded { get; }
        void Step(MovementCommand command, double delta);
    }

    /// <summary>The accepted motor: <c>MoveAndSlide</c> on a live body.</summary>
    private sealed class LegacyRun : IMotorRun
    {
        private readonly MovementMotorParityProbe _probe;
        private readonly GodotCharacterMovementDriver _driver;

        public LegacyRun(MovementMotorParityProbe probe, Vector3 spawn)
        {
            _probe = probe;
            probe._legacyBody.GlobalPosition = spawn;
            probe._legacyBody.Velocity = Vector3.Zero;
            _driver = new GodotCharacterMovementDriver(
                probe._legacyBody,
                probe._legacyBody.GetNode<CollisionShape3D>("BodyCollision"),
                probe._attributes,
                probe._rate,
                MovementRuntimeState.CreateGrounded(SimulationInstant.Zero));
        }

        public Vector3 Position => _probe._legacyBody.GlobalPosition;
        public Vector3 Velocity => _probe._legacyBody.Velocity;

        /// <remarks>
        /// <see cref="LocomotionMode"/> on both sides. Reading
        /// <c>Kinematic.IsGrounded</c> on the explicit side and a locomotion mode
        /// here compared two different quantities — the mode lags the kinematic
        /// flag by the rule simulator's transition — and the airtime tolerance had
        /// been set just above that definitional mismatch rather than above any
        /// real difference.
        /// </remarks>
        public bool IsGrounded => _driver.State.LocomotionMode != LocomotionMode.Airborne;

        public void Step(MovementCommand command, double delta) =>
            _driver.Simulate(command, delta);
    }

    /// <summary>The explicit motor: value state advanced by swept queries.</summary>
    private sealed class ExplicitRun : IMotorRun
    {
        private readonly MovementMotorParityProbe _probe;
        private readonly CharacterMovementSimulator _simulator;
        private CharacterSimulationState _state;

        public ExplicitRun(MovementMotorParityProbe probe, Vector3 spawn)
        {
            _probe = probe;
            _simulator = new CharacterMovementSimulator(
                new CapsuleMovementSimulator(probe._world),
                new MovementSourceSimulator());
            _state = CharacterSimulationState.CreateGrounded(
                new WorldPosition(spawn.X, spawn.Y, spawn.Z),
                0d,
                SimulationInstant.Zero,
                new MovementConfigurationRevision(1),
                new MovementCapabilityRevision(1));
        }

        public Vector3 Position => new(
            (float)_state.Kinematic.Position.X,
            (float)_state.Kinematic.Position.Y,
            (float)_state.Kinematic.Position.Z);

        public Vector3 Velocity => new(
            (float)_state.Kinematic.HorizontalVelocity.X,
            (float)_state.Kinematic.VerticalVelocity,
            (float)_state.Kinematic.HorizontalVelocity.Z);

        /// <remarks>
        /// The locomotion mode, matching what <see cref="LegacyRun"/> reports, so
        /// airtime compares like against like.
        /// </remarks>
        public bool IsGrounded => _state.LocomotionMode != LocomotionMode.Airborne;

        public void Step(MovementCommand command, double delta)
        {
            var input = new CharacterSimulationInput(
                MovementAxes.FromUnitVector(command.Movement),
                ViewOrientation.FromRadians(command.ViewYawRadians, command.ViewPitchRadians),
                new MovementHeldState(HeldFrom(command.HeldButtons)),
                default,
                default,
                default,
                new MovementConfigurationRevision(1),
                new MovementCapabilityRevision(1));

            Span<MovementTransitionKindTag> transitions = stackalloc MovementTransitionKindTag[4];
            var count = 0;
            if (command.WasPressed(MovementButtons.Jump))
            {
                transitions[count++] = MovementTransitionKindTag.JumpPressed;
            }
            if (command.WasReleased(MovementButtons.Jump))
            {
                transitions[count++] = MovementTransitionKindTag.JumpReleased;
            }
            if (command.WasPressed(MovementButtons.CrouchOrRoll))
            {
                transitions[count++] = MovementTransitionKindTag.CrouchOrRollPressed;
            }
            if (command.WasReleased(MovementButtons.CrouchOrRoll))
            {
                transitions[count++] = MovementTransitionKindTag.CrouchOrRollReleased;
            }

            _state = _simulator.Simulate(
                _state,
                input,
                transitions[..count],
                // The command's own tick, matching how MovementTestPlayer drives
                // the explicit motor in the arena. Advancing the context from the
                // state's frame instead would make this probe measure a wiring
                // difference it introduced rather than a motor difference.
                SimulationStepContext.Current(command.ClientTick, _probe._rate),
                _probe._attributes,
                _probe._capabilities).State;
        }

        private static MovementHeldButtons HeldFrom(MovementButtons buttons)
        {
            var held = MovementHeldButtons.None;
            if ((buttons & MovementButtons.Sprint) != 0)
            {
                held |= MovementHeldButtons.Sprint;
            }
            if ((buttons & MovementButtons.Jump) != 0)
            {
                held |= MovementHeldButtons.Jump;
            }
            if ((buttons & MovementButtons.CrouchOrRoll) != 0)
            {
                held |= MovementHeldButtons.CrouchOrRoll;
            }

            return held;
        }
    }
}
