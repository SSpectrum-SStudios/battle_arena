namespace BattleArena.Multiplayer.Protocol;

public enum ProtocolViolationCode
{
    EmptyPacket,
    PacketTooLarge,
    MalformedPayload,
    UnsupportedProtocolVersion,
    MissingPayload,
    InvalidSequence,
    UnexpectedMessageDirection,
    InvalidSession,
    InvalidCollectionCount,
    InvalidNumericValue,
    InvalidEnumValue,
    InvalidTextValue,
    InvalidCredential,
}
