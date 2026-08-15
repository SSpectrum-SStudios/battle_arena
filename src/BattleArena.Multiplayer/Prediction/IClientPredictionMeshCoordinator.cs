using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Prediction;

public interface IClientPredictionMeshCoordinator
{
    IReadOnlyCollection<AuthorizedPredictionRoute> AuthorizedRoutes { get; }
    IReadOnlyCollection<PredictionRouteStatus> RouteStatuses { get; }
    IReadOnlyList<PredictionMeshDirective> ApplyAuthorization(
        PredictionRouteAuthorization authorization,
        ulong currentAuthorityTick);
    IReadOnlyList<PredictionMeshDirective> ApplyRevocation(PredictionRouteRevoked revocation);
    IReadOnlyList<PredictionMeshDirective> RemoveMissingRosterPeers(PeerRosterUpdate roster);
    IReadOnlyList<PredictionMeshDirective> AdvanceAuthorityTick(ulong currentAuthorityTick);
    IReadOnlyList<PredictionMeshDirective> ReportRouteFailure(
        BattleArena.Multiplayer.Connection.SessionPeerId remotePeerId,
        uint routeGeneration,
        ulong attemptId,
        ulong currentAuthorityTick);
    bool MarkAuthenticated(
        BattleArena.Multiplayer.Connection.SessionPeerId remotePeerId,
        uint routeGeneration,
        ulong attemptId,
        ulong currentAuthorityTick);
}
