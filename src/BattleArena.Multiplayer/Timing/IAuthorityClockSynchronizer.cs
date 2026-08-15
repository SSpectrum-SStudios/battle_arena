namespace BattleArena.Multiplayer.Timing;

public interface IAuthorityClockSynchronizer
{
    bool HasEstimate { get; }

    AuthorityTimeEstimate Current { get; }

    void Observe(AuthorityClockExchange exchange);

    AuthorityTimeEstimate Estimate(ulong localTimestampMicroseconds);
}
