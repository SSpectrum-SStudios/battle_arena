using BattleArena.Protocol.V1;
using Google.Protobuf;

namespace BattleArena.Multiplayer.Protocol;

public sealed class ProtobufDiagnosticFormatter : IProtocolDiagnosticFormatter
{
    public string Format(PacketEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var safeCopy = envelope.Clone();
        if (safeCopy.JoinAccepted is not null)
        {
            safeCopy.JoinAccepted.ReconnectToken = ByteString.Empty;
        }

        if (safeCopy.ReconnectRequest is not null)
        {
            safeCopy.ReconnectRequest.ReconnectToken = ByteString.Empty;
        }

        return JsonFormatter.Default.Format(safeCopy);
    }
}
