namespace BattleArena.Multiplayer.Replication;

public interface IRemoteMovementTimeline<TState>
    where TState : class
{
    ulong? CurrentLifeId { get; }

    ulong LatestAuthorityTick { get; }

    RemoteMovementFrame<TState>? LatestAuthorityFrame { get; }

    void ObserveDirect(DirectMovementCommand command);

    void ObserveAccepted(AcceptedMovementCommand command);

    void ObserveAuthority(RemoteMovementFrame<TState> frame);

    RemoteMovementSampleWindow<TState>? Sample(
        double targetTick,
        int normalPredictionLimitTicks,
        int freezeAfterTicks);

    void Clear();
}
