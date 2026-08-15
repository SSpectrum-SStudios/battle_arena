using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace BattleArena.Multiplayer.Protocol;

public sealed class HmacPredictionHandshakeAuthenticator : IPredictionHandshakeAuthenticator
{
    private static readonly byte[] Domain = Encoding.ASCII.GetBytes("BattleArena/PredictionMesh/v1");

    public byte[] CreateInitiatorProof(
        ReadOnlySpan<byte> routeCredential,
        PredictionHandshakeScope scope,
        ReadOnlySpan<byte> initiatorNonce,
        ReadOnlySpan<byte> responderNonce) =>
        CreateProof(routeCredential, scope, initiatorNonce, responderNonce, role: 1, boundSequence: 0);

    public bool VerifyInitiatorProof(
        ReadOnlySpan<byte> routeCredential,
        PredictionHandshakeScope scope,
        ReadOnlySpan<byte> initiatorNonce,
        ReadOnlySpan<byte> responderNonce,
        ReadOnlySpan<byte> proof) =>
        VerifyProof(CreateInitiatorProof(
            routeCredential, scope, initiatorNonce, responderNonce), proof);

    public byte[] CreateResponderProof(
        ReadOnlySpan<byte> routeCredential,
        PredictionHandshakeScope scope,
        ReadOnlySpan<byte> initiatorNonce,
        ReadOnlySpan<byte> responderNonce) =>
        CreateProof(routeCredential, scope, initiatorNonce, responderNonce, role: 2, boundSequence: 0);

    public bool VerifyResponderProof(
        ReadOnlySpan<byte> routeCredential,
        PredictionHandshakeScope scope,
        ReadOnlySpan<byte> initiatorNonce,
        ReadOnlySpan<byte> responderNonce,
        ReadOnlySpan<byte> proof) =>
        VerifyProof(CreateResponderProof(routeCredential, scope, initiatorNonce, responderNonce), proof);

    public byte[] CreateAcceptanceConfirmation(
        ReadOnlySpan<byte> routeCredential,
        PredictionHandshakeScope scope,
        ReadOnlySpan<byte> initiatorNonce,
        ReadOnlySpan<byte> responderNonce,
        ulong proofPacketSequence)
    {
        if (proofPacketSequence == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(proofPacketSequence));
        }

        return CreateProof(
            routeCredential,
            scope,
            initiatorNonce,
            responderNonce,
            role: 3,
            boundSequence: proofPacketSequence);
    }

    public bool VerifyAcceptanceConfirmation(
        ReadOnlySpan<byte> routeCredential,
        PredictionHandshakeScope scope,
        ReadOnlySpan<byte> initiatorNonce,
        ReadOnlySpan<byte> responderNonce,
        ulong proofPacketSequence,
        ReadOnlySpan<byte> confirmation) =>
        VerifyProof(
            CreateAcceptanceConfirmation(
                routeCredential,
                scope,
                initiatorNonce,
                responderNonce,
                proofPacketSequence),
            confirmation);

    private static byte[] CreateProof(
        ReadOnlySpan<byte> routeCredential,
        PredictionHandshakeScope scope,
        ReadOnlySpan<byte> initiatorNonce,
        ReadOnlySpan<byte> responderNonce,
        byte role,
        ulong boundSequence)
    {
        scope.Validate();
        ValidateCredentialAndNonce(routeCredential, initiatorNonce, responderNonce, role);

        var transcript = new byte[
            Domain.Length + 1 + sizeof(ulong) * 3 + sizeof(uint) * 3 +
            initiatorNonce.Length + responderNonce.Length + sizeof(ulong)];
        var span = transcript.AsSpan();
        var offset = 0;
        Domain.CopyTo(span);
        offset += Domain.Length;
        span[offset++] = role;
        BinaryPrimitives.WriteUInt64BigEndian(span[offset..], scope.SessionId);
        offset += sizeof(ulong);
        BinaryPrimitives.WriteUInt64BigEndian(span[offset..], scope.InitiatorSessionPeerId);
        offset += sizeof(ulong);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], scope.InitiatorPeerSessionGeneration);
        offset += sizeof(uint);
        BinaryPrimitives.WriteUInt64BigEndian(span[offset..], scope.ResponderSessionPeerId);
        offset += sizeof(ulong);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], scope.ResponderPeerSessionGeneration);
        offset += sizeof(uint);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], scope.PredictionRouteGeneration);
        offset += sizeof(uint);
        initiatorNonce.CopyTo(span[offset..]);
        offset += initiatorNonce.Length;
        responderNonce.CopyTo(span[offset..]);
        offset += responderNonce.Length;
        BinaryPrimitives.WriteUInt64BigEndian(span[offset..], boundSequence);

        return HMACSHA256.HashData(routeCredential, transcript);
    }

    private static bool VerifyProof(byte[] expected, ReadOnlySpan<byte> supplied) =>
        supplied.Length == ProtocolConstants.PredictionHandshakeProofBytes &&
        CryptographicOperations.FixedTimeEquals(expected, supplied);

    private static void ValidateCredentialAndNonce(
        ReadOnlySpan<byte> credential,
        ReadOnlySpan<byte> initiatorNonce,
        ReadOnlySpan<byte> responderNonce,
        byte role)
    {
        if (credential.Length != ProtocolConstants.PredictionRouteCredentialBytes)
        {
            throw new ArgumentException("Route credential has the wrong length.", nameof(credential));
        }

        if (initiatorNonce.Length != ProtocolConstants.PredictionHandshakeNonceBytes ||
            responderNonce.Length != ProtocolConstants.PredictionHandshakeNonceBytes)
        {
            throw new ArgumentException("Handshake nonce has the wrong length.", nameof(initiatorNonce));
        }
    }
}
