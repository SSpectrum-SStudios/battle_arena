namespace BattleArena.Multiplayer.Timing;

public readonly record struct AuthorityTimeEstimate(
    double SimulationTick,
    double ClockOffsetMilliseconds,
    double SmoothedRttMilliseconds,
    double JitterMilliseconds,
    double Confidence);
