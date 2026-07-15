#nullable enable

using GodotSteam;

namespace BattleArena.GodotNetworking;

public sealed class SteamLobbyService : IDisposable
{
    private readonly SteamRuntimeAdapter _runtime;
    private bool _isHosting;
    private bool _disposed;

    public SteamLobbyService(SteamRuntimeAdapter runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        Steam.LobbyCreated += OnLobbyCreated;
        Steam.LobbyJoined += OnLobbyJoined;
        Steam.JoinRequested += OnJoinRequested;
    }

    public event Action<ulong>? HostLobbyCreated;

    public event Action<ulong, ulong>? ClientLobbyJoined;

    public event Action<string>? StatusChanged;

    public event Action? OperationFailed;

    public ulong CurrentLobbyId { get; private set; }

    public void Host()
    {
        EnsureReady();
        Leave();
        _isHosting = true;
        StatusChanged?.Invoke("Creating Steam friends-only lobby...");
        Steam.CreateLobby(Steam.LobbyType.FriendsOnly, SteamApplicationConfiguration.MaximumPlayers);
    }

    public void Join(ulong lobbyId)
    {
        EnsureReady();
        if (lobbyId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lobbyId));
        }

        Leave();
        _isHosting = false;
        StatusChanged?.Invoke($"Joining Steam lobby {lobbyId}...");
        Steam.JoinLobby(lobbyId);
    }

    public bool IsMember(ulong steamId)
    {
        if (CurrentLobbyId == 0 || steamId == 0)
        {
            return false;
        }

        var memberCount = Steam.GetNumLobbyMembers(CurrentLobbyId);
        for (var index = 0; index < memberCount; index++)
        {
            if (Steam.GetLobbyMemberByIndex(CurrentLobbyId, index) == steamId)
            {
                return true;
            }
        }

        return false;
    }

    public void OpenInviteOverlay()
    {
        if (CurrentLobbyId != 0)
        {
            Steam.ActivateGameOverlayInviteDialog(CurrentLobbyId);
        }
    }

    public void Leave()
    {
        if (CurrentLobbyId != 0)
        {
            Steam.LeaveLobby(CurrentLobbyId);
            CurrentLobbyId = 0;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Leave();
        Steam.LobbyCreated -= OnLobbyCreated;
        Steam.LobbyJoined -= OnLobbyJoined;
        Steam.JoinRequested -= OnJoinRequested;
    }

    private void OnLobbyCreated(long result, ulong lobbyId)
    {
        if (!_isHosting)
        {
            return;
        }

        if ((ErrorResult)result != ErrorResult.Ok || lobbyId == 0)
        {
            StatusChanged?.Invoke($"Steam lobby creation failed: {(ErrorResult)result}");
            OperationFailed?.Invoke();
            return;
        }

        CurrentLobbyId = lobbyId;
        Steam.SetLobbyData(lobbyId, SteamApplicationConfiguration.LobbyProtocolKey,
            SteamApplicationConfiguration.LobbyProtocolValue);
        Steam.SetLobbyData(lobbyId, SteamApplicationConfiguration.LobbyNameKey,
            $"{_runtime.PersonaName}'s Battle Arena");
        Steam.SetLobbyData(lobbyId, SteamApplicationConfiguration.LobbyHostKey,
            _runtime.LocalSteamId.ToString());
        Steam.SetLobbyJoinable(lobbyId, true);
        StatusChanged?.Invoke($"Steam lobby {lobbyId} created");
        HostLobbyCreated?.Invoke(lobbyId);
    }

    private void OnLobbyJoined(ulong lobbyId, long permissions, bool locked, long response)
    {
        if (_isHosting)
        {
            return;
        }

        if ((ChatRoomEnterResponse)response != ChatRoomEnterResponse.Success)
        {
            StatusChanged?.Invoke($"Unable to join Steam lobby: {(ChatRoomEnterResponse)response}");
            OperationFailed?.Invoke();
            return;
        }

        if (Steam.GetLobbyData(lobbyId, SteamApplicationConfiguration.LobbyProtocolKey) !=
            SteamApplicationConfiguration.LobbyProtocolValue)
        {
            Steam.LeaveLobby(lobbyId);
            StatusChanged?.Invoke("The Steam lobby uses an incompatible game protocol.");
            OperationFailed?.Invoke();
            return;
        }

        CurrentLobbyId = lobbyId;
        var ownerId = Steam.GetLobbyOwner(lobbyId);
        StatusChanged?.Invoke($"Joined Steam lobby {lobbyId}; connecting to host {ownerId}...");
        ClientLobbyJoined?.Invoke(lobbyId, ownerId);
    }

    private void OnJoinRequested(ulong lobbyId, ulong friendId) => Join(lobbyId);

    private void EnsureReady()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_runtime.IsInitialized)
        {
            throw new InvalidOperationException("Steam must be initialized before using lobbies.");
        }
    }
}
