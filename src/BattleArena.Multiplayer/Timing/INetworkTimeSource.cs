namespace BattleArena.Multiplayer.Timing;

public interface INetworkTimeSource
{
    ulong GetTimestampMicroseconds();
}
