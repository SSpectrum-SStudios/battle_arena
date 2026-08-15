#nullable enable

using BattleArena.Multiplayer.Protocol;

namespace BattleArena.GodotNetworking;

public static class SteamApplicationConfiguration
{
    public const uint AppId = 3820160;
    public const uint WindowsDepotId = 3820161;
    public const int VirtualPort = 0;
    public const int PredictionVirtualPort = 1;
    public const int MaximumPlayers = 8;
    public const string LobbyProtocolKey = "protocol";
    public static string LobbyProtocolValue =>
        $"battle-arena-v{ProtocolConstants.CurrentVersion}";
    public const string LobbyNameKey = "name";
    public const string LobbyHostKey = "host_steam_id";
}
