using BattleArena.Multiplayer.Connection;
using BattleArena.Multiplayer.Prediction;
using BattleArena.Multiplayer.Timing;

namespace BattleArena.Multiplayer.Tests.Prediction;

public sealed class PredictionPeerPathResolverTests
{
    [Fact]
    public void ResolvesEachRemoteCombatantToItsOwnMeasuredPath()
    {
        var peers = new[]
        {
            Peer(1, 1, isAuthority: true),
            Peer(2, 2, isAuthority: false),
            Peer(3, 3, isAuthority: false),
        };
        var paths = new[]
        {
            Path(2, rtt: 40),
            Path(3, rtt: 120),
        };

        Assert.True(PredictionPeerPathResolver.TryResolve(2, peers, paths, out var second));
        Assert.True(PredictionPeerPathResolver.TryResolve(3, peers, paths, out var third));
        Assert.Equal(40, second.Path.SmoothedRttMilliseconds);
        Assert.Equal(120, third.Path.SmoothedRttMilliseconds);
    }

    [Fact]
    public void AuthorityCombatantUsesAuthorityPathFallback()
    {
        var peers = new[] { Peer(1, 1, isAuthority: true) };

        Assert.False(PredictionPeerPathResolver.TryResolve(
            1,
            peers,
            new[] { Path(1, rtt: 1) },
            out _));
    }

    private static SessionPeer Peer(ulong peerId, ulong combatantId, bool isAuthority) =>
        new(
            new SessionPeerId(peerId),
            ConnectionGeneration.Initial,
            playerId: combatantId,
            combatantId,
            $"Peer {peerId}",
            isAuthority);

    private static PredictionPeerPathStatus Path(ulong peerId, double rtt) => new(
        new SessionPeerId(peerId),
        PredictionRouteHealth.Authenticated,
        new NetworkPathEstimate(rtt, 0, 0, 0, 0, 0, 10),
        LastMovementArrivalTimestampMicroseconds: 1,
        ConsecutiveFailures: 0);
}
