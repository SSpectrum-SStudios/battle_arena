using BattleArena.Multiplayer.Transport;

namespace BattleArena.Multiplayer.Prediction;

public interface IAuthenticatedSteamIdentityDirectory
{
    bool TryGetRemoteSteamId(TransportConnectionId connectionId, out ulong steamId);
}
