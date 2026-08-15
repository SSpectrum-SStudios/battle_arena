using BattleArena.Core.Common;
using BattleArena.Core.Movement.Simulation;
using BattleArena.Multiplayer.Connection;
using BattleArena.Multiplayer.OwnerPrediction;
using BattleArena.Multiplayer.Transport;

namespace BattleArena.Multiplayer.Tests.OwnerPrediction;

public sealed class OwnerPredictionIdsTests
{
    [Fact]
    public void ScalarTypesAreDistinctAndAdvanceExactlyOnce()
    {
        Assert.NotEqual(typeof(InputSequence), typeof(PacketSequence));
        Assert.NotEqual(typeof(PacketSequence), typeof(PredictionRouteAttemptId));
        Assert.NotEqual(typeof(MovementTransitionId), typeof(PredictedActionId));
        Assert.NotEqual(typeof(PredictedActionId), typeof(AuthorityActionExecutionId));
        Assert.NotEqual(
            typeof(TransitionResolutionSequence),
            typeof(ActionResolutionSequence));

        Assert.Equal(2UL, InputSequence.Initial.Next().Value);
        Assert.Equal(2UL, PacketSequence.Initial.Next().Value);
        Assert.Equal(2UL, PredictionRouteAttemptId.Initial.Next().Value);
        Assert.Equal(2UL, MovementTransitionId.Initial.Next().Value);
        Assert.Equal(2UL, PredictedActionId.Initial.Next().Value);
        Assert.Equal(2UL, AuthorityActionExecutionId.Initial.Next().Value);
        Assert.Equal(2UL, TransitionResolutionSequence.Initial.Next().Value);
        Assert.Equal(2UL, ActionResolutionSequence.Initial.Next().Value);
    }

    [Fact]
    public void EveryScalarRejectsZeroDefaultOrderingAndOverflow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InputSequence(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PacketSequence(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PredictionRouteAttemptId(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovementTransitionId(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PredictedActionId(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AuthorityActionExecutionId(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TransitionResolutionSequence(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ActionResolutionSequence(0));

        Assert.Throws<InvalidOperationException>(() => default(InputSequence).Next());
        Assert.Throws<InvalidOperationException>(() =>
            default(PacketSequence).CompareTo(PacketSequence.Initial));
        Assert.Throws<InvalidOperationException>(() =>
            default(PredictionRouteAttemptId).Next());
        Assert.Throws<InvalidOperationException>(() =>
            default(MovementTransitionId).CompareTo(MovementTransitionId.Initial));
        Assert.Throws<InvalidOperationException>(() => default(PredictedActionId).Next());
        Assert.Throws<InvalidOperationException>(() =>
            default(AuthorityActionExecutionId).CompareTo(AuthorityActionExecutionId.Initial));
        Assert.Throws<InvalidOperationException>(() =>
            default(TransitionResolutionSequence).Next());
        Assert.Throws<InvalidOperationException>(() =>
            default(ActionResolutionSequence).CompareTo(ActionResolutionSequence.Initial));

        Assert.Throws<OverflowException>(() => new InputSequence(ulong.MaxValue).Next());
        Assert.Throws<OverflowException>(() => new PacketSequence(ulong.MaxValue).Next());
        Assert.Throws<OverflowException>(() =>
            new PredictionRouteAttemptId(ulong.MaxValue).Next());
        Assert.Throws<OverflowException>(() => new MovementTransitionId(ulong.MaxValue).Next());
        Assert.Throws<OverflowException>(() => new PredictedActionId(ulong.MaxValue).Next());
        Assert.Throws<OverflowException>(() =>
            new AuthorityActionExecutionId(ulong.MaxValue).Next());
        Assert.Throws<OverflowException>(() =>
            new TransitionResolutionSequence(ulong.MaxValue).Next());
        Assert.Throws<OverflowException>(() =>
            new ActionResolutionSequence(ulong.MaxValue).Next());
    }

    [Fact]
    public void LifeEpochIsCoreOwnedAndRejectsInvalidComponents()
    {
        var first = new LifeEpoch(new CombatantId(1), new LifeGenerationId(1));

        Assert.Equal(typeof(SimulationStepContext).Assembly, typeof(LifeEpoch).Assembly);
        Assert.True(first.IsValid);
        Assert.NotEqual(first, new LifeEpoch(new CombatantId(1), new LifeGenerationId(2)));
        Assert.NotEqual(first, new LifeEpoch(new CombatantId(2), new LifeGenerationId(1)));
        Assert.False(default(LifeEpoch).IsValid);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LifeEpoch(default, new LifeGenerationId(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LifeEpoch(new CombatantId(1), default));
    }

    [Fact]
    public void OwnerIntentScopeChangesOnlyForSessionLifeCombatantOrControl()
    {
        var baseline = Scope(session: 10, combatant: 2, life: 3, control: 4);
        OwnerIntentScope[] differentScopes =
        [
            Scope(session: 11, combatant: 2, life: 3, control: 4),
            Scope(session: 10, combatant: 9, life: 3, control: 4),
            Scope(session: 10, combatant: 2, life: 8, control: 4),
            Scope(session: 10, combatant: 2, life: 3, control: 7),
        ];

        Assert.All(differentScopes, candidate => Assert.NotEqual(baseline, candidate));
        Assert.False(default(OwnerIntentScope).IsValid);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OwnerIntentScope(0, baseline.Life, baseline.OwnerControl));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OwnerIntentScope(1, default, baseline.OwnerControl));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OwnerIntentScope(1, baseline.Life, default));
    }

    [Fact]
    public void AuthorityDiscontinuityDoesNotResetIntentOrCursorScope()
    {
        var before = OwnerIntentScope.From(AuthorityEpoch(discontinuity: 5));
        var afterTeleport = OwnerIntentScope.From(AuthorityEpoch(discontinuity: 6));

        Assert.Equal(before, afterTeleport);
        Assert.Equal(
            0,
            new MovementTransitionIdentity(before, MovementTransitionId.Initial)
                .CompareTo(new MovementTransitionIdentity(
                    afterTeleport,
                    MovementTransitionId.Initial)));
    }

    [Fact]
    public void ExplicitOwnerIdentitiesOrderOnlyWithinTheSameScope()
    {
        var scope = Scope();
        var first = new OwnerInputIdentity(scope, InputSequence.Initial);
        var equal = new OwnerInputIdentity(scope, InputSequence.Initial);
        var second = new OwnerInputIdentity(scope, new InputSequence(2));

        Assert.Equal(0, first.CompareTo(equal));
        Assert.True(first.CompareTo(second) < 0);
        Assert.True(second.CompareTo(first) > 0);
        Assert.Throws<InvalidOperationException>(() =>
            first.CompareTo(new OwnerInputIdentity(
                Scope(control: 2),
                InputSequence.Initial)));
        Assert.Throws<InvalidOperationException>(() =>
            default(OwnerInputIdentity).CompareTo(first));

        Assert.True(new MovementTransitionIdentity(scope, MovementTransitionId.Initial).IsValid);
        Assert.True(new PredictedActionIdentity(scope, PredictedActionId.Initial).IsValid);
        Assert.True(new AuthorityActionExecutionIdentity(
            new AuthorityActionExecutionScope(scope.SessionId, scope.Life),
            AuthorityActionExecutionId.Initial).IsValid);
        Assert.True(new TransitionResolutionIdentity(
            scope,
            TransitionResolutionSequence.Initial).IsValid);
        Assert.True(new ActionResolutionIdentity(
            scope,
            ActionResolutionSequence.Initial).IsValid);
    }

    [Fact]
    public void ActiveAuthorityExecutionSurvivesOwnerControlRenewal()
    {
        var firstEpoch = AuthorityEpoch(discontinuity: 5, control: 1);
        var renewedControl = AuthorityEpoch(discontinuity: 5, control: 2);
        var before = new AuthorityActionExecutionIdentity(
            AuthorityActionExecutionScope.From(firstEpoch),
            AuthorityActionExecutionId.Initial);
        var after = new AuthorityActionExecutionIdentity(
            AuthorityActionExecutionScope.From(renewedControl),
            AuthorityActionExecutionId.Initial);

        Assert.Equal(before, after);
        Assert.Equal(0, before.CompareTo(after));
    }

    [Fact]
    public void TransitionAndActionResolutionCursorsCannotBeInterchanged()
    {
        Assert.NotEqual(
            typeof(TransitionResolutionIdentity),
            typeof(ActionResolutionIdentity));
        Assert.Equal(
            typeof(TransitionResolutionSequence),
            typeof(TransitionResolutionIdentity)
                .GetProperty(nameof(TransitionResolutionIdentity.Sequence))!
                .PropertyType);
        Assert.Equal(
            typeof(ActionResolutionSequence),
            typeof(ActionResolutionIdentity)
                .GetProperty(nameof(ActionResolutionIdentity.Sequence))!
                .PropertyType);
    }

    [Fact]
    public void SessionPacketIdentityOrdersOnlyInsideOneDirectionalStream()
    {
        var scope = SessionPacketScope();
        var first = new SessionPacketIdentity(scope, PacketSequence.Initial);
        var second = new SessionPacketIdentity(scope, new PacketSequence(2));
        SessionPacketStreamScope[] otherStreams =
        [
            SessionPacketScope(session: 2),
            SessionPacketScope(sourcePeer: 3),
            SessionPacketScope(sourceGeneration: 2),
            SessionPacketScope(destinationPeer: 4),
            SessionPacketScope(destinationGeneration: 2),
            SessionPacketScope(channel: TransportChannel.Action),
        ];

        Assert.True(first.CompareTo(second) < 0);
        Assert.All(otherStreams, other =>
            Assert.Throws<InvalidOperationException>(() =>
                first.CompareTo(new SessionPacketIdentity(other, PacketSequence.Initial))));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SessionPacketStreamScope(
            1,
            new SessionPeerId(1),
            ConnectionGeneration.Initial,
            new SessionPeerId(2),
            ConnectionGeneration.Initial,
            (TransportChannel)255));
        Assert.Throws<InvalidOperationException>(() =>
            default(SessionPacketIdentity).CompareTo(first));
    }

    [Fact]
    public void MeshPacketIdentityIncludesDestinationGenerationsAndRouteGeneration()
    {
        var baselineScope = new PredictionMeshPacketStreamScope(
            SessionPacketScope(),
            predictionRouteGeneration: 5,
            PredictionRouteAttemptId.Initial);
        var first = new PredictionMeshPacketIdentity(
            baselineScope,
            PacketSequence.Initial);
        PredictionMeshPacketStreamScope[] otherRoutes =
        [
            new(SessionPacketScope(destinationPeer: 4), 5, PredictionRouteAttemptId.Initial),
            new(SessionPacketScope(destinationGeneration: 2), 5, PredictionRouteAttemptId.Initial),
            new(SessionPacketScope(), 6, PredictionRouteAttemptId.Initial),
            new(SessionPacketScope(), 5, new PredictionRouteAttemptId(2)),
        ];

        Assert.All(otherRoutes, other =>
            Assert.Throws<InvalidOperationException>(() =>
                first.CompareTo(new PredictionMeshPacketIdentity(
                    other,
                    PacketSequence.Initial))));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PredictionMeshPacketStreamScope(
                SessionPacketScope(),
                0,
                PredictionRouteAttemptId.Initial));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PredictionMeshPacketStreamScope(SessionPacketScope(), 1, default));
        Assert.Throws<InvalidOperationException>(() =>
            default(PredictionMeshPacketIdentity).CompareTo(first));
    }

    private static OwnerIntentScope Scope(
        ulong session = 10,
        long combatant = 2,
        long life = 3,
        ulong control = 1) =>
        new(
            session,
            new LifeEpoch(new CombatantId(combatant), new LifeGenerationId(life)),
            new OwnerControlEpoch(control));

    private static CombatantAuthorityPredictionEpoch AuthorityEpoch(
        ulong discontinuity,
        ulong control = 1) => new(
        sessionId: 10,
        MatchFrameEpochId.Initial,
        new CombatantId(2),
        new LifeGenerationId(3),
        new AuthorityDiscontinuityId(discontinuity),
        new OwnerControlEpoch(control));

    private static SessionPacketStreamScope SessionPacketScope(
        ulong session = 1,
        ulong sourcePeer = 1,
        uint sourceGeneration = 1,
        ulong destinationPeer = 2,
        uint destinationGeneration = 1,
        TransportChannel channel = TransportChannel.Movement) =>
        new(
            session,
            new SessionPeerId(sourcePeer),
            new ConnectionGeneration(sourceGeneration),
            new SessionPeerId(destinationPeer),
            new ConnectionGeneration(destinationGeneration),
            channel);
}
