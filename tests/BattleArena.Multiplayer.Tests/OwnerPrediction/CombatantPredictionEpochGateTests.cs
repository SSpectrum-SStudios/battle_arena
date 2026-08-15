using BattleArena.Core.Common;
using BattleArena.Core.Movement.Simulation;
using BattleArena.Multiplayer.OwnerPrediction;

namespace BattleArena.Multiplayer.Tests.OwnerPrediction;

public sealed class CombatantPredictionEpochGateTests
{
    [Fact]
    public void DefaultDecisionsAreUnspecifiedAndNeverActionable()
    {
        Assert.Equal(
            PredictionEpochEvidenceDecision.Unspecified,
            default(PredictionEpochEvidenceDecision));
        Assert.NotEqual(
            PredictionEpochEvidenceDecision.Accepted,
            default(PredictionEpochEvidenceDecision));
        Assert.Equal(
            PredictionEpochTransitionDecision.Unspecified,
            default(PredictionEpochTransitionDecision));
        Assert.NotEqual(
            PredictionEpochTransitionDecision.Applied,
            default(PredictionEpochTransitionDecision));
    }

    [Fact]
    public void ExactCurrentAuthorityEpochIsAccepted()
    {
        var authority = Epoch();
        var gate = Gate(authority);

        Assert.Equal(PredictionEpochEvidenceDecision.Accepted, gate.Evaluate(authority));
        Assert.Equal(authority, gate.Current.Authority);
        Assert.Equal(LocalPredictionRebaseId.Initial, gate.Current.LocalRebase);
    }

    [Fact]
    public void EveryAuthorityScopeMismatchHasOneExplicitDecision()
    {
        var current = Epoch();
        var gate = Gate(current);

        Assert.Equal(PredictionEpochEvidenceDecision.SessionMismatch,
            gate.Evaluate(Epoch(session: 2)));
        Assert.Equal(PredictionEpochEvidenceDecision.MatchFrameEpochMismatch,
            gate.Evaluate(Epoch(frameEpoch: 2)));
        Assert.Equal(PredictionEpochEvidenceDecision.CombatantMismatch,
            gate.Evaluate(Epoch(combatant: 2)));
        Assert.Equal(PredictionEpochEvidenceDecision.LifeMismatch,
            gate.Evaluate(Epoch(life: 2)));
        Assert.Equal(PredictionEpochEvidenceDecision.AuthorityDiscontinuityMismatch,
            gate.Evaluate(Epoch(discontinuity: 2)));
        Assert.Equal(PredictionEpochEvidenceDecision.OwnerControlMismatch,
            gate.Evaluate(Epoch(control: 2)));
        Assert.Equal(new CombatantLocalPredictionEpoch(current, LocalPredictionRebaseId.Initial), gate.Current);
    }

    [Fact]
    public void IsolatedStaleAndFutureLifeDiscontinuityAndControlEvidenceIsRejected()
    {
        var lifeCurrent = Epoch(life: 2, discontinuity: 2, control: 2);
        var lifeGate = Gate(lifeCurrent);
        Assert.Equal(PredictionEpochEvidenceDecision.LifeMismatch,
            lifeGate.Evaluate(Epoch(life: 1, discontinuity: 2, control: 2)));
        Assert.Equal(PredictionEpochEvidenceDecision.LifeMismatch,
            lifeGate.Evaluate(Epoch(life: 3, discontinuity: 2, control: 2)));
        Assert.Equal(lifeCurrent, lifeGate.Current.Authority);

        var discontinuityCurrent = Epoch(discontinuity: 2);
        var discontinuityGate = Gate(discontinuityCurrent);
        Assert.Equal(PredictionEpochEvidenceDecision.AuthorityDiscontinuityMismatch,
            discontinuityGate.Evaluate(Epoch(discontinuity: 1)));
        Assert.Equal(PredictionEpochEvidenceDecision.AuthorityDiscontinuityMismatch,
            discontinuityGate.Evaluate(Epoch(discontinuity: 3)));
        Assert.Equal(discontinuityCurrent, discontinuityGate.Current.Authority);

        var controlCurrent = Epoch(control: 2);
        var controlGate = Gate(controlCurrent);
        Assert.Equal(PredictionEpochEvidenceDecision.OwnerControlMismatch,
            controlGate.Evaluate(Epoch(control: 1)));
        Assert.Equal(PredictionEpochEvidenceDecision.OwnerControlMismatch,
            controlGate.Evaluate(Epoch(control: 3)));
        Assert.Equal(controlCurrent, controlGate.Current.Authority);
    }

    [Fact]
    public void DuplicateTransitionDoesNotResetLocalState()
    {
        var gate = Gate(Epoch());

        Assert.Equal(
            PredictionEpochTransitionDecision.Duplicate,
            gate.ApplyAuthorityEpoch(Epoch()));
        Assert.Equal(LocalPredictionRebaseId.Initial, gate.Current.LocalRebase);
    }

    [Fact]
    public void ReconnectAdvancesControlAndInvalidatesOldControlEvidence()
    {
        var old = Epoch();
        var gate = Gate(old);
        var reconnect = Epoch(control: 2);

        Assert.Equal(
            PredictionEpochTransitionDecision.Applied,
            gate.ApplyAuthorityEpoch(reconnect));
        Assert.Equal(reconnect, gate.Current.Authority);
        Assert.Equal(new LocalPredictionRebaseId(2), gate.Current.LocalRebase);
        Assert.Equal(
            PredictionEpochEvidenceDecision.OwnerControlMismatch,
            gate.Evaluate(old));
        Assert.Equal(PredictionEpochEvidenceDecision.Accepted, gate.Evaluate(reconnect));
    }

    [Fact]
    public void RespawnMustAdvanceLifeDiscontinuityAndControlTogether()
    {
        var gate = Gate(Epoch());

        Assert.Equal(
            PredictionEpochTransitionDecision.RejectedInvalidLifecycle,
            gate.ApplyAuthorityEpoch(Epoch(life: 2, control: 2)));
        Assert.Equal(
            PredictionEpochTransitionDecision.RejectedInvalidLifecycle,
            gate.ApplyAuthorityEpoch(Epoch(life: 2, discontinuity: 2)));
        Assert.Equal(LocalPredictionRebaseId.Initial, gate.Current.LocalRebase);

        var respawn = Epoch(life: 2, discontinuity: 2, control: 2);
        Assert.Equal(
            PredictionEpochTransitionDecision.Applied,
            gate.ApplyAuthorityEpoch(respawn));
        Assert.Equal(PredictionEpochEvidenceDecision.LifeMismatch, gate.Evaluate(Epoch()));
        Assert.Equal(PredictionEpochEvidenceDecision.Accepted, gate.Evaluate(respawn));
        Assert.Equal(new LocalPredictionRebaseId(2), gate.Current.LocalRebase);
        Assert.Equal(PredictionEpochTransitionDecision.Duplicate, gate.ApplyAuthorityEpoch(respawn));
        Assert.Equal(new LocalPredictionRebaseId(2), gate.Current.LocalRebase);
    }

    [Fact]
    public void TimelineResetRequiresNewControlWhileTeleportMayChangeOnlyDiscontinuity()
    {
        var timelineGate = Gate(Epoch());
        Assert.Equal(
            PredictionEpochTransitionDecision.RejectedInvalidLifecycle,
            timelineGate.ApplyAuthorityEpoch(Epoch(frameEpoch: 2)));
        Assert.Equal(LocalPredictionRebaseId.Initial, timelineGate.Current.LocalRebase);
        Assert.Equal(
            PredictionEpochTransitionDecision.Applied,
            timelineGate.ApplyAuthorityEpoch(Epoch(frameEpoch: 2, control: 2)));
        Assert.Equal(new LocalPredictionRebaseId(2), timelineGate.Current.LocalRebase);
        Assert.Equal(
            PredictionEpochTransitionDecision.Duplicate,
            timelineGate.ApplyAuthorityEpoch(Epoch(frameEpoch: 2, control: 2)));
        Assert.Equal(new LocalPredictionRebaseId(2), timelineGate.Current.LocalRebase);

        var teleportGate = Gate(Epoch());
        Assert.Equal(
            PredictionEpochTransitionDecision.Applied,
            teleportGate.ApplyAuthorityEpoch(Epoch(discontinuity: 2)));
        Assert.Equal(new LocalPredictionRebaseId(2), teleportGate.Current.LocalRebase);
        Assert.Equal(
            PredictionEpochTransitionDecision.Duplicate,
            teleportGate.ApplyAuthorityEpoch(Epoch(discontinuity: 2)));
        Assert.Equal(new LocalPredictionRebaseId(2), teleportGate.Current.LocalRebase);
    }

    [Fact]
    public void RegressionsAndDifferentOwningScopesCannotReplaceCurrentEpoch()
    {
        var current = Epoch(frameEpoch: 3, life: 3, discontinuity: 3, control: 3);
        var gate = Gate(current);

        Assert.Equal(PredictionEpochTransitionDecision.RejectedRegression,
            gate.ApplyAuthorityEpoch(Epoch(frameEpoch: 2, life: 3, discontinuity: 3, control: 3)));
        Assert.Equal(PredictionEpochTransitionDecision.RejectedRegression,
            gate.ApplyAuthorityEpoch(Epoch(frameEpoch: 3, life: 2, discontinuity: 3, control: 3)));
        Assert.Equal(PredictionEpochTransitionDecision.RejectedRegression,
            gate.ApplyAuthorityEpoch(Epoch(frameEpoch: 3, life: 3, discontinuity: 2, control: 3)));
        Assert.Equal(PredictionEpochTransitionDecision.RejectedRegression,
            gate.ApplyAuthorityEpoch(Epoch(frameEpoch: 3, life: 3, discontinuity: 3, control: 2)));
        Assert.Equal(PredictionEpochTransitionDecision.RejectedDifferentScope,
            gate.ApplyAuthorityEpoch(Epoch(session: 2, frameEpoch: 3, life: 3, discontinuity: 3, control: 3)));
        Assert.Equal(PredictionEpochTransitionDecision.RejectedDifferentScope,
            gate.ApplyAuthorityEpoch(Epoch(combatant: 2, frameEpoch: 3, life: 3, discontinuity: 3, control: 3)));
        Assert.Equal(current, gate.Current.Authority);
        Assert.Equal(LocalPredictionRebaseId.Initial, gate.Current.LocalRebase);
    }

    [Fact]
    public void LocalRebaseChangesNoAuthorityIdentityOrEvidenceDecision()
    {
        var authority = Epoch();
        var gate = Gate(authority);

        Assert.Equal(new LocalPredictionRebaseId(2), gate.RebaseLocal());
        Assert.Equal(new LocalPredictionRebaseId(3), gate.RebaseLocal());
        Assert.Equal(authority, gate.Current.Authority);
        Assert.Equal(PredictionEpochEvidenceDecision.Accepted, gate.Evaluate(authority));
        Assert.DoesNotContain(
            typeof(LocalPredictionRebaseId),
            typeof(CombatantAuthorityPredictionEpoch).GetProperties()
                .Select(property => property.PropertyType));
    }

    [Fact]
    public void InvalidAggregatesAndCounterOverflowFailBeforeMutation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CombatantAuthorityPredictionEpoch(
            0,
            MatchFrameEpochId.Initial,
            new CombatantId(1),
            new LifeGenerationId(1),
            AuthorityDiscontinuityId.Initial,
            OwnerControlEpoch.Initial));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CombatantLocalPredictionEpoch(default, LocalPredictionRebaseId.Initial));

        var maximumLocal = new CombatantPredictionEpochGate(
            new CombatantLocalPredictionEpoch(Epoch(), new LocalPredictionRebaseId(ulong.MaxValue)));
        Assert.Throws<OverflowException>(() => maximumLocal.RebaseLocal());
        Assert.Equal(ulong.MaxValue, maximumLocal.Current.LocalRebase.Value);

        var transitionAtMaximum = new CombatantPredictionEpochGate(
            new CombatantLocalPredictionEpoch(Epoch(), new LocalPredictionRebaseId(ulong.MaxValue)));
        Assert.Throws<OverflowException>(() =>
            transitionAtMaximum.ApplyAuthorityEpoch(Epoch(control: 2)));
        Assert.Equal(Epoch(), transitionAtMaximum.Current.Authority);
    }

    private static CombatantPredictionEpochGate Gate(
        CombatantAuthorityPredictionEpoch authority) => new(
            new CombatantLocalPredictionEpoch(authority, LocalPredictionRebaseId.Initial));

    private static CombatantAuthorityPredictionEpoch Epoch(
        ulong session = 1,
        ulong frameEpoch = 1,
        long combatant = 1,
        long life = 1,
        ulong discontinuity = 1,
        ulong control = 1) => new(
            session,
            new MatchFrameEpochId(frameEpoch),
            new CombatantId(combatant),
            new LifeGenerationId(life),
            new AuthorityDiscontinuityId(discontinuity),
            new OwnerControlEpoch(control));
}
