using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Prediction;

public interface IPredictionHandshakeSession
{
    bool IsAuthenticated { get; }
    PredictionHandshakeState State { get; }
    PredictionPacketEnvelope CreateHello(
        ulong packetSequence,
        ulong clientTick,
        ulong estimatedAuthorityTick);
    PredictionHandshakeResult ReceiveHello(
        PredictionPacketEnvelope hello,
        ulong responsePacketSequence,
        ulong localClientTick,
        ulong currentEstimatedAuthorityTick);
    PredictionHandshakeResult ReceiveChallenge(
        PredictionPacketEnvelope challenge,
        ulong responsePacketSequence,
        ulong localClientTick,
        ulong currentEstimatedAuthorityTick);
    PredictionHandshakeResult ReceiveProof(
        PredictionPacketEnvelope proof,
        ulong responsePacketSequence,
        ulong localClientTick,
        ulong currentEstimatedAuthorityTick);
    PredictionHandshakeResult ReceiveAccepted(
        PredictionPacketEnvelope accepted,
        ulong currentEstimatedAuthorityTick);
    void Reset();
}
