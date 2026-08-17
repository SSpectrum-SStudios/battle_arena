using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Core.Movement.Simulation;

namespace BattleArena.Core.Tests.Movement.Simulation;

public sealed class CharacterMovementSimulatorTests
{
    private const int TicksPerSecond = 60;

    [Fact]
    public void AGroundedCharacterAcceleratesForwardUnderTheAcceptedRules()
    {
        var fixture = new Fixture();

        var state = fixture.Run(frames: 30, move: new HorizontalVector(0d, -1d));

        Assert.True(
            state.Kinematic.Position.Z < -1d,
            $"Expected forward travel, ended at Z={state.Kinematic.Position.Z}.");
        Assert.True(state.Kinematic.IsGrounded);
        Assert.Equal(LocomotionMode.Grounded, state.LocomotionMode);
    }

    [Fact]
    public void AJumpLeavesTheGroundAndLandsAgain()
    {
        var fixture = new Fixture();

        // The jump edge arrives as a durable transition, not a transient bit.
        var afterJump = fixture.Step(
            fixture.Start(),
            HorizontalVector.Zero,
            transitions: [MovementTransitionKindTag.JumpPressed]);
        Assert.True(afterJump.Kinematic.VerticalVelocity > 0d, "The jump must impart upward velocity.");

        var state = afterJump;
        var leftGround = false;
        for (var i = 0; i < 120; i++)
        {
            state = fixture.Step(state, HorizontalVector.Zero, []);
            leftGround |= !state.Kinematic.IsGrounded;
            if (leftGround && state.Kinematic.IsGrounded)
            {
                break;
            }
        }

        Assert.True(leftGround, "The character must actually leave the ground.");
        Assert.True(state.Kinematic.IsGrounded, "And must land again.");

        // Lands resting the skin distance above the floor, not exactly on it.
        Assert.InRange(state.Kinematic.Position.Y, 0d, 0.01d);
    }

    [Fact]
    public void ACharacterUnderALowCeilingCannotStandAndStandsWhenItClears()
    {
        // The pending intent is held rather than discarded, so the character
        // stands the moment clearance appears without another press.
        var world = new DeterministicCollisionWorld(Fixture.Profiles)
            .AddGround()
            .AddBox(2, new WorldPosition(-2d, 1.3d, -2d), new WorldPosition(2d, 3d, 2d));
        var fixture = new Fixture(world);

        var crouched = fixture.Step(
            fixture.Start(),
            HorizontalVector.Zero,
            transitions: [MovementTransitionKindTag.CrouchOrRollPressed],
            heldCrouch: true);
        Assert.Equal(PostureMode.Crouched, crouched.PostureMode);

        // Release under the ceiling: cannot stand.
        var stillCrouched = fixture.Step(
            crouched,
            HorizontalVector.Zero,
            transitions: [MovementTransitionKindTag.CrouchOrRollReleased]);
        Assert.Equal(CollisionProfileKind.Crouching, stillCrouched.Profile.Current);
    }

    [Fact]
    public void RestoringAnyFrameAndReplayingForwardReproducesTheSameFinalState()
    {
        // The property the whole phase exists for.
        var fixture = new Fixture();
        var moves = new[]
        {
            new HorizontalVector(0d, -1d),
            new HorizontalVector(1d, -1d),
            new HorizontalVector(1d, 0d),
            HorizontalVector.Zero,
        };

        var history = new List<CharacterSimulationState>();
        var state = fixture.Start();
        history.Add(state);
        for (var i = 0; i < 40; i++)
        {
            state = fixture.Step(state, moves[i % moves.Length], TransitionsFor(i));
            history.Add(state);
        }

        var expected = state;

        // Restore from every retained frame and replay forward.
        for (var restoreAt = 0; restoreAt < history.Count - 1; restoreAt++)
        {
            var replayed = history[restoreAt];
            for (var i = restoreAt; i < 40; i++)
            {
                replayed = fixture.Step(replayed, moves[i % moves.Length], TransitionsFor(i));
            }

            Assert.Equal(expected.Kinematic.Position, replayed.Kinematic.Position);
            Assert.Equal(expected.Kinematic.HorizontalVelocity, replayed.Kinematic.HorizontalVelocity);
            Assert.Equal(expected.Kinematic.VerticalVelocity, replayed.Kinematic.VerticalVelocity);
            Assert.Equal(expected.LocomotionMode, replayed.LocomotionMode);
            Assert.Equal(expected.JumpPhase, replayed.JumpPhase);
            Assert.Equal(expected, replayed);
        }

        static MovementTransitionKindTag[] TransitionsFor(int index) => index switch
        {
            7 => [MovementTransitionKindTag.JumpPressed],
            11 => [MovementTransitionKindTag.JumpReleased],
            23 => [MovementTransitionKindTag.CrouchOrRollPressed],
            _ => [],
        };
    }

    [Fact]
    public void AMovementSourceIsReplayableRatherThanAppliedOnce()
    {
        // The bug this replaces: a one-time impulse is lost when replay starts
        // before it fired, and applied twice when replay starts after.
        var fixture = new Fixture();
        var start = fixture.Start();
        var lunge = new MovementSourceState(
            MovementSourceKind.AttackLunge,
            sourceId: 1,
            startFrame: new SimulationInstant(start.Frame.Tick + 2),
            duration: new SimulationDuration(10),
            horizontalDirection: new HorizontalVector(0d, -1d),
            horizontalSpeed: 8d,
            verticalSpeed: 0d,
            MovementSourceFalloff.Linear);

        var withSource = start.WithMovementSources(
            new MovementSourceSimulator().Start(start.MovementSources, lunge));

        var direct = withSource;
        for (var i = 0; i < 20; i++)
        {
            direct = fixture.Step(direct, HorizontalVector.Zero, []);
        }

        // Replay from the same starting state produces the same result, and the
        // lunge is neither lost nor doubled.
        var replayed = withSource;
        for (var i = 0; i < 20; i++)
        {
            replayed = fixture.Step(replayed, HorizontalVector.Zero, []);
        }

        Assert.Equal(direct.Kinematic.Position, replayed.Kinematic.Position);
        Assert.True(
            direct.Kinematic.Position.Z < -0.5d,
            $"The lunge must actually displace the character; Z={direct.Kinematic.Position.Z}.");
    }

    [Fact]
    public void ASourceContributionIsNotPersistedIntoVelocity()
    {
        // Persisting it would double-count every frame: the next frame
        // re-evaluates the same curve on top of the copy already in velocity, so
        // a lunge would accelerate instead of decaying.
        var fixture = new Fixture();
        var start = fixture.Start();
        var lunge = new MovementSourceState(
            MovementSourceKind.AttackLunge,
            1,
            start.Frame,
            new SimulationDuration(30),
            new HorizontalVector(0d, -1d),
            10d,
            0d,
            MovementSourceFalloff.Constant);

        var state = start.WithMovementSources(
            new MovementSourceSimulator().Start(start.MovementSources, lunge));

        var speeds = new List<double>();
        for (var i = 0; i < 10; i++)
        {
            state = fixture.Step(state, HorizontalVector.Zero, []);
            speeds.Add(state.Kinematic.HorizontalVelocity.Length);
        }

        // With no input and a source that is never persisted, the character's own
        // velocity stays at rest rather than compounding.
        Assert.All(speeds, speed =>
            Assert.True(speed < 0.5d, $"Source velocity leaked into state: {speed}."));
    }

    [Fact]
    public void ContactsAreRecordedAndSurviveRestoreAndReplayIdentically()
    {
        // Contacts are derivable, so storing them is not what makes replay
        // correct. It is what makes a correction explainable: the comparer can
        // say the two simulations disagreed about which surface the character was
        // on rather than only that they disagreed about position.
        var world = new DeterministicCollisionWorld(Fixture.Profiles)
            .AddGround()
            .AddBox(2, new WorldPosition(1.5d, 0d, -10d), new WorldPosition(3d, 5d, 10d));
        var fixture = new Fixture(world);

        var history = new List<CharacterSimulationState>();
        var state = fixture.Start();
        history.Add(state);
        for (var i = 0; i < 30; i++)
        {
            state = fixture.Step(state, new HorizontalVector(1d, 0d), []);
            history.Add(state);
        }

        Assert.Contains(history, frame => frame.Contacts.Count > 0);

        // Restore from every frame and replay; the contact record must match.
        for (var restoreAt = 0; restoreAt < history.Count - 1; restoreAt++)
        {
            var replayed = history[restoreAt];
            for (var i = restoreAt; i < 30; i++)
            {
                replayed = fixture.Step(replayed, new HorizontalVector(1d, 0d), []);
                Assert.Equal(history[i + 1].Contacts, replayed.Contacts);
            }
        }
    }

    [Fact]
    public void TheSupportingSurfaceIsAmongTheRecordedContacts()
    {
        var fixture = new Fixture();

        var state = fixture.Step(fixture.Start(), HorizontalVector.Zero, []);

        Assert.True(state.Kinematic.IsGrounded);
        Assert.True(
            state.Contacts.Touches(state.Kinematic.Support),
            "The surface the character is standing on must appear in its contacts.");
    }

    [Fact]
    public void TheStateCarriesTheFrameItWasSimulatedFor()
    {
        var fixture = new Fixture();
        var state = fixture.Start();
        var first = state.Frame.Tick;

        state = fixture.Step(state, HorizontalVector.Zero, []);

        Assert.Equal(first + 1, state.Frame.Tick);
        Assert.True(state.IsValid);
    }

    private sealed class Fixture
    {
        /// <summary>
        /// The accepted authored tuning, matching what the movement configuration
        /// timeline supplies at runtime.
        /// </summary>
        internal static readonly MovementAttributeSnapshot Attributes = new(
            1,
            new GroundMovementAttributes(6, 13, 8, 10, 12, 20, 7, 2, -0.4));

        internal static readonly MovementCapabilitySnapshot Capabilities =
            MovementCapabilitySnapshot.CreateBaseFighter(1);

        internal static readonly CollisionProfileTable Profiles =
            CollisionProfileTable.FromAttributes(Attributes);

        private readonly CharacterMovementSimulator _simulator;
        private long _frame = 100;

        public Fixture(DeterministicCollisionWorld? world = null)
        {
            var collision = world ?? new DeterministicCollisionWorld(Profiles).AddGround();
            _simulator = new CharacterMovementSimulator(
                new CapsuleMovementSimulator(collision),
                new MovementSourceSimulator());
        }

        public CharacterSimulationState Start() => CharacterSimulationState.CreateGrounded(
            new WorldPosition(0d, 0d, 0d),
            0d,
            new SimulationInstant(_frame),
            new MovementConfigurationRevision(1),
            new MovementCapabilityRevision(1));

        public CharacterSimulationState Step(
            CharacterSimulationState state,
            HorizontalVector move,
            MovementTransitionKindTag[] transitions,
            bool heldCrouch = false)
        {
            var held = heldCrouch ? MovementHeldButtons.CrouchOrRoll : MovementHeldButtons.None;
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
                state,
                input,
                transitions,
                context,
                Attributes,
                Capabilities).State;
        }

        public CharacterSimulationState Run(int frames, HorizontalVector move)
        {
            var state = Start();
            for (var i = 0; i < frames; i++)
            {
                state = Step(state, move, []);
            }

            return state;
        }
    }
}
