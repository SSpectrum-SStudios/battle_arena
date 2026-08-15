using BattleArena.Core.Common;
using System.Numerics;

namespace BattleArena.Multiplayer.OwnerPrediction;

public static class OwnerInputSendWindowLimits
{
    public const int DefaultCapacity = 256;
    public const int MaximumCapacity = 1_024;
    public const int DefaultMinimumRecentCommands = 6;
    public const int DefaultMaximumCommandsPerBatch = 12;
    public const int MaximumCommandsPerBatch = 64;
    public const int DefaultMaximumTransitionsPerBatch = 1;
    public const int DefaultMaximumActionsPerBatch = 1;
    public const int MaximumJournalEntriesPerBatch = 8;
    public const int DefaultMaximumPriorityDeferrals = 2;
    public const int MaximumPriorityDeferrals = 64;
}

/// <summary>Immutable storage and service guarantees for one owner send stream.</summary>
public readonly record struct OwnerInputSendWindowPolicy
{
    public static OwnerInputSendWindowPolicy Default { get; } = new(
        OwnerInputSendWindowLimits.DefaultCapacity,
        OwnerInputSendWindowLimits.DefaultMinimumRecentCommands,
        OwnerInputSendWindowLimits.DefaultMaximumCommandsPerBatch,
        OwnerInputSendWindowLimits.DefaultMaximumTransitionsPerBatch,
        OwnerInputSendWindowLimits.DefaultMaximumActionsPerBatch,
        OwnerInputSendWindowLimits.DefaultMaximumPriorityDeferrals);

    public OwnerInputSendWindowPolicy(
        int capacity,
        int minimumRecentCommands,
        int maximumCommandsPerBatch,
        int maximumTransitionsPerBatch,
        int maximumActionsPerBatch,
        int maximumPriorityDeferrals =
            OwnerInputSendWindowLimits.DefaultMaximumPriorityDeferrals)
    {
        if (capacity <= 0 || capacity > OwnerInputSendWindowLimits.MaximumCapacity)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }
        if (minimumRecentCommands <= 0 ||
            minimumRecentCommands > maximumCommandsPerBatch)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumRecentCommands));
        }
        if (maximumCommandsPerBatch <= 0 ||
            maximumCommandsPerBatch > OwnerInputSendWindowLimits.MaximumCommandsPerBatch ||
            maximumCommandsPerBatch > capacity ||
            (capacity > minimumRecentCommands &&
             maximumCommandsPerBatch == minimumRecentCommands))
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCommandsPerBatch));
        }
        if (maximumTransitionsPerBatch <= 0 ||
            maximumTransitionsPerBatch >
                OwnerInputSendWindowLimits.MaximumJournalEntriesPerBatch)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumTransitionsPerBatch));
        }
        if (maximumActionsPerBatch <= 0 ||
            maximumActionsPerBatch >
                OwnerInputSendWindowLimits.MaximumJournalEntriesPerBatch)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumActionsPerBatch));
        }
        if (maximumPriorityDeferrals <= 0 ||
            maximumPriorityDeferrals >
                OwnerInputSendWindowLimits.MaximumPriorityDeferrals)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPriorityDeferrals));
        }

        Capacity = capacity;
        MinimumRecentCommands = minimumRecentCommands;
        MaximumCommandsPerBatch = maximumCommandsPerBatch;
        MaximumTransitionsPerBatch = maximumTransitionsPerBatch;
        MaximumActionsPerBatch = maximumActionsPerBatch;
        MaximumPriorityDeferrals = maximumPriorityDeferrals;
    }

    public int Capacity { get; }
    public int MinimumRecentCommands { get; }
    public int MaximumCommandsPerBatch { get; }
    public int MaximumTransitionsPerBatch { get; }
    public int MaximumActionsPerBatch { get; }
    public int MaximumPriorityDeferrals { get; }
    public bool IsValid =>
        Capacity > 0 &&
        Capacity <= OwnerInputSendWindowLimits.MaximumCapacity &&
        MinimumRecentCommands > 0 &&
        MinimumRecentCommands <= MaximumCommandsPerBatch &&
        MaximumCommandsPerBatch <= OwnerInputSendWindowLimits.MaximumCommandsPerBatch &&
        MaximumCommandsPerBatch <= Capacity &&
        (Capacity == MinimumRecentCommands ||
         MaximumCommandsPerBatch > MinimumRecentCommands) &&
        MaximumTransitionsPerBatch > 0 &&
        MaximumTransitionsPerBatch <=
            OwnerInputSendWindowLimits.MaximumJournalEntriesPerBatch &&
        MaximumActionsPerBatch > 0 &&
        MaximumActionsPerBatch <=
            OwnerInputSendWindowLimits.MaximumJournalEntriesPerBatch &&
        MaximumPriorityDeferrals > 0 &&
        MaximumPriorityDeferrals <= OwnerInputSendWindowLimits.MaximumPriorityDeferrals;
}

/// <summary>
/// Monotonic authority receive evidence: every sequence through the cursor was
/// received and each set bit acknowledges one of the following 64 sequences.
/// </summary>
public readonly record struct OwnerInputReceiveAcknowledgement
{
    public OwnerInputReceiveAcknowledgement(
        OwnerIntentScope scope,
        InputSequence? highestContiguousSequence,
        ulong followingReceivedMask)
    {
        if (!scope.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(scope));
        }
        if (highestContiguousSequence is { } sequence && !sequence.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(highestContiguousSequence));
        }
        if (highestContiguousSequence?.Value == ulong.MaxValue &&
            followingReceivedMask != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(followingReceivedMask));
        }

        Scope = scope;
        HighestContiguousSequence = highestContiguousSequence;
        FollowingReceivedMask = followingReceivedMask;
    }

    public OwnerIntentScope Scope { get; }
    public InputSequence? HighestContiguousSequence { get; }
    public ulong FollowingReceivedMask { get; }
    public bool IsValid =>
        Scope.IsValid &&
        (HighestContiguousSequence is null ||
         HighestContiguousSequence.Value.IsValid) &&
        (HighestContiguousSequence?.Value != ulong.MaxValue ||
         FollowingReceivedMask == 0);

    public bool Includes(InputSequence sequence)
    {
        if (!sequence.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }
        var cursor = HighestContiguousSequence?.Value ?? 0;
        if (sequence.Value <= cursor)
        {
            return true;
        }
        var distance = sequence.Value - cursor;
        return distance <= 64 &&
            (FollowingReceivedMask & (1UL << checked((int)distance - 1))) != 0;
    }
}

/// <summary>All variable header values that affect exact batch encoding size.</summary>
public readonly record struct OwnerInputSendBatchHeader
{
    public OwnerInputSendBatchHeader(
        OwnerIntentScope scope,
        PacketSequence packetSequence,
        TransitionResolutionIdentity? transitionResolutionCursor,
        ActionResolutionIdentity? actionResolutionCursor)
    {
        if (!scope.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(scope));
        }
        if (!packetSequence.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(packetSequence));
        }
        if (transitionResolutionCursor is { } transition &&
            (!transition.IsValid || transition.Scope != scope))
        {
            throw new ArgumentOutOfRangeException(nameof(transitionResolutionCursor));
        }
        if (actionResolutionCursor is { } action &&
            (!action.IsValid || action.Scope != scope))
        {
            throw new ArgumentOutOfRangeException(nameof(actionResolutionCursor));
        }

        Scope = scope;
        PacketSequence = packetSequence;
        TransitionResolutionCursor = transitionResolutionCursor;
        ActionResolutionCursor = actionResolutionCursor;
    }

    public OwnerIntentScope Scope { get; }
    public PacketSequence PacketSequence { get; }
    public TransitionResolutionIdentity? TransitionResolutionCursor { get; }
    public ActionResolutionIdentity? ActionResolutionCursor { get; }
    public bool IsValid =>
        Scope.IsValid &&
        PacketSequence.IsValid &&
        (TransitionResolutionCursor is null ||
         TransitionResolutionCursor.Value.IsValid &&
         TransitionResolutionCursor.Value.Scope == Scope) &&
        (ActionResolutionCursor is null ||
         ActionResolutionCursor.Value.IsValid &&
         ActionResolutionCursor.Value.Scope == Scope);
}

/// <summary>
/// Exact additive wire-size seam. Protobuf message entries are measured with
/// their field tag and length prefix; base bytes include the current header.
/// Implementations must be deterministic and side-effect free because production
/// journal composition preflights the base before reserving fair journal candidates.
/// </summary>
public interface IOwnerInputBatchEncodedSizer
{
    int MeasureBaseBytes(in OwnerInputSendBatchHeader header);
    int MeasureCommandEntryBytes(in OwnerSimulationCommand command);
    int MeasureTransitionEntryBytes(in MovementTransitionIntent transition);
    int MeasureActionEntryBytes(in PredictedActionIntent action);
}

public enum OwnerInputWindowAddDecision : byte
{
    Added = 1,
    DeadlineExpired = 2,
    CapacityExceeded = 3,
    WrongScope = 4,
    NonContiguousSequence = 5,
    NonContiguousTargetFrame = 6,
    TimelineExhausted = 7,
}

public enum OwnerInputProgressDecision : byte
{
    Applied = 1,
    WrongScope = 2,
    AcknowledgesUnoriginatedInput = 3,
}

public enum OwnerInputBatchBuildDecision : byte
{
    Built = 1,
    RequiredContentExceedsBudget = 2,
}

public readonly record struct OwnerInputSendBatch
{
    public OwnerInputSendBatch(
        int commandCount,
        int transitionCount,
        int actionCount,
        int encodedBytes,
        bool hasDeferredCommands,
        bool hasDeferredTransitions,
        bool hasDeferredActions)
    {
        if (commandCount < 0 || transitionCount < 0 || actionCount < 0 ||
            encodedBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(commandCount));
        }
        CommandCount = commandCount;
        TransitionCount = transitionCount;
        ActionCount = actionCount;
        EncodedBytes = encodedBytes;
        HasDeferredCommands = hasDeferredCommands;
        HasDeferredTransitions = hasDeferredTransitions;
        HasDeferredActions = hasDeferredActions;
    }

    public int CommandCount { get; }
    public int TransitionCount { get; }
    public int ActionCount { get; }
    public int EncodedBytes { get; }
    public bool HasDeferredCommands { get; }
    public bool HasDeferredTransitions { get; }
    public bool HasDeferredActions { get; }
}

/// <summary>
/// Client-side bounded command resend window. It owns delivery scheduling only;
/// prediction history and durable transition/action payloads remain separate.
/// </summary>
public sealed class OwnerInputSendWindow
{
    private readonly Entry[] _entries;
    private readonly bool[] _selected;
    private readonly OwnerInputSendWindowPolicy _policy;
    private OwnerIntentScope _scope;
    private int _count;
    private ulong _lastRegisteredSequence;
    private long _lastRegisteredTargetFrame;
    private bool _hasRegisteredCommand;
    private ulong _highestReceivedSequence;
    private ulong _followingReceivedMask;
    private SimulationInstant? _consumedThroughFrame;
    private ulong _fairRetryAfterSequence;
    private uint _transitionDeferralCount;
    private uint _actionDeferralCount;
    private UrgentServiceKind _nextUrgentService = UrgentServiceKind.Command;

    public OwnerInputSendWindow(OwnerIntentScope scope)
        : this(scope, OwnerInputSendWindowPolicy.Default)
    {
    }

    public OwnerInputSendWindow(
        OwnerIntentScope scope,
        OwnerInputSendWindowPolicy policy)
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
        _entries = new Entry[policy.Capacity];
        _selected = new bool[policy.Capacity];
    }

    public OwnerIntentScope Scope => _scope;
    public OwnerInputSendWindowPolicy Policy => _policy;
    public int Count => _count;
    public InputSequence? LastRegisteredSequence => _hasRegisteredCommand
        ? new InputSequence(_lastRegisteredSequence)
        : null;
    public SimulationInstant? LastRegisteredTargetFrame => _hasRegisteredCommand
        ? new SimulationInstant(_lastRegisteredTargetFrame)
        : null;
    public InputSequence? HighestContiguousReceived => _highestReceivedSequence == 0
        ? null
        : new InputSequence(_highestReceivedSequence);
    public ulong FollowingReceivedMask => _followingReceivedMask;
    public SimulationInstant? ConsumedThroughFrame => _consumedThroughFrame;

    /// <summary>
    /// Restores an empty window whose entire prior command prefix is confirmed
    /// received by an authenticated bootstrap. Active/unreceived commands require
    /// a later full-window bootstrap contract and cannot be discarded here.
    /// </summary>
    public static OwnerInputSendWindow RestoreEmptyReceivedBaseline(
        OwnerIntentScope scope,
        OwnerInputSendWindowPolicy policy,
        InputSequence lastRegisteredSequence,
        SimulationInstant lastRegisteredTargetFrame)
    {
        if (!lastRegisteredSequence.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(lastRegisteredSequence));
        }
        return new OwnerInputSendWindow(scope, policy)
        {
            _lastRegisteredSequence = lastRegisteredSequence.Value,
            _lastRegisteredTargetFrame = lastRegisteredTargetFrame.Tick,
            _hasRegisteredCommand = true,
            _highestReceivedSequence = lastRegisteredSequence.Value,
        };
    }

    public OwnerInputWindowAddDecision TryAdd(OwnerSimulationCommand command)
    {
        if (!command.IsValid || command.Identity.Scope != _scope)
        {
            return OwnerInputWindowAddDecision.WrongScope;
        }
        if (_hasRegisteredCommand)
        {
            if (_lastRegisteredSequence == ulong.MaxValue ||
                _lastRegisteredTargetFrame == long.MaxValue)
            {
                return OwnerInputWindowAddDecision.TimelineExhausted;
            }
            if (command.Sequence.Value != _lastRegisteredSequence + 1)
            {
                return OwnerInputWindowAddDecision.NonContiguousSequence;
            }
            if (command.TargetFrame.Tick != _lastRegisteredTargetFrame + 1)
            {
                return OwnerInputWindowAddDecision.NonContiguousTargetFrame;
            }
        }
        else if (command.Sequence != InputSequence.Initial)
        {
            return OwnerInputWindowAddDecision.NonContiguousSequence;
        }

        if (_consumedThroughFrame is { } consumed && command.TargetFrame <= consumed)
        {
            Register(command);
            return OwnerInputWindowAddDecision.DeadlineExpired;
        }
        if (_count == _entries.Length)
        {
            return OwnerInputWindowAddDecision.CapacityExceeded;
        }

        _entries[_count++] = new Entry(command);
        Register(command);
        return OwnerInputWindowAddDecision.Added;
    }

    public OwnerInputProgressDecision ApplyAuthorityProgress(
        OwnerInputReceiveAcknowledgement receive,
        SimulationInstant? consumedThroughFrame)
    {
        if (!receive.IsValid || receive.Scope != _scope)
        {
            return OwnerInputProgressDecision.WrongScope;
        }
        if (!ReceiveEvidenceIsOriginated(receive))
        {
            return OwnerInputProgressDecision.AcknowledgesUnoriginatedInput;
        }

        MergeReceiveEvidence(receive);
        if (consumedThroughFrame is { } incoming &&
            (_consumedThroughFrame is null || incoming > _consumedThroughFrame.Value))
        {
            _consumedThroughFrame = incoming;
        }
        PruneTerminalEntries();
        return OwnerInputProgressDecision.Applied;
    }

    /// <summary>
    /// Production composition seam. Header feasibility is checked before the
    /// journals advance their fair resend cursors, so a packet that cannot even
    /// carry its header has no journal-side scheduling effect.
    /// </summary>
    public OwnerInputBatchBuildDecision TryBuildBatchFromJournals(
        in OwnerInputSendBatchHeader header,
        int maximumEncodedBytes,
        IOwnerInputBatchEncodedSizer sizer,
        MovementTransitionJournal transitionJournal,
        OwnerActionCommandJournal actionJournal,
        Span<OwnerSimulationCommand> commandDestination,
        Span<MovementTransitionIntent> transitionDestination,
        Span<PredictedActionIntent> actionDestination,
        out OwnerInputSendBatch batch)
    {
        ArgumentNullException.ThrowIfNull(sizer);
        ArgumentNullException.ThrowIfNull(transitionJournal);
        ArgumentNullException.ThrowIfNull(actionJournal);
        ValidateBuildArguments(
            header,
            maximumEncodedBytes,
            commandDestination,
            transitionDestination,
            actionDestination);
        if (transitionJournal.Scope != _scope || actionJournal.Scope != _scope)
        {
            throw new ArgumentException("Journals must share the send-window scope.");
        }
        var baseBytes = RequireNonNegative(
            sizer.MeasureBaseBytes(header),
            "base header");
        if (baseBytes > maximumEncodedBytes)
        {
            batch = default;
            return OwnerInputBatchBuildDecision.RequiredContentExceedsBudget;
        }

        Span<MovementTransitionIntent> transitionCandidates =
            stackalloc MovementTransitionIntent[_policy.MaximumTransitionsPerBatch];
        Span<PredictedActionIntent> actionCandidates =
            stackalloc PredictedActionIntent[_policy.MaximumActionsPerBatch];
        var transitionCount = transitionJournal.PeekOutstanding(transitionCandidates);
        var actionCount = actionJournal.PeekOutstanding(actionCandidates);
        var transitionOutstanding = transitionJournal.OutstandingCount;
        var actionOutstanding = actionJournal.OutstandingCount;
        var decision = TryBuildBatch(
            header,
            maximumEncodedBytes,
            sizer,
            transitionCandidates[..transitionCount],
            actionCandidates[..actionCount],
            commandDestination,
            transitionDestination,
            actionDestination,
            out batch);
        if (decision == OwnerInputBatchBuildDecision.Built)
        {
            transitionJournal.CommitOutstandingTransmissions(batch.TransitionCount);
            actionJournal.CommitOutstandingTransmissions(batch.ActionCount);
            batch = new OwnerInputSendBatch(
                batch.CommandCount,
                batch.TransitionCount,
                batch.ActionCount,
                batch.EncodedBytes,
                batch.HasDeferredCommands,
                transitionOutstanding > batch.TransitionCount,
                actionOutstanding > batch.ActionCount);
        }
        return decision;
    }

    public OwnerInputBatchBuildDecision TryBuildBatch(
        in OwnerInputSendBatchHeader header,
        int maximumEncodedBytes,
        IOwnerInputBatchEncodedSizer sizer,
        ReadOnlySpan<MovementTransitionIntent> transitionCandidates,
        ReadOnlySpan<PredictedActionIntent> actionCandidates,
        Span<OwnerSimulationCommand> commandDestination,
        Span<MovementTransitionIntent> transitionDestination,
        Span<PredictedActionIntent> actionDestination,
        out OwnerInputSendBatch batch)
    {
        ArgumentNullException.ThrowIfNull(sizer);
        ValidateBuildArguments(
            header,
            maximumEncodedBytes,
            commandDestination,
            transitionDestination,
            actionDestination);
        ValidateCandidates(transitionCandidates, actionCandidates);

        Array.Clear(_selected);
        var commandCount = 0;
        var transitionCount = 0;
        var actionCount = 0;
        var encodedBytes = RequireNonNegative(
            sizer.MeasureBaseBytes(header),
            "base header");
        if (encodedBytes > maximumEncodedBytes)
        {
            batch = default;
            return OwnerInputBatchBuildDecision.RequiredContentExceedsBudget;
        }

        // Bounded fairness is a reservation: once any category is overdue it
        // may consume the packet before the nominal live-prefix minimum. Without
        // this ordering, a one-entry byte budget can starve serviceable work.
        TryServeOneOverdueCategory(
            sizer,
            maximumEncodedBytes,
            transitionCandidates,
            actionCandidates,
            commandDestination,
            transitionDestination,
            actionDestination,
            ref commandCount,
            ref transitionCount,
            ref actionCount,
            ref encodedBytes);

        var requiredCommands = Math.Min(_policy.MinimumRecentCommands, _count);
        for (var index = 0; index < requiredCommands; index++)
        {
            TryAddCommand(
                index,
                sizer,
                maximumEncodedBytes,
                commandDestination,
                ref commandCount,
                ref encodedBytes);
        }

        AddOptionalCommands(
            OptionalCommandPriority.New,
            sizer,
            maximumEncodedBytes,
            commandDestination,
            ref commandCount,
            ref encodedBytes);
        AddOptionalCommands(
            OptionalCommandPriority.SelectiveGap,
            sizer,
            maximumEncodedBytes,
            commandDestination,
            ref commandCount,
            ref encodedBytes);
        AddOptionalCommands(
            OptionalCommandPriority.DiscreteReference,
            sizer,
            maximumEncodedBytes,
            commandDestination,
            ref commandCount,
            ref encodedBytes);
        AddOptionalCommands(
            OptionalCommandPriority.Any,
            sizer,
            maximumEncodedBytes,
            commandDestination,
            ref commandCount,
            ref encodedBytes);

        AddOptionalTransitions(
            transitionCandidates,
            sizer,
            maximumEncodedBytes,
            transitionDestination,
            ref transitionCount,
            ref encodedBytes);
        AddOptionalActions(
            actionCandidates,
            sizer,
            maximumEncodedBytes,
            actionDestination,
            ref actionCount,
            ref encodedBytes);

        for (var index = 0; index < _count; index++)
        {
            if (_selected[index])
            {
                if (_entries[index].TransmissionCount != uint.MaxValue)
                {
                    _entries[index].TransmissionCount++;
                }
                _entries[index].DeferralCount = 0;
            }
            else if (_entries[index].DeferralCount != uint.MaxValue)
            {
                _entries[index].DeferralCount++;
            }
        }
        _transitionDeferralCount = NextDeferralCount(
            transitionCandidates.Length > transitionCount,
            _transitionDeferralCount);
        _actionDeferralCount = NextDeferralCount(
            actionCandidates.Length > actionCount,
            _actionDeferralCount);

        batch = new OwnerInputSendBatch(
            commandCount,
            transitionCount,
            actionCount,
            encodedBytes,
            hasDeferredCommands: commandCount < _count,
            hasDeferredTransitions: transitionCount < transitionCandidates.Length,
            hasDeferredActions: actionCount < actionCandidates.Length);
        return OwnerInputBatchBuildDecision.Built;
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
        Array.Clear(_selected);
        _scope = scope;
        _count = 0;
        _lastRegisteredSequence = 0;
        _lastRegisteredTargetFrame = 0;
        _hasRegisteredCommand = false;
        _highestReceivedSequence = 0;
        _followingReceivedMask = 0;
        _consumedThroughFrame = null;
        _fairRetryAfterSequence = 0;
        _transitionDeferralCount = 0;
        _actionDeferralCount = 0;
        _nextUrgentService = UrgentServiceKind.Command;
        return true;
    }

    private bool TryAddCommand(
        int index,
        IOwnerInputBatchEncodedSizer sizer,
        int maximumEncodedBytes,
        Span<OwnerSimulationCommand> destination,
        ref int count,
        ref int encodedBytes)
    {
        if (_selected[index] || count == _policy.MaximumCommandsPerBatch)
        {
            return false;
        }
        var bytes = RequirePositive(
            sizer.MeasureCommandEntryBytes(_entries[index].Command),
            "command entry");
        if (!Fits(encodedBytes, bytes, maximumEncodedBytes))
        {
            return false;
        }
        destination[count++] = _entries[index].Command;
        _selected[index] = true;
        encodedBytes += bytes;
        return true;
    }

    private void TryServeOneOverdueCategory(
        IOwnerInputBatchEncodedSizer sizer,
        int maximumEncodedBytes,
        ReadOnlySpan<MovementTransitionIntent> transitions,
        ReadOnlySpan<PredictedActionIntent> actions,
        Span<OwnerSimulationCommand> commandDestination,
        Span<MovementTransitionIntent> transitionDestination,
        Span<PredictedActionIntent> actionDestination,
        ref int commandCount,
        ref int transitionCount,
        ref int actionCount,
        ref int encodedBytes)
    {
        for (var offset = 0; offset < 3; offset++)
        {
            var kind = (UrgentServiceKind)(
                ((int)_nextUrgentService + offset) % 3);
            var served = kind switch
            {
                UrgentServiceKind.Command => TryAddOneFairCommand(
                    OptionalCommandPriority.Overdue,
                    sizer,
                    maximumEncodedBytes,
                    commandDestination,
                    ref commandCount,
                    ref encodedBytes),
                UrgentServiceKind.Transition =>
                    _transitionDeferralCount >=
                        (uint)_policy.MaximumPriorityDeferrals &&
                    TryAddTransition(
                        transitions,
                        sizer,
                        maximumEncodedBytes,
                        transitionDestination,
                        ref transitionCount,
                        ref encodedBytes),
                UrgentServiceKind.Action =>
                    _actionDeferralCount >=
                        (uint)_policy.MaximumPriorityDeferrals &&
                    TryAddAction(
                        actions,
                        sizer,
                        maximumEncodedBytes,
                        actionDestination,
                        ref actionCount,
                        ref encodedBytes),
                _ => false,
            };
            if (served)
            {
                _nextUrgentService = (UrgentServiceKind)(((int)kind + 1) % 3);
                return;
            }
        }
    }

    private bool TryAddOneFairCommand(
        OptionalCommandPriority priority,
        IOwnerInputBatchEncodedSizer sizer,
        int maximumEncodedBytes,
        Span<OwnerSimulationCommand> destination,
        ref int count,
        ref int encodedBytes)
    {
        var start = FindFairStartIndex();
        for (var offset = 0; offset < _count; offset++)
        {
            var index = (start + offset) % _count;
            if (_selected[index] || !MatchesPriority(_entries[index], priority))
            {
                continue;
            }
            if (TryAddCommand(
                    index,
                    sizer,
                    maximumEncodedBytes,
                    destination,
                    ref count,
                    ref encodedBytes))
            {
                _fairRetryAfterSequence = _entries[index].Command.Sequence.Value;
                return true;
            }
        }
        return false;
    }

    private bool TryAddTransition(
        ReadOnlySpan<MovementTransitionIntent> candidates,
        IOwnerInputBatchEncodedSizer sizer,
        int maximumEncodedBytes,
        Span<MovementTransitionIntent> destination,
        ref int count,
        ref int encodedBytes)
    {
        if (count >= candidates.Length ||
            count == _policy.MaximumTransitionsPerBatch)
        {
            return false;
        }
        var bytes = RequirePositive(
            sizer.MeasureTransitionEntryBytes(candidates[count]),
            "transition entry");
        if (!Fits(encodedBytes, bytes, maximumEncodedBytes))
        {
            return false;
        }
        destination[count] = candidates[count];
        count++;
        encodedBytes += bytes;
        return true;
    }

    private bool TryAddAction(
        ReadOnlySpan<PredictedActionIntent> candidates,
        IOwnerInputBatchEncodedSizer sizer,
        int maximumEncodedBytes,
        Span<PredictedActionIntent> destination,
        ref int count,
        ref int encodedBytes)
    {
        if (count >= candidates.Length || count == _policy.MaximumActionsPerBatch)
        {
            return false;
        }
        var bytes = RequirePositive(
            sizer.MeasureActionEntryBytes(candidates[count]),
            "action entry");
        if (!Fits(encodedBytes, bytes, maximumEncodedBytes))
        {
            return false;
        }
        destination[count] = candidates[count];
        count++;
        encodedBytes += bytes;
        return true;
    }

    private void AddOptionalTransitions(
        ReadOnlySpan<MovementTransitionIntent> candidates,
        IOwnerInputBatchEncodedSizer sizer,
        int maximumEncodedBytes,
        Span<MovementTransitionIntent> destination,
        ref int count,
        ref int encodedBytes)
    {
        while (TryAddTransition(
                   candidates,
                   sizer,
                   maximumEncodedBytes,
                   destination,
                   ref count,
                   ref encodedBytes))
        {
        }
    }

    private void AddOptionalActions(
        ReadOnlySpan<PredictedActionIntent> candidates,
        IOwnerInputBatchEncodedSizer sizer,
        int maximumEncodedBytes,
        Span<PredictedActionIntent> destination,
        ref int count,
        ref int encodedBytes)
    {
        while (TryAddAction(
                   candidates,
                   sizer,
                   maximumEncodedBytes,
                   destination,
                   ref count,
                   ref encodedBytes))
        {
        }
    }

    private void AddOptionalCommands(
        OptionalCommandPriority priority,
        IOwnerInputBatchEncodedSizer sizer,
        int maximumEncodedBytes,
        Span<OwnerSimulationCommand> destination,
        ref int count,
        ref int encodedBytes)
    {
        if (count == _policy.MaximumCommandsPerBatch)
        {
            return;
        }
        var iterations = _count;
        var useFairOrder = priority == OptionalCommandPriority.Any;
        var startIndex = useFairOrder ? FindFairStartIndex() : 0;
        for (var offset = 0;
             offset < iterations && count < _policy.MaximumCommandsPerBatch;
             offset++)
        {
            var index = useFairOrder && _count > 0
                ? (startIndex + offset) % _count
                : offset;
            if (_selected[index] || !MatchesPriority(_entries[index], priority))
            {
                continue;
            }
            if (TryAddCommand(
                    index,
                    sizer,
                    maximumEncodedBytes,
                    destination,
                    ref count,
                    ref encodedBytes) && useFairOrder)
            {
                _fairRetryAfterSequence = _entries[index].Command.Sequence.Value;
            }
        }
    }

    private bool MatchesPriority(Entry entry, OptionalCommandPriority priority) =>
        priority switch
        {
            OptionalCommandPriority.New => entry.TransmissionCount == 0,
            OptionalCommandPriority.SelectiveGap =>
                IsSelectiveGap(entry.Command.Sequence),
            OptionalCommandPriority.DiscreteReference =>
                entry.Command.Input.TransitionReferences.Count > 0 ||
                entry.Command.Input.ActionReferences.Count > 0,
            OptionalCommandPriority.Overdue =>
                entry.DeferralCount >= (uint)_policy.MaximumPriorityDeferrals,
            OptionalCommandPriority.Any => true,
            _ => throw new ArgumentOutOfRangeException(nameof(priority)),
        };

    private int FindFairStartIndex()
    {
        for (var index = 0; index < _count; index++)
        {
            if (_entries[index].Command.Sequence.Value > _fairRetryAfterSequence)
            {
                return index;
            }
        }
        return 0;
    }

    private void Register(OwnerSimulationCommand command)
    {
        _lastRegisteredSequence = command.Sequence.Value;
        _lastRegisteredTargetFrame = command.TargetFrame.Tick;
        _hasRegisteredCommand = true;
    }

    private bool ReceiveEvidenceIsOriginated(OwnerInputReceiveAcknowledgement receive)
    {
        if (!_hasRegisteredCommand)
        {
            return receive.HighestContiguousSequence is null &&
                receive.FollowingReceivedMask == 0;
        }
        var cursor = receive.HighestContiguousSequence?.Value ?? 0;
        if (cursor > _lastRegisteredSequence)
        {
            return false;
        }
        if (receive.FollowingReceivedMask == 0)
        {
            return true;
        }
        var highestBit = 63 - BitOperations.LeadingZeroCount(
            receive.FollowingReceivedMask);
        var distance = checked((ulong)highestBit + 1);
        return cursor <= ulong.MaxValue - distance &&
            cursor + distance <= _lastRegisteredSequence;
    }

    private void MergeReceiveEvidence(OwnerInputReceiveAcknowledgement incoming)
    {
        var incomingCursor = incoming.HighestContiguousSequence?.Value ?? 0;
        var mergedCursor = Math.Max(_highestReceivedSequence, incomingCursor);
        ulong mergedMask = 0;
        for (var distance = 1; distance <= 64; distance++)
        {
            if (mergedCursor > ulong.MaxValue - (ulong)distance)
            {
                break;
            }
            var sequence = mergedCursor + (ulong)distance;
            if (Includes(_highestReceivedSequence, _followingReceivedMask, sequence) ||
                Includes(incomingCursor, incoming.FollowingReceivedMask, sequence))
            {
                mergedMask |= 1UL << (distance - 1);
            }
        }
        while ((mergedMask & 1UL) != 0 && mergedCursor != ulong.MaxValue)
        {
            mergedCursor++;
            mergedMask >>= 1;
        }
        _highestReceivedSequence = mergedCursor;
        _followingReceivedMask = mergedMask;
    }

    private void PruneTerminalEntries()
    {
        for (var index = _count - 1; index >= 0; index--)
        {
            ref readonly var command = ref _entries[index].Command;
            if ((_consumedThroughFrame is { } consumed &&
                 command.TargetFrame <= consumed) ||
                Includes(
                    _highestReceivedSequence,
                    _followingReceivedMask,
                    command.Sequence.Value))
            {
                RemoveAt(index);
            }
        }
    }

    private bool IsSelectiveGap(InputSequence sequence)
    {
        if (Includes(_highestReceivedSequence, _followingReceivedMask, sequence.Value))
        {
            return false;
        }
        var cursor = _highestReceivedSequence;
        for (var distance = 64; distance >= 1; distance--)
        {
            if ((_followingReceivedMask & (1UL << (distance - 1))) != 0)
            {
                if (cursor > ulong.MaxValue - (ulong)distance)
                {
                    return false;
                }
                return sequence.Value < cursor + (ulong)distance;
            }
        }
        return false;
    }

    private void ValidateCandidates(
        ReadOnlySpan<MovementTransitionIntent> transitions,
        ReadOnlySpan<PredictedActionIntent> actions)
    {
        if (transitions.Length > _policy.MaximumTransitionsPerBatch)
        {
            throw new ArgumentOutOfRangeException(nameof(transitions));
        }
        if (actions.Length > _policy.MaximumActionsPerBatch)
        {
            throw new ArgumentOutOfRangeException(nameof(actions));
        }
        for (var index = 0; index < transitions.Length; index++)
        {
            if (!transitions[index].IsValid || transitions[index].Identity.Scope != _scope ||
                ContainsEarlierTransition(transitions, index))
            {
                throw new ArgumentException(
                    "Transition candidates must be current-scope and unique.",
                    nameof(transitions));
            }
        }
        for (var index = 0; index < actions.Length; index++)
        {
            if (!actions[index].IsValid || actions[index].Identity.Scope != _scope ||
                ContainsEarlierAction(actions, index))
            {
                throw new ArgumentException(
                    "Action candidates must be current-scope and unique.",
                    nameof(actions));
            }
        }
    }

    private static bool ContainsEarlierTransition(
        ReadOnlySpan<MovementTransitionIntent> transitions,
        int index)
    {
        for (var earlier = 0; earlier < index; earlier++)
        {
            if (transitions[earlier].Identity == transitions[index].Identity)
            {
                return true;
            }
        }
        return false;
    }

    private static bool ContainsEarlierAction(
        ReadOnlySpan<PredictedActionIntent> actions,
        int index)
    {
        for (var earlier = 0; earlier < index; earlier++)
        {
            if (actions[earlier].Identity == actions[index].Identity)
            {
                return true;
            }
        }
        return false;
    }

    private void RemoveAt(int index)
    {
        if (index < _count - 1)
        {
            Array.Copy(_entries, index + 1, _entries, index, _count - index - 1);
        }
        _entries[--_count] = default;
    }

    private void ValidateBuildArguments(
        in OwnerInputSendBatchHeader header,
        int maximumEncodedBytes,
        Span<OwnerSimulationCommand> commandDestination,
        Span<MovementTransitionIntent> transitionDestination,
        Span<PredictedActionIntent> actionDestination)
    {
        if (!header.IsValid || header.Scope != _scope)
        {
            throw new ArgumentOutOfRangeException(nameof(header));
        }
        if (maximumEncodedBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumEncodedBytes));
        }
        if (commandDestination.Length < _policy.MaximumCommandsPerBatch ||
            transitionDestination.Length < _policy.MaximumTransitionsPerBatch ||
            actionDestination.Length < _policy.MaximumActionsPerBatch)
        {
            throw new ArgumentException(
                "Destinations must cover the configured per-batch maxima.");
        }
    }

    private static bool Includes(ulong cursor, ulong mask, ulong sequence)
    {
        if (sequence <= cursor)
        {
            return true;
        }
        var distance = sequence - cursor;
        return distance <= 64 && (mask & (1UL << checked((int)distance - 1))) != 0;
    }

    private static int RequireNonNegative(int bytes, string component)
    {
        if (bytes < 0)
        {
            throw new InvalidOperationException(
                $"The encoded sizer returned a negative {component} size.");
        }
        return bytes;
    }

    private static int RequirePositive(int bytes, string component)
    {
        if (bytes <= 0)
        {
            throw new InvalidOperationException(
                $"The encoded sizer returned a non-positive {component} size.");
        }
        return bytes;
    }

    private static bool Fits(int current, int addition, int maximum) =>
        addition <= maximum - current;

    private static uint NextDeferralCount(bool deferred, uint current) =>
        !deferred ? 0 : current == uint.MaxValue ? uint.MaxValue : current + 1;

    private struct Entry
    {
        public Entry(OwnerSimulationCommand command)
        {
            Command = command;
        }

        public OwnerSimulationCommand Command;
        public uint TransmissionCount;
        public uint DeferralCount;
    }

    private enum OptionalCommandPriority : byte
    {
        New = 1,
        SelectiveGap = 2,
        DiscreteReference = 3,
        Overdue = 4,
        Any = 5,
    }

    private enum UrgentServiceKind : byte
    {
        Command = 0,
        Transition = 1,
        Action = 2,
    }
}
