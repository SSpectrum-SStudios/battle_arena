using BattleArena.Core.Common;
using BattleArena.Core.Movement.Simulation;

namespace BattleArena.Multiplayer.OwnerPrediction;

public enum OwnerCommandStepCount : byte
{
    Zero = 0,
    One = 1,
    Two = 2,
}

public enum OwnerCommandBuildDecision : byte
{
    CapturedWithoutSimulation = 1,
    Built = 2,
    BuiltTimelineExhausted = 3,
    TimelineExhausted = 4,
    IntentOriginContractFault = 5,
    IntentJournalBaselineRepairRequired = 6,
}

public readonly record struct OwnerCommandBuildResult(
    OwnerCommandBuildDecision Decision,
    int CommandsWritten)
{
    public bool IsBuilt => Decision is
        OwnerCommandBuildDecision.Built or
        OwnerCommandBuildDecision.BuiltTimelineExhausted;
}

public enum OwnerIntentQueueDecision : byte
{
    Added = 1,
    PendingReferenceCapacityExceeded = 2,
    JournalCapacityExceeded = 3,
    IdentityExhausted = 4,
    BaselineRepairRequired = 5,
    CommandTimelineExhausted = 6,
    IntentOriginContractFault = 7,
}

/// <summary>
/// Immutable activation seed exported by the canonical bootstrap coordinator.
/// Its internal constructor prevents adapters from independently composing
/// lifecycle identity and target-frame state.
/// </summary>
public readonly record struct OwnerCommandTimelineSeed
{
    internal OwnerCommandTimelineSeed(
        CombatantAuthorityPredictionEpoch authorityEpoch,
        InputSequence firstSequence,
        SimulationInstant firstTargetFrame)
    {
        if (!authorityEpoch.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(authorityEpoch));
        }
        if (!firstSequence.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(firstSequence));
        }
        AuthorityEpoch = authorityEpoch;
        FirstSequence = firstSequence;
        FirstTargetFrame = firstTargetFrame;
    }

    public CombatantAuthorityPredictionEpoch AuthorityEpoch { get; }
    public OwnerIntentScope Scope => OwnerIntentScope.From(AuthorityEpoch);
    public InputSequence FirstSequence { get; }
    public SimulationInstant FirstTargetFrame { get; }
    public bool IsValid => AuthorityEpoch.IsValid && FirstSequence.IsValid;
}

/// <summary>
/// Newest complete continuous/held input sampled for one physics callback.
/// Discrete edges are durable journal entries and are intentionally separate.
/// </summary>
public readonly record struct OwnerCommandInputSample
{
    public OwnerCommandInputSample(
        MovementAxes movement,
        ViewOrientation view,
        MovementHeldState movementHeld,
        CombatInputState combatInput,
        MovementConfigurationRevision movementRevision,
        MovementCapabilityRevision capabilityRevision)
    {
        if (!movementRevision.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(movementRevision));
        }
        if (!capabilityRevision.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(capabilityRevision));
        }

        Movement = movement;
        View = view;
        MovementHeld = movementHeld;
        CombatInput = combatInput;
        MovementRevision = movementRevision;
        CapabilityRevision = capabilityRevision;
    }

    public MovementAxes Movement { get; }
    public ViewOrientation View { get; }
    public MovementHeldState MovementHeld { get; }
    public CombatInputState CombatInput { get; }
    public MovementConfigurationRevision MovementRevision { get; }
    public MovementCapabilityRevision CapabilityRevision { get; }
    public bool IsValid => MovementRevision.IsValid && CapabilityRevision.IsValid;

    public CharacterSimulationInput ToSimulationInput(
        TransitionReferenceBuffer transitionReferences,
        ActionReferenceBuffer actionReferences) => new(
            Movement,
            View,
            MovementHeld,
            transitionReferences,
            CombatInput,
            actionReferences,
            MovementRevision,
            CapabilityRevision);
}

/// <summary>
/// Narrow origin port used by command construction. The adapter keeps the
/// builder independent of journal storage/resolution/transmission policy. A
/// non-Added decision must return a default intent; hidden collaborator-side
/// mutation is outside the observable contract and is a port implementation bug.
/// </summary>
public interface IOwnerCommandIntentOrigin
{
    OwnerIntentScope Scope { get; }
    bool RequiresBaselineRepair { get; }

    MovementTransitionOriginDecision TryOriginateTransition(
        OwnerInputIdentity originatingInput,
        MovementTransitionKind kind,
        SimulationInstant firstPredictedFrame,
        SimulationInstant lastValidFrame,
        out MovementTransitionIntent intent);

    OwnerActionOriginDecision TryOriginateAction(
        OwnerInputIdentity originatingInput,
        OwnerActionTrigger trigger,
        SimulationInstant predictedStartFrame,
        SimulationInstant lastValidStartFrame,
        SimulationInstant renderedAuthorityFrame,
        out PredictedActionIntent intent);
}

public sealed class OwnerCommandIntentJournalAdapter : IOwnerCommandIntentOrigin
{
    private readonly MovementTransitionJournal _transitions;
    private readonly OwnerActionCommandJournal _actions;

    public OwnerCommandIntentJournalAdapter(
        MovementTransitionJournal transitions,
        OwnerActionCommandJournal actions)
    {
        ArgumentNullException.ThrowIfNull(transitions);
        ArgumentNullException.ThrowIfNull(actions);
        if (transitions.Scope != actions.Scope)
        {
            throw new ArgumentException(
                "Movement and action journals must share one owner-intent scope.");
        }
        _transitions = transitions;
        _actions = actions;
    }

    public OwnerIntentScope Scope => _transitions.Scope == _actions.Scope
        ? _transitions.Scope
        : throw new InvalidOperationException(
            "Movement and action journal scopes diverged.");
    public bool RequiresBaselineRepair =>
        _transitions.RequiresBaselineRepair || _actions.RequiresBaselineRepair;

    public MovementTransitionOriginDecision TryOriginateTransition(
        OwnerInputIdentity originatingInput,
        MovementTransitionKind kind,
        SimulationInstant firstPredictedFrame,
        SimulationInstant lastValidFrame,
        out MovementTransitionIntent intent) => _transitions.TryOriginate(
            originatingInput,
            kind,
            firstPredictedFrame,
            lastValidFrame,
            out intent);

    public OwnerActionOriginDecision TryOriginateAction(
        OwnerInputIdentity originatingInput,
        OwnerActionTrigger trigger,
        SimulationInstant predictedStartFrame,
        SimulationInstant lastValidStartFrame,
        SimulationInstant renderedAuthorityFrame,
        out PredictedActionIntent intent) => _actions.TryOriginate(
            originatingInput,
            trigger,
            predictedStartFrame,
            lastValidStartFrame,
            renderedAuthorityFrame,
            out intent);
}

/// <summary>
/// Allocation-free pacing boundary for one owner. Zero-step callbacks update
/// the sample without consuming identity/frame state. A two-step callback uses
/// one sample for both commands and assigns all accumulated edges only to the
/// first command.
/// </summary>
public sealed class OwnerCommandStepBuilder
{
    private readonly IOwnerCommandIntentOrigin _intentOrigin;
    private readonly AuthorityDiscontinuityId _authorityDiscontinuity;
    private readonly MatchFrameEpochId _matchFrameEpoch;
    private readonly MovementTransitionId[] _pendingTransitions =
        new MovementTransitionId[OwnerSimulationLimits.MaximumTransitionReferences];
    private readonly PredictedActionId[] _pendingActions =
        new PredictedActionId[OwnerSimulationLimits.MaximumActionReferences];
    private OwnerIntentScope _scope;
    private InputSequence _nextSequence;
    private SimulationInstant _nextTargetFrame;
    private OwnerCommandInputSample _latestSample;
    private int _pendingTransitionCount;
    private int _pendingActionCount;
    private bool _timelineExhausted;
    private bool _intentOriginContractFault;
    private bool _intentJournalBaselineRepairRequired;
    private ulong _lastTransitionId;
    private ulong _lastActionId;

    public OwnerCommandStepBuilder(
        OwnerCommandTimelineSeed seed,
        IOwnerCommandIntentOrigin intentOrigin)
    {
        if (!seed.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(seed));
        }
        ArgumentNullException.ThrowIfNull(intentOrigin);
        if (intentOrigin.Scope != seed.Scope)
        {
            throw new ArgumentException(
                "The intent origin must belong to the builder scope.",
                nameof(intentOrigin));
        }

        _scope = seed.Scope;
        _authorityDiscontinuity = seed.AuthorityEpoch.AuthorityDiscontinuity;
        _matchFrameEpoch = seed.AuthorityEpoch.MatchFrameEpoch;
        _nextSequence = seed.FirstSequence;
        _nextTargetFrame = seed.FirstTargetFrame;
        _intentOrigin = intentOrigin;
    }

    public OwnerIntentScope Scope => _scope;
    public AuthorityDiscontinuityId AuthorityDiscontinuity =>
        _authorityDiscontinuity;
    public MatchFrameEpochId MatchFrameEpoch => _matchFrameEpoch;
    public OwnerInputIdentity? NextInputIdentity => _timelineExhausted
        ? null
        : new OwnerInputIdentity(_scope, _nextSequence);
    public SimulationInstant? NextTargetFrame => _timelineExhausted
        ? null
        : _nextTargetFrame;
    public int PendingTransitionCount => _pendingTransitionCount;
    public int PendingActionCount => _pendingActionCount;
    public bool RequiresTimelineRebase =>
        _timelineExhausted ||
        _intentOriginContractFault ||
        _intentJournalBaselineRepairRequired;

    public OwnerIntentQueueDecision QueueTransition(
        MovementTransitionKind kind,
        SimulationDuration validity)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        if (_intentOriginContractFault)
        {
            return OwnerIntentQueueDecision.IntentOriginContractFault;
        }
        if (_intentJournalBaselineRepairRequired)
        {
            return OwnerIntentQueueDecision.BaselineRepairRequired;
        }
        if (!IntentOriginScopeIsCurrent())
        {
            return OwnerIntentQueueDecision.IntentOriginContractFault;
        }
        if (IntentOriginNeedsBaselineRepair())
        {
            return OwnerIntentQueueDecision.BaselineRepairRequired;
        }
        if (_intentOriginContractFault)
        {
            return OwnerIntentQueueDecision.IntentOriginContractFault;
        }
        if (_timelineExhausted ||
            _nextTargetFrame.Tick > long.MaxValue - validity.Ticks)
        {
            return OwnerIntentQueueDecision.CommandTimelineExhausted;
        }
        if (_pendingTransitionCount == _pendingTransitions.Length)
        {
            return OwnerIntentQueueDecision.PendingReferenceCapacityExceeded;
        }

        var identity = new OwnerInputIdentity(_scope, _nextSequence);
        var lastValidFrame = new SimulationInstant(
            _nextTargetFrame.Tick + validity.Ticks);
        MovementTransitionOriginDecision result;
        MovementTransitionIntent intent;
        try
        {
            result = _intentOrigin.TryOriginateTransition(
                identity,
                kind,
                _nextTargetFrame,
                lastValidFrame,
                out intent);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _intentOriginContractFault = true;
            return OwnerIntentQueueDecision.IntentOriginContractFault;
        }
        if (result != MovementTransitionOriginDecision.Added)
        {
            if (intent != default)
            {
                _intentOriginContractFault = true;
                return OwnerIntentQueueDecision.IntentOriginContractFault;
            }
            var mapped = Map(result);
            ObserveOriginFailure(mapped);
            return mapped;
        }
        if (!intent.IsValid ||
            intent.Identity.Scope != _scope ||
            intent.Identity.Id.Value <= _lastTransitionId ||
            intent.OriginatingInput != identity ||
            intent.Kind != kind ||
            intent.FirstPredictedFrame != _nextTargetFrame ||
            intent.LastValidFrame != lastValidFrame)
        {
            _intentOriginContractFault = true;
            return OwnerIntentQueueDecision.IntentOriginContractFault;
        }
        _lastTransitionId = intent.Identity.Id.Value;
        _pendingTransitions[_pendingTransitionCount++] = intent.Identity.Id;
        return OwnerIntentQueueDecision.Added;
    }

    public OwnerIntentQueueDecision QueueAction(
        OwnerActionTrigger trigger,
        SimulationDuration validity,
        SimulationInstant renderedAuthorityFrame)
    {
        if (!Enum.IsDefined(trigger))
        {
            throw new ArgumentOutOfRangeException(nameof(trigger));
        }
        if (_intentOriginContractFault)
        {
            return OwnerIntentQueueDecision.IntentOriginContractFault;
        }
        if (_intentJournalBaselineRepairRequired)
        {
            return OwnerIntentQueueDecision.BaselineRepairRequired;
        }
        if (!IntentOriginScopeIsCurrent())
        {
            return OwnerIntentQueueDecision.IntentOriginContractFault;
        }
        if (IntentOriginNeedsBaselineRepair())
        {
            return OwnerIntentQueueDecision.BaselineRepairRequired;
        }
        if (_intentOriginContractFault)
        {
            return OwnerIntentQueueDecision.IntentOriginContractFault;
        }
        if (_timelineExhausted ||
            _nextTargetFrame.Tick > long.MaxValue - validity.Ticks)
        {
            return OwnerIntentQueueDecision.CommandTimelineExhausted;
        }
        if (_pendingActionCount == _pendingActions.Length)
        {
            return OwnerIntentQueueDecision.PendingReferenceCapacityExceeded;
        }

        var identity = new OwnerInputIdentity(_scope, _nextSequence);
        var lastValidFrame = new SimulationInstant(
            _nextTargetFrame.Tick + validity.Ticks);
        OwnerActionOriginDecision result;
        PredictedActionIntent intent;
        try
        {
            result = _intentOrigin.TryOriginateAction(
                identity,
                trigger,
                _nextTargetFrame,
                lastValidFrame,
                renderedAuthorityFrame,
                out intent);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _intentOriginContractFault = true;
            return OwnerIntentQueueDecision.IntentOriginContractFault;
        }
        if (result != OwnerActionOriginDecision.Added)
        {
            if (intent != default)
            {
                _intentOriginContractFault = true;
                return OwnerIntentQueueDecision.IntentOriginContractFault;
            }
            var mapped = Map(result);
            ObserveOriginFailure(mapped);
            return mapped;
        }
        if (!intent.IsValid ||
            intent.Identity.Scope != _scope ||
            intent.Identity.Id.Value <= _lastActionId ||
            intent.OriginatingInput != identity ||
            intent.Trigger != trigger ||
            intent.PredictedStartFrame != _nextTargetFrame ||
            intent.LastValidStartFrame != lastValidFrame ||
            intent.RenderedAuthorityFrame != renderedAuthorityFrame)
        {
            _intentOriginContractFault = true;
            return OwnerIntentQueueDecision.IntentOriginContractFault;
        }
        _lastActionId = intent.Identity.Id.Value;
        _pendingActions[_pendingActionCount++] = intent.Identity.Id;
        return OwnerIntentQueueDecision.Added;
    }

    public OwnerCommandBuildResult Build(
        OwnerCommandInputSample sample,
        OwnerCommandStepCount stepCount,
        Span<OwnerSimulationCommand> destination)
    {
        if (!sample.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(sample));
        }
        if (!Enum.IsDefined(stepCount))
        {
            throw new ArgumentOutOfRangeException(nameof(stepCount));
        }
        var requested = (int)stepCount;
        if (destination.Length < requested)
        {
            throw new ArgumentException(
                "The command destination is smaller than the requested step count.",
                nameof(destination));
        }
        // Capture happens even for a zero-step pacing callback.
        _latestSample = sample;
        if (requested == 0)
        {
            return new OwnerCommandBuildResult(
                OwnerCommandBuildDecision.CapturedWithoutSimulation,
                0);
        }
        if (_intentOriginContractFault)
        {
            return new OwnerCommandBuildResult(
                OwnerCommandBuildDecision.IntentOriginContractFault,
                0);
        }
        if (_intentJournalBaselineRepairRequired)
        {
            return new OwnerCommandBuildResult(
                OwnerCommandBuildDecision.IntentJournalBaselineRepairRequired,
                0);
        }
        if (!IntentOriginScopeIsCurrent())
        {
            return new OwnerCommandBuildResult(
                OwnerCommandBuildDecision.IntentOriginContractFault,
                0);
        }
        if (IntentOriginNeedsBaselineRepair())
        {
            return new OwnerCommandBuildResult(
                OwnerCommandBuildDecision.IntentJournalBaselineRepairRequired,
                0);
        }
        if (_intentOriginContractFault)
        {
            return new OwnerCommandBuildResult(
                OwnerCommandBuildDecision.IntentOriginContractFault,
                0);
        }
        if (_timelineExhausted ||
            requested == 2 &&
            (_nextSequence.Value == ulong.MaxValue ||
             _nextTargetFrame.Tick == long.MaxValue))
        {
            return new OwnerCommandBuildResult(
                OwnerCommandBuildDecision.TimelineExhausted,
                0);
        }

        var firstInput = _latestSample.ToSimulationInput(
            new TransitionReferenceBuffer(
                _pendingTransitions.AsSpan(0, _pendingTransitionCount)),
            new ActionReferenceBuffer(
                _pendingActions.AsSpan(0, _pendingActionCount)));
        destination[0] = new OwnerSimulationCommand(
            new OwnerInputIdentity(_scope, _nextSequence),
            _authorityDiscontinuity,
            _matchFrameEpoch,
            _nextTargetFrame,
            firstInput);

        if (requested == 2)
        {
            destination[1] = new OwnerSimulationCommand(
                new OwnerInputIdentity(_scope, _nextSequence.Next()),
                _authorityDiscontinuity,
                _matchFrameEpoch,
                new SimulationInstant(_nextTargetFrame.Tick + 1),
                _latestSample.ToSimulationInput(default, default));
        }

        _pendingTransitionCount = 0;
        _pendingActionCount = 0;
        var consumedFinalSequence = requested == 2
            ? _nextSequence.Value + 1
            : _nextSequence.Value;
        var consumedFinalFrame = _nextTargetFrame.Tick + requested - 1;
        if (consumedFinalSequence == ulong.MaxValue ||
            consumedFinalFrame == long.MaxValue)
        {
            _timelineExhausted = true;
            return new OwnerCommandBuildResult(
                OwnerCommandBuildDecision.BuiltTimelineExhausted,
                requested);
        }

        _nextSequence = new InputSequence(consumedFinalSequence + 1);
        _nextTargetFrame = new SimulationInstant(consumedFinalFrame + 1);
        return new OwnerCommandBuildResult(
            OwnerCommandBuildDecision.Built,
            requested);
    }

    private static OwnerIntentQueueDecision Map(
        MovementTransitionOriginDecision decision) => decision switch
        {
            MovementTransitionOriginDecision.CapacityExceeded =>
                OwnerIntentQueueDecision.JournalCapacityExceeded,
            MovementTransitionOriginDecision.IdentityExhausted =>
                OwnerIntentQueueDecision.IdentityExhausted,
            MovementTransitionOriginDecision.BaselineRepairRequired =>
                OwnerIntentQueueDecision.BaselineRepairRequired,
            _ => OwnerIntentQueueDecision.IntentOriginContractFault,
        };

    private static OwnerIntentQueueDecision Map(
        OwnerActionOriginDecision decision) => decision switch
        {
            OwnerActionOriginDecision.CapacityExceeded =>
                OwnerIntentQueueDecision.JournalCapacityExceeded,
            OwnerActionOriginDecision.IdentityExhausted =>
                OwnerIntentQueueDecision.IdentityExhausted,
            OwnerActionOriginDecision.BaselineRepairRequired =>
                OwnerIntentQueueDecision.BaselineRepairRequired,
            _ => OwnerIntentQueueDecision.IntentOriginContractFault,
        };

    private bool IntentOriginScopeIsCurrent()
    {
        try
        {
            if (_intentOrigin.Scope == _scope)
            {
                return true;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }
        _intentOriginContractFault = true;
        return false;
    }

    private void ObserveOriginFailure(OwnerIntentQueueDecision decision)
    {
        if (decision == OwnerIntentQueueDecision.IntentOriginContractFault)
        {
            _intentOriginContractFault = true;
        }
        else if (decision == OwnerIntentQueueDecision.BaselineRepairRequired)
        {
            _intentJournalBaselineRepairRequired = true;
        }
    }

    private bool IntentOriginNeedsBaselineRepair()
    {
        try
        {
            if (!_intentOrigin.RequiresBaselineRepair)
            {
                return false;
            }
            _intentJournalBaselineRepairRequired = true;
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _intentOriginContractFault = true;
            return false;
        }
    }
}
