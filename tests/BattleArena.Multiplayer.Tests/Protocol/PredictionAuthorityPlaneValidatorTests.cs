using BattleArena.Multiplayer.Protocol;
using BattleArena.Protocol.V1;
using Google.Protobuf;

namespace BattleArena.Multiplayer.Tests.Protocol;

public sealed class PredictionAuthorityPlaneValidatorTests
{
    private const ulong SessionId = 73;
    private readonly InboundMessageValidator validator = new();

    [Fact]
    public void ClientMayAdvertiseBoundedPredictionRoute()
    {
        var envelope = ClientEnvelope();
        envelope.PredictionRouteAdvertisement = new PredictionRouteAdvertisement
        {
            RouteDescriptor = PredictionProtocolTestData.RouteDescriptor(),
        };

        var result = validator.Validate(envelope, ClientContext());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void AuthorityMayPublishRosterWithExactlyOneAuthority()
    {
        var envelope = AuthorityEnvelope();
        envelope.PeerRosterUpdate = new PeerRosterUpdate { RosterRevision = 1 };
        envelope.PeerRosterUpdate.Peers.AddRange(new[]
        {
            Peer(1, isAuthority: true),
            Peer(2, isAuthority: false),
        });

        var result = validator.Validate(envelope, AuthorityContext());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void RosterRejectsDuplicatePeersOrMissingAuthority()
    {
        var duplicateEnvelope = AuthorityEnvelope();
        duplicateEnvelope.PeerRosterUpdate = new PeerRosterUpdate { RosterRevision = 1 };
        duplicateEnvelope.PeerRosterUpdate.Peers.AddRange(new[]
        {
            Peer(2, isAuthority: true),
            Peer(2, isAuthority: false),
        });
        var noAuthorityEnvelope = AuthorityEnvelope();
        noAuthorityEnvelope.PeerRosterUpdate = new PeerRosterUpdate { RosterRevision = 1 };
        noAuthorityEnvelope.PeerRosterUpdate.Peers.Add(Peer(2, isAuthority: false));

        var duplicate = validator.Validate(duplicateEnvelope, AuthorityContext());
        var noAuthority = validator.Validate(noAuthorityEnvelope, AuthorityContext());

        Assert.False(duplicate.IsValid);
        Assert.False(noAuthority.IsValid);
        Assert.Equal(ProtocolViolationCode.InvalidSession, duplicate.Violation?.Code);
        Assert.Equal(ProtocolViolationCode.InvalidSession, noAuthority.Violation?.Code);
    }

    [Fact]
    public void AuthorityRouteAuthorizationRequiresExactCredentialAndSeparatePeers()
    {
        var validEnvelope = AuthorityEnvelope();
        validEnvelope.PredictionRouteAuthorization = Authorization(
            ProtocolConstants.PredictionRouteCredentialBytes);
        var shortCredentialEnvelope = AuthorityEnvelope();
        shortCredentialEnvelope.PredictionRouteAuthorization = Authorization(16);
        var selfRouteEnvelope = AuthorityEnvelope();
        selfRouteEnvelope.PredictionRouteAuthorization = Authorization(
            ProtocolConstants.PredictionRouteCredentialBytes);
        selfRouteEnvelope.PredictionRouteAuthorization.RemoteSessionPeerId = 2;

        var valid = validator.Validate(validEnvelope, AuthorityContext());
        var shortCredential = validator.Validate(shortCredentialEnvelope, AuthorityContext());
        var selfRoute = validator.Validate(selfRouteEnvelope, AuthorityContext());

        Assert.True(valid.IsValid);
        Assert.Equal(ProtocolViolationCode.InvalidCredential, shortCredential.Violation?.Code);
        Assert.Equal(ProtocolViolationCode.InvalidSession, selfRoute.Violation?.Code);
    }

    [Fact]
    public void ClientCannotIssueRouteAuthorization()
    {
        var envelope = ClientEnvelope();
        envelope.PredictionRouteAuthorization = Authorization(
            ProtocolConstants.PredictionRouteCredentialBytes);

        var result = validator.Validate(envelope, ClientContext());

        Assert.False(result.IsValid);
        Assert.Equal(ProtocolViolationCode.UnexpectedMessageDirection, result.Violation?.Code);
    }

    [Fact]
    public void RevocationIdentifiesBothPeerPresencesAndRouteGeneration()
    {
        var envelope = AuthorityEnvelope();
        envelope.PredictionRouteRevoked = new PredictionRouteRevoked
        {
            LocalSessionPeerId = 2,
            RemoteSessionPeerId = 3,
            LocalPeerSessionGeneration = 4,
            RemotePeerSessionGeneration = 5,
            PredictionRouteGeneration = 6,
            Reason = PredictionRouteRevocationReason.RouteReplaced,
        };

        var valid = validator.Validate(envelope, AuthorityContext());
        envelope.PredictionRouteRevoked.RemotePeerSessionGeneration = 0;
        var invalid = validator.Validate(envelope, AuthorityContext());

        Assert.True(valid.IsValid);
        Assert.Equal(ProtocolViolationCode.InvalidSession, invalid.Violation?.Code);
    }

    [Fact]
    public void AuthorityPublishesTickEffectiveResolvedMovementConfiguration()
    {
        var envelope = AuthorityEnvelope();
        envelope.AuthorityMovementConfigurationBatch = new AuthorityMovementConfigurationBatch
        {
            StreamSequence = 1,
        };
        envelope.AuthorityMovementConfigurationBatch.Updates.Add(
            BattleArena.Multiplayer.Replication.MovementConfigurationProtocolMapper.ToProtocol(
                new BattleArena.Multiplayer.Replication.NetworkMovementConfiguration(
                    2,
                    4,
                    new BattleArena.Core.Common.SimulationInstant(101),
                    new BattleArena.Core.Movement.MovementAttributeSnapshot(
                        3,
                        new BattleArena.Core.Movement.GroundMovementAttributes(
                            6, 13, 8, 10, 12, 20, 7, 2, -0.4)),
                    BattleArena.Core.Movement.MovementCapabilitySnapshot.CreateBaseFighter(4))));

        var result = validator.Validate(envelope, AuthorityContext());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ClientMaySendMovementPredictionBundleOnlyInClientDirection()
    {
        var clientEnvelope = ClientEnvelope();
        clientEnvelope.MovementPredictionBundle = PredictionProtocolTestData.MovementBundle();
        var authorityEnvelope = AuthorityEnvelope();
        authorityEnvelope.MovementPredictionBundle = PredictionProtocolTestData.MovementBundle();

        var client = validator.Validate(clientEnvelope, ClientContext());
        var authority = validator.Validate(authorityEnvelope, AuthorityContext());

        Assert.True(client.IsValid);
        Assert.False(authority.IsValid);
        Assert.Equal(ProtocolViolationCode.UnexpectedMessageDirection, authority.Violation?.Code);
    }

    [Fact]
    public void AuthorityDiagnosticFormattingRedactsPairCredential()
    {
        var envelope = AuthorityEnvelope();
        envelope.PredictionRouteAuthorization = Authorization(
            ProtocolConstants.PredictionRouteCredentialBytes);
        var encodedCredential = Convert.ToBase64String(
            envelope.PredictionRouteAuthorization.RouteCredential.ToByteArray());

        var formatted = new ProtobufDiagnosticFormatter().Format(envelope);

        Assert.DoesNotContain(encodedCredential, formatted, StringComparison.Ordinal);
    }

    private static PredictionRouteAuthorization Authorization(int credentialBytes) => new()
    {
        LocalSessionPeerId = 2,
        LocalPeerSessionGeneration = 4,
        RemoteSessionPeerId = 3,
        RemotePeerSessionGeneration = 5,
        PredictionRouteGeneration = 6,
        RemoteDescriptor = PredictionProtocolTestData.RouteDescriptor(),
        RouteCredential = ByteString.CopyFrom(
            Enumerable.Repeat((byte)0x5a, credentialBytes).ToArray()),
        ExpiresAuthorityTick = 10_000,
    };

    private static PredictionPeerRosterEntry Peer(ulong id, bool isAuthority) => new()
    {
        SessionPeerId = id,
        PeerSessionGeneration = 1,
        PlayerId = id,
        CombatantId = id,
        DisplayName = $"Peer{id}",
        IsAuthority = isAuthority,
    };

    private static PacketEnvelope ClientEnvelope() => BaseEnvelope();

    private static PacketEnvelope AuthorityEnvelope() => BaseEnvelope();

    private static PacketEnvelope BaseEnvelope() => new()
    {
        ProtocolVersion = ProtocolConstants.CurrentVersion,
        SessionId = SessionId,
        Sequence = 1,
        SimulationTick = 100,
    };

    private static ProtocolValidationContext ClientContext() =>
        new(RemoteEndpointRole.Client, SessionId, SessionEstablished: true);

    private static ProtocolValidationContext AuthorityContext() =>
        new(RemoteEndpointRole.Authority, SessionId, SessionEstablished: true);
}
