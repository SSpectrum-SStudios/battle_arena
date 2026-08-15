using BattleArena.Core.Common;
using BattleArena.Core.Movement.Simulation;

namespace BattleArena.Multiplayer.OwnerPrediction;

public static class MovementTransitionJournalLimits
{
    public const int DefaultCapacity = 64;
    public const int MaximumCapacity = 256;
    public const int ResolutionReorderWindow = 64;
    public const long MaximumValidityTicks = 3_600;
    public const long MaximumTombstoneRetentionTicks = 36_000;
}

public readonly record struct MovementTransitionJournalPolicy
{
    public static MovementTransitionJournalPolicy Default { get; } = new(
        MovementTransitionJournalLimits.DefaultCapacity,
        new SimulationDuration(120),
        new SimulationDuration(600));

    public MovementTransitionJournalPolicy(
        int capacity,
        SimulationDuration maximumValidity,
        SimulationDuration tombstoneRetention)
    {
        if (capacity <= 0 || capacity > MovementTransitionJournalLimits.MaximumCapacity)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }
        if (maximumValidity.Ticks <= 0 ||
            maximumValidity.Ticks > MovementTransitionJournalLimits.MaximumValidityTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumValidity));
        }
        if (tombstoneRetention.Ticks >
            MovementTransitionJournalLimits.MaximumTombstoneRetentionTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(tombstoneRetention));
        }

        Capacity = capacity;
        MaximumValidity = maximumValidity;
        TombstoneRetention = tombstoneRetention;
    }

    public int Capacity { get; }
    public SimulationDuration MaximumValidity { get; }
    public SimulationDuration TombstoneRetention { get; }
    public bool IsValid =>
        Capacity > 0 &&
        Capacity <= MovementTransitionJournalLimits.MaximumCapacity &&
        MaximumValidity.Ticks > 0 &&
        MaximumValidity.Ticks <= MovementTransitionJournalLimits.MaximumValidityTicks &&
        TombstoneRetention.Ticks <=
            MovementTransitionJournalLimits.MaximumTombstoneRetentionTicks;

    public MovementTransitionJournalPolicy WithCapacity(int capacity) => new(
        capacity,
        MaximumValidity,
        TombstoneRetention);
}

public enum MovementTransitionKind : byte
{
    JumpPressed = 1,
    JumpReleased = 2,
    CrouchOrRollPressed = 3,
    CrouchOrRollReleased = 4,
    LedgeGrab = 5,
    LedgeClimb = 6,
    LedgeDrop = 7,
}

public enum MovementTransitionOutcome : byte
{
    Accepted = 1,
    Remapped = 2,
    Rejected = 3,
    Expired = 4,
    Superseded = 5,
}

public enum MovementTransitionRejectionReason : byte
{
    None = 0,
    AuthorityPolicyRejected = 1,
    InvalidState = 2,
    CapabilityUnavailable = 3,
    DeadlineExpired = 4,
    Superseded = 5,
}

/// <summary>
/// One immutable, durable movement transition. New transition kinds that need
/// authored parameters must introduce a typed payload/version rather than an
/// opaque property bag.
/// </summary>
public readonly record struct MovementTransitionIntent
{
    public MovementTransitionIntent(
        MovementTransitionIdentity identity,
        OwnerInputIdentity originatingInput,
        MovementTransitionKind kind,
        SimulationInstant firstPredictedFrame,
        SimulationInstant lastValidFrame)
    {
        if (!identity.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(identity));
        }
        if (!originatingInput.IsValid || originatingInput.Scope != identity.Scope)
        {
            throw new ArgumentOutOfRangeException(nameof(originatingInput));
        }
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        if (lastValidFrame < firstPredictedFrame)
        {
            throw new ArgumentOutOfRangeException(nameof(lastValidFrame));
        }

        Identity = identity;
        OriginatingInput = originatingInput;
        Kind = kind;
        FirstPredictedFrame = firstPredictedFrame;
        LastValidFrame = lastValidFrame;
    }

    public MovementTransitionIdentity Identity { get; }
    public OwnerInputIdentity OriginatingInput { get; }
    public MovementTransitionKind Kind { get; }
    public SimulationInstant FirstPredictedFrame { get; }
    public SimulationInstant LastValidFrame { get; }
    public bool IsValid =>
        Identity.IsValid &&
        OriginatingInput.IsValid &&
        OriginatingInput.Scope == Identity.Scope &&
        Enum.IsDefined(Kind) &&
        LastValidFrame >= FirstPredictedFrame;
}

/// <summary>Authority terminal result retained until cursor acknowledgement.</summary>
public readonly record struct MovementTransitionResolution
{
    public MovementTransitionResolution(
        TransitionResolutionIdentity resolutionIdentity,
        MovementTransitionIdentity transitionIdentity,
        MovementTransitionOutcome outcome,
        bool hasApplicationFrame,
        SimulationInstant applicationFrame,
        SimulationInstant decisionFrame,
        MovementTransitionRejectionReason rejectionReason)
    {
        if (!resolutionIdentity.IsValid || !transitionIdentity.IsValid ||
            resolutionIdentity.Scope != transitionIdentity.Scope)
        {
            throw new ArgumentOutOfRangeException(nameof(resolutionIdentity));
        }
        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }
        if (!Enum.IsDefined(rejectionReason))
        {
            throw new ArgumentOutOfRangeException(nameof(rejectionReason));
        }

        var applied = outcome is MovementTransitionOutcome.Accepted or
            MovementTransitionOutcome.Remapped;
        if (hasApplicationFrame != applied)
        {
            throw new ArgumentException(
                "Only accepted or remapped transitions have an application frame.",
                nameof(hasApplicationFrame));
        }
        if (!applied && applicationFrame != default)
        {
            throw new ArgumentException(
                "A non-applied transition cannot carry an application frame.",
                nameof(applicationFrame));
        }
        if (applied && applicationFrame != decisionFrame)
        {
            throw new ArgumentException(
                "An applied transition is decided on its actual application frame.",
                nameof(decisionFrame));
        }

        var expectedReason = outcome switch
        {
            MovementTransitionOutcome.Accepted or MovementTransitionOutcome.Remapped =>
                MovementTransitionRejectionReason.None,
            MovementTransitionOutcome.Expired =>
                MovementTransitionRejectionReason.DeadlineExpired,
            MovementTransitionOutcome.Superseded =>
                MovementTransitionRejectionReason.Superseded,
            MovementTransitionOutcome.Rejected => rejectionReason,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
        };
        if (rejectionReason != expectedReason ||
            (outcome == MovementTransitionOutcome.Rejected &&
             rejectionReason is MovementTransitionRejectionReason.None or
                 MovementTransitionRejectionReason.DeadlineExpired or
                 MovementTransitionRejectionReason.Superseded))
        {
            throw new ArgumentOutOfRangeException(nameof(rejectionReason));
        }

        ResolutionIdentity = resolutionIdentity;
        TransitionIdentity = transitionIdentity;
        Outcome = outcome;
        HasApplicationFrame = hasApplicationFrame;
        ApplicationFrame = applicationFrame;
        DecisionFrame = decisionFrame;
        RejectionReason = rejectionReason;
    }

    public TransitionResolutionIdentity ResolutionIdentity { get; }
    public MovementTransitionIdentity TransitionIdentity { get; }
    public MovementTransitionOutcome Outcome { get; }
    public bool HasApplicationFrame { get; }
    public SimulationInstant ApplicationFrame { get; }
    public SimulationInstant DecisionFrame { get; }
    public MovementTransitionRejectionReason RejectionReason { get; }
    public bool IsValid =>
        ResolutionIdentity.IsValid &&
        TransitionIdentity.IsValid &&
        ResolutionIdentity.Scope == TransitionIdentity.Scope &&
        Enum.IsDefined(Outcome) &&
        Enum.IsDefined(RejectionReason) &&
        HasApplicationFrame ==
            (Outcome is MovementTransitionOutcome.Accepted or
                MovementTransitionOutcome.Remapped) &&
        (HasApplicationFrame
            ? ApplicationFrame == DecisionFrame &&
              RejectionReason == MovementTransitionRejectionReason.None
            : ApplicationFrame == default &&
              Outcome switch
              {
                  MovementTransitionOutcome.Rejected =>
                      RejectionReason is
                          MovementTransitionRejectionReason.AuthorityPolicyRejected or
                          MovementTransitionRejectionReason.InvalidState or
                          MovementTransitionRejectionReason.CapabilityUnavailable,
                  MovementTransitionOutcome.Expired =>
                      RejectionReason == MovementTransitionRejectionReason.DeadlineExpired,
                  MovementTransitionOutcome.Superseded =>
                      RejectionReason == MovementTransitionRejectionReason.Superseded,
                  _ => false,
              });
}

public enum MovementTransitionOriginDecision : byte
{
    Added = 1,
    CapacityExceeded = 2,
    IdentityExhausted = 3,
    BaselineRepairRequired = 4,
}

public enum ClientTransitionResolutionDecision : byte
{
    Applied = 1,
    Duplicate = 2,
    WrongScope = 3,
    TooFarAhead = 4,
    UnknownTransition = 5,
    InvalidResolution = 6,
    ConflictingResolution = 7,
    BaselineRepairRequired = 8,
}

/// <summary>
/// Client-side owner journal. Sending is observational: entries remain
/// outstanding through arbitrary loss until a valid terminal authority result
/// is applied.
/// </summary>
public sealed class MovementTransitionJournal
{
    private readonly MovementTransitionIntent[] _outstanding;
    private readonly MovementTransitionResolution[] _receivedResolutionSlots =
        new MovementTransitionResolution[MovementTransitionJournalLimits.ResolutionReorderWindow];
    private readonly MovementTransitionJournalPolicy _policy;
    private OwnerIntentScope _scope;
    private int _count;
    private ulong _nextTransitionId = 1;
    private bool _identityExhausted;
    private ulong _highestContiguousResolution;
    private int _nextTransmissionIndex;

    public MovementTransitionJournal(
        OwnerIntentScope scope)
        : this(scope, MovementTransitionJournalPolicy.Default)
    {
    }

    public MovementTransitionJournal(OwnerIntentScope scope, int capacity)
        : this(scope, MovementTransitionJournalPolicy.Default.WithCapacity(capacity))
    {
    }

    public MovementTransitionJournal(
        OwnerIntentScope scope,
        MovementTransitionJournalPolicy policy)
    {
        ValidateScopeAndPolicy(scope, policy);
        _scope = scope;
        _policy = policy;
        _outstanding = new MovementTransitionIntent[policy.Capacity];
    }

    public OwnerIntentScope Scope => _scope;
    public int Capacity => _outstanding.Length;
    public MovementTransitionJournalPolicy Policy => _policy;
    public int OutstandingCount => _count;
    public bool RequiresBaselineRepair { get; private set; }
    public TransitionResolutionIdentity? AppliedResolutionCursor =>
        _highestContiguousResolution == 0
            ? null
            : new TransitionResolutionIdentity(
                _scope,
                new TransitionResolutionSequence(_highestContiguousResolution));

    /// <summary>
    /// Restores an empty journal from an authenticated baseline. Active intents,
    /// when protocol-next supports them, must use a separate validated restore
    /// path; this method cannot silently discard them.
    /// </summary>
    public static MovementTransitionJournal RestoreEmptyBaseline(
        OwnerIntentScope scope,
        MovementTransitionJournalPolicy policy,
        MovementTransitionId nextTransitionId,
        TransitionResolutionIdentity? appliedResolutionCursor = null)
    {
        if (!nextTransitionId.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(nextTransitionId));
        }
        if (appliedResolutionCursor is { } cursor &&
            (!cursor.IsValid || cursor.Scope != scope))
        {
            throw new ArgumentOutOfRangeException(nameof(appliedResolutionCursor));
        }

        var journal = new MovementTransitionJournal(scope, policy)
        {
            _nextTransitionId = nextTransitionId.Value,
            _highestContiguousResolution =
                appliedResolutionCursor?.Sequence.Value ?? 0,
        };
        return journal;
    }

    public MovementTransitionOriginDecision TryOriginate(
        OwnerInputIdentity originatingInput,
        MovementTransitionKind kind,
        SimulationInstant firstPredictedFrame,
        SimulationInstant lastValidFrame,
        out MovementTransitionIntent intent)
    {
        if (RequiresBaselineRepair)
        {
            intent = default;
            return MovementTransitionOriginDecision.BaselineRepairRequired;
        }
        if (!originatingInput.IsValid || originatingInput.Scope != _scope)
        {
            throw new ArgumentOutOfRangeException(nameof(originatingInput));
        }
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        if (lastValidFrame < firstPredictedFrame)
        {
            throw new ArgumentOutOfRangeException(nameof(lastValidFrame));
        }
        if ((lastValidFrame - firstPredictedFrame) > _policy.MaximumValidity)
        {
            throw new ArgumentOutOfRangeException(nameof(lastValidFrame));
        }
        if (_identityExhausted)
        {
            intent = default;
            return MovementTransitionOriginDecision.IdentityExhausted;
        }
        if (_count == Capacity)
        {
            intent = default;
            return MovementTransitionOriginDecision.CapacityExceeded;
        }

        var id = new MovementTransitionId(_nextTransitionId);
        intent = new MovementTransitionIntent(
            new MovementTransitionIdentity(_scope, id),
            originatingInput,
            kind,
            firstPredictedFrame,
            lastValidFrame);
        _outstanding[_count++] = intent;
        if (_nextTransitionId == ulong.MaxValue)
        {
            _identityExhausted = true;
        }
        else
        {
            _nextTransitionId++;
        }

        return MovementTransitionOriginDecision.Added;
    }

    public int CopyOutstanding(Span<MovementTransitionIntent> destination)
    {
        var copied = PeekOutstanding(destination);
        CommitOutstandingTransmissions(copied);
        return copied;
    }

    /// <summary>
    /// Reads the current fair resend prefix without advancing it. The send-window
    /// composer commits only the prefix that was actually encoded.
    /// </summary>
    internal int PeekOutstanding(Span<MovementTransitionIntent> destination)
    {
        if (RequiresBaselineRepair)
        {
            return 0;
        }
        var copied = Math.Min(destination.Length, _count);
        for (var index = 0; index < copied; index++)
        {
            destination[index] = _outstanding[(_nextTransmissionIndex + index) % _count];
        }
        return copied;
    }

    internal void CommitOutstandingTransmissions(int transmittedCount)
    {
        if (transmittedCount < 0 || transmittedCount > _count ||
            RequiresBaselineRepair && transmittedCount != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(transmittedCount));
        }
        if (transmittedCount > 0)
        {
            _nextTransmissionIndex =
                (_nextTransmissionIndex + transmittedCount) % _count;
        }
    }

    public ClientTransitionResolutionDecision ApplyResolution(
        MovementTransitionResolution resolution,
        out MovementTransitionIntent resolvedIntent)
    {
        resolvedIntent = default;
        if (RequiresBaselineRepair)
        {
            return ClientTransitionResolutionDecision.BaselineRepairRequired;
        }
        if (!resolution.IsValid || resolution.ResolutionIdentity.Scope != _scope)
        {
            return resolution.ResolutionIdentity.IsValid &&
                resolution.ResolutionIdentity.Scope == _scope
                ? RequireBaselineRepair(ClientTransitionResolutionDecision.InvalidResolution)
                : ClientTransitionResolutionDecision.WrongScope;
        }

        var receiveDecision = InspectResolutionSequence(resolution);
        if (receiveDecision != ClientTransitionResolutionDecision.Applied)
        {
            return receiveDecision;
        }

        var index = FindOutstanding(resolution.TransitionIdentity);
        if (index < 0)
        {
            return RequireBaselineRepair(
                ClientTransitionResolutionDecision.UnknownTransition);
        }

        resolvedIntent = _outstanding[index];
        if (!ResolutionIsPossibleForIntent(resolution, resolvedIntent))
        {
            resolvedIntent = default;
            return RequireBaselineRepair(
                ClientTransitionResolutionDecision.InvalidResolution);
        }
        RemoveOutstandingAt(index);
        RecordResolutionSequence(resolution);
        return ClientTransitionResolutionDecision.Applied;
    }

    /// <summary>
    /// Changes journal lifetime only when session/life/control scope changes.
    /// Authority discontinuities deliberately do not participate in this reset.
    /// </summary>
    public bool Reset(OwnerIntentScope scope)
    {
        if (!scope.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(scope));
        }
        if (scope == _scope)
        {
            return false;
        }

        Array.Clear(_outstanding);
        Array.Clear(_receivedResolutionSlots);
        _scope = scope;
        _count = 0;
        _nextTransitionId = 1;
        _identityExhausted = false;
        _highestContiguousResolution = 0;
        _nextTransmissionIndex = 0;
        RequiresBaselineRepair = false;
        return true;
    }

    private ClientTransitionResolutionDecision InspectResolutionSequence(
        MovementTransitionResolution resolution)
    {
        var sequence = resolution.ResolutionIdentity.Sequence.Value;
        var window = MovementTransitionJournalLimits.ResolutionReorderWindow;
        var slot = (int)(sequence % (ulong)window);
        if (sequence <= _highestContiguousResolution)
        {
            var retained = _receivedResolutionSlots[slot];
            if (retained.IsValid &&
                retained.ResolutionIdentity.Sequence.Value == sequence)
            {
                return retained == resolution
                    ? ClientTransitionResolutionDecision.Duplicate
                    : RequireBaselineRepair(
                        ClientTransitionResolutionDecision.ConflictingResolution);
            }
            if (FindOutstanding(resolution.TransitionIdentity) >= 0)
            {
                return RequireBaselineRepair(
                    ClientTransitionResolutionDecision.ConflictingResolution);
            }
            return ClientTransitionResolutionDecision.Duplicate;
        }

        var distance = sequence - _highestContiguousResolution;
        if (distance > MovementTransitionJournalLimits.ResolutionReorderWindow)
        {
            return RequireBaselineRepair(ClientTransitionResolutionDecision.TooFarAhead);
        }

        var existing = _receivedResolutionSlots[slot];
        if (!existing.IsValid ||
            existing.ResolutionIdentity.Sequence.Value != sequence)
        {
            return ClientTransitionResolutionDecision.Applied;
        }
        return existing == resolution
            ? ClientTransitionResolutionDecision.Duplicate
            : RequireBaselineRepair(
                ClientTransitionResolutionDecision.ConflictingResolution);
    }

    private void RecordResolutionSequence(MovementTransitionResolution resolution)
    {
        var sequence = resolution.ResolutionIdentity.Sequence.Value;
        var window = MovementTransitionJournalLimits.ResolutionReorderWindow;
        _receivedResolutionSlots[(int)(sequence % (ulong)window)] = resolution;
        while (_highestContiguousResolution != ulong.MaxValue)
        {
            var next = _highestContiguousResolution + 1;
            var slot = (int)(next % (ulong)window);
            if (!_receivedResolutionSlots[slot].IsValid ||
                _receivedResolutionSlots[slot].ResolutionIdentity.Sequence.Value != next)
            {
                break;
            }
            _highestContiguousResolution = next;
        }
    }

    private ClientTransitionResolutionDecision RequireBaselineRepair(
        ClientTransitionResolutionDecision decision)
    {
        RequiresBaselineRepair = true;
        return decision;
    }

    private static bool ResolutionIsPossibleForIntent(
        MovementTransitionResolution resolution,
        MovementTransitionIntent intent) => resolution.Outcome switch
    {
        MovementTransitionOutcome.Accepted =>
            resolution.ApplicationFrame == intent.FirstPredictedFrame &&
            resolution.DecisionFrame == intent.FirstPredictedFrame,
        MovementTransitionOutcome.Remapped =>
            resolution.ApplicationFrame > intent.FirstPredictedFrame &&
            resolution.ApplicationFrame <= intent.LastValidFrame &&
            resolution.DecisionFrame == resolution.ApplicationFrame,
        MovementTransitionOutcome.Rejected or MovementTransitionOutcome.Superseded =>
            resolution.DecisionFrame >= intent.FirstPredictedFrame &&
            resolution.DecisionFrame <= intent.LastValidFrame,
        MovementTransitionOutcome.Expired =>
            resolution.DecisionFrame > intent.LastValidFrame,
        _ => false,
    };

    private int FindOutstanding(MovementTransitionIdentity identity)
    {
        for (var index = 0; index < _count; index++)
        {
            if (_outstanding[index].Identity == identity)
            {
                return index;
            }
        }
        return -1;
    }

    private void RemoveOutstandingAt(int index)
    {
        if (index < _count - 1)
        {
            Array.Copy(_outstanding, index + 1, _outstanding, index, _count - index - 1);
        }
        _outstanding[--_count] = default;
        _nextTransmissionIndex = _count == 0 ? 0 : _nextTransmissionIndex % _count;
    }

    internal static void ValidateScopeAndPolicy(
        OwnerIntentScope scope,
        MovementTransitionJournalPolicy policy)
    {
        if (!scope.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(scope));
        }
        if (!policy.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(policy));
        }
    }
}

public enum AuthorityTransitionObserveDecision : byte
{
    FirstSeen = 1,
    Duplicate = 2,
    ConflictingDuplicate = 3,
    WrongScope = 4,
    TooFarAhead = 5,
    CapacityExceeded = 6,
    BaselineRepairRequired = 7,
    InvalidIntent = 8,
}

public enum AuthorityTransitionResolveDecision : byte
{
    Resolved = 1,
    AlreadyResolved = 2,
    ConflictingResolution = 3,
    UnknownTransition = 4,
    WrongScope = 5,
    BaselineRepairRequired = 6,
}

/// <summary>
/// Authority-side deduplication ledger. Transition IDs are observed once;
/// terminal results receive a separate sequence and remain as tombstones until
/// the client acknowledges a contiguous resolution cursor.
/// </summary>
public sealed class AuthorityMovementTransitionJournal
{
    private readonly AuthorityEntry[] _entries;
    private readonly MovementTransitionJournalPolicy _policy;
    private OwnerIntentScope _scope;
    private int _count;
    private ulong _highestContiguousObservedTransition;
    private ulong _followingObservedMask;
    private ulong _nextResolutionSequence = 1;
    private ulong _lastAcknowledgedResolution;

    public AuthorityMovementTransitionJournal(
        OwnerIntentScope scope)
        : this(scope, MovementTransitionJournalPolicy.Default)
    {
    }

    public AuthorityMovementTransitionJournal(OwnerIntentScope scope, int capacity)
        : this(scope, MovementTransitionJournalPolicy.Default.WithCapacity(capacity))
    {
    }

    public AuthorityMovementTransitionJournal(
        OwnerIntentScope scope,
        MovementTransitionJournalPolicy policy)
    {
        MovementTransitionJournal.ValidateScopeAndPolicy(scope, policy);
        _scope = scope;
        _policy = policy;
        _entries = new AuthorityEntry[policy.Capacity];
    }

    public OwnerIntentScope Scope => _scope;
    public int Capacity => _entries.Length;
    public MovementTransitionJournalPolicy Policy => _policy;
    public int EntryCount => _count;
    public int PendingCount => CountEntries(resolved: false);
    public int TombstoneCount => CountEntries(resolved: true);
    public bool RequiresBaselineRepair { get; private set; }
    public TransitionResolutionIdentity? LastAcknowledgedResolution =>
        _lastAcknowledgedResolution == 0
            ? null
            : new TransitionResolutionIdentity(
                _scope,
                new TransitionResolutionSequence(_lastAcknowledgedResolution));

    /// <summary>
    /// Restores empty authority journal counters from an authenticated baseline.
    /// It intentionally cannot represent active intents or unacknowledged
    /// tombstones; those require the later full bootstrap-state contract.
    /// </summary>
    public static AuthorityMovementTransitionJournal RestoreEmptyBaseline(
        OwnerIntentScope scope,
        MovementTransitionJournalPolicy policy,
        TransitionResolutionSequence nextResolutionSequence,
        TransitionResolutionIdentity? acknowledgedThrough,
        MovementTransitionId? highestContiguousObservedTransition = null)
    {
        if (!nextResolutionSequence.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(nextResolutionSequence));
        }
        if (highestContiguousObservedTransition is { } observed && !observed.IsValid)
        {
            throw new ArgumentOutOfRangeException(
                nameof(highestContiguousObservedTransition));
        }
        var expectedAcknowledgedSequence = nextResolutionSequence.Value - 1;
        if ((expectedAcknowledgedSequence == 0 &&
             acknowledgedThrough is not null) ||
            (expectedAcknowledgedSequence != 0 &&
             (acknowledgedThrough is not { } acknowledged ||
              !acknowledged.IsValid ||
              acknowledged.Scope != scope ||
              acknowledged.Sequence.Value != expectedAcknowledgedSequence)))
        {
            throw new ArgumentOutOfRangeException(nameof(acknowledgedThrough));
        }

        var journal = new AuthorityMovementTransitionJournal(scope, policy)
        {
            _highestContiguousObservedTransition =
                highestContiguousObservedTransition?.Value ?? 0,
            _nextResolutionSequence = nextResolutionSequence.Value,
            _lastAcknowledgedResolution =
                acknowledgedThrough?.Sequence.Value ?? 0,
        };
        return journal;
    }

    public AuthorityTransitionObserveDecision Observe(MovementTransitionIntent intent)
    {
        if (RequiresBaselineRepair)
        {
            return AuthorityTransitionObserveDecision.BaselineRepairRequired;
        }
        if (!intent.Identity.IsValid || intent.Identity.Scope != _scope)
        {
            return AuthorityTransitionObserveDecision.WrongScope;
        }
        if (!intent.IsValid ||
            (intent.LastValidFrame - intent.FirstPredictedFrame) >
                _policy.MaximumValidity)
        {
            return AuthorityTransitionObserveDecision.InvalidIntent;
        }

        var existing = FindEntry(intent.Identity);
        if (existing >= 0)
        {
            return _entries[existing].Intent == intent
                ? AuthorityTransitionObserveDecision.Duplicate
                : AuthorityTransitionObserveDecision.ConflictingDuplicate;
        }

        var id = intent.Identity.Id.Value;
        if (id <= _highestContiguousObservedTransition)
        {
            return AuthorityTransitionObserveDecision.Duplicate;
        }
        var distance = id - _highestContiguousObservedTransition;
        if (distance > 64)
        {
            return AuthorityTransitionObserveDecision.TooFarAhead;
        }
        var bit = 1UL << checked((int)distance - 1);
        if ((_followingObservedMask & bit) != 0)
        {
            return AuthorityTransitionObserveDecision.Duplicate;
        }
        if (_count == Capacity)
        {
            return AuthorityTransitionObserveDecision.CapacityExceeded;
        }

        _entries[_count++] = new AuthorityEntry(intent);
        _followingObservedMask |= bit;
        while ((_followingObservedMask & 1UL) != 0)
        {
            _highestContiguousObservedTransition++;
            _followingObservedMask >>= 1;
        }
        return AuthorityTransitionObserveDecision.FirstSeen;
    }

    public AuthorityTransitionResolveDecision Resolve(
        MovementTransitionIdentity transition,
        MovementTransitionOutcome outcome,
        SimulationInstant applicationFrame,
        MovementTransitionRejectionReason rejectionReason,
        SimulationInstant decisionFrame,
        out MovementTransitionResolution resolution)
    {
        resolution = default;
        if (RequiresBaselineRepair)
        {
            return AuthorityTransitionResolveDecision.BaselineRepairRequired;
        }
        if (!transition.IsValid || transition.Scope != _scope)
        {
            return AuthorityTransitionResolveDecision.WrongScope;
        }

        var index = FindEntry(transition);
        if (index < 0)
        {
            return AuthorityTransitionResolveDecision.UnknownTransition;
        }

        ref var entry = ref _entries[index];
        if (entry.HasResolution)
        {
            resolution = entry.Resolution;
            return ResolutionMatchesRequest(entry.Intent, resolution, outcome,
                applicationFrame, rejectionReason, decisionFrame)
                ? AuthorityTransitionResolveDecision.AlreadyResolved
                : AuthorityTransitionResolveDecision.ConflictingResolution;
        }

        ValidateResolutionRequest(
            entry.Intent,
            outcome,
            applicationFrame,
            rejectionReason,
            decisionFrame);
        if (_nextResolutionSequence == 0)
        {
            RequiresBaselineRepair = true;
            return AuthorityTransitionResolveDecision.BaselineRepairRequired;
        }

        resolution = new MovementTransitionResolution(
            new TransitionResolutionIdentity(
                _scope,
                new TransitionResolutionSequence(_nextResolutionSequence)),
            transition,
            outcome,
            outcome is MovementTransitionOutcome.Accepted or
                MovementTransitionOutcome.Remapped,
            applicationFrame,
            decisionFrame,
            rejectionReason);
        entry.Resolution = resolution;
        entry.HasResolution = true;
        _nextResolutionSequence = _nextResolutionSequence == ulong.MaxValue
            ? 0
            : _nextResolutionSequence + 1;
        return AuthorityTransitionResolveDecision.Resolved;
    }

    /// <summary>
    /// Reads an observed intent and its terminal result, if it has one, without
    /// changing any journal state. The frame scheduler needs the authored
    /// validity window to decide whether a reference applies on the frame being
    /// simulated, and needs to see an existing tombstone so a repeat reference
    /// can never produce a second application.
    /// </summary>
    public bool TryGetIntent(
        MovementTransitionIdentity identity,
        out MovementTransitionIntent intent,
        out MovementTransitionResolution? resolution)
    {
        intent = default;
        resolution = null;
        if (!identity.IsValid || identity.Scope != _scope)
        {
            return false;
        }

        var index = FindEntry(identity);
        if (index < 0)
        {
            return false;
        }

        ref readonly var entry = ref _entries[index];
        intent = entry.Intent;
        resolution = entry.HasResolution ? entry.Resolution : null;
        return true;
    }

    public int ExpireThrough(
        SimulationInstant currentFrame,
        Span<MovementTransitionResolution> destination)
    {
        var written = 0;
        while (written < destination.Length)
        {
            var selected = -1;
            ulong selectedId = ulong.MaxValue;
            for (var index = 0; index < _count; index++)
            {
                ref readonly var candidate = ref _entries[index];
                if (!candidate.HasResolution &&
                    currentFrame > candidate.Intent.LastValidFrame &&
                    candidate.Intent.Identity.Id.Value < selectedId)
                {
                    selected = index;
                    selectedId = candidate.Intent.Identity.Id.Value;
                }
            }
            if (selected < 0)
            {
                break;
            }

            var decision = Resolve(
                _entries[selected].Intent.Identity,
                MovementTransitionOutcome.Expired,
                default,
                MovementTransitionRejectionReason.DeadlineExpired,
                currentFrame,
                out destination[written]);
            if (decision != AuthorityTransitionResolveDecision.Resolved)
            {
                break;
            }
            written++;
        }
        return written;
    }

    public int CopyUnacknowledgedResolutions(
        Span<MovementTransitionResolution> destination)
    {
        var written = 0;
        ulong previousSequence = 0;
        while (written < destination.Length)
        {
            var selected = -1;
            var selectedSequence = 0UL;
            for (var index = 0; index < _count; index++)
            {
                if (!_entries[index].HasResolution)
                {
                    continue;
                }
                var sequence =
                    _entries[index].Resolution.ResolutionIdentity.Sequence.Value;
                if (sequence > previousSequence &&
                    (selected < 0 || sequence < selectedSequence))
                {
                    selected = index;
                    selectedSequence = sequence;
                }
            }
            if (selected < 0)
            {
                break;
            }
            destination[written++] = _entries[selected].Resolution;
            previousSequence = selectedSequence;
        }
        return written;
    }

    public bool AcknowledgeResolutions(TransitionResolutionIdentity cursor)
    {
        if (!cursor.IsValid || cursor.Scope != _scope ||
            (_nextResolutionSequence != 0 &&
             cursor.Sequence.Value >= _nextResolutionSequence))
        {
            return false;
        }
        if (cursor.Sequence.Value <= _lastAcknowledgedResolution)
        {
            return true;
        }

        _lastAcknowledgedResolution = cursor.Sequence.Value;
        for (var index = _count - 1; index >= 0; index--)
        {
            if (_entries[index].HasResolution &&
                _entries[index].Resolution.ResolutionIdentity.Sequence.Value <=
                    cursor.Sequence.Value)
            {
                RemoveEntryAt(index);
            }
        }
        return true;
    }

    public bool CheckTombstoneRetention(
        SimulationInstant currentFrame)
    {
        if (RequiresBaselineRepair)
        {
            return true;
        }

        for (var index = 0; index < _count; index++)
        {
            ref readonly var entry = ref _entries[index];
            if (!entry.HasResolution)
            {
                continue;
            }
            if (currentFrame < entry.Resolution.DecisionFrame)
            {
                throw new InvalidOperationException(
                    "Tombstone retention cannot be evaluated before its recorded frame.");
            }
            if ((currentFrame - entry.Resolution.DecisionFrame) >
                _policy.TombstoneRetention)
            {
                RequiresBaselineRepair = true;
                return true;
            }
        }
        return false;
    }

    public bool Reset(OwnerIntentScope scope)
    {
        if (!scope.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(scope));
        }
        if (scope == _scope)
        {
            return false;
        }

        Array.Clear(_entries);
        _scope = scope;
        _count = 0;
        _highestContiguousObservedTransition = 0;
        _followingObservedMask = 0;
        _nextResolutionSequence = 1;
        _lastAcknowledgedResolution = 0;
        RequiresBaselineRepair = false;
        return true;
    }

    private static void ValidateResolutionRequest(
        MovementTransitionIntent intent,
        MovementTransitionOutcome outcome,
        SimulationInstant applicationFrame,
        MovementTransitionRejectionReason rejectionReason,
        SimulationInstant decisionFrame)
    {
        if (!Enum.IsDefined(outcome) || !Enum.IsDefined(rejectionReason))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        if (outcome is not (MovementTransitionOutcome.Accepted or
                MovementTransitionOutcome.Remapped) &&
            applicationFrame != default)
        {
            throw new ArgumentException(
                "A non-applied transition cannot carry an application frame.",
                nameof(applicationFrame));
        }

        switch (outcome)
        {
            case MovementTransitionOutcome.Accepted:
                if (applicationFrame != intent.FirstPredictedFrame ||
                    decisionFrame != applicationFrame ||
                    rejectionReason != MovementTransitionRejectionReason.None)
                {
                    throw new ArgumentException("Accepted transition must apply on its predicted frame.");
                }
                break;
            case MovementTransitionOutcome.Remapped:
                if (applicationFrame <= intent.FirstPredictedFrame ||
                    applicationFrame > intent.LastValidFrame ||
                    decisionFrame != applicationFrame ||
                    rejectionReason != MovementTransitionRejectionReason.None)
                {
                    throw new ArgumentException("Remapped transition requires a later in-deadline frame.");
                }
                break;
            case MovementTransitionOutcome.Rejected:
                if (decisionFrame < intent.FirstPredictedFrame ||
                    decisionFrame > intent.LastValidFrame ||
                    rejectionReason is MovementTransitionRejectionReason.None or
                    MovementTransitionRejectionReason.DeadlineExpired or
                    MovementTransitionRejectionReason.Superseded)
                {
                    throw new ArgumentOutOfRangeException(nameof(rejectionReason));
                }
                break;
            case MovementTransitionOutcome.Expired:
                if (decisionFrame <= intent.LastValidFrame ||
                    rejectionReason != MovementTransitionRejectionReason.DeadlineExpired)
                {
                    throw new ArgumentException("Expiration must occur after the last valid frame.");
                }
                break;
            case MovementTransitionOutcome.Superseded:
                if (decisionFrame < intent.FirstPredictedFrame ||
                    decisionFrame > intent.LastValidFrame ||
                    rejectionReason != MovementTransitionRejectionReason.Superseded)
                {
                    throw new ArgumentOutOfRangeException(nameof(rejectionReason));
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(outcome));
        }
    }

    private static bool ResolutionMatchesRequest(
        MovementTransitionIntent intent,
        MovementTransitionResolution resolution,
        MovementTransitionOutcome outcome,
        SimulationInstant applicationFrame,
        MovementTransitionRejectionReason rejectionReason,
        SimulationInstant decisionFrame)
    {
        _ = intent;
        return resolution.Outcome == outcome &&
               resolution.ApplicationFrame == applicationFrame &&
               resolution.DecisionFrame == decisionFrame &&
               resolution.RejectionReason == rejectionReason;
    }

    private int FindEntry(MovementTransitionIdentity identity)
    {
        for (var index = 0; index < _count; index++)
        {
            if (_entries[index].Intent.Identity == identity)
            {
                return index;
            }
        }
        return -1;
    }

    private int CountEntries(bool resolved)
    {
        var count = 0;
        for (var index = 0; index < _count; index++)
        {
            if (_entries[index].HasResolution == resolved)
            {
                count++;
            }
        }
        return count;
    }

    private void RemoveEntryAt(int index)
    {
        if (index < _count - 1)
        {
            Array.Copy(_entries, index + 1, _entries, index, _count - index - 1);
        }
        _entries[--_count] = default;
    }

    private struct AuthorityEntry
    {
        public AuthorityEntry(MovementTransitionIntent intent)
        {
            Intent = intent;
        }

        public MovementTransitionIntent Intent;
        public bool HasResolution;
        public MovementTransitionResolution Resolution;
    }
}
