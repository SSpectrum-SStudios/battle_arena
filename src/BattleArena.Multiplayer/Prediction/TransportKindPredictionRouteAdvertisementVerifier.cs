using BattleArena.Multiplayer.Connection;
using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Prediction;

public sealed class TransportKindPredictionRouteAdvertisementVerifier(
    PredictionTransportKind expectedKind) : IPredictionRouteAdvertisementVerifier
{
    public bool IsAuthorized(ConnectedPlayer player, PredictionRouteDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(player);
        return descriptor is not null && descriptor.TransportKind == expectedKind;
    }
}
