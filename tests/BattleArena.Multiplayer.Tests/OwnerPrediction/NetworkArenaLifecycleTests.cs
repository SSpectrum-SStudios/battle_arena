using BattleArena.Core.Common;
using BattleArena.Core.Movement.Simulation;
using BattleArena.Multiplayer.OwnerPrediction;

namespace BattleArena.Multiplayer.Tests.OwnerPrediction;

public sealed class NetworkArenaLifecycleTests
{
    [Fact]
    public void ProductionRouterPopulatesOnlyFromStateBearingSources()
    {
        var resetCount = 0;
        var router = Router(_ => resetCount++);

        Assert.Equal(PredictionEpochEvidenceDecision.UnregisteredCombatant,
            router.EvaluateEvidence(Epoch(combatant: 1), ClientPredictionEvidenceRoute.Action));
        Assert.Equal(ClientPredictionBaselineDecision.Registered,
            router.ObserveStateBaseline(
                Epoch(combatant: 1),
                ClientPredictionStateBaselineSource.Spawn).Decision);
        Assert.Equal(ClientPredictionBaselineDecision.Registered,
            router.ObserveStateBaseline(
                Epoch(combatant: 2),
                ClientPredictionStateBaselineSource.Snapshot).Decision);
        Assert.Equal(ClientPredictionBaselineDecision.Current,
            router.ObserveStateBaseline(
                Epoch(combatant: 2),
                ClientPredictionStateBaselineSource.AuthorityMovement).Decision);

        Assert.Equal(2, router.RegisteredCombatantCount);
        Assert.Equal(0, resetCount);
        Assert.True(router.TryGetCurrent(new CombatantId(2), out var remote));
        Assert.Equal(new LifeGenerationId(1), remote.Authority.Life);
    }

    [Fact]
    public void EveryNetworkArenaEvidenceRouteUsesTheSameCurrentEpochGate()
    {
        var resetCount = 0;
        var router = RegisteredRouter(_ => resetCount++);
        var old = Epoch(combatant: 2);
        var current = Epoch(combatant: 2, life: 2, discontinuity: 2, control: 2);
        Assert.Equal(ClientPredictionBaselineDecision.Transitioned,
            router.ObserveStateBaseline(
                current,
                ClientPredictionStateBaselineSource.AuthorityMovement).Decision);

        ClientPredictionEvidenceRoute[] routes =
        [
            ClientPredictionEvidenceRoute.AcceptedMovement,
            ClientPredictionEvidenceRoute.DirectMovementHint,
            ClientPredictionEvidenceRoute.Action,
            ClientPredictionEvidenceRoute.DamageSource,
            ClientPredictionEvidenceRoute.DamageTarget,
        ];
        foreach (var route in routes)
        {
            Assert.Equal(PredictionEpochEvidenceDecision.Accepted,
                router.EvaluateEvidence(current, route));
            Assert.Equal(PredictionEpochEvidenceDecision.LifeMismatch,
                router.EvaluateEvidence(old, route));
        }

        Assert.Equal(1, resetCount);
    }

    [Fact]
    public void FutureLifeEventWaitsForStateBaselineBeforeResetOrNewLifeEvidence()
    {
        var resetCount = 0;
        var pendingOwnerHistory = 7;
        var router = RegisteredRouter(_ =>
        {
            resetCount++;
            pendingOwnerHistory = 0;
        });
        var old = Epoch(combatant: 1);
        var announced = Epoch(combatant: 1, life: 2, discontinuity: 2, control: 2);

        Assert.Equal(ClientLifeNotificationDecision.Current,
            router.ObserveLifeNotification(old));
        Assert.Equal(ClientLifeNotificationDecision.PendingStateBaseline,
            router.ObserveLifeNotification(announced));
        Assert.Equal(ClientLifeNotificationDecision.PendingStateBaseline,
            router.ObserveLifeNotification(announced));
        Assert.Equal(1, router.PendingLifeNotificationCount);
        Assert.Equal(0, resetCount);
        Assert.Equal(7, pendingOwnerHistory);
        Assert.True(router.TryGetCurrent(new CombatantId(1), out var beforeBaseline));
        Assert.Equal(new LifeGenerationId(1), beforeBaseline.Authority.Life);
        Assert.Equal(PredictionEpochEvidenceDecision.Accepted,
            router.EvaluateEvidence(old, ClientPredictionEvidenceRoute.Action));
        Assert.Equal(PredictionEpochEvidenceDecision.LifeMismatch,
            router.EvaluateEvidence(announced, ClientPredictionEvidenceRoute.Action));

        var committed = router.ObserveStateBaseline(
            announced,
            ClientPredictionStateBaselineSource.Snapshot);
        Assert.Equal(ClientPredictionBaselineDecision.Transitioned, committed.Decision);
        Assert.Equal(0, router.PendingLifeNotificationCount);
        Assert.Equal(1, resetCount);
        Assert.Equal(0, pendingOwnerHistory);
        Assert.Equal(PredictionEpochEvidenceDecision.Accepted,
            router.EvaluateEvidence(announced, ClientPredictionEvidenceRoute.Action));
        Assert.Equal(ClientLifeNotificationDecision.RejectedStale,
            router.ObserveLifeNotification(old));
        Assert.Equal(1, resetCount);
    }

    [Fact]
    public void LifeNotificationBeforeAnySpawnAlsoRemainsPending()
    {
        var resetCount = 0;
        var router = Router(_ => resetCount++);
        var announced = Epoch(combatant: 4, life: 3, discontinuity: 3, control: 3);

        Assert.Equal(ClientLifeNotificationDecision.PendingStateBaseline,
            router.ObserveLifeNotification(announced));
        Assert.Equal(0, router.RegisteredCombatantCount);
        Assert.Equal(1, router.PendingLifeNotificationCount);
        Assert.Equal(0, resetCount);

        Assert.Equal(ClientPredictionBaselineDecision.Registered,
            router.ObserveStateBaseline(
                announced,
                ClientPredictionStateBaselineSource.AuthorityMovement).Decision);
        Assert.Equal(0, router.PendingLifeNotificationCount);
        Assert.Equal(0, resetCount);
    }

    [Fact]
    public void OneAppliedTransitionRunsTheProductionResetSinkExactlyOnce()
    {
        var resetCount = 0;
        var ownerCommands = 7;
        var pendingCorrection = true;
        var queuedMovementTransitions = 3;
        var predictedActions = 2;
        var remoteTimelineSamples = 5;
        var presentationCues = 4;
        ClientPredictionEpochTransition? observed = null;
        var router = RegisteredRouter(transition =>
        {
            resetCount++;
            ownerCommands = 0;
            pendingCorrection = false;
            queuedMovementTransitions = 0;
            predictedActions = 0;
            remoteTimelineSamples = 0;
            presentationCues = 0;
            observed = transition;
        });
        var next = Epoch(combatant: 2, life: 2, discontinuity: 2, control: 2);

        Assert.Equal(ClientPredictionBaselineDecision.Transitioned,
            router.ObserveStateBaseline(
                next,
                ClientPredictionStateBaselineSource.AuthorityMovement).Decision);
        Assert.Equal(1, resetCount);
        Assert.Equal(0, ownerCommands);
        Assert.False(pendingCorrection);
        Assert.Equal(0, queuedMovementTransitions);
        Assert.Equal(0, predictedActions);
        Assert.Equal(0, remoteTimelineSamples);
        Assert.Equal(0, presentationCues);
        Assert.NotNull(observed);
        Assert.Equal(new LocalPredictionRebaseId(2), observed.Value.Current.LocalRebase);

        Assert.Equal(ClientPredictionBaselineDecision.Current,
            router.ObserveStateBaseline(
                next,
                ClientPredictionStateBaselineSource.Snapshot).Decision);
        Assert.Equal(ClientPredictionBaselineDecision.RejectedRegression,
            router.ObserveStateBaseline(
                Epoch(combatant: 2),
                ClientPredictionStateBaselineSource.Snapshot).Decision);
        Assert.Equal(1, resetCount);
    }

    [Fact]
    public void ReconnectAndTimelineResetEachRunOneReset()
    {
        var transitions = new List<ClientPredictionEpochTransition>();
        var router = RegisteredRouter(transitions.Add);

        var reconnect = Epoch(combatant: 1, control: 2);
        Assert.Equal(ClientPredictionBaselineDecision.Transitioned,
            router.ObserveStateBaseline(
                reconnect,
                ClientPredictionStateBaselineSource.Snapshot).Decision);
        var timelineReset = Epoch(combatant: 1, frameEpoch: 2, control: 3);
        Assert.Equal(ClientPredictionBaselineDecision.Transitioned,
            router.ObserveStateBaseline(
                timelineReset,
                ClientPredictionStateBaselineSource.AuthorityMovement).Decision);

        Assert.Equal(2, transitions.Count);
        Assert.Equal(new LocalPredictionRebaseId(2), transitions[0].Current.LocalRebase);
        Assert.Equal(new LocalPredictionRebaseId(3), transitions[1].Current.LocalRebase);
    }

    [Fact]
    public void RouterKindsAndDefaultsFailClosed()
    {
        var router = RegisteredRouter(_ => { });

        Assert.Throws<ArgumentOutOfRangeException>(() => router.ObserveStateBaseline(
            Epoch(),
            ClientPredictionStateBaselineSource.Unspecified));
        Assert.Throws<ArgumentOutOfRangeException>(() => router.EvaluateEvidence(
            Epoch(),
            ClientPredictionEvidenceRoute.Unspecified));
        Assert.Equal(ClientLifeNotificationDecision.RejectedDifferentScope,
            router.ObserveLifeNotification(Epoch(session: 78)));
        Assert.Equal(ClientPredictionStateBaselineSource.Unspecified,
            default(ClientPredictionStateBaselineSource));
        Assert.Equal(ClientPredictionEvidenceRoute.Unspecified,
            default(ClientPredictionEvidenceRoute));
        Assert.Equal(ClientLifeNotificationDecision.Unspecified,
            default(ClientLifeNotificationDecision));
    }

    private static ClientPredictionLifecycleRouter RegisteredRouter(
        Action<ClientPredictionEpochTransition> resetSink)
    {
        var router = Router(resetSink);
        Assert.True(router.ObserveStateBaseline(
            Epoch(combatant: 1),
            ClientPredictionStateBaselineSource.Spawn).IsAccepted);
        Assert.True(router.ObserveStateBaseline(
            Epoch(combatant: 2),
            ClientPredictionStateBaselineSource.Spawn).IsAccepted);
        return router;
    }

    private static ClientPredictionLifecycleRouter Router(
        Action<ClientPredictionEpochTransition> resetSink) => new(77, resetSink);

    private static CombatantAuthorityPredictionEpoch Epoch(
        ulong session = 77,
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
