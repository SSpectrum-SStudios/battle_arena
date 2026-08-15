using BattleArena.Multiplayer.Connection;
using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Prediction;

public interface IAuthorityPredictionRouteBroker
{
    IReadOnlyCollection<SessionPeer> Peers { get; }
    PredictionRouteBrokerDelta AddOrUpdatePeer(SessionPeer peer);
    PredictionRouteBrokerDelta RemovePeer(
        SessionPeerId peerId,
        PredictionRouteRevocationReason reason);
    PredictionRouteBrokerDelta ApplyAdvertisement(
        SessionPeerId authenticatedPeerId,
        ConnectionGeneration authenticatedGeneration,
        PredictionRouteDescriptor descriptor,
        ulong currentAuthorityTick);
    PredictionRouteBrokerDelta AdvanceAuthorityTick(ulong currentAuthorityTick);
    PredictionRouteBrokerDelta RevokePairForProtocolViolation(
        SessionPeerId first,
        SessionPeerId second);
}
