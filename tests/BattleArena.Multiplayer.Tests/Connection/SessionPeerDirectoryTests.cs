using BattleArena.Multiplayer.Connection;
using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Tests.Connection;

public sealed class SessionPeerDirectoryTests
{
    [Fact]
    public void AppliesOnlyNewerRosterContainingLocalPeerAndOneAuthority()
    {
        var directory = new SessionPeerDirectory(new SessionPeerId(2));
        var roster = Roster(2);

        Assert.True(directory.TryApply(roster));
        Assert.Equal(new SessionPeerId(1), directory.AuthorityPeerId);
        Assert.False(directory.TryApply(roster.Clone()));

        var missingLocal = Roster(3);
        missingLocal.RosterRevision = 2;
        Assert.False(directory.TryApply(missingLocal));
        Assert.Equal(1UL, directory.RosterRevision);
    }

    private static PeerRosterUpdate Roster(ulong clientId)
    {
        var roster = new PeerRosterUpdate { RosterRevision = 1 };
        roster.Peers.Add(new PredictionPeerRosterEntry
        {
            SessionPeerId = 1,
            PeerSessionGeneration = 1,
            PlayerId = 1,
            CombatantId = 1,
            DisplayName = "Host",
            IsAuthority = true,
        });
        roster.Peers.Add(new PredictionPeerRosterEntry
        {
            SessionPeerId = clientId,
            PeerSessionGeneration = 1,
            PlayerId = clientId,
            CombatantId = clientId,
            DisplayName = "Client",
        });
        return roster;
    }
}
