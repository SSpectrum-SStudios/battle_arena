using BattleArena.Multiplayer.Connection;
using BattleArena.Multiplayer.Transport;

namespace BattleArena.Multiplayer.Tests.Connection;

public sealed class NetworkIdentityTests
{
    [Fact]
    public void TransportConnectionIdentityMustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TransportConnectionId(0));
        Assert.Equal(42UL, new TransportConnectionId(42).Value);
    }

    [Fact]
    public void SessionPeerIdentityMustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SessionPeerId(0));
        Assert.Equal(7UL, new SessionPeerId(7).Value);
    }

    [Fact]
    public void ConnectionGenerationMustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConnectionGeneration(0));
        Assert.Equal(1U, ConnectionGeneration.Initial.Value);
    }

    [Fact]
    public void TransportAndSessionIdentitiesRemainDistinctTypes()
    {
        var connection = new TransportConnectionId(2);
        var participant = new SessionPeerId(2);

        Assert.Equal(connection.Value, participant.Value);
        Assert.NotEqual(connection.GetType(), participant.GetType());
    }
}
