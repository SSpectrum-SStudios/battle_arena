using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Protocol;

public readonly record struct ProtocolDecodeResult(
    PacketEnvelope? Envelope,
    ProtocolViolation? Violation)
{
    public bool IsSuccess => Envelope is not null && Violation is null;

    public static ProtocolDecodeResult Success(PacketEnvelope envelope) => new(envelope, null);

    public static ProtocolDecodeResult Failure(ProtocolViolationCode code, string message) =>
        new(null, new ProtocolViolation(code, message));
}
