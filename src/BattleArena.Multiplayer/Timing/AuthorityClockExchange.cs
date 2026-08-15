namespace BattleArena.Multiplayer.Timing;

public readonly record struct AuthorityClockExchange(
    ulong ClientSendTimestampMicroseconds,
    ulong AuthorityReceiveTimestampMicroseconds,
    ulong AuthoritySendTimestampMicroseconds,
    ulong ClientReceiveTimestampMicroseconds,
    ulong AuthorityTick,
    uint SimulationTicksPerSecond);
