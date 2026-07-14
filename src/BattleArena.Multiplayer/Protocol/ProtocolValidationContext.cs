namespace BattleArena.Multiplayer.Protocol;

public readonly record struct ProtocolValidationContext(
    RemoteEndpointRole RemoteRole,
    ulong? ExpectedSessionId,
    bool SessionEstablished);
