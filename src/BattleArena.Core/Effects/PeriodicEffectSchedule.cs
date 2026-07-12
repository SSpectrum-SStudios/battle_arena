using BattleArena.Core.Common;

namespace BattleArena.Core.Effects;

public sealed class PeriodicEffectSchedule
{
    private SimulationInstant _nextTickAt;
    private readonly SimulationInstant? _expiresAt;
    private int? _remainingTicks;

    public PeriodicEffectSchedule(
        SimulationInstant appliedAt,
        SimulationDuration interval,
        FirstTickPolicy firstTickPolicy,
        PeriodicCompletionPolicy completionPolicy)
    {
        if (interval == SimulationDuration.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "A periodic interval must be positive.");
        }

        ArgumentNullException.ThrowIfNull(completionPolicy);
        if (!Enum.IsDefined(firstTickPolicy))
        {
            throw new ArgumentOutOfRangeException(
                nameof(firstTickPolicy),
                firstTickPolicy,
                "The first-tick policy is not defined.");
        }

        AppliedAt = appliedAt;
        Interval = interval;
        FirstTickPolicy = firstTickPolicy;
        CompletionPolicy = completionPolicy;
        _nextTickAt = firstTickPolicy == FirstTickPolicy.Immediate
            ? appliedAt
            : appliedAt + interval;

        switch (completionPolicy)
        {
            case PeriodicCompletionPolicy.AfterTickCount tickCount:
                _remainingTicks = tickCount.TotalTicks;
                break;
            case PeriodicCompletionPolicy.AfterDuration duration:
                _expiresAt = appliedAt + duration.Duration;
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(completionPolicy),
                    completionPolicy,
                    "The completion policy is not supported.");
        }
    }

    public SimulationInstant AppliedAt { get; }

    public SimulationDuration Interval { get; private set; }

    public FirstTickPolicy FirstTickPolicy { get; }

    public PeriodicCompletionPolicy CompletionPolicy { get; }

    public SimulationInstant? LastTickAt { get; private set; }

    public int ExecutedTicks { get; private set; }

    public int? RemainingTicks => _remainingTicks;

    public bool IsExpired { get; private set; }

    public long Revision { get; private set; }

    public SimulationInstant? NextActionAt
    {
        get
        {
            if (IsExpired)
            {
                return null;
            }

            if (_expiresAt is { } expiresAt && expiresAt < _nextTickAt)
            {
                return expiresAt;
            }

            return _nextTickAt;
        }
    }

    public PeriodicScheduleAdvanceResult Advance(SimulationInstant currentTime)
    {
        if (IsExpired)
        {
            return Result(PeriodicScheduleStatus.AlreadyExpired, null);
        }

        if (NextActionAt is not { } nextActionAt || currentTime < nextActionAt)
        {
            return Result(PeriodicScheduleStatus.NotDue, null);
        }

        var tickIsWithinDuration = _expiresAt is null || _nextTickAt <= _expiresAt.Value;
        if (_nextTickAt <= currentTime && tickIsWithinDuration)
        {
            var scheduledTickAt = _nextTickAt;
            LastTickAt = scheduledTickAt;
            ExecutedTicks++;
            Revision++;

            if (_remainingTicks is not null)
            {
                _remainingTicks--;
                if (_remainingTicks == 0)
                {
                    IsExpired = true;
                    return Result(PeriodicScheduleStatus.TickedAndExpired, scheduledTickAt);
                }
            }

            if (_expiresAt is { } expiresAt && scheduledTickAt == expiresAt)
            {
                IsExpired = true;
                return Result(PeriodicScheduleStatus.TickedAndExpired, scheduledTickAt);
            }

            _nextTickAt = scheduledTickAt + Interval;
            return Result(PeriodicScheduleStatus.Ticked, scheduledTickAt);
        }

        IsExpired = true;
        Revision++;
        return Result(PeriodicScheduleStatus.ExpiredWithoutTick, null);
    }

    public void ChangeInterval(
        SimulationDuration newInterval,
        SimulationInstant currentTime)
    {
        if (newInterval == SimulationDuration.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(newInterval), "A periodic interval must be positive.");
        }

        if (IsExpired)
        {
            throw new InvalidOperationException("An expired schedule cannot be modified.");
        }

        Interval = newInterval;
        var anchor = LastTickAt ?? AppliedAt;
        var recalculatedDueAt = anchor + newInterval;
        _nextTickAt = recalculatedDueAt <= currentTime ? currentTime : recalculatedDueAt;
        Revision++;
    }

    private PeriodicScheduleAdvanceResult Result(
        PeriodicScheduleStatus status,
        SimulationInstant? tickScheduledAt) =>
        new(
            status,
            tickScheduledAt,
            NextActionAt,
            ExecutedTicks,
            RemainingTicks,
            IsExpired);
}
