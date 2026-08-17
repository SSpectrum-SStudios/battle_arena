using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Core.Movement.Simulation;
using BattleArena.Multiplayer.OwnerPrediction;

namespace BattleArena.Multiplayer.Tests.OwnerPrediction;

/// <summary>
/// P06-03 and P06-04: what makes the difference between a player being left alone
/// and a player being snapped.
/// </summary>
public sealed class OwnerReconciliationComparerTests
{
    private static readonly CombatantAuthorityPredictionEpoch Epoch = new(
        10,
        new MatchFrameEpochId(1),
        new CombatantId(4),
        new LifeGenerationId(1),
        new AuthorityDiscontinuityId(1),
        new OwnerControlEpoch(3));

    private static readonly OwnerReconciliationComparer Comparer =
        new(OwnerReconciliationTolerances.Default);

    private static readonly OwnerReconciliationPolicy Policy =
        new(OwnerReconciliationTolerances.Default);

    [Fact]
    public void AnIdenticalFrameProducesNoDifferenceAndIsConfirmed()
    {
        // The property that matters most: a correctly predicted frame compared
        // against its own authority answer must produce NO difference. If this ever
        // fails, every frame is a correction and the cause looks like a tolerance
        // problem rather than what it is.
        var state = State();

        var difference = Comparer.Compare(state, state, Epoch, null);

        Assert.Equal(OwnerStateDifferenceKind.None, difference.Kind);
        Assert.False(difference.RequiresAttention);
        Assert.Equal(
            OwnerCorrectionDisposition.Confirmed,
            Policy.Decide(difference, CapsuleMotionOutcome.Completed, state.Frame).Disposition);
    }

    [Fact]
    public void ComparingDifferentFramesFaultsRatherThanReportingADifference()
    {
        // The phase guard. A difference would be acted on; a fault will not be. A
        // one-frame phase error reads as a divergence on every frame and corrects
        // the player continuously, and P5B-02 measured the two motors exactly one
        // frame out of phase.
        Assert.Throws<ArgumentException>(
            () => Comparer.Compare(State(tick: 100), State(tick: 101), Epoch, null));
    }

    [Fact]
    public void DriftInsideToleranceIsNotACorrection()
    {
        var difference = Comparer.Compare(State(), State(x: 0.02d), Epoch, null);

        Assert.Equal(OwnerStateDifferenceKind.WithinTolerance, difference.Kind);
        Assert.False(difference.RequiresAttention);
    }

    [Fact]
    public void DriftBeyondToleranceNamesTheFieldAndReplays()
    {
        var difference = Comparer.Compare(State(), State(x: 0.5d), Epoch, null);

        Assert.Equal(OwnerStateDifferenceKind.NumericBeyondTolerance, difference.Kind);
        Assert.Equal(OwnerMismatchField.HorizontalPosition, difference.Field);

        var decision = Policy.Decide(difference, CapsuleMotionOutcome.Completed, State().Frame);
        Assert.Equal(OwnerCorrectionDisposition.OrdinaryReplay, decision.Disposition);
        Assert.True(decision.RequiresReplay);
    }

    [Fact]
    public void DiscreteFactsAreReportedBeforeNumericOnes()
    {
        // A character standing on a different surface is a bigger statement than one
        // standing a centimetre away, and reporting the centimetre would hide it.
        var predicted = State();
        var authoritative = Airborne(State(x: 0.5d));

        var difference = Comparer.Compare(predicted, authoritative, Epoch, null);

        Assert.Equal(OwnerStateDifferenceKind.DiscreteMismatch, difference.Kind);
        Assert.Equal(OwnerMismatchField.Grounded, difference.Field);
        Assert.True(difference.IsGroundingDivergence);
    }

    [Fact]
    public void ASupportChangeReplaysAsContactDivergence()
    {
        // So a correction storm on a staircase names grounding rather than position,
        // and whoever sees one goes looking at the motor. That is exactly the shape
        // P5B's step defect had.
        var predicted = State();
        var authoritative = State().WithKinematic(
            State().Kinematic.WithGround(true, SurfaceNormal.Up, new SupportIdentity(99, 0)));

        var difference = Comparer.Compare(predicted, authoritative, Epoch, null);
        Assert.Equal(OwnerMismatchField.Support, difference.Field);
        Assert.True(difference.IsContactDivergence);

        var decision = Policy.Decide(difference, CapsuleMotionOutcome.Completed, State().Frame);
        Assert.Equal(OwnerCorrectionDisposition.ContactReplay, decision.Disposition);
        Assert.Equal(OwnerCorrectionReason.ContactDivergence, decision.Reason);
    }

    [Fact]
    public void SupportIsNotComparedWhileAirborne()
    {
        // An airborne character's support is meaningless, and comparing it would
        // manufacture a mismatch out of two different flavours of nothing.
        var predicted = Airborne(State());
        var authoritative = Airborne(State());

        Assert.Equal(
            OwnerStateDifferenceKind.None,
            Comparer.Compare(predicted, authoritative, Epoch, null).Kind);
    }

    [Fact]
    public void ASeamBetweenCollidersIsTheSameSurface()
    {
        // Two authored colliders meeting at a seam report slightly different normals
        // for one continuous floor. Treating that as a support change would correct
        // the player for walking across a join.
        Assert.True(Comparer.IsSameSurface(SurfaceNormal.Up, new SurfaceNormal(0.02d, 1d, 0d)));
        Assert.False(Comparer.IsSameSurface(SurfaceNormal.Up, new SurfaceNormal(1d, 0d, 0d)));
    }

    [Fact]
    public void AHashMismatchAloneNeverCorrects()
    {
        // Every compared field agreed, so the hash covering something the comparer
        // does not is a bug to investigate rather than a player to snap.
        var state = State();
        var wrongHash = new CanonicalMovementStateHash(
            CanonicalMovementStateHash.SchemaVersion, 12345UL);

        var difference = Comparer.Compare(state, state, Epoch, wrongHash);

        Assert.Equal(OwnerStateDifferenceKind.DiagnosticHashOnly, difference.Kind);
        Assert.False(difference.RequiresAttention);
        Assert.Equal(
            OwnerCorrectionDisposition.Confirmed,
            Policy.Decide(difference, CapsuleMotionOutcome.Completed, state.Frame).Disposition);
    }

    [Fact]
    public void AMatchingHashDoesNotDisturbAgreement()
    {
        var state = State();
        var hash = CanonicalMovementStateHash.Compute(state, Epoch);

        Assert.Equal(
            OwnerStateDifferenceKind.None,
            Comparer.Compare(state, state, Epoch, hash).Kind);
    }

    [Fact]
    public void AnUnrecoverablePenetrationRebasesHoweverSmallTheDifference()
    {
        // Resimulating reproduces the same stuck state, so replay would burn the
        // budget and change nothing.
        var difference = Comparer.Compare(State(), State(x: 0.001d), Epoch, null);

        var decision = Policy.Decide(
            difference, CapsuleMotionOutcome.UnrecoverablePenetration, State().Frame);

        Assert.Equal(OwnerCorrectionDisposition.HardRebase, decision.Disposition);
        Assert.Equal(OwnerCorrectionReason.UnrecoverablePenetration, decision.Reason);
    }

    [Fact]
    public void AnExtremeDivergenceSnapsRatherThanReplays()
    {
        // Past this, replaying looks worse than snapping because the character
        // visibly retraces a path it never took.
        var difference = Comparer.Compare(State(), State(x: 25d), Epoch, null);

        var decision = Policy.Decide(difference, CapsuleMotionOutcome.Completed, State().Frame);
        Assert.Equal(OwnerCorrectionDisposition.HardRebase, decision.Disposition);
        Assert.Equal(OwnerCorrectionReason.ExtremeError, decision.Reason);
    }

    [Fact]
    public void AFrameTheOwnerHasNotSimulatedIsQueuedRatherThanCorrected()
    {
        // Rebasing on it would snap the player backwards for being ahead of nothing.
        Assert.Throws<ArgumentOutOfRangeException>(() => Policy.DecideWithoutHistory(
            OwnerHistoryLookupDecision.NotYetSimulated, State().Frame));

        Assert.Equal(
            OwnerCorrectionReason.HistoryMiss,
            Policy.DecideWithoutHistory(
                OwnerHistoryLookupDecision.OlderThanRetention, State().Frame).Reason);
    }

    [Theory]
    [InlineData(OwnerCorrectionReason.MatchFrameEpochChanged)]
    [InlineData(OwnerCorrectionReason.LifeEpochChanged)]
    [InlineData(OwnerCorrectionReason.AuthorityDiscontinuityChanged)]
    [InlineData(OwnerCorrectionReason.OwnerControlEpochChanged)]
    public void TheFourLifecycleCausesStayDistinct(OwnerCorrectionReason reason)
    {
        // Collapsing them makes a respawn indistinguishable from a control handover
        // in a trace, and those have completely different explanations.
        var decision = Policy.DecideForEpochChange(reason, State().Frame);

        Assert.Equal(reason, decision.Reason);
        Assert.True(decision.RequiresRebase);
    }

    [Fact]
    public void ANonLifecycleReasonIsRefusedByTheEpochEntryPoint() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Policy.DecideForEpochChange(
            OwnerCorrectionReason.ExtremeError, State().Frame));

    private static CharacterSimulationState State(double x = 0d, long tick = 100) =>
        CharacterSimulationState.CreateGrounded(
            new WorldPosition(x, 0d, 0d),
            0d,
            new SimulationInstant(tick),
            new MovementConfigurationRevision(1),
            new MovementCapabilityRevision(1));

    private static CharacterSimulationState Airborne(CharacterSimulationState state) =>
        state.WithKinematic(
            state.Kinematic.WithGround(false, SurfaceNormal.Up, SupportIdentity.None));
}
