using BattleArena.Protocol.V1;
using Google.Protobuf;

namespace BattleArena.Multiplayer.Protocol;

public sealed class ProtobufProtocolCodec : IProtocolCodec
{
    public byte[] Encode(PacketEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var encoded = envelope.ToByteArray();
        if (encoded.Length > ProtocolConstants.MaxPacketBytes)
        {
            throw new InvalidOperationException(
                $"Encoded packet size {encoded.Length} exceeds the {ProtocolConstants.MaxPacketBytes}-byte protocol limit.");
        }

        return encoded;
    }

    public ProtocolDecodeResult Decode(ReadOnlySpan<byte> packet)
    {
        if (packet.IsEmpty)
        {
            return ProtocolDecodeResult.Failure(
                ProtocolViolationCode.EmptyPacket,
                "A network packet cannot be empty.");
        }

        if (packet.Length > ProtocolConstants.MaxPacketBytes)
        {
            return ProtocolDecodeResult.Failure(
                ProtocolViolationCode.PacketTooLarge,
                $"Packet size {packet.Length} exceeds the {ProtocolConstants.MaxPacketBytes}-byte protocol limit.");
        }

        try
        {
            return ProtocolDecodeResult.Success(PacketEnvelope.Parser.ParseFrom(packet.ToArray()));
        }
        catch (InvalidProtocolBufferException exception)
        {
            return ProtocolDecodeResult.Failure(
                ProtocolViolationCode.MalformedPayload,
                $"The packet is not a valid protocol envelope: {exception.Message}");
        }
    }
}
