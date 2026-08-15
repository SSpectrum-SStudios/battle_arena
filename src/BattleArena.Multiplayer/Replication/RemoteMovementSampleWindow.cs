namespace BattleArena.Multiplayer.Replication;

public sealed record RemoteMovementSampleWindow<TState>(
    RemoteMovementFrame<TState> Before,
    RemoteMovementFrame<TState> After,
    IReadOnlyList<AcceptedMovementCommand> PredictionCommands,
    double RequestedTargetTick,
    double EffectiveTargetTick,
    bool PredictionLimited,
    bool Frozen)
    where TState : class
{
    public bool RequiresPrediction => EffectiveTargetTick > After.AuthorityTick;
}
