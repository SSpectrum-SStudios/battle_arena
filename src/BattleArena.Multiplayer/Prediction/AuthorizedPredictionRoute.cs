using BattleArena.Multiplayer.Connection;
using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Prediction;

public sealed record AuthorizedPredictionRoute(
    ulong SessionId,
    SessionPeerId LocalPeerId,
    ConnectionGeneration LocalPeerSessionGeneration,
    SessionPeerId RemotePeerId,
    ConnectionGeneration RemotePeerSessionGeneration,
    uint RouteGeneration,
    PredictionRouteDescriptor RemoteDescriptor,
    byte[] Credential,
    ulong ExpiresAuthorityTick)
{
    public bool LocalPeerInitiates => LocalPeerId.Value < RemotePeerId.Value;
}
