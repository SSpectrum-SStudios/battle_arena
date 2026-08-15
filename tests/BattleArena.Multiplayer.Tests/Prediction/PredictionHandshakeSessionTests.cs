using BattleArena.Multiplayer.Connection;
using BattleArena.Multiplayer.Prediction;
using BattleArena.Multiplayer.Protocol;
using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Tests.Prediction;

public sealed class PredictionHandshakeSessionTests
{
    [Fact]
    public void FourMessageChallengeResponseAuthenticatesBothEndpoints()
    {
        var pair = CreatePair();

        var hello = pair.Initiator.CreateHello(1, 100, 200);
        var challenge = pair.Responder.ReceiveHello(hello, 2, 100, 200);
        var proof = pair.Initiator.ReceiveChallenge(challenge.Response!, 3, 101, 201);
        var accepted = pair.Responder.ReceiveProof(proof.Response!, 4, 101, 201);
        var completed = pair.Initiator.ReceiveAccepted(accepted.Response!, 202);

        Assert.True(challenge.Accepted);
        Assert.True(proof.Accepted);
        Assert.True(accepted.Accepted);
        Assert.True(completed.Accepted);
        Assert.True(pair.Initiator.IsAuthenticated);
        Assert.True(pair.Responder.IsAuthenticated);
    }

    [Fact]
    public void CapturedProofCannotAnswerFreshResponderChallenge()
    {
        var pair = CreatePair();
        var hello = pair.Initiator.CreateHello(1, 100, 200);
        var oldChallenge = pair.Responder.ReceiveHello(hello, 2, 100, 200).Response!;
        var oldProof = pair.Initiator.ReceiveChallenge(oldChallenge, 3, 101, 201).Response!;
        var replacementResponder = NewSession(pair.ResponderRoute, pair.Generator);

        var freshChallenge = replacementResponder.ReceiveHello(hello, 20, 110, 210).Response!;
        var replay = oldProof.Clone();
        replay.PacketSequence = 21;
        replay.MeshProof.ChallengePacketSequence = freshChallenge.PacketSequence;
        var result = replacementResponder.ReceiveProof(replay, 22, 111, 211);

        Assert.False(result.Accepted);
        Assert.Equal(ProtocolViolationCode.InvalidCredential, result.Violation?.Code);
        Assert.False(replacementResponder.IsAuthenticated);
    }

    [Fact]
    public void ExpiredRouteCannotCompleteHandshake()
    {
        var pair = CreatePair(expires: 205);
        var hello = pair.Initiator.CreateHello(1, 100, 200);

        var result = pair.Responder.ReceiveHello(hello, 2, 100, 205);

        Assert.False(result.Accepted);
        Assert.Equal(ProtocolViolationCode.InvalidCredential, result.Violation?.Code);
    }

    [Fact]
    public void LostFinalAcceptanceIsRecoveredByDuplicateProof()
    {
        var pair = CreatePair();
        var hello = pair.Initiator.CreateHello(1, 100, 200);
        var challenge = pair.Responder.ReceiveHello(hello, 2, 100, 200).Response!;
        var proof = pair.Initiator.ReceiveChallenge(challenge, 3, 101, 201).Response!;
        var lostAccepted = pair.Responder.ReceiveProof(proof, 4, 101, 201).Response!;

        var resent = pair.Responder.ReceiveProof(proof.Clone(), 5, 102, 202);
        var completed = pair.Initiator.ReceiveAccepted(resent.Response!, 202);

        Assert.Equal(lostAccepted, resent.Response);
        Assert.True(completed.Accepted);
        Assert.True(pair.Initiator.IsAuthenticated);
        Assert.True(pair.Responder.IsAuthenticated);
    }

    [Fact]
    public void DuplicateHelloAndChallengeReturnCachedResponsesAndResetStartsFresh()
    {
        var pair = CreatePair();
        var hello = pair.Initiator.CreateHello(1, 100, 200);
        var firstChallenge = pair.Responder.ReceiveHello(hello, 2, 100, 200).Response!;
        var duplicateChallenge = pair.Responder.ReceiveHello(hello.Clone(), 99, 105, 205).Response!;
        var firstProof = pair.Initiator.ReceiveChallenge(firstChallenge, 3, 101, 201).Response!;
        var duplicateProof = pair.Initiator.ReceiveChallenge(firstChallenge.Clone(), 98, 105, 205).Response!;

        Assert.Equal(firstChallenge, duplicateChallenge);
        Assert.Equal(firstProof, duplicateProof);
        pair.Initiator.Reset();
        Assert.Equal(PredictionHandshakeState.ReadyToInitiate, pair.Initiator.State);
        Assert.NotEqual(hello.MeshHello.InitiatorNonce,
            pair.Initiator.CreateHello(10, 110, 210).MeshHello.InitiatorNonce);
    }

    [Fact]
    public void ForgedOrReplayedAcceptanceCannotAuthenticateInitiator()
    {
        var pair = CreatePair();
        var hello = pair.Initiator.CreateHello(1, 100, 200);
        var challenge = pair.Responder.ReceiveHello(hello, 2, 100, 200).Response!;
        var proof = pair.Initiator.ReceiveChallenge(challenge, 3, 101, 201).Response!;
        var forged = new PredictionPacketEnvelope
        {
            ProtocolVersion = ProtocolConstants.CurrentVersion,
            SessionId = 73,
            SourceSessionPeerId = 3,
            DestinationSessionPeerId = 2,
            SourcePeerSessionGeneration = 5,
            DestinationPeerSessionGeneration = 4,
            PredictionRouteGeneration = 6,
            PacketSequence = 4,
            ClientTick = 101,
            EstimatedAuthorityTick = 201,
            MeshAccepted = new PredictionMeshAccepted
            {
                ProofPacketSequence = proof.PacketSequence,
                ResponderConfirmation = Google.Protobuf.ByteString.CopyFrom(
                    Enumerable.Repeat((byte)0x33, 32).ToArray()),
            },
        };

        var result = pair.Initiator.ReceiveAccepted(forged, 202);

        Assert.False(result.Accepted);
        Assert.Equal(ProtocolViolationCode.InvalidCredential, result.Violation?.Code);
        Assert.False(pair.Initiator.IsAuthenticated);
    }

    private static HandshakePair CreatePair(ulong expires = 1_000)
    {
        var credential = Enumerable.Repeat((byte)0x5a, 32).ToArray();
        var generator = new PredictionTestCredentialGenerator();
        var initiatorRoute = Route(2, 3, credential, expires);
        var responderRoute = Route(3, 2, credential, expires);
        return new HandshakePair(
            NewSession(initiatorRoute, generator),
            NewSession(responderRoute, generator),
            responderRoute,
            generator);
    }

    private static PredictionHandshakeSession NewSession(
        AuthorizedPredictionRoute route,
        IPredictionRouteCredentialGenerator generator) =>
        new(
            route,
            new HmacPredictionHandshakeAuthenticator(),
            generator,
            new PredictionInboundMessageValidator());

    private static AuthorizedPredictionRoute Route(
        ulong local,
        ulong remote,
        byte[] credential,
        ulong expires) => new(
        73,
        new SessionPeerId(local),
        new ConnectionGeneration(local == 2 ? 4U : 5U),
        new SessionPeerId(remote),
        new ConnectionGeneration(remote == 2 ? 4U : 5U),
        6,
        AuthorityPredictionRouteBrokerTests.Descriptor(7780 + (int)remote),
        credential.ToArray(),
        expires);

    private sealed record HandshakePair(
        PredictionHandshakeSession Initiator,
        PredictionHandshakeSession Responder,
        AuthorizedPredictionRoute ResponderRoute,
        PredictionTestCredentialGenerator Generator);
}
