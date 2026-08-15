using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Multiplayer.Presentation;

namespace BattleArena.Multiplayer.Tests.Presentation;

public sealed class CharacterPresentationControllerTests
{
    [Fact]
    public void OneCommittedSamplePublishesExactlyOneLocomotionUpdate()
    {
        var view = new RecordingView();
        var controller = Controller(view);
        var sample = Sample(
            right: 0.25f,
            forward: 1.5f,
            mode: LocomotionMode.Airborne);

        var decision = controller.Present(
            sample,
            CharacterPresentationPass.CommittedFrame,
            1d / 60d);

        Assert.Equal(CharacterPresentationDecision.Published, decision);
        Assert.Equal(1, view.ApplyCalls);
        Assert.Equal(1, view.AnimationOperations);
        Assert.Equal(CharacterPresentationUpdateKind.Locomotion, view.Last.Kind);
        Assert.Equal(0.25f, view.Last.NormalizedLocalRightVelocity);
        Assert.Equal(1.5f, view.Last.NormalizedLocalForwardVelocity);
        Assert.True(view.Last.Airborne);
    }

    [Fact]
    public void HistoricalReplayProducesNoPresentationOperations()
    {
        var view = new RecordingView();
        var controller = Controller(view);

        for (var index = 0; index < 30; index++)
        {
            Assert.Equal(
                CharacterPresentationDecision.SuppressedHistoricalReplay,
                controller.Present(
                    default,
                    CharacterPresentationPass.HistoricalReplay,
                    double.NaN));
        }

        Assert.Equal(0, view.ApplyCalls);
        Assert.Equal(0, view.AnimationOperations);
        Assert.Equal(0, view.AudioOperations);
        Assert.Equal(0, view.VfxOperations);
    }

    [Theory]
    [MemberData(nameof(CommittedUpdateCases))]
    public void CommittedSampleSelectsOneSemanticUpdate(
        CharacterPresentationSample sample,
        CharacterPresentationUpdateKind expectedKind,
        int expectedAnimationOperations)
    {
        var view = new RecordingView();
        var controller = Controller(view);

        controller.Present(
            sample,
            CharacterPresentationPass.CommittedFrame,
            1d / 60d);

        Assert.Equal(1, view.ApplyCalls);
        Assert.Equal(expectedKind, view.Last.Kind);
        Assert.Equal(expectedAnimationOperations, view.AnimationOperations);
        Assert.Equal(0, view.AudioOperations);
        Assert.Equal(0, view.VfxOperations);
    }

    [Fact]
    public void RollStartsOnceAndResetPermitsAReplacementLifeToStartItAgain()
    {
        var view = new RecordingView();
        var controller = Controller(view);
        var rolling = Sample(
            mode: LocomotionMode.Rolling,
            rollDuration: new SimulationDuration(30));

        controller.Present(
            rolling,
            CharacterPresentationPass.CommittedFrame,
            1d / 60d);
        controller.Present(
            rolling,
            CharacterPresentationPass.CommittedFrame,
            1d / 60d);

        Assert.Equal(2, view.ApplyCalls);
        Assert.Equal(CharacterPresentationUpdateKind.HoldRollContinuation, view.Last.Kind);
        Assert.Equal(1, view.AnimationOperations);
        Assert.Equal(0.5d, view.Updates[0].RollDurationSeconds, precision: 8);

        controller.Reset();
        controller.Present(
            rolling,
            CharacterPresentationPass.CommittedFrame,
            1d / 60d);
        Assert.Equal(CharacterPresentationUpdateKind.RollStart, view.Last.Kind);
        Assert.Equal(2, view.AnimationOperations);
    }

    [Fact]
    public void ReplayCannotChangeRollTransitionMemory()
    {
        var view = new RecordingView();
        var controller = Controller(view);
        var rolling = Sample(
            mode: LocomotionMode.Rolling,
            rollDuration: new SimulationDuration(12));
        controller.Present(
            rolling,
            CharacterPresentationPass.CommittedFrame,
            1d / 60d);

        controller.Present(
            Sample(),
            CharacterPresentationPass.HistoricalReplay,
            1d / 60d);
        controller.Present(
            rolling,
            CharacterPresentationPass.CommittedFrame,
            1d / 60d);

        Assert.Equal(2, view.ApplyCalls);
        Assert.Equal(CharacterPresentationUpdateKind.HoldRollContinuation, view.Last.Kind);
        Assert.Equal(1, view.AnimationOperations);
    }

    [Fact]
    public void LeavingRollAllowsTheNextRollToStart()
    {
        var view = new RecordingView();
        var controller = Controller(view);
        var rolling = Sample(
            mode: LocomotionMode.Rolling,
            rollDuration: new SimulationDuration(12));
        controller.Present(rolling, CharacterPresentationPass.CommittedFrame, 1d / 60d);
        controller.Present(Sample(), CharacterPresentationPass.CommittedFrame, 1d / 60d);
        controller.Present(rolling, CharacterPresentationPass.CommittedFrame, 1d / 60d);

        Assert.Equal(CharacterPresentationUpdateKind.RollStart, view.Last.Kind);
        Assert.Equal(3, view.AnimationOperations);
    }

    [Fact]
    public void DefaultsUnknownContextsAndInvalidValuesFailClosed()
    {
        var view = new RecordingView();
        var controller = Controller(view);
        Assert.False(default(CharacterPresentationSample).IsValid);
        Assert.False(default(CharacterPresentationUpdate).IsValid);
        Assert.Equal(CharacterPresentationDecision.Unspecified,
            default(CharacterPresentationDecision));
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.Present(
            default,
            CharacterPresentationPass.CommittedFrame,
            1d / 60d));
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.Present(
            Sample(),
            CharacterPresentationPass.Unspecified,
            1d / 60d));
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.Present(
            Sample(),
            CharacterPresentationPass.CommittedFrame,
            0d));
        Assert.Throws<ArgumentOutOfRangeException>(() => Sample(right: float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Sample(
            mode: LocomotionMode.Rolling,
            rollDuration: SimulationDuration.Zero));
        Assert.Equal(0, view.ApplyCalls);
    }

    public static TheoryData<CharacterPresentationSample, CharacterPresentationUpdateKind, int>
        CommittedUpdateCases => new()
        {
            { Sample(eliminated: true), CharacterPresentationUpdateKind.HoldEliminated, 0 },
            { Sample(activeAction: true), CharacterPresentationUpdateKind.HoldActiveAction, 0 },
            {
                Sample(
                    mode: LocomotionMode.Rolling,
                    rollDuration: new SimulationDuration(30)),
                CharacterPresentationUpdateKind.RollStart,
                1
            },
            {
                Sample(posture: PostureMode.Crouched, speed: 0.1f),
                CharacterPresentationUpdateKind.CrouchIdle,
                1
            },
            {
                Sample(posture: PostureMode.Crouched, speed: 0.1001f),
                CharacterPresentationUpdateKind.CrouchMove,
                1
            },
            { Sample(), CharacterPresentationUpdateKind.Locomotion, 1 },
        };

    private static CharacterPresentationController Controller(RecordingView view) =>
        new(view, new SimulationRate(60));

    private static CharacterPresentationSample Sample(
        LocomotionMode mode = LocomotionMode.Grounded,
        PostureMode posture = PostureMode.Standing,
        float right = 0f,
        float forward = 0f,
        float speed = 0f,
        SimulationDuration rollDuration = default,
        bool eliminated = false,
        bool activeAction = false) => new(
            new SimulationInstant(10),
            mode,
            posture,
            right,
            forward,
            speed,
            rollDuration,
            eliminated,
            activeAction);

    private sealed class RecordingView : ICharacterPresentationView
    {
        public int ApplyCalls { get; private set; }
        public int AnimationOperations { get; private set; }
        public int AudioOperations { get; private set; }
        public int VfxOperations { get; private set; }
        public CharacterPresentationUpdate Last { get; private set; }
        public List<CharacterPresentationUpdate> Updates { get; } = [];

        public void Apply(in CharacterPresentationUpdate update)
        {
            ApplyCalls++;
            Last = update;
            Updates.Add(update);
            if (update.Kind is
                CharacterPresentationUpdateKind.Locomotion or
                CharacterPresentationUpdateKind.CrouchIdle or
                CharacterPresentationUpdateKind.CrouchMove or
                CharacterPresentationUpdateKind.RollStart)
            {
                AnimationOperations++;
            }
        }
    }
}
