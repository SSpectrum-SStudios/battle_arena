using BattleArena.Core.Common;

namespace BattleArena.Multiplayer.OwnerPrediction;

public static class PredictionLeadLimits
{
    public const int DefaultMinimumFrames = 2;
    public const int DefaultMaximumFrames = 48;
    public const int MaximumSupportedFrames = 256;
    public const int DefaultMinimumNoticeFrames = 1;
    public const int MaximumNoticeFrames = 256;
}

/// <summary>An absolute owner prediction distance measured in fixed simulation frames.</summary>
public readonly record struct PredictionLeadFrameCount :
    IComparable<PredictionLeadFrameCount>
{
    public PredictionLeadFrameCount(int value)
    {
        if (value <= 0 || value > PredictionLeadLimits.MaximumSupportedFrames)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
        Value = value;
    }

    public int Value { get; }
    public bool IsValid => Value > 0 && Value <= PredictionLeadLimits.MaximumSupportedFrames;
    public int CompareTo(PredictionLeadFrameCount other)
    {
        if (!IsValid || !other.IsValid)
        {
            throw new InvalidOperationException(
                "A default/invalid prediction lead cannot be ordered.");
        }
        return Value.CompareTo(other.Value);
    }
}

/// <summary>Monotonic authority revision for one owner-control lead policy.</summary>
public readonly record struct PredictionLeadPolicyRevision :
    IComparable<PredictionLeadPolicyRevision>
{
    public static PredictionLeadPolicyRevision Initial { get; } = new(1);

    public PredictionLeadPolicyRevision(ulong value)
    {
        if (value == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
        Value = value;
    }

    public ulong Value { get; }
    public bool IsValid => Value != 0;
    public PredictionLeadPolicyRevision Next()
    {
        if (!IsValid)
        {
            throw new InvalidOperationException(
                "A default/invalid prediction lead policy revision cannot be advanced.");
        }
        if (Value == ulong.MaxValue)
        {
            throw new InvalidOperationException(
                "Prediction lead policy revision is exhausted.");
        }
        return new PredictionLeadPolicyRevision(Value + 1);
    }

    public int CompareTo(PredictionLeadPolicyRevision other)
    {
        if (!IsValid || !other.IsValid)
        {
            throw new InvalidOperationException(
                "A default/invalid prediction lead policy revision cannot be ordered.");
        }
        return Value.CompareTo(other.Value);
    }
}

/// <summary>Immutable negotiated bounds used to validate authority lead updates.</summary>
public readonly record struct PredictionLeadUpdatePolicy
{
    public static PredictionLeadUpdatePolicy Default { get; } = new(
        PredictionLeadLimits.DefaultMinimumFrames,
        PredictionLeadLimits.DefaultMaximumFrames,
        PredictionLeadLimits.DefaultMinimumNoticeFrames);

    public PredictionLeadUpdatePolicy(
        int minimumLeadFrames,
        int maximumLeadFrames,
        int minimumNoticeFrames)
    {
        if (minimumLeadFrames <= 0 ||
            minimumLeadFrames > PredictionLeadLimits.MaximumSupportedFrames)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumLeadFrames));
        }
        if (maximumLeadFrames < minimumLeadFrames ||
            maximumLeadFrames > PredictionLeadLimits.MaximumSupportedFrames)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumLeadFrames));
        }
        if (minimumNoticeFrames <= 0 ||
            minimumNoticeFrames > PredictionLeadLimits.MaximumNoticeFrames)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumNoticeFrames));
        }

        MinimumLeadFrames = minimumLeadFrames;
        MaximumLeadFrames = maximumLeadFrames;
        MinimumNoticeFrames = minimumNoticeFrames;
    }

    public int MinimumLeadFrames { get; }
    public int MaximumLeadFrames { get; }
    public int MinimumNoticeFrames { get; }
    public bool IsValid =>
        MinimumLeadFrames > 0 &&
        MinimumLeadFrames <= MaximumLeadFrames &&
        MaximumLeadFrames <= PredictionLeadLimits.MaximumSupportedFrames &&
        MinimumNoticeFrames > 0 &&
        MinimumNoticeFrames <= PredictionLeadLimits.MaximumNoticeFrames;

    public bool Contains(PredictionLeadFrameCount lead) =>
        lead.IsValid &&
        lead.Value >= MinimumLeadFrames &&
        lead.Value <= MaximumLeadFrames;
}

/// <summary>
/// Absolute authority policy. A newer revision replaces, rather than increments,
/// any older pending target and never relabels already scheduled commands.
/// </summary>
public readonly record struct PredictionLeadUpdate
{
    public PredictionLeadUpdate(
        OwnerIntentScope scope,
        PredictionLeadFrameCount targetLead,
        PredictionLeadPolicyRevision revision,
        SimulationInstant effectiveFrame)
    {
        if (!scope.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(scope));
        }
        if (!targetLead.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(targetLead));
        }
        if (!revision.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(revision));
        }

        Scope = scope;
        TargetLead = targetLead;
        Revision = revision;
        EffectiveFrame = effectiveFrame;
    }

    public OwnerIntentScope Scope { get; }
    public PredictionLeadFrameCount TargetLead { get; }
    public PredictionLeadPolicyRevision Revision { get; }
    public SimulationInstant EffectiveFrame { get; }
    public bool IsValid => Scope.IsValid && TargetLead.IsValid && Revision.IsValid;
}

/// <summary>The local scheduling frontier at the instant an update is observed.</summary>
public readonly record struct PredictionLeadSafetyContext
{
    public PredictionLeadSafetyContext(
        SimulationInstant observedAuthorityFrame,
        SimulationInstant? lastScheduledCommandFrame)
    {
        ObservedAuthorityFrame = observedAuthorityFrame;
        LastScheduledCommandFrame = lastScheduledCommandFrame;
    }

    public SimulationInstant ObservedAuthorityFrame { get; }
    public SimulationInstant? LastScheduledCommandFrame { get; }

    public bool IsSafe(
        SimulationInstant effectiveFrame,
        PredictionLeadUpdatePolicy policy)
    {
        if (!policy.IsValid ||
            ObservedAuthorityFrame.Tick > long.MaxValue - policy.MinimumNoticeFrames)
        {
            return false;
        }

        var earliest = ObservedAuthorityFrame.Tick + policy.MinimumNoticeFrames;
        if (LastScheduledCommandFrame is { } scheduled)
        {
            if (scheduled.Tick == long.MaxValue)
            {
                return false;
            }
            earliest = Math.Max(earliest, scheduled.Tick + 1);
        }
        return effectiveFrame.Tick >= earliest;
    }
}

public enum PredictionLeadUpdateDecision : byte
{
    Applied = 1,
    IdempotentDuplicate = 2,
    IgnoredStaleRevision = 3,
    WrongScope = 4,
    InvalidUpdate = 5,
    TargetOutsidePolicy = 6,
    UnsafeEffectiveFrame = 7,
    ConflictingRevision = 8,
    TimelineRebaseRequired = 9,
}

public enum PredictionLeadBootstrapDecision : byte
{
    AppliedNewControl = 1,
    AppliedNewLife = 2,
    Duplicate = 3,
    RejectedDifferentOwner = 4,
    RejectedStaleLife = 5,
    RejectedStaleControl = 6,
}

public enum PredictionLeadBootstrapBaselineDecision : byte
{
    SeededCurrentScope = 1,
    SeededNewControl = 2,
    SeededNewLife = 3,
    IdempotentDuplicate = 4,
    ConflictingCurrentScope = 5,
    RejectedDifferentOwner = 6,
    RejectedStaleLife = 7,
    RejectedStaleControl = 8,
    TargetOutsidePolicy = 9,
}

public readonly record struct PredictionLeadPolicySnapshot(
    OwnerIntentScope Scope,
    PredictionLeadUpdatePolicy Policy,
    PredictionLeadUpdate? LatestAccepted,
    bool RequiresTimelineRebase);

/// <summary>
/// Scope-bound, allocation-free receive gate for authority lead policy. Same-scope
/// contradictory or unschedulable evidence locks until a new owner-control scope
/// supplies a fresh bootstrap baseline.
/// </summary>
public sealed class PredictionLeadUpdateGate
{
    private readonly PredictionLeadUpdatePolicy _policy;
    private OwnerIntentScope _scope;
    private PredictionLeadUpdate _latestAccepted;
    private bool _hasAccepted;

    public PredictionLeadUpdateGate(
        OwnerIntentScope scope,
        PredictionLeadUpdatePolicy policy)
    {
        if (!scope.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(scope));
        }
        if (!policy.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(policy));
        }
        _scope = scope;
        _policy = policy;
    }

    public PredictionLeadUpdateGate(OwnerIntentScope scope)
        : this(scope, PredictionLeadUpdatePolicy.Default)
    {
    }

    public OwnerIntentScope Scope => _scope;
    public PredictionLeadUpdatePolicy Policy => _policy;
    public PredictionLeadUpdate? LatestAccepted => _hasAccepted
        ? _latestAccepted
        : null;
    public bool RequiresTimelineRebase { get; private set; }

    public PredictionLeadUpdateDecision Observe(
        PredictionLeadUpdate update,
        PredictionLeadSafetyContext context)
    {
        if (!update.IsValid)
        {
            RequiresTimelineRebase = true;
            return PredictionLeadUpdateDecision.InvalidUpdate;
        }
        if (update.Scope != _scope)
        {
            return PredictionLeadUpdateDecision.WrongScope;
        }
        if (RequiresTimelineRebase)
        {
            return PredictionLeadUpdateDecision.TimelineRebaseRequired;
        }

        if (_hasAccepted)
        {
            var revisionOrder = update.Revision.CompareTo(_latestAccepted.Revision);
            if (revisionOrder < 0)
            {
                return PredictionLeadUpdateDecision.IgnoredStaleRevision;
            }
            if (revisionOrder == 0)
            {
                if (update == _latestAccepted)
                {
                    return PredictionLeadUpdateDecision.IdempotentDuplicate;
                }
                RequiresTimelineRebase = true;
                return PredictionLeadUpdateDecision.ConflictingRevision;
            }
        }

        if (!_policy.Contains(update.TargetLead))
        {
            RequiresTimelineRebase = true;
            return PredictionLeadUpdateDecision.TargetOutsidePolicy;
        }
        if (!context.IsSafe(update.EffectiveFrame, _policy))
        {
            RequiresTimelineRebase = true;
            return PredictionLeadUpdateDecision.UnsafeEffectiveFrame;
        }

        _latestAccepted = update;
        _hasAccepted = true;
        return PredictionLeadUpdateDecision.Applied;
    }

    /// <summary>
    /// Atomically seeds the absolute policy carried by a state-bearing authority
    /// bootstrap. Its effective frame is the bootstrap input-enable frame.
    /// </summary>
    public PredictionLeadBootstrapBaselineDecision ApplyAuthorityBootstrapBaseline(
        PredictionLeadUpdate baseline)
    {
        var decision = PreviewAuthorityBootstrapBaseline(baseline);
        if (decision ==
            PredictionLeadBootstrapBaselineDecision.ConflictingCurrentScope)
        {
            RequiresTimelineRebase = true;
            return decision;
        }
        if (decision is not (
            PredictionLeadBootstrapBaselineDecision.SeededCurrentScope or
            PredictionLeadBootstrapBaselineDecision.SeededNewControl or
            PredictionLeadBootstrapBaselineDecision.SeededNewLife))
        {
            return decision;
        }

        _scope = baseline.Scope;
        _latestAccepted = baseline;
        _hasAccepted = true;
        RequiresTimelineRebase = false;
        return decision;
    }

    /// <summary>Validates a bootstrap policy baseline without changing state.</summary>
    public PredictionLeadBootstrapBaselineDecision PreviewAuthorityBootstrapBaseline(
        PredictionLeadUpdate baseline)
    {
        if (!baseline.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(baseline));
        }
        if (!_policy.Contains(baseline.TargetLead))
        {
            return PredictionLeadBootstrapBaselineDecision.TargetOutsidePolicy;
        }
        if (baseline.Scope.SessionId != _scope.SessionId ||
            baseline.Scope.Life.CombatantId != _scope.Life.CombatantId)
        {
            return PredictionLeadBootstrapBaselineDecision.RejectedDifferentOwner;
        }

        var lifeOrder = baseline.Scope.Life.Life.Value.CompareTo(
            _scope.Life.Life.Value);
        if (lifeOrder < 0)
        {
            return PredictionLeadBootstrapBaselineDecision.RejectedStaleLife;
        }
        var controlOrder = baseline.Scope.OwnerControl.Value.CompareTo(
            _scope.OwnerControl.Value);
        if (controlOrder < 0 || controlOrder == 0 && lifeOrder > 0)
        {
            return PredictionLeadBootstrapBaselineDecision.RejectedStaleControl;
        }
        if (lifeOrder == 0 && controlOrder == 0 && _hasAccepted)
        {
            if (baseline == _latestAccepted)
            {
                return PredictionLeadBootstrapBaselineDecision.IdempotentDuplicate;
            }
            return PredictionLeadBootstrapBaselineDecision.ConflictingCurrentScope;
        }

        return lifeOrder > 0
            ? PredictionLeadBootstrapBaselineDecision.SeededNewLife
            : controlOrder > 0
                ? PredictionLeadBootstrapBaselineDecision.SeededNewControl
                : PredictionLeadBootstrapBaselineDecision.SeededCurrentScope;
    }

    /// <summary>
    /// Applies a scope that has already passed authenticated authority-bootstrap
    /// validation. A gate is permanently bound to one session/combatant; a new
    /// match constructs a new gate. Same-life control and life generations may
    /// only advance, so delayed bootstrap evidence cannot roll state backward.
    /// </summary>
    public PredictionLeadBootstrapDecision ApplyAuthorityBootstrapScope(
        OwnerIntentScope scope)
    {
        if (!scope.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(scope));
        }
        if (scope.SessionId != _scope.SessionId ||
            scope.Life.CombatantId != _scope.Life.CombatantId)
        {
            return PredictionLeadBootstrapDecision.RejectedDifferentOwner;
        }

        var lifeOrder = scope.Life.Life.Value.CompareTo(_scope.Life.Life.Value);
        if (lifeOrder < 0)
        {
            return PredictionLeadBootstrapDecision.RejectedStaleLife;
        }
        var controlOrder = scope.OwnerControl.Value.CompareTo(
            _scope.OwnerControl.Value);
        if (controlOrder < 0 || controlOrder == 0 && lifeOrder > 0)
        {
            return PredictionLeadBootstrapDecision.RejectedStaleControl;
        }
        if (controlOrder == 0)
        {
            return PredictionLeadBootstrapDecision.Duplicate;
        }

        _scope = scope;
        _latestAccepted = default;
        _hasAccepted = false;
        RequiresTimelineRebase = false;
        return lifeOrder > 0
            ? PredictionLeadBootstrapDecision.AppliedNewLife
            : PredictionLeadBootstrapDecision.AppliedNewControl;
    }
}
