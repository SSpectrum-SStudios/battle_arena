using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Protocol;

public interface IProtocolDiagnosticFormatter
{
    string Format(PacketEnvelope envelope);
}
