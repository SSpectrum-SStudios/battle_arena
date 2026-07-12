using BattleArena.Core.Common;
using BattleArena.Core.Effects;

namespace BattleArena.Core.Tests.Effects;

public sealed class PeriodicEffectScheduleTests
{
    [Fact]
    public void TickCountPolicyExecutesExactDelayedTickSchedule()
    {
        var schedule = TickCountSchedule(
            intervalSeconds: 2m,
            ticks: 5,
            FirstTickPolicy.AfterInterval);

        var tickTimes = new List<long>();
        foreach (var second in new[] { 2m, 4m, 6m, 8m, 10m })
        {
            var result = schedule.Advance(AtSeconds(second));
            Assert.True(result.ExecutedTick);
            tickTimes.Add(result.TickScheduledAt!.Value.Microseconds);
        }

        Assert.Equal(
            new long[] { 2_000_000, 4_000_000, 6_000_000, 8_000_000, 10_000_000 },
            tickTimes);
        Assert.Equal(5, schedule.ExecutedTicks);
        Assert.True(schedule.IsExpired);
    }

    [Fact]
    public void ImmediatePolicyTicksAtApplicationTime()
    {
        var schedule = TickCountSchedule(
            intervalSeconds: 2m,
            ticks: 3,
            FirstTickPolicy.Immediate);

        var first = schedule.Advance(SimulationInstant.Zero);
        var second = schedule.Advance(AtSeconds(2m));
        var third = schedule.Advance(AtSeconds(4m));

        Assert.Equal(SimulationInstant.Zero, first.TickScheduledAt);
        Assert.Equal(AtSeconds(2m), second.TickScheduledAt);
        Assert.Equal(AtSeconds(4m), third.TickScheduledAt);
        Assert.True(third.IsExpired);
    }

    [Fact]
    public void DurationPolicyTicksAtExactExpirationThenExpires()
    {
        var schedule = DurationSchedule(
            intervalSeconds: 2m,
            durationSeconds: 10m,
            FirstTickPolicy.AfterInterval);

        foreach (var second in new[] { 2m, 4m, 6m, 8m })
        {
            var result = schedule.Advance(AtSeconds(second));
            Assert.Equal(PeriodicScheduleStatus.Ticked, result.Status);
        }

        var final = schedule.Advance(AtSeconds(10m));

        Assert.Equal(PeriodicScheduleStatus.TickedAndExpired, final.Status);
        Assert.Equal(5, schedule.ExecutedTicks);
        Assert.True(schedule.IsExpired);
    }

    [Fact]
    public void DurationPolicyCanExpireWithoutATick()
    {
        var schedule = DurationSchedule(
            intervalSeconds: 10m,
            durationSeconds: 3m,
            FirstTickPolicy.AfterInterval);

        var result = schedule.Advance(AtSeconds(3m));

        Assert.Equal(PeriodicScheduleStatus.ExpiredWithoutTick, result.Status);
        Assert.Equal(0, schedule.ExecutedTicks);
        Assert.True(schedule.IsExpired);
    }

    [Fact]
    public void ScheduleDoesNotAdvanceUnlessAuthoritativeTimeAdvances()
    {
        var schedule = TickCountSchedule(
            intervalSeconds: 2m,
            ticks: 1,
            FirstTickPolicy.AfterInterval);

        var firstCheck = schedule.Advance(AtSeconds(1m));
        var pausedCheck = schedule.Advance(AtSeconds(1m));

        Assert.Equal(PeriodicScheduleStatus.NotDue, firstCheck.Status);
        Assert.Equal(PeriodicScheduleStatus.NotDue, pausedCheck.Status);
        Assert.Equal(0, schedule.ExecutedTicks);
    }

    [Fact]
    public void ShorterIntervalInterruptsAndCanMakeTickImmediatelyDue()
    {
        var schedule = TickCountSchedule(
            intervalSeconds: 2m,
            ticks: 2,
            FirstTickPolicy.AfterInterval);
        var currentTime = AtSeconds(1.5m);

        schedule.ChangeInterval(SimulationDuration.FromSeconds(1m), currentTime);
        var result = schedule.Advance(currentTime);

        Assert.Equal(PeriodicScheduleStatus.Ticked, result.Status);
        Assert.Equal(currentTime, result.TickScheduledAt);
        Assert.Equal(AtSeconds(2.5m), result.NextActionAt);
    }

    [Fact]
    public void LongerIntervalImmediatelyReschedulesCurrentCycle()
    {
        var schedule = TickCountSchedule(
            intervalSeconds: 2m,
            ticks: 1,
            FirstTickPolicy.AfterInterval);

        schedule.ChangeInterval(
            SimulationDuration.FromSeconds(4m),
            AtSeconds(1m));

        Assert.Equal(AtSeconds(4m), schedule.NextActionAt);
        Assert.Equal(PeriodicScheduleStatus.NotDue, schedule.Advance(AtSeconds(2m)).Status);
    }

    [Fact]
    public void ADelayedCallerCanProcessMissedTicksInScheduledOrder()
    {
        var schedule = TickCountSchedule(
            intervalSeconds: 2m,
            ticks: 3,
            FirstTickPolicy.AfterInterval);
        var currentTime = AtSeconds(10m);

        var first = schedule.Advance(currentTime);
        var second = schedule.Advance(currentTime);
        var third = schedule.Advance(currentTime);

        Assert.Equal(AtSeconds(2m), first.TickScheduledAt);
        Assert.Equal(AtSeconds(4m), second.TickScheduledAt);
        Assert.Equal(AtSeconds(6m), third.TickScheduledAt);
        Assert.True(third.IsExpired);
    }

    private static PeriodicEffectSchedule TickCountSchedule(
        decimal intervalSeconds,
        int ticks,
        FirstTickPolicy firstTickPolicy) =>
        new(
            SimulationInstant.Zero,
            SimulationDuration.FromSeconds(intervalSeconds),
            firstTickPolicy,
            new PeriodicCompletionPolicy.AfterTickCount(ticks));

    private static PeriodicEffectSchedule DurationSchedule(
        decimal intervalSeconds,
        decimal durationSeconds,
        FirstTickPolicy firstTickPolicy) =>
        new(
            SimulationInstant.Zero,
            SimulationDuration.FromSeconds(intervalSeconds),
            firstTickPolicy,
            new PeriodicCompletionPolicy.AfterDuration(
                SimulationDuration.FromSeconds(durationSeconds)));

    private static SimulationInstant AtSeconds(decimal seconds) =>
        new(SimulationDuration.FromSeconds(seconds).Microseconds);
}
