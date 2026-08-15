using BattleArena.Multiplayer.OwnerPrediction;

namespace BattleArena.Multiplayer.Tests.OwnerPrediction;

public sealed class OwnerPredictionModePolicyTests
{
    [Fact]
    public void DefaultKeepsTheLegacyOwnerPathSelected()
    {
        Assert.Equal(OwnerPredictionMode.Legacy, OwnerPredictionModePolicy.Default.SelectedMode);
    }

    [Theory]
    [InlineData(OwnerPredictionMode.Legacy)]
    [InlineData(OwnerPredictionMode.FrameRewindV2)]
    public void DefinedModesCanBeSelected(OwnerPredictionMode mode)
    {
        var policy = new OwnerPredictionModePolicy(mode);

        Assert.Equal(mode, policy.SelectedMode);
    }

    [Fact]
    public void UndefinedModesAreRejected()
    {
        const OwnerPredictionMode undefinedMode = (OwnerPredictionMode)99;

        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new OwnerPredictionModePolicy(undefinedMode));

        Assert.Equal("selectedMode", exception.ParamName);
        Assert.Equal(undefinedMode, exception.ActualValue);
    }
}
