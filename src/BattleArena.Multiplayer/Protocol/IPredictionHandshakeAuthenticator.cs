namespace BattleArena.Multiplayer.Protocol;

public interface IPredictionHandshakeAuthenticator
{
    byte[] CreateInitiatorProof(
        ReadOnlySpan<byte> routeCredential,
        PredictionHandshakeScope scope,
        ReadOnlySpan<byte> initiatorNonce,
        ReadOnlySpan<byte> responderNonce);

    bool VerifyInitiatorProof(
        ReadOnlySpan<byte> routeCredential,
        PredictionHandshakeScope scope,
        ReadOnlySpan<byte> initiatorNonce,
        ReadOnlySpan<byte> responderNonce,
        ReadOnlySpan<byte> proof);

    byte[] CreateResponderProof(
        ReadOnlySpan<byte> routeCredential,
        PredictionHandshakeScope scope,
        ReadOnlySpan<byte> initiatorNonce,
        ReadOnlySpan<byte> responderNonce);

    bool VerifyResponderProof(
        ReadOnlySpan<byte> routeCredential,
        PredictionHandshakeScope scope,
        ReadOnlySpan<byte> initiatorNonce,
        ReadOnlySpan<byte> responderNonce,
        ReadOnlySpan<byte> proof);

    byte[] CreateAcceptanceConfirmation(
        ReadOnlySpan<byte> routeCredential,
        PredictionHandshakeScope scope,
        ReadOnlySpan<byte> initiatorNonce,
        ReadOnlySpan<byte> responderNonce,
        ulong proofPacketSequence);

    bool VerifyAcceptanceConfirmation(
        ReadOnlySpan<byte> routeCredential,
        PredictionHandshakeScope scope,
        ReadOnlySpan<byte> initiatorNonce,
        ReadOnlySpan<byte> responderNonce,
        ulong proofPacketSequence,
        ReadOnlySpan<byte> confirmation);
}
