using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Core.Movement.Simulation;
using BattleArena.Multiplayer.OwnerPrediction;

namespace BattleArena.Multiplayer.Tests.OwnerPrediction;

/// <summary>
/// P06-01: the bounded record of frames the owner simulated.
/// </summary>
/// <remarks>
/// Retention is what defines the failure mode rather than only memory: an
/// authority answer older than the window cannot be replayed from and becomes a
/// hard rebase, which the player sees. So the eviction and lookup boundaries below
/// are gameplay behaviour, not bookkeeping.
/// </remarks>
public sealed class OwnerPredictionHistoryTests
{
    private static readonly CombatantAuthorityPredictionEpoch Epoch = new(
        10,
        new MatchFrameEpochId(1),
        new CombatantId(4),
        new LifeGenerationId(1),
        new AuthorityDiscontinuityId(1),
        new OwnerControlEpoch(3));

    [Fact]
    public void FramesAreRetainedAndFoundByNumber()
    {
        var history = new OwnerPredictionHistory(Epoch, capacityFrames: 8);

        for (var tick = 100L; tick < 104L; tick++)
        {
            Assert.Equal(OwnerHistoryInsertDecision.Inserted, history.TryInsert(Frame(tick)));
        }

        Assert.Equal(4, history.Count);
        Assert.Equal(100L, history.OldestFrame!.Value.Tick);
        Assert.Equal(103L, history.NewestFrame!.Value.Tick);

        Assert.Equal(
            OwnerHistoryLookupDecision.Found,
            history.TryGet(new SimulationInstant(102), out var found));
        Assert.Equal(102L, found.Frame.Tick);
    }

    [Fact]
    public void AGapIsRefusedRatherThanAccepted()
    {
        // A gap is indistinguishable later from a frame that simulated differently,
        // and replay across one would produce a state no simulation ever generated.
        var history = new OwnerPredictionHistory(Epoch, capacityFrames: 8);
        Assert.Equal(OwnerHistoryInsertDecision.Inserted, history.TryInsert(Frame(100)));

        Assert.Equal(OwnerHistoryInsertDecision.NonContiguous, history.TryInsert(Frame(102)));
        Assert.Equal(1, history.Count);
    }

    [Fact]
    public void ReinsertingARetainedFrameIsRefused()
    {
        // Replacing a replayed frame must go through TryReplacePostState, which
        // keeps the command and transitions the first run consumed. Re-inserting
        // would let a caller quietly substitute different inputs.
        var history = new OwnerPredictionHistory(Epoch, capacityFrames: 8);
        history.TryInsert(Frame(100));
        history.TryInsert(Frame(101));

        Assert.Equal(OwnerHistoryInsertDecision.NonContiguous, history.TryInsert(Frame(101)));
    }

    [Fact]
    public void TheWindowSlidesAndTheEvictedFrameBecomesUnreplayable()
    {
        var history = new OwnerPredictionHistory(Epoch, capacityFrames: 4);
        for (var tick = 100L; tick < 106L; tick++)
        {
            Assert.Equal(OwnerHistoryInsertDecision.Inserted, history.TryInsert(Frame(tick)));
        }

        Assert.Equal(4, history.Count);
        Assert.Equal(102L, history.OldestFrame!.Value.Tick);
        Assert.Equal(105L, history.NewestFrame!.Value.Tick);

        // This is the hard-rebase trigger, so it must be reported distinctly rather
        // than as "not found".
        Assert.Equal(
            OwnerHistoryLookupDecision.OlderThanRetention,
            history.TryGet(new SimulationInstant(101), out _));
    }

    [Fact]
    public void LookupDistinguishesTooOldFromNotYetSimulatedFromEmpty()
    {
        // Three different situations wanting three different responses: rebase,
        // queue the answer, and nothing to compare against.
        var history = new OwnerPredictionHistory(Epoch, capacityFrames: 4);
        Assert.Equal(
            OwnerHistoryLookupDecision.Empty,
            history.TryGet(new SimulationInstant(100), out _));

        history.TryInsert(Frame(100));
        Assert.Equal(
            OwnerHistoryLookupDecision.NotYetSimulated,
            history.TryGet(new SimulationInstant(101), out _));
    }

    [Fact]
    public void ReplacingAPostStateKeepsTheCommandAndTransitionsTheFirstRunConsumed()
    {
        var history = new OwnerPredictionHistory(Epoch, capacityFrames: 8);
        history.TryInsert(Frame(100, forwardInput: true));

        var corrected = State(100, x: 9.5d);
        Assert.True(history.TryReplacePostState(
            new SimulationInstant(100), corrected, new CanonicalMovementStateHash(1, 77UL)));

        Assert.Equal(
            OwnerHistoryLookupDecision.Found,
            history.TryGet(new SimulationInstant(100), out var stored));
        Assert.Equal(9.5d, stored.PostState.Kinematic.Position.X);
        Assert.Equal(77UL, stored.CanonicalHash.Value);

        // The inputs must survive, or replay would feed the frame something the
        // first run never consumed.
        Assert.Equal(1, stored.AppliedTransitions.Count);
        Assert.Equal(MovementTransitionKindTag.JumpPressed, stored.AppliedTransitions[0]);
        Assert.Equal(100L, stored.Command.TargetFrame.Tick);
    }

    [Fact]
    public void ReplacingAFrameThatIsNotRetainedFails()
    {
        var history = new OwnerPredictionHistory(Epoch, capacityFrames: 8);
        history.TryInsert(Frame(100));

        Assert.False(history.TryReplacePostState(
            new SimulationInstant(400), State(400), default));
    }

    [Fact]
    public void PruningDropsConfirmedFramesAndKeepsTheRest()
    {
        var history = new OwnerPredictionHistory(Epoch, capacityFrames: 8);
        for (var tick = 100L; tick < 106L; tick++)
        {
            history.TryInsert(Frame(tick));
        }

        Assert.Equal(3, history.PruneThrough(new SimulationInstant(102)));
        Assert.Equal(3, history.Count);
        Assert.Equal(103L, history.OldestFrame!.Value.Tick);
        Assert.Equal(
            OwnerHistoryLookupDecision.OlderThanRetention,
            history.TryGet(new SimulationInstant(102), out _));
    }

    [Fact]
    public void TruncationDiscardsTheFutureForARebase()
    {
        var history = new OwnerPredictionHistory(Epoch, capacityFrames: 8);
        for (var tick = 100L; tick < 106L; tick++)
        {
            history.TryInsert(Frame(tick));
        }

        Assert.Equal(3, history.TruncateAfter(new SimulationInstant(102)));
        Assert.Equal(102L, history.NewestFrame!.Value.Tick);

        // And history stays insertable from the new head, so a rebase can simulate
        // forward again without a gap.
        Assert.Equal(OwnerHistoryInsertDecision.Inserted, history.TryInsert(Frame(103)));
    }

    [Fact]
    public void ResetClearsEverythingAndRebinds()
    {
        var history = new OwnerPredictionHistory(Epoch, capacityFrames: 4);
        for (var tick = 100L; tick < 104L; tick++)
        {
            history.TryInsert(Frame(tick));
        }

        var next = new CombatantAuthorityPredictionEpoch(
            11,
            new MatchFrameEpochId(2),
            new CombatantId(4),
            new LifeGenerationId(2),
            new AuthorityDiscontinuityId(1),
            new OwnerControlEpoch(3));
        history.Reset(next);

        Assert.Equal(0, history.Count);
        Assert.Null(history.OldestFrame);
        Assert.Equal(next, history.Epoch);

        // A stale frame must not be resurrectable by a later insert landing on the
        // same ring slot.
        Assert.Equal(
            OwnerHistoryLookupDecision.Empty,
            history.TryGet(new SimulationInstant(101), out _));
    }

    [Fact]
    public void AFrameMustBeCoherentAboutWhichFrameItIs()
    {
        // The guard whose absence produces a one-frame phase error, which reads as a
        // divergence on every frame and corrects the player continuously. P5B-02
        // measured the two motors exactly one frame out of phase, so this is live.
        Assert.Throws<ArgumentException>(() => new OwnerPredictedFrame(
            new SimulationInstant(100),
            State(99),
            State(101),
            Command(100),
            default,
            default,
            CapsuleMotionOutcome.Completed,
            0,
            false,
            default));

        Assert.Throws<ArgumentException>(() => new OwnerPredictedFrame(
            new SimulationInstant(100),
            State(98),
            State(100),
            Command(100),
            default,
            default,
            CapsuleMotionOutcome.Completed,
            0,
            false,
            default));

        Assert.Throws<ArgumentException>(() => new OwnerPredictedFrame(
            new SimulationInstant(100),
            State(99),
            State(100),
            Command(101),
            default,
            default,
            CapsuleMotionOutcome.Completed,
            0,
            false,
            default));
    }

    private static OwnerPredictedFrame Frame(long tick, bool forwardInput = false)
    {
        var transitions = default(AppliedTransitionBuffer);
        if (forwardInput)
        {
            transitions.TryAdd(MovementTransitionKindTag.JumpPressed);
        }

        return new OwnerPredictedFrame(
            new SimulationInstant(tick),
            State(tick - 1),
            State(tick),
            Command(tick),
            transitions,
            default,
            CapsuleMotionOutcome.Completed,
            0,
            false,
            default);
    }

    private static CharacterSimulationState State(long tick, double x = 0d) =>
        CharacterSimulationState.CreateGrounded(
            new WorldPosition(x, 0d, 0d),
            0d,
            new SimulationInstant(tick),
            new MovementConfigurationRevision(1),
            new MovementCapabilityRevision(1));

    private static OwnerSimulationCommand Command(long tick) => new(
        new OwnerInputIdentity(OwnerIntentScope.From(Epoch), new InputSequence(checked((uint)tick))),
        new AuthorityDiscontinuityId(1),
        new MatchFrameEpochId(1),
        new SimulationInstant(tick),
        Input());

    private static CharacterSimulationInput Input() => new(
        MovementAxes.FromUnitVector(new HorizontalVector(0d, -1d)),
        ViewOrientation.FromRadians(0d, 0d),
        new MovementHeldState(MovementHeldButtons.None),
        default,
        default,
        default,
        new MovementConfigurationRevision(1),
        new MovementCapabilityRevision(1));
}
