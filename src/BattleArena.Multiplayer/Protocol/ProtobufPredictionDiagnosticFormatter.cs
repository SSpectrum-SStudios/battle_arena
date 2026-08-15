using BattleArena.Protocol.V1;
using Google.Protobuf;

namespace BattleArena.Multiplayer.Protocol;

public sealed class ProtobufPredictionDiagnosticFormatter
{
    public string Format(PredictionPacketEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var safeCopy = envelope.Clone();
        if (safeCopy.MeshHello is not null)
        {
            safeCopy.MeshHello.InitiatorNonce = ByteString.Empty;
        }

        if (safeCopy.MeshChallenge is not null)
        {
            safeCopy.MeshChallenge.ResponderNonce = ByteString.Empty;
            safeCopy.MeshChallenge.ResponderProof = ByteString.Empty;
        }

        if (safeCopy.MeshProof is not null)
        {
            safeCopy.MeshProof.InitiatorProof = ByteString.Empty;
        }

        if (safeCopy.MeshAccepted is not null)
        {
            safeCopy.MeshAccepted.ResponderConfirmation = ByteString.Empty;
        }

        return JsonFormatter.Default.Format(safeCopy);
    }
}
