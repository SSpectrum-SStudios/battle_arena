namespace BattleArena.Multiplayer.Protocol;

public sealed record ProtocolViolation(ProtocolViolationCode Code, string Message);
