using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Prediction;

public interface IPredictionMeshDriver : IDisposable
{
    event Action<AuthenticatedPredictionPacket>? AuthenticatedPacketReceived;
    event Action<PredictionRouteStatus>? RouteStatusChanged;

    IReadOnlyCollection<PredictionRouteStatus> RouteStatuses { get; }
    IReadOnlyCollection<PredictionPeerPathStatus> DirectPathStatuses { get; }
    void ApplyAuthorization(PredictionRouteAuthorization authorization, ulong authorityTick);
    void ApplyRevocation(PredictionRouteRevoked revocation);
    void ApplyRoster(PeerRosterUpdate roster);
    void Advance(ulong clientTick, ulong estimatedAuthorityTick);
    void PublishMovement(MovementPredictionBundle bundle);
}
