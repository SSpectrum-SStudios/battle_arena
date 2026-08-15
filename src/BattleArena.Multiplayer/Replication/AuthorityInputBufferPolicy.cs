namespace BattleArena.Multiplayer.Replication;

public sealed record AuthorityInputBufferPolicy
{
    public static AuthorityInputBufferPolicy Default { get; } = new(
        maximumPendingCommands: 64,
        staleInputHoldTicks: 6,
        edgePreservationCommandCount: 3);

    public AuthorityInputBufferPolicy(
        int maximumPendingCommands,
        int staleInputHoldTicks,
        int edgePreservationCommandCount = 3)
    {
        if (maximumPendingCommands < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPendingCommands));
        }

        if (staleInputHoldTicks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(staleInputHoldTicks));
        }

        if (edgePreservationCommandCount < 1 ||
            edgePreservationCommandCount > maximumPendingCommands)
        {
            throw new ArgumentOutOfRangeException(nameof(edgePreservationCommandCount));
        }

        MaximumPendingCommands = maximumPendingCommands;
        StaleInputHoldTicks = staleInputHoldTicks;
        EdgePreservationCommandCount = edgePreservationCommandCount;
    }

    public int MaximumPendingCommands { get; }

    public int StaleInputHoldTicks { get; }

    public int EdgePreservationCommandCount { get; }
}
