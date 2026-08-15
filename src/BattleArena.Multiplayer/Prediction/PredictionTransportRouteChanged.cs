using BattleArena.Multiplayer.Connection;

namespace BattleArena.Multiplayer.Prediction;

public sealed record PredictionTransportRouteChanged(
    SessionPeerId RemotePeerId,
    uint RouteGeneration,
    ulong AttemptId,
    PredictionTransportRouteState State,
    string? Detail = null);
