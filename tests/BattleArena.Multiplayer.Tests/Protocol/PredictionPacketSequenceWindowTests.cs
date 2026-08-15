using BattleArena.Multiplayer.Protocol;

namespace BattleArena.Multiplayer.Tests.Protocol;

public sealed class PredictionPacketSequenceWindowTests
{
    [Fact]
    public void DuplicateAndReorderedPacketsAreRejectedUntilRouteReset()
    {
        var window = new PredictionPacketSequenceWindow();

        Assert.True(window.TryAccept(10));
        Assert.False(window.TryAccept(10));
        Assert.False(window.TryAccept(9));
        Assert.True(window.TryAccept(12));

        window.Reset();

        Assert.True(window.TryAccept(1));
    }
}
