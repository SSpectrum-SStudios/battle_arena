namespace BattleArena.Multiplayer.Connection;

public sealed record ClientSessionIdentity(
    ulong SessionId,
    SessionPeerId SessionPeerId,
    ConnectionGeneration ConnectionGeneration,
    ulong PlayerId,
    ulong CombatantId,
    byte[] ReconnectToken,
    uint SimulationTicksPerSecond,
    uint SnapshotRate,
    uint CheckpointIntervalTicks);
