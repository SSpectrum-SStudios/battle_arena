using BattleArena.Core.Common;
using BattleArena.Core.Movement;

namespace BattleArena.Multiplayer.Replication;

public interface IAuthorityMovementInputBuffer
{
    ulong LastProcessedSequence { get; }

    int PendingCommandCount { get; }

    int LastCompactedCommandCount { get; }

    long TotalCompactedCommandCount { get; }

    void Enqueue(MovementCommand command);

    void Enqueue(RevisionedMovementCommand command);

    void AuthorizeAttack(SimulationInstant clientTick);

    MovementCommand Consume(SimulationInstant authorityTick);

    RevisionedMovementCommand ConsumeRevisioned(SimulationInstant authorityTick);

    void DiscardPending();
}
