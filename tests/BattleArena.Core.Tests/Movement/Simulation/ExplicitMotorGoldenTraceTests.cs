using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Core.Movement.Simulation;

namespace BattleArena.Core.Tests.Movement.Simulation;

/// <summary>
/// The gate the explicit motor must pass before networking cuts over to it.
/// </summary>
/// <remarks>
/// These tests exist to catch the failure mode that unit tests structurally
/// cannot: a rule that is individually correct but produces a different result
/// on the second pass over the same frames. Owner prediction replays constantly,
/// so a divergence of one bit per thousand frames is a correction the player
/// feels, and it will not show up in a test that simulates each frame once.
/// </remarks>
public sealed class ExplicitMotorGoldenTraceTests
{
    private const int TicksPerSecond = 60;
    private const long FirstFrame = 1_000;

    /// <remarks>
    /// This is deliberately the weaker of the two reproducibility tests: the
    /// step is a pure function of state and frame index over a world with no
    /// mutable state, so identical results are close to true by construction.
    /// What it genuinely proves is that neither the simulator nor the motor
    /// holds hidden per-instance state across ten thousand frames — which a
    /// cached buffer or a memoized contact would break. The restore-and-replay
    /// test below is the one that proves reconciliation is safe.
    /// </remarks>
    [Fact]
    public void TenThousandFramesOverTheCourseAreBitIdenticalOnASecondRun()
    {
        var first = RunCourse(frames: 10_000);
        var second = RunCourse(frames: 10_000);

        Assert.Equal(first.Count, second.Count);
        for (var index = 0; index < first.Count; index++)
        {
            Assert.True(
                first[index].Equals(second[index]),
                $"Frame {index} diverged between two identical runs.");
        }
    }

    [Fact]
    public void RestoringAtEveryHundredthFrameAndReplayingReachesTheSameEndState()
    {
        // Restore-and-replay is what reconciliation actually does. Sampling every
        // hundredth frame across the whole trace covers grounded travel, airborne
        // arcs, wall contact, steps, and the crouch passage.
        const int frames = 3_000;
        var history = RunCourse(frames);
        var expected = history[^1];

        for (var restoreAt = 0; restoreAt < frames; restoreAt += 100)
        {
            // Compared per frame, not just at the end. A divergence that appears
            // mid-trace and re-converges by the last frame is still a divergence:
            // the player saw it, and reconciliation would have corrected for it.
            var fixture = new Fixture();
            var replayed = history[restoreAt];
            for (var index = restoreAt; index < frames; index++)
            {
                replayed = fixture.Step(replayed, index).State;
                Assert.True(
                    history[index + 1].Equals(replayed),
                    $"Replay from frame {restoreAt} diverged at frame {index + 1}.");
            }

            Assert.True(expected.Equals(replayed));
        }
    }

    [Fact]
    public void ReplayingTheSameSpanRepeatedlyIsIdempotent()
    {
        // A frame may be resimulated many times as corrections arrive. The tenth
        // pass must equal the first.
        const int frames = 600;
        var history = RunCourse(frames);
        var baseline = ReplayFrom(history[200], 200, frames);

        for (var attempt = 0; attempt < 10; attempt++)
        {
            Assert.True(
                baseline.Equals(ReplayFrom(history[200], 200, frames)),
                $"Replay attempt {attempt} differed from the first.");
        }

        // And the repeated replay matches the original trace frame by frame,
        // not merely itself.
        var fixture = new Fixture();
        var stepped = history[200];
        for (var index = 200; index < frames; index++)
        {
            stepped = fixture.Step(stepped, index).State;
            Assert.True(
                history[index + 1].Equals(stepped),
                $"Repeated replay diverged from the original at frame {index + 1}.");
        }
    }

    [Fact]
    public void TheCourseActuallyExercisesEveryMotorOutcomeItClaimsTo()
    {
        // Guards against the reproducibility tests passing vacuously because the
        // character stood still for ten thousand frames.
        var outcomes = new HashSet<CapsuleMotionOutcome>();
        var sawAirborne = false;
        var sawCrouched = false;
        var sawStep = false;

        var fixture = new Fixture();
        var state = fixture.Start();
        for (var index = 0; index < 3_000; index++)
        {
            var result = fixture.Step(state, index);
            state = result.State;
            outcomes.Add(result.Outcome);
            sawAirborne |= !state.Kinematic.IsGrounded;
            sawCrouched |= state.Profile.Current != CollisionProfileKind.Standing;
            sawStep |= result.Outcome == CapsuleMotionOutcome.Stepped;
        }

        Assert.Contains(CapsuleMotionOutcome.Completed, outcomes);
        Assert.Contains(CapsuleMotionOutcome.Slid, outcomes);
        Assert.True(sawAirborne, "The course must leave the ground.");
        Assert.True(sawCrouched, "The course must change collision profile.");
        Assert.True(sawStep, "The course must climb a step.");
    }

    [Fact]
    public void NoFrameOfTheCourseEndsInAnInvalidState()
    {
        var fixture = new Fixture();
        var state = fixture.Start();
        for (var index = 0; index < 3_000; index++)
        {
            state = fixture.Step(state, index).State;
            Assert.True(state.IsValid, $"Frame {index} produced an invalid state.");
            Assert.True(state.Kinematic.Position.IsFinite, $"Frame {index} lost finiteness.");
        }
    }

    [Fact]
    public void TheCourseNeverFallsThroughTheFloor()
    {
        // Tunnelling is the failure that a swept motor exists to prevent, and it
        // is silent until a character is under the map.
        var fixture = new Fixture();
        var state = fixture.Start();
        for (var index = 0; index < 3_000; index++)
        {
            state = fixture.Step(state, index).State;
            Assert.True(
                state.Kinematic.Position.Y > -1d,
                $"Frame {index} fell through the floor to Y={state.Kinematic.Position.Y}.");
        }
    }

    private static List<CharacterSimulationState> RunCourse(int frames)
    {
        var fixture = new Fixture();
        var history = new List<CharacterSimulationState>(frames + 1);
        var state = fixture.Start();
        history.Add(state);
        for (var index = 0; index < frames; index++)
        {
            state = fixture.Step(state, index).State;
            history.Add(state);
        }

        return history;
    }

    private static CharacterSimulationState ReplayFrom(
        CharacterSimulationState restored,
        int fromIndex,
        int toIndex)
    {
        var fixture = new Fixture();
        var state = restored;
        for (var index = fromIndex; index < toIndex; index++)
        {
            state = fixture.Step(state, index).State;
        }

        return state;
    }

    /// <summary>
    /// The course: a floor, a wall to slide along, a climbable step, and a low
    /// passage that forces a crouch.
    /// </summary>
    private sealed class Fixture
    {
        private static readonly MovementAttributeSnapshot Attributes = new(
            1,
            new GroundMovementAttributes(6, 13, 8, 10, 12, 20, 7, 2, -0.4));

        private static readonly MovementCapabilitySnapshot Capabilities =
            MovementCapabilitySnapshot.CreateBaseFighter(1);

        private static readonly CollisionProfileTable Profiles =
            CollisionProfileTable.FromAttributes(Attributes);

        private readonly CharacterMovementSimulator _simulator;

        public Fixture()
        {
            var world = new DeterministicCollisionWorld(Profiles)
                .AddGround()
                // A wall to slide along.
                .AddBox(2, new WorldPosition(6d, 0d, -20d), new WorldPosition(7d, 6d, 20d))
                // A climbable step.
                .AddBox(3, new WorldPosition(-8d, 0d, -20d), new WorldPosition(-6d, 0.3d, 20d))
                // A low passage forcing a crouch.
                .AddBox(4, new WorldPosition(-4d, 1.4d, -4d), new WorldPosition(-2d, 4d, 4d));

            _simulator = new CharacterMovementSimulator(
                new CapsuleMovementSimulator(world),
                new MovementSourceSimulator());
        }

        public CharacterSimulationState Start() => CharacterSimulationState.CreateGrounded(
            new WorldPosition(0d, 0d, 0d),
            0d,
            new SimulationInstant(FirstFrame),
            new MovementConfigurationRevision(1),
            new MovementCapabilityRevision(1));

        /// <summary>
        /// The scripted input for one frame. A pure function of the frame index,
        /// so a replayed frame receives exactly the input the original did.
        /// </summary>
        public CharacterFrameResult Step(CharacterSimulationState state, int index)
        {
            var phase = index % 400;
            var move = phase switch
            {
                < 90 => new HorizontalVector(1d, 0d),      // into the wall
                < 180 => new HorizontalVector(-1d, 0.4d),  // away and across
                < 260 => new HorizontalVector(-1d, 0d),    // toward the step
                < 320 => new HorizontalVector(0d, 1d),
                _ => HorizontalVector.Zero,
            };

            var transitions = phase switch
            {
                120 => new[] { MovementTransitionKindTag.JumpPressed },
                135 => new[] { MovementTransitionKindTag.JumpReleased },
                300 => new[] { MovementTransitionKindTag.CrouchOrRollPressed },
                360 => new[] { MovementTransitionKindTag.CrouchOrRollReleased },
                _ => [],
            };

            var held = phase is >= 300 and < 360
                ? MovementHeldButtons.CrouchOrRoll
                : phase < 90
                    ? MovementHeldButtons.Sprint
                    : MovementHeldButtons.None;

            var input = new CharacterSimulationInput(
                MovementAxes.FromUnitVector(move),
                ViewOrientation.FromRadians(0d, 0d),
                new MovementHeldState(held),
                default,
                default,
                default,
                new MovementConfigurationRevision(1),
                new MovementCapabilityRevision(1));

            var context = SimulationStepContext.Current(
                new SimulationInstant(state.Frame.Tick + 1),
                new SimulationRate(TicksPerSecond));

            return _simulator.Simulate(
                state, input, transitions, context, Attributes, Capabilities);
        }
    }
}
