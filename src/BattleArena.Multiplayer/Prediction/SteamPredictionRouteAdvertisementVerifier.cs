using BattleArena.Multiplayer.Connection;
using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Prediction;

public sealed class SteamPredictionRouteAdvertisementVerifier(
    IAuthenticatedSteamIdentityDirectory identities,
    Func<ulong, bool> isLobbyMember) : IPredictionRouteAdvertisementVerifier
{
    public bool IsAuthorized(ConnectedPlayer player, PredictionRouteDescriptor descriptor) =>
        SteamPredictionRouteDescriptorCodec.TryDecode(descriptor, out var endpoint) &&
        identities.TryGetRemoteSteamId(player.ConnectionId, out var authenticatedSteamId) &&
        authenticatedSteamId == endpoint.SteamId &&
        isLobbyMember(authenticatedSteamId);
}
