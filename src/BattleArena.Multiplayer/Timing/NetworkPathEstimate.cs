namespace BattleArena.Multiplayer.Timing;

public readonly record struct NetworkPathEstimate(
    double SmoothedRttMilliseconds,
    double RttJitterMilliseconds,
    double ArrivalJitterMilliseconds,
    double EstimatedLossRate,
    long MissingPackets,
    long ReorderedPackets,
    ulong LatestSequence)
{
    public double EffectiveJitterMilliseconds =>
        Math.Max(RttJitterMilliseconds, ArrivalJitterMilliseconds);
}
