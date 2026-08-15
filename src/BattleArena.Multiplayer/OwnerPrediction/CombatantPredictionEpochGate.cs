using BattleArena.Core.Common;
using BattleArena.Core.Movement.Simulation;

namespace BattleArena.Multiplayer.OwnerPrediction;

/// <summary>
/// Authority-owned scope carried by prediction evidence. Local repair identity is
/// structurally absent so a client can never present a rebase as authority state.
/// </summary>
public readonly record struct CombatantAuthorityPredictionEpoch
{
    public CombatantAuthorityPredictionEpoch(
        ulong sessionId,
        MatchFrameEpochId matchFrameEpoch,
        CombatantId combatantId,
        LifeGenerationId life,
        AuthorityDiscontinuityId authorityDiscontinuity,
        OwnerControlEpoch ownerControl)
    {
        if (sessionId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionId));
        }

        RequireValid(matchFrameEpoch.IsValid, nameof(matchFrameEpoch));
        RequireValid(combatantId.Value > 0, nameof(combatantId));
        RequireValid(life.Value > 0, nameof(life));
        RequireValid(authorityDiscontinuity.IsValid, nameof(authorityDiscontinuity));
        RequireValid(ownerControl.IsValid, nameof(ownerControl));

        SessionId = sessionId;
        MatchFrameEpoch = matchFrameEpoch;
        CombatantId = combatantId;
        Life = life;
        AuthorityDiscontinuity = authorityDiscontinuity;
        OwnerControl = ownerControl;
    }

    public ulong SessionId { get; }
    public MatchFrameEpochId MatchFrameEpoch { get; }
    public CombatantId CombatantId { get; }
    public LifeGenerationId Life { get; }
    public AuthorityDiscontinuityId AuthorityDiscontinuity { get; }
    public OwnerControlEpoch OwnerControl { get; }
    public bool IsValid =>
        SessionId != 0 &&
        MatchFrameEpoch.IsValid &&
        CombatantId.Value > 0 &&
        Life.Value > 0 &&
        AuthorityDiscontinuity.IsValid &&
        OwnerControl.IsValid;

    private static void RequireValid(bool valid, string parameterName)
    {
        if (!valid)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Every authority prediction epoch identity must be initialized.");
        }
    }
}

/// <summary>
/// Complete client-local epoch state. Only this aggregate contains the local
/// rebase identity; inbound authority evidence uses
/// <see cref="CombatantAuthorityPredictionEpoch"/>.
/// </summary>
public readonly record struct CombatantLocalPredictionEpoch
{
    public CombatantLocalPredictionEpoch(
        CombatantAuthorityPredictionEpoch authority,
        LocalPredictionRebaseId localRebase)
    {
        if (!authority.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(authority));
        }

        if (!localRebase.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(localRebase));
        }

        Authority = authority;
        LocalRebase = localRebase;
    }

    public CombatantAuthorityPredictionEpoch Authority { get; }
    public LocalPredictionRebaseId LocalRebase { get; }
    public bool IsValid => Authority.IsValid && LocalRebase.IsValid;
}

public enum PredictionEpochEvidenceDecision
{
    Unspecified = 0,
    Accepted = 1,
    SessionMismatch = 2,
    MatchFrameEpochMismatch = 3,
    CombatantMismatch = 4,
    LifeMismatch = 5,
    AuthorityDiscontinuityMismatch = 6,
    OwnerControlMismatch = 7,
    UnregisteredCombatant = 8,
}

public enum ClientPredictionBaselineDecision
{
    Unspecified = 0,
    Registered = 1,
    Current = 2,
    Transitioned = 3,
    RejectedDifferentScope = 4,
    RejectedRegression = 5,
    RejectedInvalidLifecycle = 6,
    RejectedExpectedCurrent = 7,
}

public readonly record struct ClientPredictionEpochTransition(
    CombatantLocalPredictionEpoch Previous,
    CombatantLocalPredictionEpoch Current);

public readonly record struct ClientPredictionBaselineResult(
    ClientPredictionBaselineDecision Decision,
    CombatantLocalPredictionEpoch Current)
{
    public bool IsAccepted => Decision is
        ClientPredictionBaselineDecision.Registered or
        ClientPredictionBaselineDecision.Current or
        ClientPredictionBaselineDecision.Transitioned;

    public bool RequiresReset =>
        Decision == ClientPredictionBaselineDecision.Transitioned;
}

public enum PredictionEpochTransitionDecision
{
    Unspecified = 0,
    Applied = 1,
    Duplicate = 2,
    RejectedDifferentScope = 3,
    RejectedRegression = 4,
    RejectedInvalidLifecycle = 5,
}

/// <summary>
/// Owns the current prediction epoch for exactly one combatant in one match.
/// Delivery/packet sequencing remains a separate concern; "duplicate" here means
/// a repeated lifecycle transition to the already-current epoch.
/// </summary>
public sealed class CombatantPredictionEpochGate
{
    public CombatantPredictionEpochGate(CombatantLocalPredictionEpoch initial)
    {
        if (!initial.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(initial));
        }

        Current = initial;
    }

    public CombatantLocalPredictionEpoch Current { get; private set; }

    public PredictionEpochEvidenceDecision Evaluate(
        CombatantAuthorityPredictionEpoch evidence)
    {
        if (!evidence.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(evidence));
        }

        var current = Current.Authority;
        if (evidence.SessionId != current.SessionId)
        {
            return PredictionEpochEvidenceDecision.SessionMismatch;
        }

        if (evidence.MatchFrameEpoch != current.MatchFrameEpoch)
        {
            return PredictionEpochEvidenceDecision.MatchFrameEpochMismatch;
        }

        if (evidence.CombatantId != current.CombatantId)
        {
            return PredictionEpochEvidenceDecision.CombatantMismatch;
        }

        if (evidence.Life != current.Life)
        {
            return PredictionEpochEvidenceDecision.LifeMismatch;
        }

        if (evidence.AuthorityDiscontinuity != current.AuthorityDiscontinuity)
        {
            return PredictionEpochEvidenceDecision.AuthorityDiscontinuityMismatch;
        }

        return evidence.OwnerControl != current.OwnerControl
            ? PredictionEpochEvidenceDecision.OwnerControlMismatch
            : PredictionEpochEvidenceDecision.Accepted;
    }

    /// <summary>
    /// Applies a reliable authority lifecycle baseline. Applied transitions also
    /// advance the client-only rebase identity, giving downstream stores one
    /// aggregate generation to reset against.
    /// </summary>
    public PredictionEpochTransitionDecision ApplyAuthorityEpoch(
        CombatantAuthorityPredictionEpoch next)
    {
        var decision = EvaluateAuthorityEpochTransition(next);
        if (decision != PredictionEpochTransitionDecision.Applied)
        {
            return decision;
        }

        Current = new CombatantLocalPredictionEpoch(
            next,
            Current.LocalRebase.Next());
        return PredictionEpochTransitionDecision.Applied;
    }

    /// <summary>Evaluates a lifecycle transition without mutating this gate.</summary>
    public PredictionEpochTransitionDecision EvaluateAuthorityEpochTransition(
        CombatantAuthorityPredictionEpoch next)
    {
        if (!next.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(next));
        }

        var current = Current.Authority;
        if (next.SessionId != current.SessionId || next.CombatantId != current.CombatantId)
        {
            return PredictionEpochTransitionDecision.RejectedDifferentScope;
        }

        if (next == current)
        {
            return PredictionEpochTransitionDecision.Duplicate;
        }

        if (next.MatchFrameEpoch < current.MatchFrameEpoch ||
            next.Life.CompareTo(current.Life) < 0 ||
            next.AuthorityDiscontinuity < current.AuthorityDiscontinuity ||
            next.OwnerControl < current.OwnerControl)
        {
            return PredictionEpochTransitionDecision.RejectedRegression;
        }

        var matchFrameChanged = next.MatchFrameEpoch != current.MatchFrameEpoch;
        var lifeChanged = next.Life != current.Life;
        var discontinuityChanged =
            next.AuthorityDiscontinuity != current.AuthorityDiscontinuity;
        var controlChanged = next.OwnerControl != current.OwnerControl;

        // Timeline resets establish a new control epoch. Respawns establish a
        // new life, discontinuity, and control epoch together. Reconnect may
        // advance control alone; teleport may advance discontinuity alone.
        if ((matchFrameChanged && !controlChanged) ||
            (lifeChanged && (!discontinuityChanged || !controlChanged)))
        {
            return PredictionEpochTransitionDecision.RejectedInvalidLifecycle;
        }

        return PredictionEpochTransitionDecision.Applied;
    }

    /// <summary>
    /// Repairs local history/presentation without changing any authority-owned
    /// identity and without affecting which authority evidence is accepted.
    /// </summary>
    public LocalPredictionRebaseId RebaseLocal()
    {
        var next = Current.LocalRebase.Next();
        Current = new CombatantLocalPredictionEpoch(Current.Authority, next);
        return next;
    }
}

/// <summary>
/// Client-side directory that owns one epoch gate per combatant for a single
/// authenticated match session. Reliable baselines may populate or transition
/// a gate; ordinary evidence can only be evaluated against an existing gate.
/// </summary>
public sealed class ClientPredictionEpochDirectory
{
    private readonly ulong _sessionId;
    private readonly Dictionary<CombatantId, CombatantPredictionEpochGate> _gates = [];

    public ClientPredictionEpochDirectory(ulong sessionId)
    {
        if (sessionId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionId));
        }

        _sessionId = sessionId;
    }

    public event Action<ClientPredictionEpochTransition>? EpochTransitioned;

    public int Count => _gates.Count;

    public ClientPredictionBaselineResult ObserveBaseline(
        CombatantAuthorityPredictionEpoch baseline)
        => ObserveBaselineCore(
            baseline,
            expectedCurrent: null,
            enforceExpectedCurrent: false,
            beforeCommit: null);

    /// <summary>
    /// Commits only when the directory still has the caller's exact expected
    /// epoch. The participant runs after all validation but before mutation and
    /// transition publication, allowing one synchronous aggregate commit.
    /// </summary>
    public ClientPredictionBaselineResult CompareAndObserveBaseline(
        CombatantAuthorityPredictionEpoch baseline,
        CombatantLocalPredictionEpoch? expectedCurrent,
        Action beforeCommit)
    {
        ArgumentNullException.ThrowIfNull(beforeCommit);
        return ObserveBaselineCore(
            baseline,
            expectedCurrent,
            enforceExpectedCurrent: true,
            beforeCommit);
    }

    private ClientPredictionBaselineResult ObserveBaselineCore(
        CombatantAuthorityPredictionEpoch baseline,
        CombatantLocalPredictionEpoch? expectedCurrent,
        bool enforceExpectedCurrent,
        Action? beforeCommit)
    {
        if (!baseline.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(baseline));
        }

        if (baseline.SessionId != _sessionId)
        {
            return new ClientPredictionBaselineResult(
                ClientPredictionBaselineDecision.RejectedDifferentScope,
                default);
        }

        if (!_gates.TryGetValue(baseline.CombatantId, out var gate))
        {
            if (enforceExpectedCurrent && expectedCurrent is not null)
            {
                return new ClientPredictionBaselineResult(
                    ClientPredictionBaselineDecision.RejectedExpectedCurrent,
                    default);
            }
            var registered = new CombatantLocalPredictionEpoch(
                baseline,
                LocalPredictionRebaseId.Initial);
            beforeCommit?.Invoke();
            _gates.Add(baseline.CombatantId, new CombatantPredictionEpochGate(registered));
            return new ClientPredictionBaselineResult(
                ClientPredictionBaselineDecision.Registered,
                registered);
        }

        var previous = gate.Current;
        if (enforceExpectedCurrent &&
            (expectedCurrent is null || expectedCurrent.Value != previous))
        {
            return new ClientPredictionBaselineResult(
                ClientPredictionBaselineDecision.RejectedExpectedCurrent,
                previous);
        }
        var decision = gate.EvaluateAuthorityEpochTransition(baseline);
        var mapped = decision switch
        {
            PredictionEpochTransitionDecision.Applied =>
                ClientPredictionBaselineDecision.Transitioned,
            PredictionEpochTransitionDecision.Duplicate =>
                ClientPredictionBaselineDecision.Current,
            PredictionEpochTransitionDecision.RejectedDifferentScope =>
                ClientPredictionBaselineDecision.RejectedDifferentScope,
            PredictionEpochTransitionDecision.RejectedRegression =>
                ClientPredictionBaselineDecision.RejectedRegression,
            PredictionEpochTransitionDecision.RejectedInvalidLifecycle =>
                ClientPredictionBaselineDecision.RejectedInvalidLifecycle,
            _ => throw new InvalidOperationException(
                $"Unhandled epoch transition decision {decision}."),
        };

        if (mapped is ClientPredictionBaselineDecision.Current or
            ClientPredictionBaselineDecision.Transitioned)
        {
            beforeCommit?.Invoke();
        }
        if (mapped == ClientPredictionBaselineDecision.Transitioned)
        {
            var applied = gate.ApplyAuthorityEpoch(baseline);
            if (applied != PredictionEpochTransitionDecision.Applied)
            {
                throw new InvalidOperationException(
                    "A synchronous lifecycle preview diverged during commit.");
            }
        }

        if (mapped == ClientPredictionBaselineDecision.Transitioned)
        {
            EpochTransitioned?.Invoke(new ClientPredictionEpochTransition(
                previous,
                gate.Current));
        }

        return new ClientPredictionBaselineResult(mapped, gate.Current);
    }

    public PredictionEpochEvidenceDecision EvaluateEvidence(
        CombatantAuthorityPredictionEpoch evidence)
    {
        if (!evidence.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(evidence));
        }

        if (evidence.SessionId != _sessionId)
        {
            return PredictionEpochEvidenceDecision.SessionMismatch;
        }

        return _gates.TryGetValue(evidence.CombatantId, out var gate)
            ? gate.Evaluate(evidence)
            : PredictionEpochEvidenceDecision.UnregisteredCombatant;
    }

    public bool TryGetCurrent(
        CombatantId combatantId,
        out CombatantLocalPredictionEpoch current)
    {
        if (_gates.TryGetValue(combatantId, out var gate))
        {
            current = gate.Current;
            return true;
        }

        current = default;
        return false;
    }
}

public enum ClientPredictionStateBaselineSource
{
    Unspecified = 0,
    Spawn = 1,
    Snapshot = 2,
    AuthorityMovement = 3,
}

public enum ClientPredictionEvidenceRoute
{
    Unspecified = 0,
    AcceptedMovement = 1,
    DirectMovementHint = 2,
    Action = 3,
    DamageSource = 4,
    DamageTarget = 5,
}

public enum ClientLifeNotificationDecision
{
    Unspecified = 0,
    Current = 1,
    PendingStateBaseline = 2,
    RejectedStale = 3,
    RejectedDifferentScope = 4,
}

/// <summary>
/// Production client routing seam for lifecycle-sensitive evidence. Only a
/// state-bearing source may register/transition an epoch. Stateless life events
/// can announce a future epoch, but remain pending until that state arrives.
/// </summary>
public sealed class ClientPredictionLifecycleRouter
{
    private readonly ClientPredictionEpochDirectory _directory;
    private readonly Dictionary<CombatantId, CombatantAuthorityPredictionEpoch>
        _pendingLifeNotifications = [];

    public ClientPredictionLifecycleRouter(
        ulong sessionId,
        Action<ClientPredictionEpochTransition> resetSink)
    {
        ArgumentNullException.ThrowIfNull(resetSink);
        _directory = new ClientPredictionEpochDirectory(sessionId);
        _directory.EpochTransitioned += resetSink;
    }

    public int RegisteredCombatantCount => _directory.Count;
    public int PendingLifeNotificationCount => _pendingLifeNotifications.Count;

    public ClientPredictionBaselineResult ObserveStateBaseline(
        CombatantAuthorityPredictionEpoch baseline,
        ClientPredictionStateBaselineSource source)
    {
        if (source is not (
            ClientPredictionStateBaselineSource.Spawn or
            ClientPredictionStateBaselineSource.Snapshot or
            ClientPredictionStateBaselineSource.AuthorityMovement))
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }

        var result = _directory.ObserveBaseline(baseline);
        if (result.IsAccepted &&
            _pendingLifeNotifications.TryGetValue(baseline.CombatantId, out var pending) &&
            IsComponentwiseAtLeast(baseline, pending))
        {
            _pendingLifeNotifications.Remove(baseline.CombatantId);
        }

        return result;
    }

    public ClientPredictionBaselineResult CompareAndObserveStateBaseline(
        CombatantAuthorityPredictionEpoch baseline,
        ClientPredictionStateBaselineSource source,
        CombatantLocalPredictionEpoch? expectedCurrent,
        Action beforeCommit)
    {
        if (source is not (
            ClientPredictionStateBaselineSource.Spawn or
            ClientPredictionStateBaselineSource.Snapshot or
            ClientPredictionStateBaselineSource.AuthorityMovement))
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }
        ArgumentNullException.ThrowIfNull(beforeCommit);

        var result = _directory.CompareAndObserveBaseline(
            baseline,
            expectedCurrent,
            beforeCommit);
        if (result.IsAccepted &&
            _pendingLifeNotifications.TryGetValue(baseline.CombatantId, out var pending) &&
            IsComponentwiseAtLeast(baseline, pending))
        {
            _pendingLifeNotifications.Remove(baseline.CombatantId);
        }
        return result;
    }

    public PredictionEpochEvidenceDecision EvaluateEvidence(
        CombatantAuthorityPredictionEpoch evidence,
        ClientPredictionEvidenceRoute route)
    {
        if (route is not (
            ClientPredictionEvidenceRoute.AcceptedMovement or
            ClientPredictionEvidenceRoute.DirectMovementHint or
            ClientPredictionEvidenceRoute.Action or
            ClientPredictionEvidenceRoute.DamageSource or
            ClientPredictionEvidenceRoute.DamageTarget))
        {
            throw new ArgumentOutOfRangeException(nameof(route));
        }

        return _directory.EvaluateEvidence(evidence);
    }

    public ClientLifeNotificationDecision ObserveLifeNotification(
        CombatantAuthorityPredictionEpoch notification)
    {
        var decision = _directory.EvaluateEvidence(notification);
        if (decision == PredictionEpochEvidenceDecision.Accepted)
        {
            return ClientLifeNotificationDecision.Current;
        }

        if (decision == PredictionEpochEvidenceDecision.SessionMismatch)
        {
            return ClientLifeNotificationDecision.RejectedDifferentScope;
        }

        if (!_directory.TryGetCurrent(notification.CombatantId, out var current))
        {
            _pendingLifeNotifications[notification.CombatantId] = notification;
            return ClientLifeNotificationDecision.PendingStateBaseline;
        }

        if (!IsComponentwiseAtLeast(notification, current.Authority))
        {
            return ClientLifeNotificationDecision.RejectedStale;
        }

        _pendingLifeNotifications[notification.CombatantId] = notification;
        return ClientLifeNotificationDecision.PendingStateBaseline;
    }

    public bool TryGetCurrent(
        CombatantId combatantId,
        out CombatantLocalPredictionEpoch current) =>
        _directory.TryGetCurrent(combatantId, out current);

    public bool TryGetPendingLifeNotification(
        CombatantId combatantId,
        out CombatantAuthorityPredictionEpoch pending) =>
        _pendingLifeNotifications.TryGetValue(combatantId, out pending);

    private static bool IsComponentwiseAtLeast(
        CombatantAuthorityPredictionEpoch candidate,
        CombatantAuthorityPredictionEpoch baseline) =>
        candidate.SessionId == baseline.SessionId &&
        candidate.CombatantId == baseline.CombatantId &&
        candidate.MatchFrameEpoch.Value >= baseline.MatchFrameEpoch.Value &&
        candidate.Life.Value >= baseline.Life.Value &&
        candidate.AuthorityDiscontinuity.Value >= baseline.AuthorityDiscontinuity.Value &&
        candidate.OwnerControl.Value >= baseline.OwnerControl.Value;
}
