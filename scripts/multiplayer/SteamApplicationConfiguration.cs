#nullable enable

namespace BattleArena.GodotNetworking;

public static class SteamApplicationConfiguration
{
    public const uint AppId = 3820160;
    public const uint WindowsDepotId = 3820161;
    public const int VirtualPort = 0;
    public const int MaximumPlayers = 8;
    public const string LobbyProtocolKey = "protocol";
    public const string LobbyProtocolValue = "battle-arena-v1";
    public const string LobbyNameKey = "name";
    public const string LobbyHostKey = "host_steam_id";
}
