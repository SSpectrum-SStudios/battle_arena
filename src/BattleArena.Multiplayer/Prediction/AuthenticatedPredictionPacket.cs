using BattleArena.Multiplayer.Connection;
using BattleArena.Multiplayer.Transport;
using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Prediction;

public sealed record AuthenticatedPredictionPacket(
    SessionPeerId Sender,
    uint RouteGeneration,
    ulong AttemptId,
    TransportDelivery Delivery,
    PredictionPacketEnvelope Envelope);
