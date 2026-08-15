using BattleArena.Multiplayer.Connection;

namespace BattleArena.Multiplayer.Prediction;

/// <summary>
/// Resolves transport measurements by stable combatant identity. The Godot
/// presentation adapter does not need to know how roster peer IDs map to paths.
/// </summary>
public static class PredictionPeerPathResolver
{
    public static bool TryResolve(
        ulong combatantId,
        IEnumerable<SessionPeer> peers,
        IEnumerable<PredictionPeerPathStatus> paths,
        out PredictionPeerPathStatus status)
    {
        ArgumentNullException.ThrowIfNull(peers);
        ArgumentNullException.ThrowIfNull(paths);
        if (combatantId == 0)
        {
            status = null!;
            return false;
        }

        var peer = peers.SingleOrDefault(candidate =>
            candidate.CombatantId == combatantId && !candidate.IsAuthority);
        if (peer is null)
        {
            status = null!;
            return false;
        }

        var resolved = paths.SingleOrDefault(candidate =>
            candidate.RemotePeerId == peer.Id);
        if (resolved is null)
        {
            status = null!;
            return false;
        }

        status = resolved;
        return true;
    }
}
