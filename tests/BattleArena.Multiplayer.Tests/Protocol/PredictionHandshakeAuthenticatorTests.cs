using BattleArena.Multiplayer.Protocol;

namespace BattleArena.Multiplayer.Tests.Protocol;

public sealed class PredictionHandshakeAuthenticatorTests
{
    private readonly HmacPredictionHandshakeAuthenticator authenticator = new();
    private readonly byte[] credential = Enumerable.Repeat((byte)0x5a, 32).ToArray();
    private readonly byte[] initiatorNonce = Enumerable.Repeat((byte)0x11, 32).ToArray();
    private readonly byte[] responderNonce = Enumerable.Repeat((byte)0x22, 32).ToArray();
    private readonly PredictionHandshakeScope scope = new(73, 2, 4, 3, 6, 8);

    [Fact]
    public void BothEndpointsProveCredentialWithoutSendingIt()
    {
        var initiatorProof = authenticator.CreateInitiatorProof(
            credential, scope, initiatorNonce, responderNonce);
        var responderProof = authenticator.CreateResponderProof(
            credential, scope, initiatorNonce, responderNonce);

        Assert.True(authenticator.VerifyInitiatorProof(
            credential, scope, initiatorNonce, responderNonce, initiatorProof));
        Assert.True(authenticator.VerifyResponderProof(
            credential, scope, initiatorNonce, responderNonce, responderProof));
        Assert.NotEqual(credential, initiatorProof);
        Assert.NotEqual(credential, responderProof);
    }

    [Fact]
    public void ProofIsBoundToBothPeersGenerationsRouteAndNonce()
    {
        var proof = authenticator.CreateInitiatorProof(
            credential, scope, initiatorNonce, responderNonce);
        var replacedRoute = scope with { PredictionRouteGeneration = 9 };
        var reconnectedPeer = scope with { ResponderPeerSessionGeneration = 7 };
        var differentNonce = initiatorNonce.ToArray();
        differentNonce[0]++;

        Assert.False(authenticator.VerifyInitiatorProof(
            credential, replacedRoute, initiatorNonce, responderNonce, proof));
        Assert.False(authenticator.VerifyInitiatorProof(
            credential, reconnectedPeer, initiatorNonce, responderNonce, proof));
        Assert.False(authenticator.VerifyInitiatorProof(
            credential, scope, differentNonce, responderNonce, proof));
    }

    [Fact]
    public void ResponderProofCannotBeSubstitutedForInitiatorProof()
    {
        var responderProof = authenticator.CreateResponderProof(
            credential, scope, initiatorNonce, responderNonce);

        Assert.False(authenticator.VerifyInitiatorProof(
            credential, scope, initiatorNonce, responderNonce, responderProof));
    }

    [Fact]
    public void FinalKeyConfirmationIsBoundToProofSequence()
    {
        var confirmation = authenticator.CreateAcceptanceConfirmation(
            credential, scope, initiatorNonce, responderNonce, 20);

        Assert.True(authenticator.VerifyAcceptanceConfirmation(
            credential, scope, initiatorNonce, responderNonce, 20, confirmation));
        Assert.False(authenticator.VerifyAcceptanceConfirmation(
            credential, scope, initiatorNonce, responderNonce, 21, confirmation));
    }
}
