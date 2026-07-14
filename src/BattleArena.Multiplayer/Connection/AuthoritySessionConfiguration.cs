namespace BattleArena.Multiplayer.Connection;

public sealed record AuthoritySessionConfiguration
{
    public AuthoritySessionConfiguration(
        uint simulationTicksPerSecond,
        uint snapshotRate,
        uint checkpointIntervalTicks,
        int maximumRemotePlayers = 7)
    {
        if (simulationTicksPerSecond == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(simulationTicksPerSecond));
        }

        if (snapshotRate == 0 || snapshotRate > simulationTicksPerSecond)
        {
            throw new ArgumentOutOfRangeException(nameof(snapshotRate));
        }

        if (checkpointIntervalTicks == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(checkpointIntervalTicks));
        }

        if (maximumRemotePlayers is < 1 or > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRemotePlayers));
        }

        SimulationTicksPerSecond = simulationTicksPerSecond;
        SnapshotRate = snapshotRate;
        CheckpointIntervalTicks = checkpointIntervalTicks;
        MaximumRemotePlayers = maximumRemotePlayers;
    }

    public uint SimulationTicksPerSecond { get; }

    public uint SnapshotRate { get; }

    public uint CheckpointIntervalTicks { get; }

    public int MaximumRemotePlayers { get; }
}
