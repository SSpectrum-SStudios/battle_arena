using BattleArena.Protocol.V1;
using Google.Protobuf;

namespace BattleArena.Multiplayer.Protocol;

public sealed class ProtobufPredictionProtocolCodec : IPredictionProtocolCodec
{
    public byte[] Encode(PredictionPacketEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var encoded = envelope.ToByteArray();
        var limit = GetPacketLimit(envelope.PayloadCase);
        if (encoded.Length > limit)
        {
            throw new InvalidOperationException(
                $"Encoded prediction packet size {encoded.Length} exceeds the " +
                $"{limit}-byte prediction limit for {envelope.PayloadCase}.");
        }

        return encoded;
    }

    public PredictionProtocolDecodeResult Decode(ReadOnlySpan<byte> packet)
    {
        if (packet.IsEmpty)
        {
            return PredictionProtocolDecodeResult.Failure(
                ProtocolViolationCode.EmptyPacket,
                "A prediction packet cannot be empty.");
        }

        if (packet.Length > ProtocolConstants.MaxPredictionControlPacketBytes)
        {
            return PredictionProtocolDecodeResult.Failure(
                ProtocolViolationCode.PacketTooLarge,
                $"Prediction packet size {packet.Length} exceeds the " +
                $"{ProtocolConstants.MaxPredictionControlPacketBytes}-byte control limit.");
        }

        try
        {
            var envelope = PredictionPacketEnvelope.Parser.ParseFrom(packet.ToArray());
            var limit = GetPacketLimit(envelope.PayloadCase);
            return packet.Length <= limit
                ? PredictionProtocolDecodeResult.Success(envelope)
                : PredictionProtocolDecodeResult.Failure(
                    ProtocolViolationCode.PacketTooLarge,
                    $"Prediction packet size {packet.Length} exceeds the " +
                    $"{limit}-byte limit for {envelope.PayloadCase}.");
        }
        catch (InvalidProtocolBufferException exception)
        {
            return PredictionProtocolDecodeResult.Failure(
                ProtocolViolationCode.MalformedPayload,
                $"The packet is not a valid prediction envelope: {exception.Message}");
        }
    }

    private static int GetPacketLimit(PredictionPacketEnvelope.PayloadOneofCase payload) =>
        payload is PredictionPacketEnvelope.PayloadOneofCase.MovementPredictionBundle or
            PredictionPacketEnvelope.PayloadOneofCase.DirectClockProbe or
            PredictionPacketEnvelope.PayloadOneofCase.DirectClockReply
            ? ProtocolConstants.MaxPredictionUnreliablePacketBytes
            : ProtocolConstants.MaxPredictionControlPacketBytes;
}
