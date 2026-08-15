using BattleArena.Multiplayer.Connection;
using BattleArena.Multiplayer.Prediction;
using BattleArena.Protocol.V1;
using Google.Protobuf;

namespace BattleArena.Multiplayer.Tests.Prediction;

public sealed class AuthorityPredictionRouteBrokerTests
{
    [Fact]
    public void AuthenticatedAdvertisementsCreateOneSymmetricClientPairRoute()
    {
        var broker = CreateBroker();
        AddPeers(broker);

        var first = broker.ApplyAdvertisement(
            new SessionPeerId(2), new ConnectionGeneration(1), Descriptor(7782), 100);
        var second = broker.ApplyAdvertisement(
            new SessionPeerId(3), new ConnectionGeneration(1), Descriptor(7783), 100);

        Assert.Empty(first.Authorizations);
        Assert.Equal(2, second.Authorizations.Count);
        var forTwo = second.Authorizations.Single(value => value.LocalSessionPeerId == 2);
        var forThree = second.Authorizations.Single(value => value.LocalSessionPeerId == 3);
        Assert.Equal(forTwo.PredictionRouteGeneration, forThree.PredictionRouteGeneration);
        Assert.Equal(forTwo.RouteCredential, forThree.RouteCredential);
        Assert.Equal(7783, Port(forTwo.RemoteDescriptor));
        Assert.Equal(7782, Port(forThree.RemoteDescriptor));
        Assert.Equal(700UL, forTwo.ExpiresAuthorityTick);
    }

    [Fact]
    public void DescriptorReplacementRevokesOldGenerationBeforeIssuingNewOne()
    {
        var broker = CreateBroker();
        AddPeers(broker);
        broker.ApplyAdvertisement(new SessionPeerId(2), new ConnectionGeneration(1), Descriptor(7782), 100);
        var created = broker.ApplyAdvertisement(
            new SessionPeerId(3), new ConnectionGeneration(1), Descriptor(7783), 100);
        var oldGeneration = created.Authorizations[0].PredictionRouteGeneration;

        var replaced = broker.ApplyAdvertisement(
            new SessionPeerId(2), new ConnectionGeneration(1), Descriptor(7792), 110);

        Assert.Equal(2, replaced.Revocations.Count);
        Assert.All(replaced.Revocations, value => Assert.Equal(oldGeneration, value.PredictionRouteGeneration));
        Assert.Equal(2, replaced.Authorizations.Count);
        Assert.All(replaced.Authorizations, value =>
            Assert.Equal(oldGeneration + 1, value.PredictionRouteGeneration));
    }

    [Fact]
    public void ExpiryRotatesCredentialAndRouteWithoutDroppingRoster()
    {
        var broker = CreateBroker();
        AddPeers(broker);
        broker.ApplyAdvertisement(new SessionPeerId(2), new ConnectionGeneration(1), Descriptor(7782), 100);
        var created = broker.ApplyAdvertisement(
            new SessionPeerId(3), new ConnectionGeneration(1), Descriptor(7783), 100);

        var rotated = broker.AdvanceAuthorityTick(700);

        Assert.Null(rotated.Roster);
        Assert.Equal(2, rotated.Revocations.Count);
        Assert.Equal(2, rotated.Authorizations.Count);
        Assert.NotEqual(
            created.Authorizations[0].RouteCredential,
            rotated.Authorizations[0].RouteCredential);
    }

    [Fact]
    public void StaleOrWrongGenerationAdvertisementCannotCreateRoute()
    {
        var broker = CreateBroker();
        AddPeers(broker);
        broker.ApplyAdvertisement(new SessionPeerId(2), new ConnectionGeneration(1), Descriptor(7782), 100);

        var stale = broker.ApplyAdvertisement(
            new SessionPeerId(3), new ConnectionGeneration(2), Descriptor(7783), 100);

        Assert.Empty(stale.Authorizations);
    }

    [Fact]
    public void RosterRejectsDuplicateOwnershipAuthorityMutationAndCapacityOverflow()
    {
        var broker = CreateBroker();
        Assert.NotNull(broker.AddOrUpdatePeer(Peer(2)).Roster);

        var duplicateCombatant = new SessionPeer(
            new SessionPeerId(3), ConnectionGeneration.Initial, 3, 2, "Duplicate", false);
        Assert.Null(broker.AddOrUpdatePeer(duplicateCombatant).Roster);
        var authorityMutation = new SessionPeer(
            new SessionPeerId(2), ConnectionGeneration.Initial, 2, 2, "Peer2", true);
        Assert.Null(broker.AddOrUpdatePeer(authorityMutation).Roster);

        for (ulong id = 3; id <= 8; id++)
        {
            Assert.NotNull(broker.AddOrUpdatePeer(Peer(id)).Roster);
        }

        Assert.Null(broker.AddOrUpdatePeer(Peer(9)).Roster);
        Assert.Equal(8, broker.Peers.Count);
        Assert.Single(broker.Peers, peer => peer.IsAuthority);
    }

    [Fact]
    public void ProtocolViolationRevokesOnlyTheOffendingPair()
    {
        var broker = CreateBroker();
        AddPeers(broker);
        broker.ApplyAdvertisement(new SessionPeerId(2), ConnectionGeneration.Initial, Descriptor(7782), 100);
        broker.ApplyAdvertisement(new SessionPeerId(3), ConnectionGeneration.Initial, Descriptor(7783), 100);

        var delta = broker.RevokePairForProtocolViolation(
            new SessionPeerId(2), new SessionPeerId(3));

        Assert.Equal(2, delta.Revocations.Count);
        Assert.All(delta.Revocations, revocation =>
            Assert.Equal(PredictionRouteRevocationReason.ProtocolViolation, revocation.Reason));
    }

    private static AuthorityPredictionRouteBroker CreateBroker() =>
        new(
            new PredictionTestCredentialGenerator(),
            Peer(1, authority: true),
            credentialLifetimeTicks: 600);

    private static void AddPeers(AuthorityPredictionRouteBroker broker)
    {
        broker.AddOrUpdatePeer(Peer(2));
        broker.AddOrUpdatePeer(Peer(3));
    }

    internal static SessionPeer Peer(
        ulong id,
        bool authority = false,
        uint generation = 1) =>
        new(
            new SessionPeerId(id),
            new ConnectionGeneration(generation),
            id,
            id,
            $"Peer{id}",
            authority);

    internal static PredictionRouteDescriptor Descriptor(int port) => new()
    {
        DescriptorVersion = 1,
        TransportKind = PredictionTransportKind.Enet,
        Payload = ByteString.CopyFrom(BitConverter.GetBytes(port)),
    };

    private static int Port(PredictionRouteDescriptor descriptor) =>
        BitConverter.ToInt32(descriptor.Payload.Span);
}
