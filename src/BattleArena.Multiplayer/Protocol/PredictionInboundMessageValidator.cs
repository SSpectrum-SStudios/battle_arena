using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Protocol;

public sealed class PredictionInboundMessageValidator
{
    public ProtocolValidationResult Validate(
        PredictionPacketEnvelope envelope,
        PredictionProtocolValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (envelope.ProtocolVersion != ProtocolConstants.CurrentVersion)
        {
            return Invalid(
                ProtocolViolationCode.UnsupportedProtocolVersion,
                $"Prediction protocol version {envelope.ProtocolVersion} is not supported.");
        }

        if (envelope.SessionId == 0 ||
            envelope.SessionId != context.ExpectedSessionId ||
            envelope.SourceSessionPeerId != context.RemoteSessionPeerId ||
            envelope.DestinationSessionPeerId != context.LocalSessionPeerId ||
            envelope.SourceSessionPeerId == envelope.DestinationSessionPeerId)
        {
            return Invalid(
                ProtocolViolationCode.InvalidSession,
                "Prediction packet does not match the authorized session peer pair.");
        }

        if (envelope.SourcePeerSessionGeneration == 0 ||
            envelope.SourcePeerSessionGeneration != context.ExpectedRemotePeerSessionGeneration ||
            envelope.DestinationPeerSessionGeneration == 0 ||
            envelope.DestinationPeerSessionGeneration != context.ExpectedLocalPeerSessionGeneration ||
            envelope.PredictionRouteGeneration == 0 ||
            envelope.PredictionRouteGeneration != context.ExpectedPredictionRouteGeneration)
        {
            return Invalid(
                ProtocolViolationCode.InvalidSession,
                "Prediction packet uses a stale peer-session or route generation.");
        }

        if (envelope.PacketSequence == 0)
        {
            return Invalid(
                ProtocolViolationCode.InvalidSequence,
                "Prediction packet sequence must be positive.");
        }

        if (context.RouteExpiresAuthorityTick == 0 ||
            context.CurrentEstimatedAuthorityTick == 0 ||
            context.CurrentEstimatedAuthorityTick >= context.RouteExpiresAuthorityTick)
        {
            return Invalid(
                ProtocolViolationCode.InvalidCredential,
                "Prediction route authorization has expired.");
        }

        if (envelope.PayloadCase == PredictionPacketEnvelope.PayloadOneofCase.None)
        {
            return Invalid(
                ProtocolViolationCode.MissingPayload,
                "Prediction envelope does not contain a payload.");
        }

        var isDataPacket = envelope.PayloadCase is
            PredictionPacketEnvelope.PayloadOneofCase.MovementPredictionBundle or
            PredictionPacketEnvelope.PayloadOneofCase.DirectClockProbe or
            PredictionPacketEnvelope.PayloadOneofCase.DirectClockReply;
        if (isDataPacket && !context.RouteAuthenticated)
        {
            return Invalid(
                ProtocolViolationCode.InvalidCredential,
                "Prediction data is not accepted before the route handshake completes.");
        }

        return envelope.PayloadCase switch
        {
            PredictionPacketEnvelope.PayloadOneofCase.MeshHello =>
                ValidateHello(envelope.MeshHello),
            PredictionPacketEnvelope.PayloadOneofCase.MeshChallenge =>
                ValidateChallenge(envelope.MeshChallenge),
            PredictionPacketEnvelope.PayloadOneofCase.MeshProof =>
                ValidateProof(envelope.MeshProof),
            PredictionPacketEnvelope.PayloadOneofCase.MeshAccepted =>
                ValidateAccepted(envelope.MeshAccepted),
            PredictionPacketEnvelope.PayloadOneofCase.MeshRejected =>
                ValidateRejected(envelope.MeshRejected),
            PredictionPacketEnvelope.PayloadOneofCase.MovementPredictionBundle =>
                ValidateMovement(envelope),
            PredictionPacketEnvelope.PayloadOneofCase.DirectClockProbe =>
                ValidateClockProbe(envelope.DirectClockProbe),
            PredictionPacketEnvelope.PayloadOneofCase.DirectClockReply =>
                ValidateClockReply(envelope.DirectClockReply),
            _ => Invalid(
                ProtocolViolationCode.MissingPayload,
                "Prediction payload type is unsupported."),
        };
    }

    private static ProtocolValidationResult ValidateHello(PredictionMeshHello hello) =>
        hello.InitiatorNonce.Length == ProtocolConstants.PredictionHandshakeNonceBytes &&
        hello.InitiatorNonce.ToByteArray().Any(value => value != 0)
            ? ProtocolValidationResult.Valid
            : Invalid(
                ProtocolViolationCode.InvalidCredential,
                "Prediction mesh hello nonce has an invalid length.");

    private static ProtocolValidationResult ValidateChallenge(PredictionMeshChallenge challenge) =>
        challenge.HelloPacketSequence != 0 &&
        challenge.ResponderNonce.Length == ProtocolConstants.PredictionHandshakeNonceBytes &&
        challenge.ResponderNonce.ToByteArray().Any(value => value != 0) &&
        challenge.ResponderProof.Length == ProtocolConstants.PredictionHandshakeProofBytes
            ? ProtocolValidationResult.Valid
            : Invalid(
                ProtocolViolationCode.InvalidCredential,
                "Prediction mesh challenge requires its hello sequence, nonce, and proof.");

    private static ProtocolValidationResult ValidateProof(PredictionMeshProof proof) =>
        proof.ChallengePacketSequence != 0 &&
        proof.InitiatorProof.Length == ProtocolConstants.PredictionHandshakeProofBytes
            ? ProtocolValidationResult.Valid
            : Invalid(
                ProtocolViolationCode.InvalidCredential,
                "Prediction mesh proof requires its challenge sequence and proof bytes.");

    private static ProtocolValidationResult ValidateAccepted(PredictionMeshAccepted accepted) =>
        accepted.ProofPacketSequence != 0 &&
        accepted.ResponderConfirmation.Length == ProtocolConstants.PredictionHandshakeProofBytes
            ? ProtocolValidationResult.Valid
            : Invalid(
                ProtocolViolationCode.InvalidCredential,
                "Prediction mesh acceptance must identify its proof packet.");

    private static ProtocolValidationResult ValidateRejected(PredictionMeshRejected rejected)
    {
        if (rejected.HelloPacketSequence == 0)
        {
            return Invalid(
                ProtocolViolationCode.InvalidSequence,
                "Prediction mesh rejection must identify its hello packet.");
        }

        return Enum.IsDefined(rejected.Reason) &&
               rejected.Reason != PredictionMeshRejectionReason.Unspecified
            ? ProtocolValidationResult.Valid
            : Invalid(
                ProtocolViolationCode.InvalidEnumValue,
                "Prediction mesh rejection reason is not recognized.");
    }

    private static ProtocolValidationResult ValidateMovement(
        PredictionPacketEnvelope envelope)
    {
        if (envelope.ClientTick == 0 || envelope.EstimatedAuthorityTick == 0)
        {
            return Invalid(
                ProtocolViolationCode.InvalidNumericValue,
                "Direct movement packet requires client and estimated authority ticks.");
        }

        var bundle = envelope.MovementPredictionBundle;
        var validation = InboundMessageValidator.ValidateMovementPredictionBundle(bundle);
        if (!validation.IsValid)
        {
            return validation;
        }

        var newest = bundle.Commands[^1];
        return newest.ClientTick == envelope.ClientTick &&
               newest.EstimatedAuthorityTick == envelope.EstimatedAuthorityTick
            ? ProtocolValidationResult.Valid
            : Invalid(
                ProtocolViolationCode.InvalidNumericValue,
                "Prediction envelope timing does not match its newest command.");
    }

    private static ProtocolValidationResult ValidateClockProbe(DirectClockProbe probe) =>
        probe.ProbeSequence != 0 && probe.SourceSendTimestampMicroseconds != 0
            ? ProtocolValidationResult.Valid
            : Invalid(
                ProtocolViolationCode.InvalidSequence,
                "Direct clock probe requires a sequence and timestamp.");

    private static ProtocolValidationResult ValidateClockReply(DirectClockReply reply) =>
        reply.ProbeSequence != 0 &&
        reply.SourceSendTimestampMicroseconds != 0 &&
        reply.ReceiverReceiveTimestampMicroseconds != 0 &&
        reply.ReceiverSendTimestampMicroseconds >= reply.ReceiverReceiveTimestampMicroseconds &&
        reply.ReceiverEstimatedAuthorityTick != 0
            ? ProtocolValidationResult.Valid
            : Invalid(
                ProtocolViolationCode.InvalidNumericValue,
                "Direct clock reply contains invalid timing fields.");

    private static ProtocolValidationResult Invalid(
        ProtocolViolationCode code,
        string message) => ProtocolValidationResult.Invalid(code, message);
}
