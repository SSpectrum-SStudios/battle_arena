using BattleArena.Multiplayer.Connection;
using BattleArena.Multiplayer.Transport;
using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Prediction;

/// <summary>
/// Adapter boundary for optional direct prediction routes. Transport connection
/// hints do not authenticate a peer; callers must complete the credential
/// handshake before treating movement packets as usable.
/// </summary>
public interface IPredictionMeshTransport
{
    event Action<PredictionTransportRouteChanged>? RouteChanged;
    event Action<InboundPredictionPacket>? PacketReceived;

    TransportKind Kind { get; }
    bool SupportsDirectRoutes { get; }
    PredictionRouteDescriptor LocalRouteDescriptor { get; }

    void Apply(PredictionMeshDirective directive);
    bool TrySendControl(
        SessionPeerId recipient,
        uint routeGeneration,
        ulong attemptId,
        ReadOnlyMemory<byte> payload);
    bool TrySendMovement(
        SessionPeerId recipient,
        uint routeGeneration,
        ulong attemptId,
        ReadOnlyMemory<byte> payload);
    void Stop();
}
