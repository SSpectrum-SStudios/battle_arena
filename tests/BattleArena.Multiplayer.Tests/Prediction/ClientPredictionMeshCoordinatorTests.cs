using BattleArena.Multiplayer.Connection;
using BattleArena.Multiplayer.Prediction;

namespace BattleArena.Multiplayer.Tests.Prediction;

public sealed class ClientPredictionMeshCoordinatorTests
{
    [Fact]
    public void LowerPeerInitiatesAndHigherPeerListensWithoutTrustedFlag()
    {
        var broker = new AuthorityPredictionRouteBroker(
            new PredictionTestCredentialGenerator(),
            AuthorityPredictionRouteBrokerTests.Peer(1, authority: true),
            credentialLifetimeTicks: 10_000);
        var rosterForTwo = broker.AddOrUpdatePeer(AuthorityPredictionRouteBrokerTests.Peer(2)).Roster!;
        var roster = broker.AddOrUpdatePeer(AuthorityPredictionRouteBrokerTests.Peer(3)).Roster!;
        broker.ApplyAdvertisement(
            new SessionPeerId(2), ConnectionGeneration.Initial,
            AuthorityPredictionRouteBrokerTests.Descriptor(7782), 100);
        var delta = broker.ApplyAdvertisement(
            new SessionPeerId(3), ConnectionGeneration.Initial,
            AuthorityPredictionRouteBrokerTests.Descriptor(7783), 100);
        var directoryTwo = new SessionPeerDirectory(new SessionPeerId(2));
        var directoryThree = new SessionPeerDirectory(new SessionPeerId(3));
        Assert.True(directoryTwo.TryApply(roster));
        Assert.True(directoryThree.TryApply(roster));
        var two = new ClientPredictionMeshCoordinator(73, directoryTwo);
        var three = new ClientPredictionMeshCoordinator(73, directoryThree);

        var directiveTwo = Assert.Single(two.ApplyAuthorization(
            delta.Authorizations.Single(value => value.LocalSessionPeerId == 2), 100));
        var directiveThree = Assert.Single(three.ApplyAuthorization(
            delta.Authorizations.Single(value => value.LocalSessionPeerId == 3), 100));

        Assert.Equal(PredictionMeshDirectiveKind.Initiate, directiveTwo.Kind);
        Assert.Equal(PredictionMeshDirectiveKind.Listen, directiveThree.Kind);
        Assert.True(directiveTwo.Route!.LocalPeerInitiates);
        Assert.False(directiveThree.Route!.LocalPeerInitiates);
    }

    [Fact]
    public void StaleRevocationCannotRemoveReplacementAuthorization()
    {
        var setup = SetupClientTwo();
        var first = setup.First;
        Assert.Single(setup.Coordinator.ApplyAuthorization(first, 100));
        var second = first.Clone();
        second.PredictionRouteGeneration++;
        Assert.Single(setup.Coordinator.ApplyAuthorization(second, 101));
        var stale = new BattleArena.Protocol.V1.PredictionRouteRevoked
        {
            LocalSessionPeerId = first.LocalSessionPeerId,
            LocalPeerSessionGeneration = first.LocalPeerSessionGeneration,
            RemoteSessionPeerId = first.RemoteSessionPeerId,
            RemotePeerSessionGeneration = first.RemotePeerSessionGeneration,
            PredictionRouteGeneration = first.PredictionRouteGeneration,
            Reason = BattleArena.Protocol.V1.PredictionRouteRevocationReason.RouteReplaced,
        };

        Assert.Empty(setup.Coordinator.ApplyRevocation(stale));
        Assert.Single(setup.Coordinator.AuthorizedRoutes);
    }

    [Fact]
    public void ExpiryAndReconnectRosterImmediatelyRemoveOldRoute()
    {
        var setup = SetupClientTwo();
        setup.Coordinator.ApplyAuthorization(setup.First, 100);

        var expired = setup.Coordinator.AdvanceAuthorityTick(setup.First.ExpiresAuthorityTick);
        Assert.Single(expired);
        Assert.Empty(setup.Coordinator.AuthorizedRoutes);

        setup = SetupClientTwo();
        setup.Coordinator.ApplyAuthorization(setup.First, 100);
        var roster = setup.Roster.Clone();
        roster.RosterRevision++;
        roster.Peers.Single(peer => peer.SessionPeerId == 3).PeerSessionGeneration++;
        var reconnect = setup.Coordinator.RemoveMissingRosterPeers(roster);
        Assert.Single(reconnect);
        Assert.Empty(setup.Coordinator.AuthorizedRoutes);
    }

    [Fact]
    public void FailureUsesAuthorityFallbackAndBoundedRetry()
    {
        var setup = SetupClientTwo();
        setup.Coordinator.ApplyAuthorization(setup.First, 100);

        for (var failure = 1; failure <= 6; failure++)
        {
            var active = Assert.Single(setup.Coordinator.RouteStatuses);
            Assert.Single(setup.Coordinator.ReportRouteFailure(
                new SessionPeerId(3),
                setup.First.PredictionRouteGeneration,
                active.AttemptId,
                (ulong)(100 * failure)));
            var status = Assert.Single(setup.Coordinator.RouteStatuses);
            Assert.Equal(PredictionRouteHealth.AuthorityFallback, status.Health);
            if (failure <= 5)
            {
                var retry = setup.Coordinator.AdvanceAuthorityTick(status.NextRetryAuthorityTick);
                Assert.Single(retry);
            }
            else
            {
                Assert.Equal(ulong.MaxValue, status.NextRetryAuthorityTick);
                Assert.Empty(setup.Coordinator.AdvanceAuthorityTick(601));
            }
        }
    }

    [Fact]
    public void StaleAttemptCallbacksCannotChangeNewerAttempt()
    {
        var setup = SetupClientTwo();
        var start = Assert.Single(setup.Coordinator.ApplyAuthorization(setup.First, 100));
        Assert.Single(setup.Coordinator.ReportRouteFailure(
            new SessionPeerId(3),
            setup.First.PredictionRouteGeneration,
            start.AttemptId,
            110));
        var fallback = Assert.Single(setup.Coordinator.RouteStatuses);
        var retry = Assert.Single(setup.Coordinator.AdvanceAuthorityTick(
            fallback.NextRetryAuthorityTick));

        Assert.Empty(setup.Coordinator.ReportRouteFailure(
            new SessionPeerId(3),
            setup.First.PredictionRouteGeneration,
            start.AttemptId,
            fallback.NextRetryAuthorityTick + 1));
        Assert.False(setup.Coordinator.MarkAuthenticated(
            new SessionPeerId(3),
            setup.First.PredictionRouteGeneration,
            start.AttemptId,
            fallback.NextRetryAuthorityTick + 1));
        Assert.True(setup.Coordinator.MarkAuthenticated(
            new SessionPeerId(3),
            setup.First.PredictionRouteGeneration,
            retry.AttemptId,
            fallback.NextRetryAuthorityTick + 1));
        Assert.Equal(
            PredictionRouteHealth.Authenticated,
            Assert.Single(setup.Coordinator.RouteStatuses).Health);
    }

    [Fact]
    public void DelayedFailureFromReplacedRouteCannotAffectNewAuthorization()
    {
        var setup = SetupClientTwo();
        var oldStart = Assert.Single(setup.Coordinator.ApplyAuthorization(setup.First, 100));
        var replacement = setup.First.Clone();
        replacement.PredictionRouteGeneration++;
        var newStart = Assert.Single(setup.Coordinator.ApplyAuthorization(replacement, 101));

        Assert.True(newStart.AttemptId > oldStart.AttemptId);
        Assert.Empty(setup.Coordinator.ReportRouteFailure(
            new SessionPeerId(3),
            setup.First.PredictionRouteGeneration,
            oldStart.AttemptId,
            102));
        Assert.True(setup.Coordinator.MarkAuthenticated(
            new SessionPeerId(3),
            replacement.PredictionRouteGeneration,
            newStart.AttemptId,
            102));
    }

    [Fact]
    public void MalformedAuthorizationIsRejectedWithoutThrowing()
    {
        var setup = SetupClientTwo();
        var malformed = setup.First.Clone();
        malformed.RemoteSessionPeerId = 0;

        var exception = Record.Exception(() =>
            setup.Coordinator.ApplyAuthorization(malformed, 100));

        Assert.Null(exception);
        Assert.Empty(setup.Coordinator.AuthorizedRoutes);
    }

    private static (
        ClientPredictionMeshCoordinator Coordinator,
        BattleArena.Protocol.V1.PredictionRouteAuthorization First,
        BattleArena.Protocol.V1.PeerRosterUpdate Roster) SetupClientTwo()
    {
        var broker = new AuthorityPredictionRouteBroker(
            new PredictionTestCredentialGenerator(),
            AuthorityPredictionRouteBrokerTests.Peer(1, authority: true),
            credentialLifetimeTicks: 10_000);
        broker.AddOrUpdatePeer(AuthorityPredictionRouteBrokerTests.Peer(2));
        var roster = broker.AddOrUpdatePeer(AuthorityPredictionRouteBrokerTests.Peer(3)).Roster!;
        broker.ApplyAdvertisement(new SessionPeerId(2), ConnectionGeneration.Initial,
            AuthorityPredictionRouteBrokerTests.Descriptor(7782), 100);
        var delta = broker.ApplyAdvertisement(new SessionPeerId(3), ConnectionGeneration.Initial,
            AuthorityPredictionRouteBrokerTests.Descriptor(7783), 100);
        var directory = new SessionPeerDirectory(new SessionPeerId(2));
        directory.TryApply(roster);
        return (
            new ClientPredictionMeshCoordinator(73, directory),
            delta.Authorizations.Single(value => value.LocalSessionPeerId == 2),
            roster);
    }
}
