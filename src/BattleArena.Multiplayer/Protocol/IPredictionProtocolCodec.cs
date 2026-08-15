using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Protocol;

public interface IPredictionProtocolCodec
{
    byte[] Encode(PredictionPacketEnvelope envelope);

    PredictionProtocolDecodeResult Decode(ReadOnlySpan<byte> packet);
}
