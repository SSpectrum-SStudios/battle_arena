using BattleArena.Multiplayer.Protocol;
using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Prediction;

public sealed record PredictionHandshakeResult(
    bool Accepted,
    PredictionPacketEnvelope? Response,
    ProtocolViolation? Violation)
{
    public static PredictionHandshakeResult Success(PredictionPacketEnvelope? response = null) =>
        new(true, response, null);

    public static PredictionHandshakeResult Failure(ProtocolViolation violation) =>
        new(false, null, violation);
}
