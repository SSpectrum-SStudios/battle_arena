using BattleArena.Core.Common;
using BattleArena.Core.Movement.Simulation;
using BattleArena.Multiplayer.OwnerPrediction;

namespace BattleArena.Multiplayer.Tests.OwnerPrediction;

public sealed class PredictedCueLedgerTests
{
    [Theory]
    [InlineData(PredictedCueKind.Jump, PredictedCueOriginKind.MovementTransition)]
    [InlineData(PredictedCueKind.Roll, PredictedCueOriginKind.MovementTransition)]
    [InlineData(PredictedCueKind.Swing, PredictedCueOriginKind.PredictedAction)]
    [InlineData(PredictedCueKind.Impact, PredictedCueOriginKind.AuthoritySimulationEvent)]
    public void StableIdentityEmitsOnlyOnFirstPrediction(
        PredictedCueKind kind,
        PredictedCueOriginKind origin)
    {
        var ledger = Ledger();
        var identity = Cue(Epoch(), kind, origin, originId: 9, ordinal: 2);

        var first = ledger.ObservePredicted(identity, new SimulationInstant(10));
        var replay = ledger.ObservePredicted(identity, new SimulationInstant(10));

        Assert.Equal(PredictedCueLedgerDisposition.EmitPredicted, first.Disposition);
        Assert.True(first.ShouldEmit);
        Assert.Equal(PredictedCueLedgerDisposition.SuppressAlreadyObserved, replay.Disposition);
        Assert.False(replay.ShouldEmit);
        Assert.Equal(1, ledger.Count);
    }

    [Fact]
    public void ConfirmationOfPredictedCueDoesNotRestartPresentation()
    {
        var ledger = Ledger();
        var identity = Cue(Epoch(), PredictedCueKind.Swing, PredictedCueOriginKind.PredictedAction);
        Assert.True(ledger.ObservePredicted(identity, new SimulationInstant(10)).ShouldEmit);

        var confirmation = ledger.ObserveAuthorityConfirmation(
            identity,
            new SimulationInstant(12));
        var duplicate = ledger.ObserveAuthorityConfirmation(
            identity,
            new SimulationInstant(13));

        Assert.Equal(PredictedCueLedgerDisposition.ConfirmWithoutEmission, confirmation.Disposition);
        Assert.False(confirmation.ShouldEmit);
        Assert.Equal(PredictedCueLedgerDisposition.SuppressAlreadyObserved, duplicate.Disposition);
        Assert.True(ledger.TryGet(identity, out var entry));
        Assert.Equal(PredictedCueLedgerStatus.Confirmed, entry.Status);
        Assert.Equal(new SimulationInstant(10), entry.FirstRelevantFrame);
        Assert.Equal(new SimulationInstant(13), entry.LatestRelevantFrame);
    }

    [Fact]
    public void AuthorityOnlyCueEmitsOnce()
    {
        var ledger = Ledger();
        var impact = Cue(
            Epoch(),
            PredictedCueKind.Impact,
            PredictedCueOriginKind.AuthoritySimulationEvent);

        Assert.Equal(PredictedCueLedgerDisposition.EmitAuthority,
            ledger.ObserveAuthorityConfirmation(impact, new SimulationInstant(20)).Disposition);
        Assert.Equal(PredictedCueLedgerDisposition.SuppressAlreadyObserved,
            ledger.ObserveAuthorityConfirmation(impact, new SimulationInstant(20)).Disposition);
        Assert.Equal(1, ledger.Count);
    }

    [Fact]
    public void RejectionInvokesAuthoredRepairExactlyOnce()
    {
        var ledger = Ledger();
        var roll = Cue(Epoch(), PredictedCueKind.Roll, PredictedCueOriginKind.MovementTransition);
        ledger.ObservePredicted(roll, new SimulationInstant(30));

        var rejected = ledger.ObserveAuthorityRejection(
            roll,
            new SimulationInstant(31),
            PredictedCueRepairKind.FadeOut);
        var duplicate = ledger.ObserveAuthorityRejection(
            roll,
            new SimulationInstant(32),
            PredictedCueRepairKind.FadeOut);

        Assert.Equal(PredictedCueLedgerDisposition.InvokeRepair, rejected.Disposition);
        Assert.True(rejected.ShouldRepair);
        Assert.Equal(PredictedCueRepairKind.FadeOut, rejected.Repair);
        Assert.Equal(PredictedCueLedgerDisposition.SuppressAlreadyObserved, duplicate.Disposition);
        Assert.False(duplicate.ShouldRepair);
        Assert.True(ledger.TryGet(roll, out var entry));
        Assert.Equal(PredictedCueLedgerStatus.Rejected, entry.Status);
        Assert.Equal(PredictedCueRepairKind.FadeOut, entry.Repair);
        Assert.Throws<InvalidOperationException>(() =>
            ledger.ObserveAuthorityConfirmation(roll, new SimulationInstant(33)));
    }

    [Fact]
    public void ConflictingRejectionRepairsFailClosedForPredictedAndTombstoneCues()
    {
        var ledger = Ledger();
        var predicted = Cue(
            Epoch(),
            PredictedCueKind.Roll,
            PredictedCueOriginKind.MovementTransition);
        ledger.ObservePredicted(predicted, new SimulationInstant(10));
        ledger.ObserveAuthorityRejection(
            predicted,
            new SimulationInstant(11),
            PredictedCueRepairKind.FadeOut);
        Assert.Throws<InvalidOperationException>(() => ledger.ObserveAuthorityRejection(
            predicted,
            new SimulationInstant(12),
            PredictedCueRepairKind.CancelImmediately));

        var tombstone = Cue(
            Epoch(),
            PredictedCueKind.Jump,
            PredictedCueOriginKind.MovementTransition,
            originId: 2);
        ledger.ObserveAuthorityRejection(
            tombstone,
            new SimulationInstant(21),
            PredictedCueRepairKind.SeekAuthorityState);
        Assert.Throws<InvalidOperationException>(() => ledger.ObserveAuthorityRejection(
            tombstone,
            new SimulationInstant(22),
            PredictedCueRepairKind.CancelImmediately));
    }

    [Fact]
    public void RejectionArrivingBeforePredictionCreatesTombstoneWithoutRepair()
    {
        var ledger = Ledger();
        var jump = Cue(Epoch(), PredictedCueKind.Jump, PredictedCueOriginKind.MovementTransition);

        var rejection = ledger.ObserveAuthorityRejection(
            jump,
            new SimulationInstant(8),
            PredictedCueRepairKind.CancelImmediately);
        var latePrediction = ledger.ObservePredicted(jump, new SimulationInstant(7));

        Assert.Equal(PredictedCueLedgerDisposition.RejectionRecordedWithoutCue, rejection.Disposition);
        Assert.False(rejection.ShouldRepair);
        Assert.Equal(PredictedCueLedgerDisposition.SuppressAlreadyObserved, latePrediction.Disposition);
    }

    [Fact]
    public void NewLifeClearsOldLedgerAndCanReuseOriginIds()
    {
        var oldEpoch = Epoch();
        var ledger = Ledger(oldEpoch);
        var oldCue = Cue(oldEpoch, PredictedCueKind.Jump, PredictedCueOriginKind.MovementTransition);
        Assert.True(ledger.ObservePredicted(oldCue, new SimulationInstant(5)).ShouldEmit);
        ledger.RetireThrough(new SimulationInstant(20), new SimulationInstant(20));

        var newEpoch = Epoch(life: 2, discontinuity: 2, control: 2);
        Assert.True(ledger.ResetForAuthorityEpoch(newEpoch));
        Assert.Equal(0, ledger.Count);
        Assert.Equal(newEpoch, ledger.CurrentEpoch);
        Assert.Equal(PredictedCueLedgerDisposition.RejectEpochMismatch,
            ledger.ObservePredicted(oldCue, new SimulationInstant(5)).Disposition);

        var newCue = Cue(newEpoch, PredictedCueKind.Jump, PredictedCueOriginKind.MovementTransition);
        Assert.Equal(PredictedCueLedgerDisposition.EmitPredicted,
            ledger.ObservePredicted(newCue, new SimulationInstant(5)).Disposition);
        Assert.False(ledger.ResetForAuthorityEpoch(newEpoch));
        Assert.Equal(1, ledger.Count);
        Assert.Throws<InvalidOperationException>(() => ledger.ResetForAuthorityEpoch(oldEpoch));
    }

    [Fact]
    public void IdentitySeparatesKindOriginOrdinalAndAuthorityEpochButNotLocalRebase()
    {
        var epoch = Epoch();
        var identities = new HashSet<PredictedCueIdentity>
        {
            Cue(epoch, PredictedCueKind.Jump, PredictedCueOriginKind.MovementTransition, 1, 0),
            Cue(epoch, PredictedCueKind.Roll, PredictedCueOriginKind.MovementTransition, 1, 0),
            Cue(epoch, PredictedCueKind.Jump, PredictedCueOriginKind.PredictedAction, 1, 0),
            Cue(epoch, PredictedCueKind.Jump, PredictedCueOriginKind.MovementTransition, 2, 0),
            Cue(epoch, PredictedCueKind.Jump, PredictedCueOriginKind.MovementTransition, 1, 1),
        };

        Assert.Equal(5, identities.Count);
        Assert.DoesNotContain(
            typeof(LocalPredictionRebaseId),
            typeof(PredictedCueIdentity).GetProperties().Select(property => property.PropertyType));
    }

    [Fact]
    public void BoundedCapacityRequiresSafeHistoryPruning()
    {
        var ledger = Ledger(capacity: 2);
        var first = Cue(Epoch(), PredictedCueKind.Jump,
            PredictedCueOriginKind.MovementTransition, 1);
        var second = Cue(Epoch(), PredictedCueKind.Roll,
            PredictedCueOriginKind.MovementTransition, 2);
        var third = Cue(Epoch(), PredictedCueKind.Swing,
            PredictedCueOriginKind.PredictedAction, 3);
        ledger.ObservePredicted(first, new SimulationInstant(10));
        ledger.ObservePredicted(second, new SimulationInstant(20));

        Assert.Throws<InvalidOperationException>(() =>
            ledger.ObservePredicted(third, new SimulationInstant(30)));
        Assert.Equal(0, ledger.RetireThrough(
            new SimulationInstant(15),
            new SimulationInstant(15)));
        Assert.Equal(PredictedCueLedgerDisposition.ConfirmWithoutEmission,
            ledger.ObserveAuthorityConfirmation(first, new SimulationInstant(16)).Disposition);
        Assert.Equal(1, ledger.RetireThrough(
            new SimulationInstant(16),
            new SimulationInstant(16)));
        Assert.Equal(PredictedCueLedgerDisposition.EmitPredicted,
            ledger.ObservePredicted(third, new SimulationInstant(30)).Disposition);
        Assert.Equal(2, ledger.Count);
    }

    [Fact]
    public void RetirementNeverReemitsReplayConfirmationRejectionOrSnapshotDiscovery()
    {
        var ledger = Ledger();
        var confirmed = Cue(Epoch(), PredictedCueKind.Swing,
            PredictedCueOriginKind.PredictedAction, 1);
        ledger.ObservePredicted(confirmed, new SimulationInstant(10));

        Assert.Equal(0, ledger.RetireThrough(
            new SimulationInstant(20),
            new SimulationInstant(20)));
        Assert.Equal(PredictedCueLedgerDisposition.ConfirmWithoutEmission,
            ledger.ObserveAuthorityConfirmation(confirmed, new SimulationInstant(21)).Disposition);
        Assert.Equal(1, ledger.RetireThrough(
            new SimulationInstant(21),
            new SimulationInstant(21)));
        Assert.Equal(PredictedCueLedgerDisposition.SuppressRetired,
            ledger.ObservePredicted(confirmed, new SimulationInstant(21)).Disposition);
        Assert.Equal(PredictedCueLedgerDisposition.SuppressRetired,
            ledger.ObserveAuthorityConfirmation(confirmed, new SimulationInstant(21)).Disposition);

        var rejected = Cue(Epoch(), PredictedCueKind.Roll,
            PredictedCueOriginKind.MovementTransition, 2);
        ledger.ObservePredicted(rejected, new SimulationInstant(40));
        ledger.ObserveAuthorityRejection(
            rejected,
            new SimulationInstant(41),
            PredictedCueRepairKind.FadeOut);
        Assert.Equal(1, ledger.RetireThrough(
            new SimulationInstant(50),
            new SimulationInstant(50)));
        var lateRejection = ledger.ObserveAuthorityRejection(
            rejected,
            new SimulationInstant(41),
            PredictedCueRepairKind.FadeOut);
        Assert.Equal(PredictedCueLedgerDisposition.SuppressRetired, lateRejection.Disposition);
        Assert.False(lateRejection.ShouldRepair);

        var snapshotDiscovered = Cue(Epoch(), PredictedCueKind.Impact,
            PredictedCueOriginKind.AuthoritySimulationEvent, 3);
        Assert.Equal(PredictedCueLedgerDisposition.SuppressRetired,
            ledger.ObserveAuthorityConfirmation(
                snapshotDiscovered,
                new SimulationInstant(45)).Disposition);
    }

    [Fact]
    public void RemappedTransitionKeepsStableIdentityAndLaterFrameForRetirement()
    {
        var ledger = Ledger();
        var transition = Cue(
            Epoch(),
            PredictedCueKind.Roll,
            PredictedCueOriginKind.MovementTransition,
            originId: 79);

        Assert.Equal(PredictedCueLedgerDisposition.EmitPredicted,
            ledger.ObservePredicted(transition, new SimulationInstant(1204)).Disposition);
        Assert.Equal(PredictedCueLedgerDisposition.SuppressAlreadyObserved,
            ledger.ObservePredicted(transition, new SimulationInstant(1205)).Disposition);
        Assert.Equal(PredictedCueLedgerDisposition.ConfirmWithoutEmission,
            ledger.ObserveAuthorityConfirmation(
                transition,
                new SimulationInstant(1205)).Disposition);
        Assert.True(ledger.TryGet(transition, out var entry));
        Assert.Equal(new SimulationInstant(1204), entry.FirstRelevantFrame);
        Assert.Equal(new SimulationInstant(1205), entry.LatestRelevantFrame);

        Assert.Equal(0, ledger.RetireThrough(
            new SimulationInstant(1204),
            new SimulationInstant(1204)));
        Assert.Equal(1, ledger.RetireThrough(
            new SimulationInstant(1205),
            new SimulationInstant(1205)));
        Assert.Equal(PredictedCueLedgerDisposition.SuppressRetired,
            ledger.ObservePredicted(transition, new SimulationInstant(1205)).Disposition);
    }

    [Fact]
    public void RemappedActionConfirmationDoesNotRestartPresentation()
    {
        var ledger = Ledger();
        var action = Cue(
            Epoch(),
            PredictedCueKind.Swing,
            PredictedCueOriginKind.PredictedAction,
            originId: 88,
            ordinal: 1);

        Assert.True(ledger.ObservePredicted(action, new SimulationInstant(500)).ShouldEmit);
        var remapped = ledger.ObserveAuthorityConfirmation(
            action,
            new SimulationInstant(503));

        Assert.Equal(PredictedCueLedgerDisposition.ConfirmWithoutEmission, remapped.Disposition);
        Assert.False(remapped.ShouldEmit);
        Assert.True(ledger.TryGet(action, out var entry));
        Assert.Equal(new SimulationInstant(503), entry.LatestRelevantFrame);
    }

    [Fact]
    public void FixedStepCueOperationsAllocateNothingAfterWarmup()
    {
        const int count = 32;
        var identities = Enumerable.Range(0, count)
            .Select(index => Cue(
                Epoch(),
                PredictedCueKind.Footstep,
                PredictedCueOriginKind.MovementTransition,
                (ulong)index + 1))
            .ToArray();
        var warmLedger = Ledger(capacity: 64);
        ExerciseReplayFinalCueBatch(warmLedger, identities);

        var ledger = Ledger(capacity: 64);

        var before = GC.GetAllocatedBytesForCurrentThread();
        ExerciseReplayFinalCueBatch(ledger, identities);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.Equal(0, ledger.Count);
    }

    private static void ExerciseReplayFinalCueBatch(
        PredictedCueLedger ledger,
        PredictedCueIdentity[] identities)
    {
        for (var index = 0; index < identities.Length; index++)
        {
            var frame = new SimulationInstant(index + 10);
            ledger.ObservePredicted(identities[index], frame);
            ledger.ObservePredicted(identities[index], frame);
        }

        ledger.RetireThrough(new SimulationInstant(100), SimulationInstant.Zero);
    }

    [Fact]
    public void ReplayFinalCuesRetirePastCapacityWithoutAuthorityOutcomes()
    {
        const int capacity = 4;
        var ledger = Ledger(capacity: capacity);

        for (var index = 1; index <= capacity * 4; index++)
        {
            var frame = new SimulationInstant(index);
            var footstep = Cue(
                Epoch(),
                PredictedCueKind.Footstep,
                PredictedCueOriginKind.MovementTransition,
                (ulong)index);
            Assert.True(ledger.ObservePredicted(footstep, frame).ShouldEmit);
            Assert.Equal(1, ledger.RetireThrough(frame, SimulationInstant.Zero));
        }

        Assert.Equal(0, ledger.Count);
    }

    [Fact]
    public void ReplayFinalityDoesNotRetireUnresolvedAuthorityCue()
    {
        var ledger = Ledger(capacity: 2);
        var transition = Cue(Epoch(), PredictedCueKind.Roll,
            PredictedCueOriginKind.MovementTransition, 1);
        var footstep = Cue(Epoch(), PredictedCueKind.Footstep,
            PredictedCueOriginKind.MovementTransition, 2);
        ledger.ObservePredicted(transition, new SimulationInstant(10));
        ledger.ObservePredicted(footstep, new SimulationInstant(11));

        Assert.Equal(1, ledger.RetireThrough(
            new SimulationInstant(100),
            new SimulationInstant(100)));
        Assert.Equal(1, ledger.Count);
        Assert.True(ledger.TryGet(transition, out var unresolved));
        Assert.Equal(PredictedCueLedgerStatus.Predicted, unresolved.Status);
        Assert.Equal(PredictedCueFinalityPolicy.AuthorityResolved,
            unresolved.FinalityPolicy);

        ledger.ObserveAuthorityConfirmation(transition, new SimulationInstant(10));
        Assert.Equal(1, ledger.RetireThrough(
            new SimulationInstant(100),
            new SimulationInstant(100)));
        Assert.Equal(0, ledger.Count);
    }

    [Fact]
    public void FinalityPolicyIsDerivedFromImmutableCueKind()
    {
        var ledger = Ledger();
        var authorityCue = Cue(Epoch(), PredictedCueKind.Roll,
            PredictedCueOriginKind.MovementTransition, 1);
        Assert.Equal(PredictedCueFinalityPolicy.AuthorityResolved,
            authorityCue.FinalityPolicy);

        var replayCue = Cue(Epoch(), PredictedCueKind.Footstep,
            PredictedCueOriginKind.MovementTransition, 2);
        Assert.Equal(PredictedCueFinalityPolicy.ReplayFinalOnly,
            replayCue.FinalityPolicy);
        Assert.DoesNotContain(
            typeof(PredictedCueFinalityPolicy),
            typeof(PredictedCueIdentity).GetConstructors()
                .SelectMany(constructor => constructor.GetParameters())
                .Select(parameter => parameter.ParameterType));
        Assert.DoesNotContain(
            typeof(PredictedCueFinalityPolicy),
            typeof(PredictedCueLedger).GetMethod(nameof(PredictedCueLedger.ObservePredicted))!
                .GetParameters()
                .Select(parameter => parameter.ParameterType));

        ledger.ObservePredicted(replayCue, new SimulationInstant(20));
        Assert.Throws<InvalidOperationException>(() =>
            ledger.ObserveAuthorityConfirmation(replayCue, new SimulationInstant(20)));
        Assert.Throws<InvalidOperationException>(() => ledger.ObserveAuthorityRejection(
            replayCue,
            new SimulationInstant(20),
            PredictedCueRepairKind.CancelImmediately));
    }

    [Fact]
    public void RetiredReplayOnlyIdentityCannotBeReclassifiedOrReemitted()
    {
        var ledger = Ledger();
        var footstep = Cue(
            Epoch(),
            PredictedCueKind.Footstep,
            PredictedCueOriginKind.MovementTransition,
            originId: 44);
        Assert.True(ledger.ObservePredicted(
            footstep,
            new SimulationInstant(10)).ShouldEmit);
        Assert.Equal(1, ledger.RetireThrough(
            new SimulationInstant(100),
            SimulationInstant.Zero));

        Assert.Equal(PredictedCueLedgerDisposition.SuppressRetired,
            ledger.ObservePredicted(footstep, new SimulationInstant(10)).Disposition);
        Assert.Equal(PredictedCueLedgerDisposition.SuppressRetired,
            ledger.ObserveAuthorityConfirmation(
                footstep,
                new SimulationInstant(10)).Disposition);
        var rejection = ledger.ObserveAuthorityRejection(
            footstep,
            new SimulationInstant(10),
            PredictedCueRepairKind.CancelImmediately);
        Assert.Equal(PredictedCueLedgerDisposition.SuppressRetired, rejection.Disposition);
        Assert.False(rejection.ShouldRepair);
    }

    [Fact]
    public void DefaultsUnknownEnumsAndInvalidCapacityFailClosed()
    {
        Assert.Equal(PredictedCueLedgerDisposition.Unspecified,
            default(PredictedCueLedgerDecision).Disposition);
        Assert.False(default(PredictedCueLedgerDecision).ShouldEmit);
        Assert.False(default(PredictedCueLedgerDecision).ShouldRepair);
        Assert.Throws<ArgumentOutOfRangeException>(() => Ledger(capacity: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PredictedCueIdentity(
            Epoch(),
            (PredictedCueKind)999,
            PredictedCueOriginKind.MovementTransition,
            1,
            0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Ledger().ObserveAuthorityRejection(
            Cue(Epoch(), PredictedCueKind.Jump, PredictedCueOriginKind.MovementTransition),
            new SimulationInstant(1),
            PredictedCueRepairKind.Unspecified));
        Assert.Throws<ArgumentException>(() => new PredictedCueLedgerDecision(
            PredictedCueLedgerDisposition.InvokeRepair,
            PredictedCueRepairKind.Unspecified));
        Assert.Throws<ArgumentException>(() => new PredictedCueLedgerDecision(
            PredictedCueLedgerDisposition.EmitPredicted,
            PredictedCueRepairKind.FadeOut));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PredictedCueLedgerDecision(
            (PredictedCueLedgerDisposition)999,
            PredictedCueRepairKind.Unspecified));
    }

    private static PredictedCueLedger Ledger(
        CombatantAuthorityPredictionEpoch? epoch = null,
        int capacity = PredictedCueLedger.DefaultCapacity) => new(epoch ?? Epoch(), capacity);

    private static PredictedCueIdentity Cue(
        CombatantAuthorityPredictionEpoch epoch,
        PredictedCueKind kind,
        PredictedCueOriginKind origin,
        ulong originId = 1,
        uint ordinal = 0) => new(
            epoch,
            kind,
            origin,
            originId,
            ordinal);

    private static CombatantAuthorityPredictionEpoch Epoch(
        long life = 1,
        ulong discontinuity = 1,
        ulong control = 1) => new(
            77,
            MatchFrameEpochId.Initial,
            new CombatantId(1),
            new LifeGenerationId(life),
            new AuthorityDiscontinuityId(discontinuity),
            new OwnerControlEpoch(control));
}
