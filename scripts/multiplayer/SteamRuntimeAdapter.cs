#nullable enable

using Godot;
using GodotSteam;
using Steam = GodotSteam.Steam;

namespace BattleArena.GodotNetworking;

public partial class SteamRuntimeAdapter : Node
{
    public event Action<string>? StatusChanged;

    public bool IsInitialized { get; private set; }

    public ulong LocalSteamId { get; private set; }

    public string PersonaName { get; private set; } = string.Empty;

    public bool Initialize()
    {
        if (IsInitialized)
        {
            return true;
        }

        try
        {
            // These environment values allow local development launches without
            // shipping steam_appid.txt beside the editor executable.
            OS.SetEnvironment("SteamAppId", SteamApplicationConfiguration.AppId.ToString());
            OS.SetEnvironment("SteamGameId", SteamApplicationConfiguration.AppId.ToString());

            var result = Steam.SteamInitEx(retrieveStats: false, SteamApplicationConfiguration.AppId);
            if (result.Status != SteamInitExStatus.SteamworksActive)
            {
                StatusChanged?.Invoke($"Steam initialization failed: {result.Verbal}");
                return false;
            }

            LocalSteamId = Steam.GetSteamID();
            PersonaName = Steam.GetPersonaName();
            Steam.InitRelayNetworkAccess();
            IsInitialized = true;
            StatusChanged?.Invoke($"Steam connected as {PersonaName} ({LocalSteamId})");
            return true;
        }
        catch (Exception exception)
        {
            StatusChanged?.Invoke($"Steam is unavailable: {exception.Message}");
            return false;
        }
    }

    public override void _Process(double delta)
    {
        if (IsInitialized)
        {
            Steam.RunCallbacks();
        }
    }

    // Steam is process-scoped. Explicit shutdown during Godot's tree teardown can
    // race sibling transport/lobby nodes, so the operating system owns final cleanup.
}
