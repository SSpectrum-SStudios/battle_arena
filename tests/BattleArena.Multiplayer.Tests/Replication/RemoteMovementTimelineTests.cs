using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Multiplayer.Connection;
using BattleArena.Multiplayer.Replication;

namespace BattleArena.Multiplayer.Tests.Replication;

public sealed class RemoteMovementTimelineTests
{
    [Fact]
    public void AuthorityFramesProduceInterpolationWindow()
    {
        var timeline = new RemoteMovementTimeline<string>();
        timeline.ObserveAuthority(new RemoteMovementFrame<string>(10, 1, 8, "before"));
        timeline.ObserveAuthority(new RemoteMovementFrame<string>(12, 1, 10, "after"));

        var sample = Assert.IsType<RemoteMovementSampleWindow<string>>(
            timeline.Sample(11, normalPredictionLimitTicks: 9, freezeAfterTicks: 15));

        Assert.Equal("before", sample.Before.State);
        Assert.Equal("after", sample.After.State);
        Assert.False(sample.RequiresPrediction);
    }

    [Fact]
    public void AcceptedCommandsBeyondLatestAuthorityFrameDrivePrediction()
    {
        var timeline = new RemoteMovementTimeline<string>();
        timeline.ObserveAuthority(new RemoteMovementFrame<string>(10, 1, 8, "baseline"));
        timeline.ObserveAccepted(Command(lifeId: 1, authorityTick: 11));
        timeline.ObserveAccepted(Command(lifeId: 1, authorityTick: 12));

        var sample = Assert.IsType<RemoteMovementSampleWindow<string>>(
            timeline.Sample(12, normalPredictionLimitTicks: 9, freezeAfterTicks: 15));

        Assert.True(sample.RequiresPrediction);
        Assert.Equal([11UL, 12UL], sample.PredictionCommands
            .Select(command => command.AppliedAuthorityTick));
    }

    [Fact]
    public void AuthorityObservationPrunesCommandsAlreadyRepresentedByState()
    {
        var timeline = new RemoteMovementTimeline<string>();
        timeline.ObserveAuthority(new RemoteMovementFrame<string>(10, 1, 8, "baseline"));
        timeline.ObserveAccepted(Command(1, 11));
        timeline.ObserveAccepted(Command(1, 12));
        timeline.ObserveAuthority(new RemoteMovementFrame<string>(11, 1, 9, "repair"));

        var sample = Assert.IsType<RemoteMovementSampleWindow<string>>(
            timeline.Sample(12, 9, 15));

        var command = Assert.Single(sample.PredictionCommands);
        Assert.Equal(12UL, command.AppliedAuthorityTick);
    }

    [Fact]
    public void PredictionIsBoundedAndEventuallyFrozen()
    {
        var timeline = new RemoteMovementTimeline<string>();
        timeline.ObserveAuthority(new RemoteMovementFrame<string>(100, 1, 80, "baseline"));

        var limited = Assert.IsType<RemoteMovementSampleWindow<string>>(
            timeline.Sample(112, normalPredictionLimitTicks: 9, freezeAfterTicks: 15));
        var frozen = Assert.IsType<RemoteMovementSampleWindow<string>>(
            timeline.Sample(120, normalPredictionLimitTicks: 9, freezeAfterTicks: 15));

        Assert.Equal(109d, limited.EffectiveTargetTick);
        Assert.True(limited.PredictionLimited);
        Assert.False(limited.Frozen);
        Assert.Equal(109d, frozen.EffectiveTargetTick);
        Assert.True(frozen.Frozen);
    }

    [Fact]
    public void NewLifeClearsOldFramesAndCommands()
    {
        var timeline = new RemoteMovementTimeline<string>();
        timeline.ObserveAuthority(new RemoteMovementFrame<string>(10, 1, 8, "old"));
        timeline.ObserveAccepted(Command(1, 11));
        timeline.ObserveAuthority(new RemoteMovementFrame<string>(20, 2, 0, "new"));

        var sample = Assert.IsType<RemoteMovementSampleWindow<string>>(
            timeline.Sample(20, 9, 15));

        Assert.Equal(2UL, timeline.CurrentLifeId);
        Assert.Equal("new", sample.Before.State);
        Assert.Empty(sample.PredictionCommands);
    }

    [Fact]
    public void DelayedCommandFromPreviousLifeCannotReplaceCurrentLife()
    {
        var timeline = new RemoteMovementTimeline<string>();
        timeline.ObserveAuthority(new RemoteMovementFrame<string>(20, 2, 0, "current"));

        timeline.ObserveAccepted(Command(lifeId: 1, authorityTick: 21));

        Assert.Equal(2UL, timeline.CurrentLifeId);
        Assert.Equal("current", timeline.LatestAuthorityFrame?.State);
    }

    [Fact]
    public void DelayedCommandFromPreviousConnectionGenerationIsRejected()
    {
        var timeline = new RemoteMovementTimeline<string>();
        timeline.ObserveAuthority(new RemoteMovementFrame<string>(20, 1, 10, "current"));
        timeline.ObserveAccepted(Command(1, 21, connectionGeneration: 2));
        timeline.ObserveAccepted(Command(1, 22, connectionGeneration: 1));

        var sample = Assert.IsType<RemoteMovementSampleWindow<string>>(
            timeline.Sample(22, 9, 15));

        var command = Assert.Single(sample.PredictionCommands);
        Assert.Equal(21UL, command.AppliedAuthorityTick);
        Assert.Equal(2U, command.ConnectionGeneration.Value);
    }

    [Fact]
    public void DirectCommandDrivesPredictionBeforeAuthorityRelayArrives()
    {
        var timeline = new RemoteMovementTimeline<string>();
        timeline.ObserveAuthority(new RemoteMovementFrame<string>(10, 1, 8, "baseline"));

        timeline.ObserveDirect(Direct(lifeId: 1, estimatedAuthorityTick: 11));

        var sample = Assert.IsType<RemoteMovementSampleWindow<string>>(
            timeline.Sample(11, 9, 15));
        var command = Assert.Single(sample.PredictionCommands);
        Assert.Equal(11UL, command.AppliedAuthorityTick);
        Assert.Equal(1, timeline.PendingDirectCommandCount);
    }

    [Fact]
    public void MatchingAcceptedCommandConfirmsAndReplacesDirectEvidence()
    {
        var timeline = new RemoteMovementTimeline<string>();
        timeline.ObserveAuthority(new RemoteMovementFrame<string>(10, 1, 8, "baseline"));
        timeline.ObserveDirect(Direct(1, 11));

        timeline.ObserveAccepted(Command(1, 11));

        var sample = Assert.IsType<RemoteMovementSampleWindow<string>>(
            timeline.Sample(11, 9, 15));
        Assert.Single(sample.PredictionCommands);
        Assert.Equal(1, timeline.DirectConfirmations);
        Assert.Equal(0, timeline.DirectMismatches);
        Assert.Equal(0, timeline.PendingDirectCommandCount);
    }

    [Fact]
    public void ConflictingAcceptedCommandWinsAndRecordsMismatch()
    {
        var timeline = new RemoteMovementTimeline<string>();
        timeline.ObserveAuthority(new RemoteMovementFrame<string>(10, 1, 8, "baseline"));
        timeline.ObserveDirect(Direct(1, 11));
        var original = Command(1, 11);
        var accepted = new AcceptedMovementCommand(
            original.SourcePeerId,
            original.ConnectionGeneration,
            original.CombatantId,
            original.LifeId,
            original.AppliedAuthorityTick,
            new MovementCommand(
                original.Command.Sequence,
                original.Command.ClientTick,
                new HorizontalVector(1, 0),
                original.Command.ViewYawRadians,
                original.Command.ViewPitchRadians,
                original.Command.HeldButtons,
                original.Command.PressedButtons,
                original.Command.ReleasedButtons));

        timeline.ObserveAccepted(accepted);

        var sample = Assert.IsType<RemoteMovementSampleWindow<string>>(
            timeline.Sample(11, 9, 15));
        Assert.Equal(new HorizontalVector(1, 0),
            Assert.Single(sample.PredictionCommands).Command.Movement);
        Assert.Equal(1, timeline.DirectMismatches);
        Assert.Equal(0, timeline.PendingDirectCommandCount);
    }

    [Fact]
    public void AuthorityStatePrunesDirectCommandsItHasProcessed()
    {
        var timeline = new RemoteMovementTimeline<string>();
        timeline.ObserveAuthority(new RemoteMovementFrame<string>(10, 1, 8, "baseline"));
        timeline.ObserveDirect(Direct(1, 11));

        timeline.ObserveAuthority(new RemoteMovementFrame<string>(11, 1, 11, "repair"));

        Assert.Equal(0, timeline.PendingDirectCommandCount);
        Assert.Empty(Assert.IsType<RemoteMovementSampleWindow<string>>(
            timeline.Sample(12, 9, 15)).PredictionCommands);
    }

    [Fact]
    public void NewerAcceptedSequenceSuppressesOlderLateTickDirectEvidence()
    {
        var timeline = new RemoteMovementTimeline<string>();
        timeline.ObserveAuthority(new RemoteMovementFrame<string>(10, 1, 8, "baseline"));
        timeline.ObserveDirect(Direct(1, estimatedAuthorityTick: 14));
        timeline.ObserveAccepted(Command(1, authorityTick: 11));
        var newer = Command(1, authorityTick: 12);
        newer = new AcceptedMovementCommand(
            newer.SourcePeerId,
            newer.ConnectionGeneration,
            newer.CombatantId,
            newer.LifeId,
            newer.AppliedAuthorityTick,
            new MovementCommand(
                sequence: 15,
                new SimulationInstant(12),
                new HorizontalVector(1, 0),
                0,
                0));

        timeline.ObserveAccepted(newer);
        timeline.ObserveDirect(Direct(1, estimatedAuthorityTick: 15));

        var sample = Assert.IsType<RemoteMovementSampleWindow<string>>(
            timeline.Sample(15, 9, 15));
        Assert.DoesNotContain(sample.PredictionCommands,
            value => value.Command.Sequence < 15 && value.AppliedAuthorityTick > 12);
        Assert.Equal(0, timeline.PendingDirectCommandCount);
    }

    [Fact]
    public void AuthorityInsertedAttackAndCompactedEdgesDoNotCountAsMovementMismatch()
    {
        var timeline = new RemoteMovementTimeline<string>();
        timeline.ObserveAuthority(new RemoteMovementFrame<string>(10, 1, 8, "baseline"));
        var direct = Direct(1, 11);
        timeline.ObserveDirect(direct);
        var source = direct.Command;
        var accepted = new AcceptedMovementCommand(
            direct.SourcePeerId,
            direct.ConnectionGeneration,
            direct.CombatantId,
            direct.LifeId,
            appliedAuthorityTick: 11,
            new MovementCommand(
                source.Sequence,
                source.ClientTick,
                source.Movement,
                source.ViewYawRadians,
                source.ViewPitchRadians,
                source.HeldButtons | MovementButtons.Attack,
                MovementButtons.Attack | MovementButtons.Jump,
                MovementButtons.CrouchOrRoll));

        timeline.ObserveAccepted(accepted);

        Assert.Equal(1, timeline.DirectConfirmations);
        Assert.Equal(0, timeline.DirectMismatches);
    }

    [Fact]
    public void DirectOnlyMovementPressCountsAsMismatch()
    {
        var timeline = new RemoteMovementTimeline<string>();
        timeline.ObserveAuthority(new RemoteMovementFrame<string>(10, 1, 8, "baseline"));
        var direct = Direct(1, 11);
        timeline.ObserveDirect(new DirectMovementCommand(
            direct.SourcePeerId,
            direct.ConnectionGeneration,
            direct.CombatantId,
            direct.LifeId,
            direct.BundleSequence,
            direct.EstimatedAuthorityTick,
            WithEdges(direct.Command, MovementButtons.Jump, MovementButtons.None)));

        timeline.ObserveAccepted(Command(1, 11));

        Assert.Equal(1, timeline.DirectMismatches);
        Assert.Equal(0, timeline.DirectConfirmations);
    }

    [Fact]
    public void DeferredReleaseOfSameFrameTapStillConfirms()
    {
        var timeline = new RemoteMovementTimeline<string>();
        timeline.ObserveAuthority(new RemoteMovementFrame<string>(10, 1, 8, "baseline"));
        var direct = Direct(1, 11);
        timeline.ObserveDirect(new DirectMovementCommand(
            direct.SourcePeerId,
            direct.ConnectionGeneration,
            direct.CombatantId,
            direct.LifeId,
            direct.BundleSequence,
            direct.EstimatedAuthorityTick,
            WithEdges(direct.Command, MovementButtons.Jump, MovementButtons.Jump)));
        var accepted = Command(1, 11);
        timeline.ObserveAccepted(new AcceptedMovementCommand(
            accepted.SourcePeerId,
            accepted.ConnectionGeneration,
            accepted.CombatantId,
            accepted.LifeId,
            accepted.AppliedAuthorityTick,
            WithEdges(accepted.Command, MovementButtons.Jump, MovementButtons.None)));

        Assert.Equal(1, timeline.DirectConfirmations);
        Assert.Equal(0, timeline.DirectMismatches);
    }

    [Fact]
    public void RepeatedMismatchesDisableFutureDirectPredictionEvidence()
    {
        var timeline = new RemoteMovementTimeline<string>();
        timeline.ObserveAuthority(new RemoteMovementFrame<string>(10, 1, 8, "baseline"));
        for (ulong tick = 11; tick <= 13; tick++)
        {
            timeline.ObserveDirect(Direct(1, tick));
            var accepted = Command(1, tick);
            timeline.ObserveAccepted(new AcceptedMovementCommand(
                accepted.SourcePeerId,
                accepted.ConnectionGeneration,
                accepted.CombatantId,
                accepted.LifeId,
                accepted.AppliedAuthorityTick,
                new MovementCommand(
                    accepted.Command.Sequence,
                    accepted.Command.ClientTick,
                    new HorizontalVector(1, 0),
                    accepted.Command.ViewYawRadians,
                    accepted.Command.ViewPitchRadians)));
        }

        timeline.ObserveDirect(Direct(1, 14));
        var sample = Assert.IsType<RemoteMovementSampleWindow<string>>(
            timeline.Sample(14, 9, 15));

        Assert.False(timeline.DirectStateHintsAllowed);
        Assert.DoesNotContain(sample.PredictionCommands, command =>
            command.Command.Sequence == 14);
    }

    private static AcceptedMovementCommand Command(
        ulong lifeId,
        ulong authorityTick,
        uint connectionGeneration = 1) => new(
        new SessionPeerId(2),
        new ConnectionGeneration(connectionGeneration),
        combatantId: 2,
        lifeId,
        authorityTick,
        new MovementCommand(
            authorityTick,
            new SimulationInstant(checked((long)authorityTick)),
            new HorizontalVector(0, -1),
            0,
            0));

    private static DirectMovementCommand Direct(
        ulong lifeId,
        ulong estimatedAuthorityTick,
        uint connectionGeneration = 1) => new(
        new SessionPeerId(2),
        new ConnectionGeneration(connectionGeneration),
        combatantId: 2,
        lifeId,
        bundleSequence: estimatedAuthorityTick,
        estimatedAuthorityTick,
        new MovementCommand(
            estimatedAuthorityTick,
            new SimulationInstant(checked((long)estimatedAuthorityTick)),
            new HorizontalVector(0, -1),
            0,
            0));

    private static MovementCommand WithEdges(
        MovementCommand command,
        MovementButtons pressed,
        MovementButtons released) => new(
        command.Sequence,
        command.ClientTick,
        command.Movement,
        command.ViewYawRadians,
        command.ViewPitchRadians,
        command.HeldButtons,
        pressed,
        released);
}
