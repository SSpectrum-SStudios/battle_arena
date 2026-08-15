using BattleArena.Multiplayer.Connection;
using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Prediction;

public interface IPredictionRouteAdvertisementVerifier
{
    bool IsAuthorized(ConnectedPlayer player, PredictionRouteDescriptor descriptor);
}
