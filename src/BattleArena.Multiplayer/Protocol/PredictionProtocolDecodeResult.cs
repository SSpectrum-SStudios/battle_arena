using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Protocol;

public readonly record struct PredictionProtocolDecodeResult(
    PredictionPacketEnvelope? Envelope,
    ProtocolViolation? Violation)
{
    public bool IsSuccess => Envelope is not null && Violation is null;

    public static PredictionProtocolDecodeResult Success(PredictionPacketEnvelope envelope) =>
        new(envelope, null);

    public static PredictionProtocolDecodeResult Failure(
        ProtocolViolationCode code,
        string message) => new(null, new ProtocolViolation(code, message));
}
