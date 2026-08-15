namespace BattleArena.Multiplayer.Timing;

public readonly record struct PredictionTimingContext(
    uint SimulationTicksPerSecond,
    NetworkPathEstimate Path,
    int RecentBufferUnderruns);
