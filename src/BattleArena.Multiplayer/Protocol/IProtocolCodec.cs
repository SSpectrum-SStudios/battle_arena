using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Protocol;

public interface IProtocolCodec
{
    byte[] Encode(PacketEnvelope envelope);

    ProtocolDecodeResult Decode(ReadOnlySpan<byte> packet);
}
