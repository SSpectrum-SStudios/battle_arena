using BattleArena.Multiplayer.Timing;

namespace BattleArena.Multiplayer.Tests.Timing;

public sealed class AdaptivePredictionTimingPolicyTests
{
    [Fact]
    public void StableEightyMillisecondPathUsesOneTickPresentationDelay()
    {
        var policy = new AdaptivePredictionTimingPolicy();
        var path = new NetworkPathEstimate(80, 2, 2, 0, 0, 0, 10);

        var decision = policy.Evaluate(new PredictionTimingContext(60, path, 0));

        Assert.Equal(1, decision.PresentationDelayTicks);
        Assert.Equal(9, decision.NormalPredictionLimitTicks);
        Assert.Equal(15, decision.FreezeAfterTicks);
    }

    [Fact]
    public void InstabilityRaisesDelayImmediately()
    {
        var policy = new AdaptivePredictionTimingPolicy();
        var path = new NetworkPathEstimate(160, 10, 14, 0.08, 4, 1, 20);

        var decision = policy.Evaluate(new PredictionTimingContext(60, path, 2));

        Assert.Equal(6, decision.PresentationDelayTicks);
    }

    [Fact]
    public void RecoveryLowersDelayGradually()
    {
        var policy = new AdaptivePredictionTimingPolicy();
        var unstable = new NetworkPathEstimate(160, 10, 14, 0.08, 4, 1, 20);
        var stable = new NetworkPathEstimate(40, 0, 0, 0, 0, 0, 100);
        policy.Evaluate(new PredictionTimingContext(60, unstable, 2));

        PredictionTimingDecision decision = default;
        for (var index = 0; index < 59; index++)
        {
            decision = policy.Evaluate(new PredictionTimingContext(60, stable, 0));
        }

        Assert.Equal(6, decision.PresentationDelayTicks);
        decision = policy.Evaluate(new PredictionTimingContext(60, stable, 0));
        Assert.Equal(5, decision.PresentationDelayTicks);
    }
}
