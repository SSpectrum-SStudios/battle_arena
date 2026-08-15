namespace BattleArena.Multiplayer.Connection;

public interface ISessionPeerDirectory
{
    SessionPeerId LocalPeerId { get; }
    SessionPeerId AuthorityPeerId { get; }
    ulong RosterRevision { get; }
    IReadOnlyCollection<SessionPeer> Peers { get; }

    bool TryGetPeer(SessionPeerId id, out SessionPeer peer);
}
