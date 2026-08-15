using BattleArena.Multiplayer.Connection;
using BattleArena.Multiplayer.Prediction;
using BattleArena.Multiplayer.Replication;
using BattleArena.Multiplayer.Tests.Protocol;
using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Tests.Prediction;

public sealed class MovementDistributionTests
{
    [Fact]
    public void FactoryPublishesNewestThreeCommandsInCanonicalOrder()
    {
        var source = PredictionProtocolTestData.MovementBundle().Commands[^1];
        var history = Enumerable.Range(1, 6)
            .Select(index => Frame((ulong)index, source))
            .Reverse()
            .ToArray();

        var bundle = new MovementPredictionBundleFactory().Create(
            1, 2, 3, history, rollbackState: null);

        Assert.Equal(new ulong[] { 4, 5, 6 },
            bundle.Commands.Select(command => command.InputSequence));
    }

    [Fact]
    public void AuthorityCanonicalizesOnlyAuthenticatedOwnersAppliedCommand()
    {
        var bundle = PredictionProtocolTestData.MovementBundle();
        var peer = AuthorityPredictionRouteBrokerTests.Peer(2);
        var service = new AuthorityMovementDistribution();

        var accepted = service.AcceptAppliedCommand(peer, 7, bundle, 12, 300);

        Assert.Equal(2UL, accepted.SourceSessionPeerId);
        Assert.Equal(12UL, accepted.Input.InputSequence);
        Assert.Equal(accepted.Input.MovementProfileRevision,
            accepted.AppliedMovementProfileRevision);

        var wrongOwner = AuthorityPredictionRouteBrokerTests.Peer(8);
        Assert.Throws<ArgumentException>(() =>
            service.AcceptAppliedCommand(wrongOwner, 7, bundle, 12, 300));
        Assert.Throws<ArgumentException>(() =>
            service.AcceptAppliedCommand(peer, 8, bundle, 12, 300));
    }

    [Fact]
    public void AuthorityRelaysExactRevisionsOfActuallyAppliedCommand()
    {
        var peer = AuthorityPredictionRouteBrokerTests.Peer(2);
        var service = new AuthorityMovementDistribution();
        var source = PredictionProtocolTestData.MovementBundle().Commands[^1];
        source.MovementProfileRevision = 42;
        source.MovementCapabilityRevision = 77;

        var accepted = service.AcceptAppliedCommand(
            peer,
            expectedLifeId: 7,
            RevisionedMovementCommand.FromProtocol(source),
            appliedAuthorityTick: 300);

        Assert.Equal(42UL, accepted.Input.MovementProfileRevision);
        Assert.Equal(77UL, accepted.Input.MovementCapabilityRevision);
        Assert.Equal(42UL, accepted.AppliedMovementProfileRevision);
        Assert.Equal(77UL, accepted.AppliedMovementCapabilityRevision);
    }

    private static ClientInputFrame Frame(ulong sequence, ClientInputFrame template)
    {
        var result = template.Clone();
        result.InputSequence = sequence;
        result.ClientTick = 100 + sequence;
        result.EstimatedAuthorityTick = 200 + sequence;
        return result;
    }
}
