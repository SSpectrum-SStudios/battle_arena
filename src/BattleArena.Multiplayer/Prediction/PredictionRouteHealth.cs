namespace BattleArena.Multiplayer.Prediction;

public enum PredictionRouteHealth
{
    AwaitingHandshake,
    Authenticated,
    AuthorityFallback,
}

public sealed record PredictionRouteStatus(
    AuthorizedPredictionRoute Route,
    PredictionRouteHealth Health,
    int ConsecutiveFailures,
    ulong NextRetryAuthorityTick,
    ulong AttemptId);
