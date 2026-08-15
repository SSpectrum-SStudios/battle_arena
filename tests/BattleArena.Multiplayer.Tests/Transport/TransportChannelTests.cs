using BattleArena.Multiplayer.Transport;

namespace BattleArena.Multiplayer.Tests.Transport;

public sealed class TransportChannelTests
{
    [Fact]
    public void ChannelCatalogCoversEveryDeclaredChannel()
    {
        var channels = Enum.GetValues<TransportChannel>();

        Assert.All(channels, channel => Assert.True(TransportChannels.IsDefined(channel)));
        Assert.Equal(
            channels.Max(channel => (int)channel) + 1,
            TransportChannels.RequiredChannelCount);
    }

    [Fact]
    public void ChannelCatalogRejectsUnknownWireValue()
    {
        Assert.False(TransportChannels.IsDefined((TransportChannel)byte.MaxValue));
    }
}
