using BattleArena.Multiplayer.Connection;
using BattleArena.Multiplayer.Timing;

namespace BattleArena.Multiplayer.Prediction;

public sealed record PredictionPeerPathStatus(
    SessionPeerId RemotePeerId,
    PredictionRouteHealth RouteHealth,
    NetworkPathEstimate Path,
    ulong LastMovementArrivalTimestampMicroseconds,
    int ConsecutiveFailures);
