namespace BattleArena.Multiplayer.Connection;

public sealed record ClientSessionIdentity(
    ulong SessionId,
    ulong PlayerId,
    ulong CombatantId,
    byte[] ReconnectToken,
    uint SimulationTicksPerSecond,
    uint SnapshotRate,
    uint CheckpointIntervalTicks);
