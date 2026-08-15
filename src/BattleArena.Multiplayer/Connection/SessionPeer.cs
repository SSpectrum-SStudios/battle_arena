namespace BattleArena.Multiplayer.Connection;

public sealed record SessionPeer
{
    public SessionPeer(
        SessionPeerId id,
        ConnectionGeneration peerSessionGeneration,
        ulong playerId,
        ulong combatantId,
        string displayName,
        bool isAuthority)
    {
        if (playerId == 0 || combatantId == 0 || string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Session peer identity is incomplete.");
        }

        Id = id;
        PeerSessionGeneration = peerSessionGeneration;
        PlayerId = playerId;
        CombatantId = combatantId;
        DisplayName = displayName;
        IsAuthority = isAuthority;
    }

    public SessionPeerId Id { get; }
    public ConnectionGeneration PeerSessionGeneration { get; }
    public ulong PlayerId { get; }
    public ulong CombatantId { get; }
    public string DisplayName { get; }
    public bool IsAuthority { get; }
}
