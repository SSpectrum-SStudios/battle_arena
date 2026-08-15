using BattleArena.Multiplayer.Protocol;
using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Tests.Protocol;

public sealed class PredictionRouteEpochGateTests
{
    [Fact]
    public void StaleRevocationCannotDisableReplacementRoute()
    {
        var gate = new PredictionRouteEpochGate(2, 3);
        Assert.True(gate.TryApplyAuthorization(Authorization(route: 5), 100));
        Assert.True(gate.TryApplyAuthorization(Authorization(route: 6), 100));

        var stale = Revocation(route: 5);

        Assert.False(gate.TryApplyRevocation(stale));
        Assert.True(gate.IsAuthorized);
        Assert.True(gate.TryApplyRevocation(Revocation(route: 6)));
        Assert.False(gate.IsAuthorized);
    }

    [Fact]
    public void ReconnectRejectsOldPresenceAndAllowsFreshRouteGeneration()
    {
        var gate = new PredictionRouteEpochGate(2, 3);
        Assert.True(gate.TryApplyAuthorization(Authorization(route: 8), 100));
        Assert.True(gate.TryApplyAuthorization(
            Authorization(route: 1, remoteGeneration: 6), 100));

        Assert.False(gate.TryApplyAuthorization(Authorization(route: 9), 100));
        Assert.True(gate.IsAuthorized);
    }

    [Fact]
    public void ExpiredAuthorizationIsNeverApplied()
    {
        var gate = new PredictionRouteEpochGate(2, 3);

        Assert.False(gate.TryApplyAuthorization(Authorization(route: 1, expires: 100), 100));
        Assert.False(gate.IsAuthorized);
    }

    private static PredictionRouteAuthorization Authorization(
        uint route,
        uint remoteGeneration = 5,
        ulong expires = 1_000) => new()
        {
            LocalSessionPeerId = 2,
            LocalPeerSessionGeneration = 4,
            RemoteSessionPeerId = 3,
            RemotePeerSessionGeneration = remoteGeneration,
            PredictionRouteGeneration = route,
            ExpiresAuthorityTick = expires,
        };

    private static PredictionRouteRevoked Revocation(uint route) => new()
    {
        LocalSessionPeerId = 2,
        LocalPeerSessionGeneration = 4,
        RemoteSessionPeerId = 3,
        RemotePeerSessionGeneration = 5,
        PredictionRouteGeneration = route,
        Reason = PredictionRouteRevocationReason.RouteReplaced,
    };
}
