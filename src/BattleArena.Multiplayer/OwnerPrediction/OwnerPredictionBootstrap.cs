using BattleArena.Core.Common;
using BattleArena.Core.Movement.Simulation;

namespace BattleArena.Multiplayer.OwnerPrediction;

public readonly record struct PredictionBootstrapPlanId :
    IComparable<PredictionBootstrapPlanId>
{
    public static PredictionBootstrapPlanId Initial { get; } = new(1);

    public PredictionBootstrapPlanId(ulong value)
    {
        if (value == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
        Value = value;
    }

    public ulong Value { get; }
    public bool IsValid => Value != 0;
    public int CompareTo(PredictionBootstrapPlanId other)
    {
        if (!IsValid || !other.IsValid)
        {
            throw new InvalidOperationException(
                "A default/invalid bootstrap plan identity cannot be ordered.");
        }
        return Value.CompareTo(other.Value);
    }
}

public enum OwnerPredictionBootstrapKind : byte
{
    PreMatch = 1,
    Reconnect = 2,
    Respawn = 3,
    TimelineRebase = 4,
}

public enum OwnerPredictionBaselinePreparation : byte
{
    FrozenCommandPredecessor = 1,
    NeutralPreroll = 2,
}

/// <summary>
/// Complete authority schedule for entering one owner-control epoch. It names
/// both the synchronized input-enable frame E and first command target T=E+L.
/// </summary>
public readonly record struct OwnerPredictionBootstrapPlan
{
    public OwnerPredictionBootstrapPlan(
        PredictionBootstrapPlanId planId,
        CombatantAuthorityPredictionEpoch authorityEpoch,
        OwnerPredictionBootstrapKind kind,
        OwnerPredictionBaselinePreparation preparation,
        SimulationInstant publishedAuthorityFrame,
        SimulationInstant baselineFrame,
        SimulationInstant localInputEnableFrame,
        SimulationInstant firstCommandTargetFrame,
        PredictionLeadFrameCount targetLead,
        PredictionLeadPolicyRevision leadRevision)
    {
        if (!planId.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(planId));
        }
        if (!authorityEpoch.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(authorityEpoch));
        }
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        if (!Enum.IsDefined(preparation))
        {
            throw new ArgumentOutOfRangeException(nameof(preparation));
        }
        if (!targetLead.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(targetLead));
        }
        if (!leadRevision.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(leadRevision));
        }
        if (localInputEnableFrame <= publishedAuthorityFrame ||
            localInputEnableFrame.Tick > long.MaxValue - targetLead.Value ||
            firstCommandTargetFrame.Tick !=
                localInputEnableFrame.Tick + targetLead.Value)
        {
            throw new ArgumentOutOfRangeException(nameof(firstCommandTargetFrame));
        }

        var expectedPreparation = kind is
            OwnerPredictionBootstrapKind.PreMatch or
            OwnerPredictionBootstrapKind.Respawn
                ? OwnerPredictionBaselinePreparation.FrozenCommandPredecessor
                : OwnerPredictionBaselinePreparation.NeutralPreroll;
        if (preparation != expectedPreparation)
        {
            throw new ArgumentException(
                "Pre-match/respawn require a frozen T-1 baseline; reconnect/rebase require neutral preroll.",
                nameof(preparation));
        }

        if (preparation == OwnerPredictionBaselinePreparation.FrozenCommandPredecessor)
        {
            if (baselineFrame.Tick != firstCommandTargetFrame.Tick - 1)
            {
                throw new ArgumentOutOfRangeException(nameof(baselineFrame));
            }
        }
        else if (baselineFrame > publishedAuthorityFrame ||
                 baselineFrame.Tick >= firstCommandTargetFrame.Tick)
        {
            throw new ArgumentOutOfRangeException(nameof(baselineFrame));
        }

        PlanId = planId;
        AuthorityEpoch = authorityEpoch;
        Kind = kind;
        Preparation = preparation;
        PublishedAuthorityFrame = publishedAuthorityFrame;
        BaselineFrame = baselineFrame;
        LocalInputEnableFrame = localInputEnableFrame;
        FirstCommandTargetFrame = firstCommandTargetFrame;
        TargetLead = targetLead;
        LeadRevision = leadRevision;
    }

    public PredictionBootstrapPlanId PlanId { get; }
    public CombatantAuthorityPredictionEpoch AuthorityEpoch { get; }
    public OwnerIntentScope Scope => OwnerIntentScope.From(AuthorityEpoch);
    public OwnerPredictionBootstrapKind Kind { get; }
    public OwnerPredictionBaselinePreparation Preparation { get; }
    public SimulationInstant PublishedAuthorityFrame { get; }
    public SimulationInstant BaselineFrame { get; }
    public SimulationInstant LocalInputEnableFrame { get; }
    public SimulationInstant FirstCommandTargetFrame { get; }
    public PredictionLeadFrameCount TargetLead { get; }
    public PredictionLeadPolicyRevision LeadRevision { get; }
    public long NeutralPrerollFrameCount =>
        Preparation == OwnerPredictionBaselinePreparation.NeutralPreroll
            ? FirstCommandTargetFrame.Tick - 1 - BaselineFrame.Tick
            : 0;
    public bool IsValid
    {
        get
        {
            var frozen =
                Preparation == OwnerPredictionBaselinePreparation.FrozenCommandPredecessor &&
                Kind is OwnerPredictionBootstrapKind.PreMatch or
                    OwnerPredictionBootstrapKind.Respawn &&
                BaselineFrame.Tick == FirstCommandTargetFrame.Tick - 1;
            var neutral =
                Preparation == OwnerPredictionBaselinePreparation.NeutralPreroll &&
                Kind is OwnerPredictionBootstrapKind.Reconnect or
                    OwnerPredictionBootstrapKind.TimelineRebase &&
                BaselineFrame <= PublishedAuthorityFrame &&
                BaselineFrame.Tick < FirstCommandTargetFrame.Tick;
            return PlanId.IsValid &&
                AuthorityEpoch.IsValid &&
                Enum.IsDefined(Kind) &&
                Enum.IsDefined(Preparation) &&
                TargetLead.IsValid &&
                LeadRevision.IsValid &&
                LocalInputEnableFrame > PublishedAuthorityFrame &&
                LocalInputEnableFrame.Tick <= long.MaxValue - TargetLead.Value &&
                FirstCommandTargetFrame.Tick ==
                    LocalInputEnableFrame.Tick + TargetLead.Value &&
                (frozen || neutral);
        }
    }
}

public static class OwnerPredictionBootstrapLimits
{
    public const int DefaultMaximumNeutralPrerollFrames = 64;
    public const int MaximumNeutralPrerollFrames = 512;
    public const int DefaultMaximumEnableNoticeFrames = 600;
    public const int MaximumEnableNoticeFrames = 3_600;
}

public readonly record struct OwnerPredictionBootstrapPolicy
{
    public static OwnerPredictionBootstrapPolicy Default { get; } = new(
        PredictionLeadUpdatePolicy.Default,
        OwnerPredictionBootstrapLimits.DefaultMaximumNeutralPrerollFrames,
        OwnerPredictionBootstrapLimits.DefaultMaximumEnableNoticeFrames);

    public OwnerPredictionBootstrapPolicy(
        PredictionLeadUpdatePolicy leadPolicy,
        int maximumNeutralPrerollFrames,
        int maximumEnableNoticeFrames =
            OwnerPredictionBootstrapLimits.DefaultMaximumEnableNoticeFrames)
    {
        if (!leadPolicy.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(leadPolicy));
        }
        if (maximumNeutralPrerollFrames <= 0 ||
            maximumNeutralPrerollFrames >
                OwnerPredictionBootstrapLimits.MaximumNeutralPrerollFrames)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumNeutralPrerollFrames));
        }
        if (maximumEnableNoticeFrames <= 0 ||
            maximumEnableNoticeFrames > OwnerPredictionBootstrapLimits.MaximumEnableNoticeFrames)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumEnableNoticeFrames));
        }
        LeadPolicy = leadPolicy;
        MaximumNeutralPrerollFrames = maximumNeutralPrerollFrames;
        MaximumEnableNoticeFrames = maximumEnableNoticeFrames;
    }

    public PredictionLeadUpdatePolicy LeadPolicy { get; }
    public int MaximumNeutralPrerollFrames { get; }
    public int MaximumEnableNoticeFrames { get; }
    public bool IsValid =>
        LeadPolicy.IsValid &&
        MaximumNeutralPrerollFrames > 0 &&
        MaximumNeutralPrerollFrames <=
            OwnerPredictionBootstrapLimits.MaximumNeutralPrerollFrames &&
        MaximumEnableNoticeFrames > 0 &&
        MaximumEnableNoticeFrames <=
            OwnerPredictionBootstrapLimits.MaximumEnableNoticeFrames;
}

public enum OwnerPredictionBootstrapState : byte
{
    AwaitingBootstrap = 1,
    AwaitingBaselineRestore = 2,
    PreparingNeutralPreroll = 3,
    WaitingForSynchronizedEnable = 4,
    Active = 5,
    SuspendedForTimelineRebase = 6,
}

public enum OwnerPredictionBootstrapApplyDecision : byte
{
    AppliedInitial = 1,
    AppliedReconnect = 2,
    AppliedRespawn = 3,
    AppliedTimelineRebase = 4,
    Duplicate = 5,
    RejectedDifferentScope = 6,
    RejectedRegression = 7,
    RejectedInvalidLifecycle = 8,
    ConflictingCurrentEpoch = 9,
    TargetOutsidePolicy = 10,
    NeutralPrerollTooLarge = 11,
    EnableWindowTooLarge = 12,
    ScheduleNotFutureOfKnownClock = 13,
    RejectedPlanIdRegression = 14,
}

public enum OwnerPredictionNeutralFrameDecision : byte
{
    Committed = 1,
    Complete = 2,
    WrongFrame = 3,
    NotRequired = 4,
    RebaseRequired = 5,
    BaselineNotRestored = 6,
}

public enum OwnerPredictionBaselineRestoreDecision : byte
{
    Committed = 1,
    Duplicate = 2,
    NoBootstrap = 3,
    WrongEpoch = 4,
    WrongFrame = 5,
    RebaseRequired = 6,
    WrongPlan = 7,
    RejectedLifecycle = 8,
}

public enum PredictionClockConfidence : byte
{
    Acquiring = 1,
    Stable = 2,
    Lost = 3,
}

public readonly record struct SynchronizedAuthorityFrameSample
{
    public SynchronizedAuthorityFrameSample(
        ulong sessionId,
        MatchFrameEpochId matchFrameEpoch,
        SimulationInstant estimatedAuthorityFrame,
        PredictionClockConfidence confidence)
    {
        if (sessionId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionId));
        }
        if (!matchFrameEpoch.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(matchFrameEpoch));
        }
        if (!Enum.IsDefined(confidence))
        {
            throw new ArgumentOutOfRangeException(nameof(confidence));
        }
        SessionId = sessionId;
        MatchFrameEpoch = matchFrameEpoch;
        EstimatedAuthorityFrame = estimatedAuthorityFrame;
        Confidence = confidence;
    }

    public ulong SessionId { get; }
    public MatchFrameEpochId MatchFrameEpoch { get; }
    public SimulationInstant EstimatedAuthorityFrame { get; }
    public PredictionClockConfidence Confidence { get; }
    public bool IsValid =>
        SessionId != 0 && MatchFrameEpoch.IsValid && Enum.IsDefined(Confidence);
}

public readonly record struct OwnerPredictionBaselineReceipt
{
    public OwnerPredictionBaselineReceipt(
        PredictionBootstrapPlanId planId,
        CombatantAuthorityPredictionEpoch authorityEpoch,
        SimulationInstant baselineFrame,
        ClientPredictionStateBaselineSource source)
    {
        if (!planId.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(planId));
        }
        if (!authorityEpoch.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(authorityEpoch));
        }
        if (source is not (
            ClientPredictionStateBaselineSource.Spawn or
            ClientPredictionStateBaselineSource.Snapshot or
            ClientPredictionStateBaselineSource.AuthorityMovement))
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }
        PlanId = planId;
        AuthorityEpoch = authorityEpoch;
        BaselineFrame = baselineFrame;
        Source = source;
    }

    public PredictionBootstrapPlanId PlanId { get; }
    public CombatantAuthorityPredictionEpoch AuthorityEpoch { get; }
    public SimulationInstant BaselineFrame { get; }
    public ClientPredictionStateBaselineSource Source { get; }
    public bool IsValid => PlanId.IsValid && AuthorityEpoch.IsValid && Source is
        ClientPredictionStateBaselineSource.Spawn or
        ClientPredictionStateBaselineSource.Snapshot or
        ClientPredictionStateBaselineSource.AuthorityMovement;
}

public enum OwnerPredictionClockDecision : byte
{
    NoBootstrap = 1,
    WaitingForConfidence = 2,
    BeforeEnableFrame = 3,
    NeutralPrerollIncomplete = 4,
    BaselineNotRestored = 5,
    InputEnabled = 6,
    Active = 7,
    ClockConfidenceLost = 8,
    MissedFirstCommandDeadline = 9,
    RebaseRequired = 10,
    RejectedClockScope = 11,
}

public enum OwnerPredictionCommandFrameDecision : byte
{
    Committed = 1,
    NotActive = 2,
    WrongFrame = 3,
    RebaseRequired = 4,
    CommittedTimelineExhausted = 5,
}

/// <summary>
/// Pure lifecycle/scheduling coordinator. It never simulates movement: callers
/// prepare declared neutral frames, wait on synchronized authority time, and
/// commit exact consecutive command targets through this gate.
/// </summary>
public sealed class OwnerPredictionBootstrapCoordinator
{
    private readonly ClientPredictionLifecycleRouter _lifecycle;
    private readonly ulong _sessionId;
    private readonly CombatantId _combatantId;
    private readonly OwnerPredictionBootstrapPolicy _policy;
    private readonly Action _commitPendingAggregate;
    private OwnerPredictionBootstrapPlan _currentPlan;
    private OwnerPredictionBootstrapPlan _pendingPlan;
    private CombatantLocalPredictionEpoch _pendingExpectedEpoch;
    private bool _hasCurrentPlan;
    private bool _hasPendingPlan;
    private bool _hasPendingExpectedEpoch;
    private PredictionLeadUpdateGate? _leadPolicyGate;
    private PredictionLeadUpdateGate? _preparedLeadPolicyGate;
    private PredictionLeadUpdate _preparedLeadBaseline;
    private long _lastPreparedFrame;
    private long _nextCommandTargetFrame;
    private long _highestStableAuthorityFrame;
    private bool _hasStableAuthorityFrame;
    private MatchFrameEpochId _clockMatchFrameEpoch;
    private long _pendingHighestStableAuthorityFrame;
    private bool _hasPendingStableAuthorityFrame;

    public OwnerPredictionBootstrapCoordinator(
        ClientPredictionLifecycleRouter lifecycle,
        ulong sessionId,
        CombatantId combatantId,
        OwnerPredictionBootstrapPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(lifecycle);
        if (sessionId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionId));
        }
        if (combatantId.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(combatantId));
        }
        if (!policy.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(policy));
        }
        _lifecycle = lifecycle;
        _sessionId = sessionId;
        _combatantId = combatantId;
        _policy = policy;
        _commitPendingAggregate = CommitPendingAggregate;
        State = OwnerPredictionBootstrapState.AwaitingBootstrap;
    }

    public OwnerPredictionBootstrapCoordinator(
        ClientPredictionLifecycleRouter lifecycle,
        ulong sessionId,
        CombatantId combatantId)
        : this(
            lifecycle,
            sessionId,
            combatantId,
            OwnerPredictionBootstrapPolicy.Default)
    {
    }

    public OwnerPredictionBootstrapPolicy Policy => _policy;
    public OwnerPredictionBootstrapState State { get; private set; }
    public OwnerPredictionBootstrapPlan? CurrentPlan =>
        _hasCurrentPlan ? _currentPlan : null;
    public OwnerPredictionBootstrapPlan? PendingPlan =>
        _hasPendingPlan ? _pendingPlan : null;
    public PredictionLeadPolicySnapshot? LeadPolicy => _leadPolicyGate is null
        ? null
        : new PredictionLeadPolicySnapshot(
            _leadPolicyGate.Scope,
            _leadPolicyGate.Policy,
            _leadPolicyGate.LatestAccepted,
            _leadPolicyGate.RequiresTimelineRebase);
    public CombatantLocalPredictionEpoch? CommittedEpoch =>
        _lifecycle.TryGetCurrent(_combatantId, out var current)
            ? current
            : null;
    public bool RequiresTimelineRebase =>
        State == OwnerPredictionBootstrapState.SuspendedForTimelineRebase;
    public SimulationInstant? NextCommandTargetFrame =>
        State == OwnerPredictionBootstrapState.Active && !_hasPendingPlan
            ? new SimulationInstant(_nextCommandTargetFrame)
            : null;
    public long NeutralFramesRemaining => !_hasCurrentPlan || _hasPendingPlan ||
        _currentPlan.Preparation != OwnerPredictionBaselinePreparation.NeutralPreroll
            ? 0
            : Math.Max(
                0,
                _currentPlan.FirstCommandTargetFrame.Tick - 1 - _lastPreparedFrame);

    public OwnerPredictionBootstrapApplyDecision ApplyBootstrap(
        OwnerPredictionBootstrapPlan plan)
    {
        if (!plan.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(plan));
        }
        if (plan.AuthorityEpoch.SessionId != _sessionId ||
            plan.AuthorityEpoch.CombatantId != _combatantId)
        {
            return OwnerPredictionBootstrapApplyDecision.RejectedDifferentScope;
        }
        if (_hasPendingPlan)
        {
            if (plan == _pendingPlan)
            {
                return OwnerPredictionBootstrapApplyDecision.Duplicate;
            }
            if (plan.PlanId.CompareTo(_pendingPlan.PlanId) <= 0)
            {
                if (plan.PlanId == _pendingPlan.PlanId)
                {
                    RequireTimelineRebase();
                    return OwnerPredictionBootstrapApplyDecision.ConflictingCurrentEpoch;
                }
                return OwnerPredictionBootstrapApplyDecision.RejectedPlanIdRegression;
            }
        }
        if (_hasCurrentPlan)
        {
            if (plan == _currentPlan)
            {
                return OwnerPredictionBootstrapApplyDecision.Duplicate;
            }
            if (plan.PlanId.CompareTo(_currentPlan.PlanId) <= 0)
            {
                if (plan.PlanId == _currentPlan.PlanId)
                {
                    RequireTimelineRebase();
                    return OwnerPredictionBootstrapApplyDecision.ConflictingCurrentEpoch;
                }
                return OwnerPredictionBootstrapApplyDecision.RejectedPlanIdRegression;
            }
        }

        if (!_lifecycle.TryGetCurrent(_combatantId, out var committed))
        {
            if (plan.Kind != OwnerPredictionBootstrapKind.PreMatch)
            {
                return OwnerPredictionBootstrapApplyDecision.RejectedInvalidLifecycle;
            }
            var initialPolicyDecision = ValidatePlanPolicy(plan, suspend: false);
            if (initialPolicyDecision is { } rejected)
            {
                return rejected;
            }
            StagePlan(plan, expectedEpoch: null);
            return OwnerPredictionBootstrapApplyDecision.AppliedInitial;
        }
        if (plan.AuthorityEpoch == committed.Authority)
        {
            RequireTimelineRebase();
            return OwnerPredictionBootstrapApplyDecision.ConflictingCurrentEpoch;
        }

        var current = committed.Authority;
        if (EpochRegresses(current, plan.AuthorityEpoch))
        {
            return OwnerPredictionBootstrapApplyDecision.RejectedRegression;
        }
        if (!KindMatchesLifecycle(plan.Kind, current, plan.AuthorityEpoch))
        {
            return OwnerPredictionBootstrapApplyDecision.RejectedInvalidLifecycle;
        }
        var replacementPolicyDecision = ValidatePlanPolicy(plan, suspend: true);
        if (replacementPolicyDecision is { } policyRejected)
        {
            return policyRejected;
        }
        if (HasSameClockScope(plan.AuthorityEpoch) &&
            _hasStableAuthorityFrame &&
            plan.FirstCommandTargetFrame.Tick <= _highestStableAuthorityFrame)
        {
            RequireTimelineRebase();
            return OwnerPredictionBootstrapApplyDecision.ScheduleNotFutureOfKnownClock;
        }

        StagePlan(plan, committed);
        return plan.Kind switch
        {
            OwnerPredictionBootstrapKind.Reconnect =>
                OwnerPredictionBootstrapApplyDecision.AppliedReconnect,
            OwnerPredictionBootstrapKind.Respawn =>
                OwnerPredictionBootstrapApplyDecision.AppliedRespawn,
            OwnerPredictionBootstrapKind.TimelineRebase =>
                OwnerPredictionBootstrapApplyDecision.AppliedTimelineRebase,
            _ => throw new InvalidOperationException("Pre-match bootstrap cannot replace an epoch."),
        };
    }

    public PredictionLeadUpdateDecision ObserveLeadUpdate(
        PredictionLeadUpdate update,
        PredictionLeadSafetyContext context)
    {
        if (_leadPolicyGate is null)
        {
            RequireTimelineRebase();
            return PredictionLeadUpdateDecision.TimelineRebaseRequired;
        }
        var decision = _leadPolicyGate.Observe(update, context);
        if (_leadPolicyGate.RequiresTimelineRebase)
        {
            RequireTimelineRebase();
        }
        return decision;
    }

    /// <summary>
    /// Exports the only valid command-builder activation seed. Lifecycle
    /// identity and the next target frame come from this committed aggregate,
    /// never from independently supplied adapter values.
    /// </summary>
    public OwnerCommandTimelineSeed CreateCommandTimelineSeed(
        InputSequence firstSequence)
    {
        if (!firstSequence.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(firstSequence));
        }
        if (State != OwnerPredictionBootstrapState.Active ||
            _hasPendingPlan ||
            !_hasCurrentPlan ||
            !_lifecycle.TryGetCurrent(_combatantId, out var committed) ||
            committed.Authority != _currentPlan.AuthorityEpoch)
        {
            throw new InvalidOperationException(
                "A command timeline can only be seeded from the active committed bootstrap epoch.");
        }

        return new OwnerCommandTimelineSeed(
            committed.Authority,
            firstSequence,
            new SimulationInstant(_nextCommandTargetFrame));
    }

    public bool TryGetNextNeutralFrame(out SimulationInstant frame)
    {
        if (!_hasCurrentPlan || _hasPendingPlan || RequiresTimelineRebase ||
            _currentPlan.Preparation !=
                OwnerPredictionBaselinePreparation.NeutralPreroll ||
            NeutralFramesRemaining == 0)
        {
            frame = default;
            return false;
        }
        frame = new SimulationInstant(_lastPreparedFrame + 1);
        return true;
    }

    public OwnerPredictionNeutralFrameDecision CommitNeutralFrame(
        SimulationInstant frame)
    {
        if (RequiresTimelineRebase)
        {
            return OwnerPredictionNeutralFrameDecision.RebaseRequired;
        }
        if (_hasPendingPlan)
        {
            return OwnerPredictionNeutralFrameDecision.BaselineNotRestored;
        }
        if (!_hasCurrentPlan ||
            _currentPlan.Preparation !=
                OwnerPredictionBaselinePreparation.NeutralPreroll)
        {
            return OwnerPredictionNeutralFrameDecision.NotRequired;
        }
        if (NeutralFramesRemaining == 0)
        {
            return OwnerPredictionNeutralFrameDecision.Complete;
        }
        if (frame.Tick != _lastPreparedFrame + 1)
        {
            return OwnerPredictionNeutralFrameDecision.WrongFrame;
        }

        _lastPreparedFrame = frame.Tick;
        if (NeutralFramesRemaining == 0)
        {
            State = OwnerPredictionBootstrapState.WaitingForSynchronizedEnable;
        }
        return OwnerPredictionNeutralFrameDecision.Committed;
    }

    public OwnerPredictionBaselineRestoreDecision CommitStateBearingBaseline(
        OwnerPredictionBaselineReceipt receipt)
    {
        if (!receipt.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(receipt));
        }
        if (!_hasPendingPlan)
        {
            if (!_hasCurrentPlan)
            {
                return OwnerPredictionBaselineRestoreDecision.NoBootstrap;
            }
            return receipt.PlanId == _currentPlan.PlanId &&
                receipt.AuthorityEpoch == _currentPlan.AuthorityEpoch &&
                receipt.BaselineFrame == _currentPlan.BaselineFrame
                    ? OwnerPredictionBaselineRestoreDecision.Duplicate
                    : OwnerPredictionBaselineRestoreDecision.WrongPlan;
        }
        if (RequiresTimelineRebase)
        {
            return OwnerPredictionBaselineRestoreDecision.RebaseRequired;
        }
        if (receipt.PlanId != _pendingPlan.PlanId)
        {
            return OwnerPredictionBaselineRestoreDecision.WrongPlan;
        }
        if (receipt.AuthorityEpoch != _pendingPlan.AuthorityEpoch)
        {
            return OwnerPredictionBaselineRestoreDecision.WrongEpoch;
        }
        if (receipt.BaselineFrame != _pendingPlan.BaselineFrame)
        {
            return OwnerPredictionBaselineRestoreDecision.WrongFrame;
        }
        if (HasSameClockScope(_pendingPlan.AuthorityEpoch) &&
            _hasStableAuthorityFrame &&
            _pendingPlan.FirstCommandTargetFrame.Tick <= _highestStableAuthorityFrame)
        {
            RequireTimelineRebase();
            return OwnerPredictionBaselineRestoreDecision.RebaseRequired;
        }
        if (_hasPendingStableAuthorityFrame &&
            _pendingPlan.FirstCommandTargetFrame.Tick <=
                _pendingHighestStableAuthorityFrame)
        {
            RequireTimelineRebase();
            return OwnerPredictionBaselineRestoreDecision.RebaseRequired;
        }

        var leadBaseline = new PredictionLeadUpdate(
            _pendingPlan.Scope,
            _pendingPlan.TargetLead,
            _pendingPlan.LeadRevision,
            _pendingPlan.LocalInputEnableFrame);
        var leadGate = _leadPolicyGate ?? new PredictionLeadUpdateGate(
            _pendingPlan.Scope,
            _policy.LeadPolicy);
        var leadDecision = leadGate.PreviewAuthorityBootstrapBaseline(leadBaseline);
        if (leadDecision is not (
            PredictionLeadBootstrapBaselineDecision.SeededCurrentScope or
            PredictionLeadBootstrapBaselineDecision.SeededNewControl or
            PredictionLeadBootstrapBaselineDecision.SeededNewLife or
            PredictionLeadBootstrapBaselineDecision.IdempotentDuplicate))
        {
            RequireTimelineRebase();
            return OwnerPredictionBaselineRestoreDecision.RejectedLifecycle;
        }

        _preparedLeadPolicyGate = leadGate;
        _preparedLeadBaseline = leadBaseline;
        var expectedEpoch = _hasPendingExpectedEpoch
            ? _pendingExpectedEpoch
            : (CombatantLocalPredictionEpoch?)null;
        var lifecycleResult = _lifecycle.CompareAndObserveStateBaseline(
            receipt.AuthorityEpoch,
            receipt.Source,
            expectedEpoch,
            _commitPendingAggregate);
        _preparedLeadPolicyGate = null;
        _preparedLeadBaseline = default;
        if (!lifecycleResult.IsAccepted)
        {
            RequireTimelineRebase();
            return OwnerPredictionBaselineRestoreDecision.RejectedLifecycle;
        }
        return OwnerPredictionBaselineRestoreDecision.Committed;
    }

    public OwnerPredictionClockDecision ObserveClock(
        SynchronizedAuthorityFrameSample sample)
    {
        if (!sample.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(sample));
        }
        if (!_hasCurrentPlan && !_hasPendingPlan)
        {
            return OwnerPredictionClockDecision.NoBootstrap;
        }
        var expectedEpoch = _hasPendingPlan
            ? _pendingPlan.AuthorityEpoch
            : _currentPlan.AuthorityEpoch;
        if (sample.SessionId != expectedEpoch.SessionId ||
            sample.MatchFrameEpoch != expectedEpoch.MatchFrameEpoch)
        {
            return OwnerPredictionClockDecision.RejectedClockScope;
        }
        if (RequiresTimelineRebase)
        {
            return OwnerPredictionClockDecision.RebaseRequired;
        }
        if (sample.Confidence == PredictionClockConfidence.Lost)
        {
            RequireTimelineRebase();
            return OwnerPredictionClockDecision.ClockConfidenceLost;
        }
        if (sample.Confidence != PredictionClockConfidence.Stable)
        {
            return OwnerPredictionClockDecision.WaitingForConfidence;
        }

        if (_hasPendingPlan)
        {
            ObservePendingStableFrame(sample.EstimatedAuthorityFrame);
            return OwnerPredictionClockDecision.BaselineNotRestored;
        }

        ObserveStableFrame(sample.EstimatedAuthorityFrame);
        if (State == OwnerPredictionBootstrapState.Active)
        {
            return OwnerPredictionClockDecision.Active;
        }
        if (_highestStableAuthorityFrame < _currentPlan.LocalInputEnableFrame.Tick)
        {
            return OwnerPredictionClockDecision.BeforeEnableFrame;
        }
        if (_highestStableAuthorityFrame >= _currentPlan.FirstCommandTargetFrame.Tick)
        {
            RequireTimelineRebase();
            return OwnerPredictionClockDecision.MissedFirstCommandDeadline;
        }
        if (NeutralFramesRemaining != 0)
        {
            return OwnerPredictionClockDecision.NeutralPrerollIncomplete;
        }

        State = OwnerPredictionBootstrapState.Active;
        _nextCommandTargetFrame = _currentPlan.FirstCommandTargetFrame.Tick;
        return OwnerPredictionClockDecision.InputEnabled;
    }

    public OwnerPredictionCommandFrameDecision CommitCommandTargetFrame(
        SimulationInstant frame)
    {
        if (RequiresTimelineRebase)
        {
            return OwnerPredictionCommandFrameDecision.RebaseRequired;
        }
        if (State != OwnerPredictionBootstrapState.Active)
        {
            return OwnerPredictionCommandFrameDecision.NotActive;
        }
        if (frame.Tick != _nextCommandTargetFrame)
        {
            return OwnerPredictionCommandFrameDecision.WrongFrame;
        }
        if (_nextCommandTargetFrame == long.MaxValue)
        {
            RequireTimelineRebase();
            return OwnerPredictionCommandFrameDecision.CommittedTimelineExhausted;
        }

        _nextCommandTargetFrame++;
        return OwnerPredictionCommandFrameDecision.Committed;
    }

    private void StagePlan(
        OwnerPredictionBootstrapPlan plan,
        CombatantLocalPredictionEpoch? expectedEpoch)
    {
        var preservePendingClock = _hasPendingPlan &&
            _pendingPlan.AuthorityEpoch.SessionId == plan.AuthorityEpoch.SessionId &&
            _pendingPlan.AuthorityEpoch.MatchFrameEpoch ==
                plan.AuthorityEpoch.MatchFrameEpoch;
        if (!preservePendingClock)
        {
            _pendingHighestStableAuthorityFrame = 0;
            _hasPendingStableAuthorityFrame = false;
        }
        _pendingPlan = plan;
        _hasPendingPlan = true;
        _pendingExpectedEpoch = expectedEpoch.GetValueOrDefault();
        _hasPendingExpectedEpoch = expectedEpoch.HasValue;
        State = OwnerPredictionBootstrapState.AwaitingBaselineRestore;
    }

    private void CommitPendingAggregate()
    {
        var leadGate = _preparedLeadPolicyGate ?? throw new InvalidOperationException(
            "A lifecycle aggregate commit requires a prepared lead-policy gate.");
        var leadDecision = leadGate.ApplyAuthorityBootstrapBaseline(
            _preparedLeadBaseline);
        if (leadDecision is not (
            PredictionLeadBootstrapBaselineDecision.SeededCurrentScope or
            PredictionLeadBootstrapBaselineDecision.SeededNewControl or
            PredictionLeadBootstrapBaselineDecision.SeededNewLife or
            PredictionLeadBootstrapBaselineDecision.IdempotentDuplicate))
        {
            throw new InvalidOperationException(
                "A synchronous lead-policy preview diverged during commit.");
        }
        _leadPolicyGate = leadGate;
        CommitPendingPlan();
        State = _currentPlan.NeutralPrerollFrameCount > 0
            ? OwnerPredictionBootstrapState.PreparingNeutralPreroll
            : OwnerPredictionBootstrapState.WaitingForSynchronizedEnable;
    }

    private void CommitPendingPlan()
    {
        var matchFrameChanged = !_hasCurrentPlan ||
            _currentPlan.AuthorityEpoch.MatchFrameEpoch !=
                _pendingPlan.AuthorityEpoch.MatchFrameEpoch;
        _currentPlan = _pendingPlan;
        _hasCurrentPlan = true;
        _pendingPlan = default;
        _hasPendingPlan = false;
        _pendingExpectedEpoch = default;
        _hasPendingExpectedEpoch = false;
        _lastPreparedFrame = _currentPlan.BaselineFrame.Tick;
        _nextCommandTargetFrame = _currentPlan.FirstCommandTargetFrame.Tick;
        if (matchFrameChanged)
        {
            _clockMatchFrameEpoch = _currentPlan.AuthorityEpoch.MatchFrameEpoch;
            _highestStableAuthorityFrame = _pendingHighestStableAuthorityFrame;
            _hasStableAuthorityFrame = _hasPendingStableAuthorityFrame;
        }
        else if (_hasPendingStableAuthorityFrame)
        {
            _highestStableAuthorityFrame = _hasStableAuthorityFrame
                ? Math.Max(
                    _highestStableAuthorityFrame,
                    _pendingHighestStableAuthorityFrame)
                : _pendingHighestStableAuthorityFrame;
            _hasStableAuthorityFrame = true;
        }
        _pendingHighestStableAuthorityFrame = 0;
        _hasPendingStableAuthorityFrame = false;
    }

    private void ObserveStableFrame(SimulationInstant frame)
    {
        _highestStableAuthorityFrame = _hasStableAuthorityFrame
            ? Math.Max(_highestStableAuthorityFrame, frame.Tick)
            : frame.Tick;
        _hasStableAuthorityFrame = true;
    }

    private void ObservePendingStableFrame(SimulationInstant frame)
    {
        _pendingHighestStableAuthorityFrame = _hasPendingStableAuthorityFrame
            ? Math.Max(_pendingHighestStableAuthorityFrame, frame.Tick)
            : frame.Tick;
        _hasPendingStableAuthorityFrame = true;
    }

    private void RequireTimelineRebase() =>
        State = OwnerPredictionBootstrapState.SuspendedForTimelineRebase;

    private OwnerPredictionBootstrapApplyDecision? ValidatePlanPolicy(
        OwnerPredictionBootstrapPlan plan,
        bool suspend)
    {
        if (!_policy.LeadPolicy.Contains(plan.TargetLead))
        {
            if (suspend)
            {
                RequireTimelineRebase();
            }
            return OwnerPredictionBootstrapApplyDecision.TargetOutsidePolicy;
        }
        if (plan.NeutralPrerollFrameCount > _policy.MaximumNeutralPrerollFrames)
        {
            if (suspend)
            {
                RequireTimelineRebase();
            }
            return OwnerPredictionBootstrapApplyDecision.NeutralPrerollTooLarge;
        }
        var enableNotice =
            plan.LocalInputEnableFrame.Tick - plan.PublishedAuthorityFrame.Tick;
        if (enableNotice > _policy.MaximumEnableNoticeFrames)
        {
            if (suspend)
            {
                RequireTimelineRebase();
            }
            return OwnerPredictionBootstrapApplyDecision.EnableWindowTooLarge;
        }
        return null;
    }

    private bool HasSameClockScope(CombatantAuthorityPredictionEpoch epoch) =>
        _hasCurrentPlan &&
        epoch.SessionId == _sessionId &&
        epoch.MatchFrameEpoch == _clockMatchFrameEpoch;

    private static bool EpochRegresses(
        CombatantAuthorityPredictionEpoch current,
        CombatantAuthorityPredictionEpoch next) =>
        next.MatchFrameEpoch < current.MatchFrameEpoch ||
        next.Life.CompareTo(current.Life) < 0 ||
        next.AuthorityDiscontinuity < current.AuthorityDiscontinuity ||
        next.OwnerControl < current.OwnerControl;

    private static bool KindMatchesLifecycle(
        OwnerPredictionBootstrapKind kind,
        CombatantAuthorityPredictionEpoch current,
        CombatantAuthorityPredictionEpoch next)
    {
        var matchChanged = next.MatchFrameEpoch != current.MatchFrameEpoch;
        var lifeChanged = next.Life != current.Life;
        var discontinuityChanged =
            next.AuthorityDiscontinuity != current.AuthorityDiscontinuity;
        var controlChanged = next.OwnerControl != current.OwnerControl;
        return kind switch
        {
            OwnerPredictionBootstrapKind.Reconnect =>
                !matchChanged && !lifeChanged && !discontinuityChanged && controlChanged,
            OwnerPredictionBootstrapKind.Respawn =>
                !matchChanged && lifeChanged && discontinuityChanged && controlChanged,
            OwnerPredictionBootstrapKind.TimelineRebase =>
                !lifeChanged && !discontinuityChanged && controlChanged,
            _ => false,
        };
    }
}
