namespace BattleArena.Multiplayer.Timing;

public interface INetworkPathEstimator
{
    NetworkPathEstimate Current { get; }

    void ObserveRoundTrip(double milliseconds);

    void ObservePacket(ulong sequence, ulong arrivalTimestampMicroseconds);
}
