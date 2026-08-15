using BattleArena.Multiplayer.Connection;
using BattleArena.Multiplayer.Transport;

namespace BattleArena.Multiplayer.Prediction;

public sealed record InboundPredictionPacket(
    SessionPeerId Sender,
    uint RouteGeneration,
    ulong AttemptId,
    TransportDelivery Delivery,
    ReadOnlyMemory<byte> Payload);
