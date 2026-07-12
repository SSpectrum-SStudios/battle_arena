using BattleArena.Core.Common;
using BattleArena.Core.Effects;

namespace BattleArena.Core.Tests.Effects;

public sealed class EffectChainContextTests
{
    [Fact]
    public void EnforcesPerTriggerActivationBudget()
    {
        var chain = new EffectChainContext(new EffectChainId(1), maximumOperations: 100);
        var trigger = new TriggerInstanceId(10);

        Assert.Equal(TriggerBudgetConsumeStatus.Consumed, chain.TryConsume(trigger, 2));
        Assert.Equal(TriggerBudgetConsumeStatus.Consumed, chain.TryConsume(trigger, 2));
        Assert.Equal(
            TriggerBudgetConsumeStatus.TriggerBudgetExhausted,
            chain.TryConsume(trigger, 2));
        Assert.Equal(2, chain.ConsumedOperations);
    }

    [Fact]
    public void EnforcesGlobalChainBudgetAcrossDifferentTriggers()
    {
        var chain = new EffectChainContext(new EffectChainId(1), maximumOperations: 2);

        Assert.Equal(
            TriggerBudgetConsumeStatus.Consumed,
            chain.TryConsume(new TriggerInstanceId(1), 10));
        Assert.Equal(
            TriggerBudgetConsumeStatus.Consumed,
            chain.TryConsume(new TriggerInstanceId(2), 10));
        Assert.Equal(
            TriggerBudgetConsumeStatus.GlobalChainBudgetExhausted,
            chain.TryConsume(new TriggerInstanceId(3), 10));
    }

    [Fact]
    public void ANewPeriodicTickCanUseAFreshChainContext()
    {
        var trigger = new TriggerInstanceId(1);
        var firstTick = new EffectChainContext(new EffectChainId(1), 100);
        var secondTick = new EffectChainContext(new EffectChainId(2), 100);

        Assert.Equal(TriggerBudgetConsumeStatus.Consumed, firstTick.TryConsume(trigger, 1));
        Assert.Equal(TriggerBudgetConsumeStatus.Consumed, secondTick.TryConsume(trigger, 1));
    }
}
