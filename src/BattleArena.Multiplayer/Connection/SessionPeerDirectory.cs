using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Connection;

public sealed class SessionPeerDirectory : ISessionPeerDirectory
{
    private readonly Dictionary<SessionPeerId, SessionPeer> _peers = [];

    public SessionPeerDirectory(SessionPeerId localPeerId)
    {
        LocalPeerId = localPeerId;
    }

    public SessionPeerId LocalPeerId { get; }
    public SessionPeerId AuthorityPeerId { get; private set; }
    public ulong RosterRevision { get; private set; }
    public IReadOnlyCollection<SessionPeer> Peers => _peers.Values;

    public bool TryApply(PeerRosterUpdate roster)
    {
        ArgumentNullException.ThrowIfNull(roster);
        if (roster.RosterRevision <= RosterRevision || roster.Peers.Count == 0)
        {
            return false;
        }

        var peers = new Dictionary<SessionPeerId, SessionPeer>();
        SessionPeerId? authority = null;
        foreach (var entry in roster.Peers)
        {
            var id = new SessionPeerId(entry.SessionPeerId);
            if (!peers.TryAdd(id, new SessionPeer(
                    id,
                    new ConnectionGeneration(entry.PeerSessionGeneration),
                    entry.PlayerId,
                    entry.CombatantId,
                    entry.DisplayName,
                    entry.IsAuthority)))
            {
                return false;
            }

            if (entry.IsAuthority)
            {
                if (authority is not null)
                {
                    return false;
                }

                authority = id;
            }
        }

        if (authority is null || !peers.ContainsKey(LocalPeerId))
        {
            return false;
        }

        _peers.Clear();
        foreach (var peer in peers)
        {
            _peers.Add(peer.Key, peer.Value);
        }

        AuthorityPeerId = authority.Value;
        RosterRevision = roster.RosterRevision;
        return true;
    }

    public bool TryGetPeer(SessionPeerId id, out SessionPeer peer) =>
        _peers.TryGetValue(id, out peer!);
}
