using BattleArena.Multiplayer.Protocol;
using BattleArena.Protocol.V1;
using Google.Protobuf;

namespace BattleArena.Multiplayer.Tests.Protocol;

public sealed class InboundMessageValidatorTests
{
    private const ulong SessionId = 73;
    private readonly InboundMessageValidator validator = new();

    [Fact]
    public void ValidClientInputIsAcceptedForEstablishedSession()
    {
        var envelope = CreateInputEnvelope();

        var result = validator.Validate(envelope, EstablishedClientContext());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ClientCannotSendAuthoritySnapshot()
    {
        var envelope = CreateEnvelope();
        envelope.AuthoritySnapshot = new AuthoritySnapshot();

        var result = validator.Validate(envelope, EstablishedClientContext());

        Assert.False(result.IsValid);
        Assert.Equal(ProtocolViolationCode.UnexpectedMessageDirection, result.Violation?.Code);
    }

    [Fact]
    public void EstablishedPacketMustMatchTransportSession()
    {
        var envelope = CreateInputEnvelope();
        envelope.SessionId = 999;

        var result = validator.Validate(envelope, EstablishedClientContext());

        Assert.False(result.IsValid);
        Assert.Equal(ProtocolViolationCode.InvalidSession, result.Violation?.Code);
    }

    [Fact]
    public void NonFiniteInputIsRejected()
    {
        var envelope = CreateInputEnvelope();
        envelope.ClientInputBatch.Frames[0].MoveX = float.NaN;

        var result = validator.Validate(envelope, EstablishedClientContext());

        Assert.False(result.IsValid);
        Assert.Equal(ProtocolViolationCode.InvalidNumericValue, result.Violation?.Code);
    }

    [Fact]
    public void InputFramesMustBeStrictlyOrderedWithinBatch()
    {
        var envelope = CreateInputEnvelope();
        envelope.ClientInputBatch.Frames.Add(new ClientInputFrame
        {
            InputSequence = 1,
            ClientTick = 2,
        });

        var result = validator.Validate(envelope, EstablishedClientContext());

        Assert.False(result.IsValid);
        Assert.Equal(ProtocolViolationCode.InvalidSequence, result.Violation?.Code);
    }

    [Fact]
    public void InitialJoinRequiresNoClaimedSessionAndValidNonce()
    {
        var envelope = new PacketEnvelope
        {
            ProtocolVersion = ProtocolConstants.CurrentVersion,
            Sequence = 1,
            JoinRequest = new JoinRequest
            {
                ClientProtocolVersion = ProtocolConstants.CurrentVersion,
                DisplayName = "Player",
                ClientNonce = ByteString.CopyFrom(new byte[ProtocolConstants.ClientNonceBytes]),
            },
        };
        var context = new ProtocolValidationContext(RemoteEndpointRole.Client, null, false);

        var result = validator.Validate(envelope, context);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ReconnectTokenMustHaveExactLength()
    {
        var envelope = CreateEnvelope();
        envelope.ReconnectRequest = new ReconnectRequest
        {
            SessionId = SessionId,
            PlayerId = 1,
            ReconnectToken = ByteString.CopyFrom(new byte[4]),
        };

        var context = new ProtocolValidationContext(RemoteEndpointRole.Client, SessionId, false);

        var result = validator.Validate(envelope, context);

        Assert.False(result.IsValid);
        Assert.Equal(ProtocolViolationCode.InvalidCredential, result.Violation?.Code);
    }

    [Fact]
    public void AuthorityCanEstablishSessionWithJoinAcceptance()
    {
        var envelope = CreateEnvelope();
        envelope.JoinAccepted = new JoinAccepted
        {
            SessionId = SessionId,
            PlayerId = 10,
            CombatantId = 11,
            ReconnectToken = ByteString.CopyFrom(new byte[ProtocolConstants.ReconnectTokenBytes]),
            SimulationTicksPerSecond = 60,
            SnapshotRate = 30,
            CheckpointIntervalTicks = 60,
        };
        var context = new ProtocolValidationContext(RemoteEndpointRole.Authority, null, false);

        var result = validator.Validate(envelope, context);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void UnauthenticatedPeerCanRequestReconnectToKnownSession()
    {
        var envelope = CreateEnvelope();
        envelope.ReconnectRequest = new ReconnectRequest
        {
            SessionId = SessionId,
            PlayerId = 1,
            ReconnectToken = ByteString.CopyFrom(new byte[ProtocolConstants.ReconnectTokenBytes]),
        };
        var context = new ProtocolValidationContext(RemoteEndpointRole.Client, SessionId, false);

        var result = validator.Validate(envelope, context);

        Assert.True(result.IsValid);
    }

    private static PacketEnvelope CreateInputEnvelope()
    {
        var envelope = CreateEnvelope();
        envelope.ClientInputBatch = new ClientInputBatch();
        envelope.ClientInputBatch.Frames.Add(new ClientInputFrame
        {
            InputSequence = 1,
            ClientTick = 1,
            MoveX = 0.5f,
            MoveZ = -0.25f,
            ViewYawRadians = 0.2f,
            ViewPitchRadians = -0.1f,
            ButtonBits = 3,
        });
        return envelope;
    }

    private static PacketEnvelope CreateEnvelope() => new()
    {
        ProtocolVersion = ProtocolConstants.CurrentVersion,
        SessionId = SessionId,
        Sequence = 1,
        SimulationTick = 1,
    };

    private static ProtocolValidationContext EstablishedClientContext() =>
        new(RemoteEndpointRole.Client, SessionId, true);
}
