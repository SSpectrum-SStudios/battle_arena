namespace BattleArena.Multiplayer.Replication;

public sealed record RemoteMovementFrame<TState>(
    ulong AuthorityTick,
    ulong LifeId,
    ulong LastProcessedInputSequence,
    TState State)
    where TState : class;
