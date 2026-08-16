using BattleArena.Core.Common;
using BattleArena.Core.Movement.Simulation;

namespace BattleArena.Multiplayer.OwnerPrediction;

public static class OwnerActionJournalLimits
{
    public const int DefaultCapacity = 64;
    public const int MaximumCapacity = 256;
    public const int ResolutionReorderWindow = 64;
    public const long MaximumValidityTicks = 3_600;
    public const long MaximumTombstoneRetentionTicks = 36_000;
}

/// <summary>Immutable memory, deadline, and repair limits shared by both endpoints.</summary>
public readonly record struct OwnerActionJournalPolicy
{
    public static OwnerActionJournalPolicy Default { get; } = new(
        OwnerActionJournalLimits.DefaultCapacity,
        new SimulationDuration(12),
        new SimulationDuration(600));

    public OwnerActionJournalPolicy(
        int capacity,
        SimulationDuration maximumValidity,
        SimulationDuration tombstoneRetention)
    {
        if (capacity <= 0 || capacity > OwnerActionJournalLimits.MaximumCapacity)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }
        if (maximumValidity.Ticks <= 0 ||
            maximumValidity.Ticks > OwnerActionJournalLimits.MaximumValidityTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumValidity));
        }
        if (tombstoneRetention.Ticks >
            OwnerActionJournalLimits.MaximumTombstoneRetentionTicks)
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
        Capacity <= OwnerActionJournalLimits.MaximumCapacity &&
        MaximumValidity.Ticks > 0 &&
        MaximumValidity.Ticks <= OwnerActionJournalLimits.MaximumValidityTicks &&
        TombstoneRetention.Ticks <=
            OwnerActionJournalLimits.MaximumTombstoneRetentionTicks;

    public OwnerActionJournalPolicy WithCapacity(int capacity) => new(
        capacity,
        MaximumValidity,
        TombstoneRetention);
}

/// <summary>
/// The player control that initiated an action. The authority resolves the
/// authored action definition from canonical equipment state at the target
/// frame; a client never chooses or supplies an item definition.
/// </summary>
public enum OwnerActionTrigger : byte
{
    Attack = 1,
    Block = 2,
    ActivateWeapon = 3,
    ActivateHelmet = 4,
    ActivateChestArmor = 5,
    ActivateGloves = 6,
    ActivateBoots = 7,
    ActivateAmulet = 8,
    ActivateSelectedFlexibleItem = 9,
}

/// <summary>
/// One client-predicted action request, advertised independently of commands
/// until the authority returns an idempotent terminal result.
/// </summary>
public readonly record struct PredictedActionIntent
{
    public PredictedActionIntent(
        PredictedActionIdentity identity,
        OwnerInputIdentity originatingInput,
        OwnerActionTrigger trigger,
        SimulationInstant predictedStartFrame,
        SimulationInstant lastValidStartFrame,
        SimulationInstant renderedAuthorityFrame)
    {
        if (!identity.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(identity));
        }
        if (!originatingInput.IsValid || originatingInput.Scope != identity.Scope)
        {
            throw new ArgumentOutOfRangeException(nameof(originatingInput));
        }
        if (!Enum.IsDefined(trigger))
        {
            throw new ArgumentOutOfRangeException(nameof(trigger));
        }
        if (lastValidStartFrame < predictedStartFrame)
        {
            throw new ArgumentOutOfRangeException(nameof(lastValidStartFrame));
        }
        if (renderedAuthorityFrame > predictedStartFrame)
        {
            throw new ArgumentOutOfRangeException(nameof(renderedAuthorityFrame));
        }

        Identity = identity;
        OriginatingInput = originatingInput;
        Trigger = trigger;
        PredictedStartFrame = predictedStartFrame;
        LastValidStartFrame = lastValidStartFrame;
        RenderedAuthorityFrame = renderedAuthorityFrame;
    }

    public PredictedActionIdentity Identity { get; }
    public OwnerInputIdentity OriginatingInput { get; }
    public OwnerActionTrigger Trigger { get; }
    public SimulationInstant PredictedStartFrame { get; }
    public SimulationInstant LastValidStartFrame { get; }
    public SimulationInstant RenderedAuthorityFrame { get; }
    public bool IsValid =>
        Identity.IsValid &&
        OriginatingInput.IsValid &&
        OriginatingInput.Scope == Identity.Scope &&
        Enum.IsDefined(Trigger) &&
        LastValidStartFrame >= PredictedStartFrame &&
        RenderedAuthorityFrame <= PredictedStartFrame;
}

public enum PredictedActionOutcome : byte
{
    Accepted = 1,
    Remapped = 2,
    Rejected = 3,
    Expired = 4,
    Superseded = 5,
}

public enum PredictedActionRejectionReason : byte
{
    None = 0,
    AuthorityPolicyRejected = 1,
    InvalidState = 2,
    CooldownActive = 3,
    CapabilityUnavailable = 4,
    DeadlineExpired = 5,
    Superseded = 6,
}

/// <summary>
/// Terminal authority correlation. Accepted/remapped results attach a distinct
/// life-scoped authority execution without restarting local presentation.
/// </summary>
public readonly record struct PredictedActionResolution
{
    public PredictedActionResolution(
        ActionResolutionIdentity resolutionIdentity,
        PredictedActionIdentity actionIdentity,
        PredictedActionOutcome outcome,
        bool hasAuthorityExecution,
        AuthorityActionExecutionIdentity authorityExecution,
        SimulationInstant startFrame,
        SimulationInstant decisionFrame,
        PredictedActionRejectionReason rejectionReason)
    {
        if (!resolutionIdentity.IsValid || !actionIdentity.IsValid ||
            resolutionIdentity.Scope != actionIdentity.Scope)
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

        var applied = outcome is PredictedActionOutcome.Accepted or
            PredictedActionOutcome.Remapped;
        if (hasAuthorityExecution != applied)
        {
            throw new ArgumentException(
                "Only accepted or remapped actions have an authority execution.",
                nameof(hasAuthorityExecution));
        }
        if (applied)
        {
            var expectedScope = new AuthorityActionExecutionScope(
                resolutionIdentity.Scope.SessionId,
                resolutionIdentity.Scope.Life);
            if (!authorityExecution.IsValid || authorityExecution.Scope != expectedScope)
            {
                throw new ArgumentOutOfRangeException(nameof(authorityExecution));
            }
            if (startFrame != decisionFrame)
            {
                throw new ArgumentException(
                    "An accepted execution starts on its authority decision frame.",
                    nameof(decisionFrame));
            }
        }
        else if (authorityExecution != default || startFrame != default)
        {
            throw new ArgumentException(
                "A non-applied action cannot carry execution state.",
                nameof(authorityExecution));
        }

        var expectedReason = outcome switch
        {
            PredictedActionOutcome.Accepted or PredictedActionOutcome.Remapped =>
                PredictedActionRejectionReason.None,
            PredictedActionOutcome.Expired =>
                PredictedActionRejectionReason.DeadlineExpired,
            PredictedActionOutcome.Superseded =>
                PredictedActionRejectionReason.Superseded,
            PredictedActionOutcome.Rejected => rejectionReason,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
        };
        if (rejectionReason != expectedReason ||
            (outcome == PredictedActionOutcome.Rejected &&
             rejectionReason is PredictedActionRejectionReason.None or
                 PredictedActionRejectionReason.DeadlineExpired or
                 PredictedActionRejectionReason.Superseded))
        {
            throw new ArgumentOutOfRangeException(nameof(rejectionReason));
        }

        ResolutionIdentity = resolutionIdentity;
        ActionIdentity = actionIdentity;
        Outcome = outcome;
        HasAuthorityExecution = hasAuthorityExecution;
        AuthorityExecution = authorityExecution;
        StartFrame = startFrame;
        DecisionFrame = decisionFrame;
        RejectionReason = rejectionReason;
    }

    public ActionResolutionIdentity ResolutionIdentity { get; }
    public PredictedActionIdentity ActionIdentity { get; }
    public PredictedActionOutcome Outcome { get; }
    public bool HasAuthorityExecution { get; }
    public AuthorityActionExecutionIdentity AuthorityExecution { get; }
    public SimulationInstant StartFrame { get; }
    public SimulationInstant DecisionFrame { get; }
    public PredictedActionRejectionReason RejectionReason { get; }
    public bool IsValid
    {
        get
        {
            if (!ResolutionIdentity.IsValid || !ActionIdentity.IsValid ||
                ResolutionIdentity.Scope != ActionIdentity.Scope ||
                !Enum.IsDefined(Outcome) || !Enum.IsDefined(RejectionReason))
            {
                return false;
            }

            var applied = Outcome is PredictedActionOutcome.Accepted or
                PredictedActionOutcome.Remapped;
            if (HasAuthorityExecution != applied)
            {
                return false;
            }
            if (applied)
            {
                var expectedScope = new AuthorityActionExecutionScope(
                    ResolutionIdentity.Scope.SessionId,
                    ResolutionIdentity.Scope.Life);
                return AuthorityExecution.IsValid &&
                    AuthorityExecution.Scope == expectedScope &&
                    StartFrame == DecisionFrame &&
                    RejectionReason == PredictedActionRejectionReason.None;
            }

            return AuthorityExecution == default &&
                StartFrame == default &&
                Outcome switch
                {
                    PredictedActionOutcome.Rejected =>
                        RejectionReason is
                            PredictedActionRejectionReason.AuthorityPolicyRejected or
                            PredictedActionRejectionReason.InvalidState or
                            PredictedActionRejectionReason.CooldownActive or
                            PredictedActionRejectionReason.CapabilityUnavailable,
                    PredictedActionOutcome.Expired =>
                        RejectionReason == PredictedActionRejectionReason.DeadlineExpired,
                    PredictedActionOutcome.Superseded =>
                        RejectionReason == PredictedActionRejectionReason.Superseded,
                    _ => false,
                };
        }
    }
}

public enum OwnerActionOriginDecision : byte
{
    Added = 1,
    CapacityExceeded = 2,
    IdentityExhausted = 3,
    BaselineRepairRequired = 4,
}

public enum ClientActionResolutionDecision : byte
{
    Applied = 1,
    Duplicate = 2,
    WrongScope = 3,
    TooFarAhead = 4,
    UnknownAction = 5,
    InvalidResolution = 6,
    ConflictingResolution = 7,
    BaselineRepairRequired = 8,
}

/// <summary>Client-side predicted-action resend and terminal-result journal.</summary>
public sealed class OwnerActionCommandJournal
{
    private readonly PredictedActionIntent[] _outstanding;
    private readonly PredictedActionResolution[] _receivedResolutionSlots =
        new PredictedActionResolution[OwnerActionJournalLimits.ResolutionReorderWindow];
    private readonly OwnerActionJournalPolicy _policy;
    private OwnerIntentScope _scope;
    private int _count;
    private ulong _nextActionId = 1;
    private bool _identityExhausted;
    private ulong _highestContiguousResolution;
    private int _nextTransmissionIndex;

    public OwnerActionCommandJournal(OwnerIntentScope scope)
        : this(scope, OwnerActionJournalPolicy.Default)
    {
    }

    public OwnerActionCommandJournal(OwnerIntentScope scope, int capacity)
        : this(scope, OwnerActionJournalPolicy.Default.WithCapacity(capacity))
    {
    }

    public OwnerActionCommandJournal(
        OwnerIntentScope scope,
        OwnerActionJournalPolicy policy)
    {
        ValidateScopeAndPolicy(scope, policy);
        _scope = scope;
        _policy = policy;
        _outstanding = new PredictedActionIntent[policy.Capacity];
    }

    public OwnerIntentScope Scope => _scope;
    public int Capacity => _outstanding.Length;
    public OwnerActionJournalPolicy Policy => _policy;
    public int OutstandingCount => _count;
    public bool RequiresBaselineRepair { get; private set; }
    public ActionResolutionIdentity? AppliedResolutionCursor =>
        _highestContiguousResolution == 0
            ? null
            : new ActionResolutionIdentity(
                _scope,
                new ActionResolutionSequence(_highestContiguousResolution));

    public static OwnerActionCommandJournal RestoreEmptyBaseline(
        OwnerIntentScope scope,
        OwnerActionJournalPolicy policy,
        PredictedActionId nextActionId,
        ActionResolutionIdentity? appliedResolutionCursor = null)
    {
        if (!nextActionId.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(nextActionId));
        }
        if (appliedResolutionCursor is { } cursor &&
            (!cursor.IsValid || cursor.Scope != scope))
        {
            throw new ArgumentOutOfRangeException(nameof(appliedResolutionCursor));
        }

        return new OwnerActionCommandJournal(scope, policy)
        {
            _nextActionId = nextActionId.Value,
            _highestContiguousResolution = appliedResolutionCursor?.Sequence.Value ?? 0,
        };
    }

    public OwnerActionOriginDecision TryOriginate(
        OwnerInputIdentity originatingInput,
        OwnerActionTrigger trigger,
        SimulationInstant predictedStartFrame,
        SimulationInstant lastValidStartFrame,
        SimulationInstant renderedAuthorityFrame,
        out PredictedActionIntent intent)
    {
        if (RequiresBaselineRepair)
        {
            intent = default;
            return OwnerActionOriginDecision.BaselineRepairRequired;
        }
        if (!originatingInput.IsValid || originatingInput.Scope != _scope)
        {
            throw new ArgumentOutOfRangeException(nameof(originatingInput));
        }
        if (!Enum.IsDefined(trigger))
        {
            throw new ArgumentOutOfRangeException(nameof(trigger));
        }
        if (lastValidStartFrame < predictedStartFrame ||
            (lastValidStartFrame - predictedStartFrame) > _policy.MaximumValidity)
        {
            throw new ArgumentOutOfRangeException(nameof(lastValidStartFrame));
        }
        if (renderedAuthorityFrame > predictedStartFrame)
        {
            throw new ArgumentOutOfRangeException(nameof(renderedAuthorityFrame));
        }
        if (_identityExhausted)
        {
            intent = default;
            return OwnerActionOriginDecision.IdentityExhausted;
        }
        if (_count == Capacity)
        {
            intent = default;
            return OwnerActionOriginDecision.CapacityExceeded;
        }

        var id = new PredictedActionId(_nextActionId);
        intent = new PredictedActionIntent(
            new PredictedActionIdentity(_scope, id),
            originatingInput,
            trigger,
            predictedStartFrame,
            lastValidStartFrame,
            renderedAuthorityFrame);
        _outstanding[_count++] = intent;
        if (_nextActionId == ulong.MaxValue)
        {
            _identityExhausted = true;
        }
        else
        {
            _nextActionId++;
        }
        return OwnerActionOriginDecision.Added;
    }

    public int CopyOutstanding(Span<PredictedActionIntent> destination)
    {
        var copied = PeekOutstanding(destination);
        CommitOutstandingTransmissions(copied);
        return copied;
    }

    /// <summary>
    /// Reads the current fair resend prefix without advancing it. The send-window
    /// composer commits only the prefix that was actually encoded.
    /// </summary>
    internal int PeekOutstanding(Span<PredictedActionIntent> destination)
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

    public ClientActionResolutionDecision ApplyResolution(
        PredictedActionResolution resolution,
        out PredictedActionIntent resolvedIntent)
    {
        resolvedIntent = default;
        if (RequiresBaselineRepair)
        {
            return ClientActionResolutionDecision.BaselineRepairRequired;
        }
        if (!resolution.IsValid || resolution.ResolutionIdentity.Scope != _scope)
        {
            return resolution.ResolutionIdentity.IsValid &&
                resolution.ResolutionIdentity.Scope == _scope
                ? RequireBaselineRepair(ClientActionResolutionDecision.InvalidResolution)
                : ClientActionResolutionDecision.WrongScope;
        }

        var receiveDecision = InspectResolutionSequence(resolution);
        if (receiveDecision != ClientActionResolutionDecision.Applied)
        {
            return receiveDecision;
        }

        var index = FindOutstanding(resolution.ActionIdentity);
        if (index < 0)
        {
            return RequireBaselineRepair(ClientActionResolutionDecision.UnknownAction);
        }
        resolvedIntent = _outstanding[index];
        if (!ResolutionIsPossibleForIntent(resolution, resolvedIntent))
        {
            resolvedIntent = default;
            return RequireBaselineRepair(ClientActionResolutionDecision.InvalidResolution);
        }

        RemoveOutstandingAt(index);
        RecordResolutionSequence(resolution);
        return ClientActionResolutionDecision.Applied;
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

        Array.Clear(_outstanding);
        Array.Clear(_receivedResolutionSlots);
        _scope = scope;
        _count = 0;
        _nextActionId = 1;
        _identityExhausted = false;
        _highestContiguousResolution = 0;
        _nextTransmissionIndex = 0;
        RequiresBaselineRepair = false;
        return true;
    }

    private ClientActionResolutionDecision InspectResolutionSequence(
        PredictedActionResolution resolution)
    {
        var sequence = resolution.ResolutionIdentity.Sequence.Value;
        var window = OwnerActionJournalLimits.ResolutionReorderWindow;
        var slot = (int)(sequence % (ulong)window);
        if (sequence <= _highestContiguousResolution)
        {
            var retained = _receivedResolutionSlots[slot];
            if (retained.IsValid &&
                retained.ResolutionIdentity.Sequence.Value == sequence)
            {
                return retained == resolution
                    ? ClientActionResolutionDecision.Duplicate
                    : RequireBaselineRepair(
                        ClientActionResolutionDecision.ConflictingResolution);
            }
            if (FindOutstanding(resolution.ActionIdentity) >= 0)
            {
                return RequireBaselineRepair(
                    ClientActionResolutionDecision.ConflictingResolution);
            }
            return ClientActionResolutionDecision.Duplicate;
        }

        var distance = sequence - _highestContiguousResolution;
        if (distance > OwnerActionJournalLimits.ResolutionReorderWindow)
        {
            return RequireBaselineRepair(ClientActionResolutionDecision.TooFarAhead);
        }
        var existing = _receivedResolutionSlots[slot];
        if (!existing.IsValid || existing.ResolutionIdentity.Sequence.Value != sequence)
        {
            return ClientActionResolutionDecision.Applied;
        }
        return existing == resolution
            ? ClientActionResolutionDecision.Duplicate
            : RequireBaselineRepair(ClientActionResolutionDecision.ConflictingResolution);
    }

    private void RecordResolutionSequence(PredictedActionResolution resolution)
    {
        var sequence = resolution.ResolutionIdentity.Sequence.Value;
        var window = OwnerActionJournalLimits.ResolutionReorderWindow;
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

    private ClientActionResolutionDecision RequireBaselineRepair(
        ClientActionResolutionDecision decision)
    {
        RequiresBaselineRepair = true;
        return decision;
    }

    private static bool ResolutionIsPossibleForIntent(
        PredictedActionResolution resolution,
        PredictedActionIntent intent) => resolution.Outcome switch
    {
        PredictedActionOutcome.Accepted =>
            resolution.StartFrame == intent.PredictedStartFrame &&
            resolution.DecisionFrame == intent.PredictedStartFrame,
        PredictedActionOutcome.Remapped =>
            resolution.StartFrame > intent.PredictedStartFrame &&
            resolution.StartFrame <= intent.LastValidStartFrame &&
            resolution.DecisionFrame == resolution.StartFrame,
        PredictedActionOutcome.Rejected or PredictedActionOutcome.Superseded =>
            resolution.DecisionFrame >= intent.PredictedStartFrame &&
            resolution.DecisionFrame <= intent.LastValidStartFrame,
        PredictedActionOutcome.Expired =>
            resolution.DecisionFrame > intent.LastValidStartFrame,
        _ => false,
    };

    private int FindOutstanding(PredictedActionIdentity identity)
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

    private static void ValidateScopeAndPolicy(
        OwnerIntentScope scope,
        OwnerActionJournalPolicy policy)
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

public enum AuthorityActionObserveDecision : byte
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

public enum AuthorityActionResolveDecision : byte
{
    Resolved = 1,
    AlreadyResolved = 2,
    ConflictingResolution = 3,
    UnknownAction = 4,
    WrongScope = 5,
    BaselineRepairRequired = 6,
}

/// <summary>
/// Authority-side exact-once action ledger. Authority execution IDs are
/// supplied by the separate life-scoped execution allocator; this journal only
/// correlates them with owner predictions and retains terminal tombstones.
/// </summary>
public sealed class AuthorityOwnerActionCommandJournal
{
    private readonly AuthorityEntry[] _entries;
    private readonly OwnerActionJournalPolicy _policy;
    private OwnerIntentScope _scope;
    private int _count;
    private ulong _highestContiguousObservedAction;
    private ulong _followingObservedMask;
    private ulong _nextResolutionSequence = 1;
    private ulong _lastAcknowledgedResolution;

    public AuthorityOwnerActionCommandJournal(OwnerIntentScope scope)
        : this(scope, OwnerActionJournalPolicy.Default)
    {
    }

    public AuthorityOwnerActionCommandJournal(OwnerIntentScope scope, int capacity)
        : this(scope, OwnerActionJournalPolicy.Default.WithCapacity(capacity))
    {
    }

    public AuthorityOwnerActionCommandJournal(
        OwnerIntentScope scope,
        OwnerActionJournalPolicy policy)
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
        _entries = new AuthorityEntry[policy.Capacity];
    }

    public OwnerIntentScope Scope => _scope;
    public int Capacity => _entries.Length;
    public OwnerActionJournalPolicy Policy => _policy;
    public int EntryCount => _count;
    public int PendingCount => CountEntries(resolved: false);
    public int TombstoneCount => CountEntries(resolved: true);
    public bool RequiresBaselineRepair { get; private set; }
    public ActionResolutionIdentity? LastAcknowledgedResolution =>
        _lastAcknowledgedResolution == 0
            ? null
            : new ActionResolutionIdentity(
                _scope,
                new ActionResolutionSequence(_lastAcknowledgedResolution));

    /// <summary>
    /// The sequence the next terminal result will carry, or zero when the
    /// numbering is exhausted. Read-only; carried across a same-scope rebuild.
    /// </summary>
    internal ulong NextResolutionSequenceValue => _nextResolutionSequence;

    /// <summary>
    /// Highest contiguously observed action identity, or null when none has been
    /// observed. Carried across a same-scope rebuild.
    /// </summary>
    internal PredictedActionId? HighestContiguousObservedActionId =>
        _highestContiguousObservedAction == 0
            ? null
            : new PredictedActionId(_highestContiguousObservedAction);

    public static AuthorityOwnerActionCommandJournal RestoreEmptyBaseline(
        OwnerIntentScope scope,
        OwnerActionJournalPolicy policy,
        ActionResolutionSequence nextResolutionSequence,
        ActionResolutionIdentity? acknowledgedThrough,
        PredictedActionId? highestContiguousObservedAction = null)
    {
        if (!nextResolutionSequence.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(nextResolutionSequence));
        }
        if (highestContiguousObservedAction is { } observed && !observed.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(highestContiguousObservedAction));
        }
        var expectedAcknowledgedSequence = nextResolutionSequence.Value - 1;
        if ((expectedAcknowledgedSequence == 0 && acknowledgedThrough is not null) ||
            (expectedAcknowledgedSequence != 0 &&
             (acknowledgedThrough is not { } acknowledged ||
              !acknowledged.IsValid ||
              acknowledged.Scope != scope ||
              acknowledged.Sequence.Value != expectedAcknowledgedSequence)))
        {
            throw new ArgumentOutOfRangeException(nameof(acknowledgedThrough));
        }

        return new AuthorityOwnerActionCommandJournal(scope, policy)
        {
            _highestContiguousObservedAction =
                highestContiguousObservedAction?.Value ?? 0,
            _nextResolutionSequence = nextResolutionSequence.Value,
            _lastAcknowledgedResolution = acknowledgedThrough?.Sequence.Value ?? 0,
        };
    }

    public AuthorityActionObserveDecision Observe(PredictedActionIntent intent)
    {
        if (RequiresBaselineRepair)
        {
            return AuthorityActionObserveDecision.BaselineRepairRequired;
        }
        if (!intent.Identity.IsValid || intent.Identity.Scope != _scope)
        {
            return AuthorityActionObserveDecision.WrongScope;
        }
        if (!intent.IsValid ||
            (intent.LastValidStartFrame - intent.PredictedStartFrame) >
                _policy.MaximumValidity)
        {
            return AuthorityActionObserveDecision.InvalidIntent;
        }

        var existing = FindEntry(intent.Identity);
        if (existing >= 0)
        {
            return _entries[existing].Intent == intent
                ? AuthorityActionObserveDecision.Duplicate
                : AuthorityActionObserveDecision.ConflictingDuplicate;
        }

        var id = intent.Identity.Id.Value;
        if (id <= _highestContiguousObservedAction)
        {
            return AuthorityActionObserveDecision.Duplicate;
        }
        var distance = id - _highestContiguousObservedAction;
        if (distance > OwnerActionJournalLimits.ResolutionReorderWindow)
        {
            return AuthorityActionObserveDecision.TooFarAhead;
        }
        var bit = 1UL << checked((int)distance - 1);
        if ((_followingObservedMask & bit) != 0)
        {
            return AuthorityActionObserveDecision.Duplicate;
        }
        if (_count == Capacity)
        {
            return AuthorityActionObserveDecision.CapacityExceeded;
        }

        _entries[_count++] = new AuthorityEntry(intent);
        _followingObservedMask |= bit;
        while ((_followingObservedMask & 1UL) != 0)
        {
            _highestContiguousObservedAction++;
            _followingObservedMask >>= 1;
        }
        return AuthorityActionObserveDecision.FirstSeen;
    }

    public AuthorityActionResolveDecision Resolve(
        PredictedActionIdentity action,
        PredictedActionOutcome outcome,
        AuthorityActionExecutionIdentity authorityExecution,
        SimulationInstant startFrame,
        PredictedActionRejectionReason rejectionReason,
        SimulationInstant decisionFrame,
        out PredictedActionResolution resolution)
    {
        resolution = default;
        if (RequiresBaselineRepair)
        {
            return AuthorityActionResolveDecision.BaselineRepairRequired;
        }
        if (!action.IsValid || action.Scope != _scope)
        {
            return AuthorityActionResolveDecision.WrongScope;
        }
        var index = FindEntry(action);
        if (index < 0)
        {
            return AuthorityActionResolveDecision.UnknownAction;
        }

        ref var entry = ref _entries[index];
        if (entry.HasResolution)
        {
            resolution = entry.Resolution;
            return ResolutionMatchesRequest(
                resolution,
                outcome,
                authorityExecution,
                startFrame,
                rejectionReason,
                decisionFrame)
                ? AuthorityActionResolveDecision.AlreadyResolved
                : AuthorityActionResolveDecision.ConflictingResolution;
        }

        ValidateResolutionRequest(
            entry.Intent,
            outcome,
            authorityExecution,
            startFrame,
            rejectionReason,
            decisionFrame);
        if (_nextResolutionSequence == 0)
        {
            RequiresBaselineRepair = true;
            return AuthorityActionResolveDecision.BaselineRepairRequired;
        }

        resolution = new PredictedActionResolution(
            new ActionResolutionIdentity(
                _scope,
                new ActionResolutionSequence(_nextResolutionSequence)),
            action,
            outcome,
            outcome is PredictedActionOutcome.Accepted or PredictedActionOutcome.Remapped,
            authorityExecution,
            startFrame,
            decisionFrame,
            rejectionReason);
        entry.Resolution = resolution;
        entry.HasResolution = true;
        _nextResolutionSequence = _nextResolutionSequence == ulong.MaxValue
            ? 0
            : _nextResolutionSequence + 1;
        return AuthorityActionResolveDecision.Resolved;
    }

    /// <summary>
    /// Reads an observed intent and its terminal result, if it has one, without
    /// changing any journal state. See the transition journal's equivalent: the
    /// frame scheduler needs the authored start window and any existing
    /// tombstone before it can decide one frame's applications.
    /// </summary>
    public bool TryGetIntent(
        PredictedActionIdentity identity,
        out PredictedActionIntent intent,
        out PredictedActionResolution? resolution)
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
        Span<PredictedActionResolution> destination)
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
                    currentFrame > candidate.Intent.LastValidStartFrame &&
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
                PredictedActionOutcome.Expired,
                default,
                default,
                PredictedActionRejectionReason.DeadlineExpired,
                currentFrame,
                out destination[written]);
            if (decision != AuthorityActionResolveDecision.Resolved)
            {
                break;
            }
            written++;
        }
        return written;
    }

    public int CopyUnacknowledgedResolutions(
        Span<PredictedActionResolution> destination)
    {
        var written = 0;
        ulong previousSequence = 0;
        while (written < destination.Length)
        {
            var selected = -1;
            ulong selectedSequence = 0;
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

    public bool AcknowledgeResolutions(ActionResolutionIdentity cursor)
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

    public bool CheckTombstoneRetention(SimulationInstant currentFrame)
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
                    "Tombstone retention cannot be evaluated before its decision frame.");
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
        _highestContiguousObservedAction = 0;
        _followingObservedMask = 0;
        _nextResolutionSequence = 1;
        _lastAcknowledgedResolution = 0;
        RequiresBaselineRepair = false;
        return true;
    }

    private static void ValidateResolutionRequest(
        PredictedActionIntent intent,
        PredictedActionOutcome outcome,
        AuthorityActionExecutionIdentity authorityExecution,
        SimulationInstant startFrame,
        PredictedActionRejectionReason rejectionReason,
        SimulationInstant decisionFrame)
    {
        if (!Enum.IsDefined(outcome) || !Enum.IsDefined(rejectionReason))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        var applied = outcome is PredictedActionOutcome.Accepted or
            PredictedActionOutcome.Remapped;
        if (applied)
        {
            var expectedScope = new AuthorityActionExecutionScope(
                intent.Identity.Scope.SessionId,
                intent.Identity.Scope.Life);
            if (!authorityExecution.IsValid || authorityExecution.Scope != expectedScope)
            {
                throw new ArgumentOutOfRangeException(nameof(authorityExecution));
            }
        }
        else if (authorityExecution != default || startFrame != default)
        {
            throw new ArgumentException(
                "A non-applied action cannot carry execution state.");
        }

        switch (outcome)
        {
            case PredictedActionOutcome.Accepted:
                if (startFrame != intent.PredictedStartFrame ||
                    decisionFrame != startFrame ||
                    rejectionReason != PredictedActionRejectionReason.None)
                {
                    throw new ArgumentException(
                        "An accepted action must start on its predicted frame.");
                }
                break;
            case PredictedActionOutcome.Remapped:
                if (startFrame <= intent.PredictedStartFrame ||
                    startFrame > intent.LastValidStartFrame ||
                    decisionFrame != startFrame ||
                    rejectionReason != PredictedActionRejectionReason.None)
                {
                    throw new ArgumentException(
                        "A remapped action requires a later in-deadline frame.");
                }
                break;
            case PredictedActionOutcome.Rejected:
                if (decisionFrame < intent.PredictedStartFrame ||
                    decisionFrame > intent.LastValidStartFrame ||
                    rejectionReason is PredictedActionRejectionReason.None or
                        PredictedActionRejectionReason.DeadlineExpired or
                        PredictedActionRejectionReason.Superseded)
                {
                    throw new ArgumentOutOfRangeException(nameof(rejectionReason));
                }
                break;
            case PredictedActionOutcome.Expired:
                if (decisionFrame <= intent.LastValidStartFrame ||
                    rejectionReason != PredictedActionRejectionReason.DeadlineExpired)
                {
                    throw new ArgumentException(
                        "Expiration must occur after the last valid start frame.");
                }
                break;
            case PredictedActionOutcome.Superseded:
                if (decisionFrame < intent.PredictedStartFrame ||
                    decisionFrame > intent.LastValidStartFrame ||
                    rejectionReason != PredictedActionRejectionReason.Superseded)
                {
                    throw new ArgumentOutOfRangeException(nameof(rejectionReason));
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(outcome));
        }
    }

    private static bool ResolutionMatchesRequest(
        PredictedActionResolution resolution,
        PredictedActionOutcome outcome,
        AuthorityActionExecutionIdentity authorityExecution,
        SimulationInstant startFrame,
        PredictedActionRejectionReason rejectionReason,
        SimulationInstant decisionFrame) =>
        resolution.Outcome == outcome &&
        resolution.AuthorityExecution == authorityExecution &&
        resolution.StartFrame == startFrame &&
        resolution.DecisionFrame == decisionFrame &&
        resolution.RejectionReason == rejectionReason;

    private int FindEntry(PredictedActionIdentity identity)
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
        public AuthorityEntry(PredictedActionIntent intent)
        {
            Intent = intent;
        }

        public PredictedActionIntent Intent;
        public bool HasResolution;
        public PredictedActionResolution Resolution;
    }
}
