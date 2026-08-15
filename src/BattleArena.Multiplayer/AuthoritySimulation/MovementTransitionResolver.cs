using BattleArena.Core.Common;
using BattleArena.Core.Movement.Simulation;
using BattleArena.Multiplayer.OwnerPrediction;

namespace BattleArena.Multiplayer.AuthoritySimulation;

/// <summary>
/// What the authority did with one durable intent reference on one frame.
/// </summary>
/// <remarks>
/// Every value except <see cref="AppliedOnPredictedFrame"/> and
/// <see cref="AppliedOnRemappedFrame"/> means the intent did not take effect on
/// this frame. Only the two applied values may influence simulation, which is
/// what keeps "referenced" and "applied" from collapsing into one concept.
/// </remarks>
public enum AuthorityIntentApplication : byte
{
    /// <summary>Applied on exactly the frame the owner predicted it.</summary>
    AppliedOnPredictedFrame = 1,

    /// <summary>
    /// Arrived too late for its predicted frame but still inside its authored
    /// deadline, so authority named an explicit later frame. The owner sees this
    /// as a normal historical correction rather than a silent insertion.
    /// </summary>
    AppliedOnRemappedFrame = 2,

    /// <summary>Legal to consider, refused by gameplay policy or capability.</summary>
    RefusedByPolicy = 3,

    /// <summary>Deadline passed before any frame could carry it.</summary>
    DeadlineMissed = 4,

    /// <summary>
    /// Already terminal. A resend or a later command referencing the same ID is
    /// idempotent and must never produce a second application.
    /// </summary>
    AlreadyTerminal = 5,

    /// <summary>
    /// The command referenced an ID whose intent record has not arrived. The
    /// reference is ignored and the intent stays outstanding; the client keeps
    /// advertising it until it receives a terminal result.
    /// </summary>
    IntentNotObserved = 6,

    /// <summary>
    /// Referenced before its own first predicted frame. Nothing is applied
    /// early; the intent waits for its own frame.
    /// </summary>
    NotYetDue = 7,

    /// <summary>
    /// Replaced by a newer intent of the same kind before it could take effect,
    /// for example a second jump press retiring an unresolved first one.
    /// </summary>
    SupersededByPolicy = 8,

    /// <summary>The reference does not belong to this authority scope.</summary>
    ForeignScope = 9,

    /// <summary>The journal needs an explicit baseline repair before it can decide.</summary>
    BaselineRepairRequired = 10,
}

/// <summary>One resolved intent reference, paired with its terminal result when one was produced.</summary>
public readonly record struct AuthorityIntentOutcome<TResolution>
    where TResolution : struct
{
    internal AuthorityIntentOutcome(
        AuthorityIntentApplication application,
        TResolution? resolution)
    {
        Application = application;
        Resolution = resolution;
    }

    public AuthorityIntentApplication Application { get; }

    /// <summary>
    /// The terminal result produced by THIS call. It is null when the intent was
    /// already terminal, was not observed, or is not yet due, so a caller
    /// publishing resolutions never republishes an old one by accident.
    /// </summary>
    public TResolution? Resolution { get; }

    public bool WasAppliedThisFrame => Application is
        AuthorityIntentApplication.AppliedOnPredictedFrame or
        AuthorityIntentApplication.AppliedOnRemappedFrame;
}

/// <summary>
/// What gameplay policy wants done with an intent that is inside its authored
/// window. All four terminal categories the design names are reachable from
/// here: admitting yields accepted or remapped, and the other two are explicit.
/// </summary>
public enum DurableIntentAdmissionDecision : byte
{
    Admit = 1,
    Reject = 2,
    Supersede = 3,
}

/// <summary>
/// Gameplay admission for a durable movement transition. Capability and state
/// rules live behind this seam because the explicit motor that owns them is
/// Phase 5 work; the scheduling contract is complete without them.
/// </summary>
public interface IMovementTransitionAdmissionPolicy
{
    DurableIntentAdmissionDecision Evaluate(
        MovementTransitionIntent intent,
        SimulationInstant applicationFrame,
        out MovementTransitionRejectionReason rejectionReason);
}

/// <summary>Gameplay admission for a predicted action.</summary>
public interface IPredictedActionAdmissionPolicy
{
    DurableIntentAdmissionDecision Evaluate(
        PredictedActionIntent intent,
        SimulationInstant startFrame,
        out PredictedActionRejectionReason rejectionReason);
}

/// <summary>
/// Allocates the authority-owned execution identity attached to an accepted
/// action. It is deliberately separate from the client's correlation ID so the
/// two can never accidentally coincide, which they previously did.
/// </summary>
public interface IAuthorityActionExecutionAllocator
{
    /// <returns>
    /// False when identities are exhausted. Exhaustion is a repair condition, not
    /// an exception: throwing here would abort a frame the scheduler has already
    /// committed.
    /// </returns>
    bool TryAllocate(
        PredictedActionIntent intent,
        SimulationInstant startFrame,
        out AuthorityActionExecutionIdentity identity);
}

/// <summary>Admits everything. Used until Phase 5 supplies real capability state.</summary>
public sealed class PermissiveMovementTransitionAdmissionPolicy
    : IMovementTransitionAdmissionPolicy
{
    public static PermissiveMovementTransitionAdmissionPolicy Instance { get; } = new();

    public DurableIntentAdmissionDecision Evaluate(
        MovementTransitionIntent intent,
        SimulationInstant applicationFrame,
        out MovementTransitionRejectionReason rejectionReason)
    {
        rejectionReason = MovementTransitionRejectionReason.None;
        return DurableIntentAdmissionDecision.Admit;
    }
}

/// <summary>Admits everything. Used until Phase 8 supplies real action policy.</summary>
public sealed class PermissivePredictedActionAdmissionPolicy
    : IPredictedActionAdmissionPolicy
{
    public static PermissivePredictedActionAdmissionPolicy Instance { get; } = new();

    public DurableIntentAdmissionDecision Evaluate(
        PredictedActionIntent intent,
        SimulationInstant startFrame,
        out PredictedActionRejectionReason rejectionReason)
    {
        rejectionReason = PredictedActionRejectionReason.None;
        return DurableIntentAdmissionDecision.Admit;
    }
}

/// <summary>Monotonic per-life execution identity allocator.</summary>
public sealed class MonotonicAuthorityActionExecutionAllocator
    : IAuthorityActionExecutionAllocator
{
    private ulong _next;

    public MonotonicAuthorityActionExecutionAllocator(ulong firstId = 1)
    {
        if (firstId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(firstId));
        }

        _next = firstId;
    }

    public bool TryAllocate(
        PredictedActionIntent intent,
        SimulationInstant startFrame,
        out AuthorityActionExecutionIdentity identity)
    {
        identity = default;
        if (!intent.IsValid || _next == 0)
        {
            return false;
        }

        identity = new AuthorityActionExecutionIdentity(
            new AuthorityActionExecutionScope(
                intent.Identity.Scope.SessionId,
                intent.Identity.Scope.Life),
            new AuthorityActionExecutionId(_next));
        _next = _next == ulong.MaxValue ? 0 : _next + 1;
        return true;
    }
}

/// <summary>
/// Decides, for one authority frame, whether a referenced movement transition
/// takes effect on that frame.
/// </summary>
/// <remarks>
/// The decision is purely a function of the intent's own authored window and the
/// frame being simulated, so it is identical on every authority and reproducible
/// during owner replay:
/// <list type="bullet">
/// <item>frame before the predicted frame: nothing happens yet;</item>
/// <item>frame equal to the predicted frame: accepted, applied there;</item>
/// <item>frame after the predicted frame but within the deadline: remapped to an
/// explicitly named frame, never silently inserted into an already simulated
/// one;</item>
/// <item>frame past the deadline: expired.</item>
/// </list>
/// Resolution is delegated to the journal, which owns exact-once semantics, so a
/// duplicate reference can only ever return the existing terminal result.
/// </remarks>
public sealed class MovementTransitionResolver
{
    private readonly IMovementTransitionAdmissionPolicy _admissionPolicy;

    public MovementTransitionResolver()
        : this(PermissiveMovementTransitionAdmissionPolicy.Instance)
    {
    }

    public MovementTransitionResolver(IMovementTransitionAdmissionPolicy admissionPolicy)
    {
        ArgumentNullException.ThrowIfNull(admissionPolicy);
        _admissionPolicy = admissionPolicy;
    }

    public AuthorityIntentOutcome<MovementTransitionResolution> Resolve(
        AuthorityMovementTransitionJournal journal,
        MovementTransitionId id,
        SimulationInstant frame)
    {
        ArgumentNullException.ThrowIfNull(journal);
        if (journal.RequiresBaselineRepair)
        {
            return new(AuthorityIntentApplication.BaselineRepairRequired, null);
        }
        if (!id.IsValid)
        {
            return new(AuthorityIntentApplication.ForeignScope, null);
        }

        var identity = new MovementTransitionIdentity(journal.Scope, id);
        if (!journal.TryGetIntent(identity, out var intent, out var existing))
        {
            return new(AuthorityIntentApplication.IntentNotObserved, null);
        }

        if (existing is not null)
        {
            return new(AuthorityIntentApplication.AlreadyTerminal, null);
        }

        if (frame < intent.FirstPredictedFrame)
        {
            return new(AuthorityIntentApplication.NotYetDue, null);
        }

        if (frame > intent.LastValidFrame)
        {
            var expired = journal.Resolve(
                identity,
                MovementTransitionOutcome.Expired,
                default,
                MovementTransitionRejectionReason.DeadlineExpired,
                frame,
                out var expiredResolution);
            return Outcome(
                expired,
                AuthorityIntentApplication.DeadlineMissed,
                expiredResolution);
        }

        var admission = _admissionPolicy.Evaluate(intent, frame, out var rejectionReason);
        if (admission == DurableIntentAdmissionDecision.Supersede)
        {
            var superseded = journal.Resolve(
                identity,
                MovementTransitionOutcome.Superseded,
                default,
                MovementTransitionRejectionReason.Superseded,
                frame,
                out var supersededResolution);
            return Outcome(
                superseded,
                AuthorityIntentApplication.SupersededByPolicy,
                supersededResolution);
        }

        if (admission != DurableIntentAdmissionDecision.Admit)
        {
            // A policy that returns a reserved or undefined reason is buggy, but
            // this frame is already committed. Substituting the generic policy
            // refusal keeps the intent terminal instead of aborting a frame
            // mid-resolution. `Enum.IsDefined` matters as much as the named
            // reserved values: an undefined byte would otherwise reach the
            // journal's validation and throw from inside the sink.
            if (!Enum.IsDefined(rejectionReason) ||
                rejectionReason is MovementTransitionRejectionReason.None or
                MovementTransitionRejectionReason.DeadlineExpired or
                MovementTransitionRejectionReason.Superseded)
            {
                rejectionReason = MovementTransitionRejectionReason.AuthorityPolicyRejected;
            }

            var refused = journal.Resolve(
                identity,
                MovementTransitionOutcome.Rejected,
                default,
                rejectionReason,
                frame,
                out var refusedResolution);
            return Outcome(
                refused,
                AuthorityIntentApplication.RefusedByPolicy,
                refusedResolution);
        }

        var onPredictedFrame = frame == intent.FirstPredictedFrame;
        var decision = journal.Resolve(
            identity,
            onPredictedFrame
                ? MovementTransitionOutcome.Accepted
                : MovementTransitionOutcome.Remapped,
            frame,
            MovementTransitionRejectionReason.None,
            frame,
            out var resolution);

        return Outcome(
            decision,
            onPredictedFrame
                ? AuthorityIntentApplication.AppliedOnPredictedFrame
                : AuthorityIntentApplication.AppliedOnRemappedFrame,
            resolution);
    }

    private static AuthorityIntentOutcome<MovementTransitionResolution> Outcome(
        AuthorityTransitionResolveDecision decision,
        AuthorityIntentApplication application,
        MovementTransitionResolution resolution) => decision switch
        {
            AuthorityTransitionResolveDecision.Resolved =>
                new(application, resolution),
            AuthorityTransitionResolveDecision.AlreadyResolved or
            AuthorityTransitionResolveDecision.ConflictingResolution =>
                new(AuthorityIntentApplication.AlreadyTerminal, null),
            AuthorityTransitionResolveDecision.UnknownTransition =>
                new(AuthorityIntentApplication.IntentNotObserved, null),
            AuthorityTransitionResolveDecision.WrongScope =>
                new(AuthorityIntentApplication.ForeignScope, null),
            _ => new(AuthorityIntentApplication.BaselineRepairRequired, null),
        };
}

/// <summary>
/// The action counterpart of <see cref="MovementTransitionResolver"/>. It shares
/// the predicted/remapped/expired frame rule so movement and action intents
/// cannot drift apart in how they interpret one match frame.
/// </summary>
/// <remarks>
/// This resolver correlates and schedules only. It never decides a hit, damage,
/// or effect: an accepted action yields an authority execution identity and a
/// start frame, and everything downstream of that remains authority combat work.
/// </remarks>
public sealed class PredictedActionResolver
{
    private readonly IPredictedActionAdmissionPolicy _admissionPolicy;
    private readonly IAuthorityActionExecutionAllocator _executionAllocator;

    public PredictedActionResolver(IAuthorityActionExecutionAllocator executionAllocator)
        : this(PermissivePredictedActionAdmissionPolicy.Instance, executionAllocator)
    {
    }

    public PredictedActionResolver(
        IPredictedActionAdmissionPolicy admissionPolicy,
        IAuthorityActionExecutionAllocator executionAllocator)
    {
        ArgumentNullException.ThrowIfNull(admissionPolicy);
        ArgumentNullException.ThrowIfNull(executionAllocator);
        _admissionPolicy = admissionPolicy;
        _executionAllocator = executionAllocator;
    }

    public AuthorityIntentOutcome<PredictedActionResolution> Resolve(
        AuthorityOwnerActionCommandJournal journal,
        PredictedActionId id,
        SimulationInstant frame)
    {
        ArgumentNullException.ThrowIfNull(journal);
        if (journal.RequiresBaselineRepair)
        {
            return new(AuthorityIntentApplication.BaselineRepairRequired, null);
        }
        if (!id.IsValid)
        {
            return new(AuthorityIntentApplication.ForeignScope, null);
        }

        var identity = new PredictedActionIdentity(journal.Scope, id);
        if (!journal.TryGetIntent(identity, out var intent, out var existing))
        {
            return new(AuthorityIntentApplication.IntentNotObserved, null);
        }

        if (existing is not null)
        {
            return new(AuthorityIntentApplication.AlreadyTerminal, null);
        }

        if (frame < intent.PredictedStartFrame)
        {
            return new(AuthorityIntentApplication.NotYetDue, null);
        }

        if (frame > intent.LastValidStartFrame)
        {
            var expired = journal.Resolve(
                identity,
                PredictedActionOutcome.Expired,
                default,
                default,
                PredictedActionRejectionReason.DeadlineExpired,
                frame,
                out var expiredResolution);
            return Outcome(
                expired,
                AuthorityIntentApplication.DeadlineMissed,
                expiredResolution);
        }

        var admission = _admissionPolicy.Evaluate(intent, frame, out var rejectionReason);
        if (admission == DurableIntentAdmissionDecision.Supersede)
        {
            var superseded = journal.Resolve(
                identity,
                PredictedActionOutcome.Superseded,
                default,
                default,
                PredictedActionRejectionReason.Superseded,
                frame,
                out var supersededResolution);
            return Outcome(
                superseded,
                AuthorityIntentApplication.SupersededByPolicy,
                supersededResolution);
        }

        if (admission != DurableIntentAdmissionDecision.Admit)
        {
            // Same substitution rule as the transition resolver, including the
            // undefined-value guard.
            if (!Enum.IsDefined(rejectionReason) ||
                rejectionReason is PredictedActionRejectionReason.None or
                PredictedActionRejectionReason.DeadlineExpired or
                PredictedActionRejectionReason.Superseded)
            {
                rejectionReason = PredictedActionRejectionReason.AuthorityPolicyRejected;
            }

            var refused = journal.Resolve(
                identity,
                PredictedActionOutcome.Rejected,
                default,
                default,
                rejectionReason,
                frame,
                out var refusedResolution);
            return Outcome(
                refused,
                AuthorityIntentApplication.RefusedByPolicy,
                refusedResolution);
        }

        var onPredictedFrame = frame == intent.PredictedStartFrame;

        // The allocator is an injected seam like the admission policy, so its
        // output is validated here rather than trusted. An identity that is
        // invalid or belongs to another session/life would throw inside the
        // journal, aborting a frame the scheduler has already committed.
        var expectedExecutionScope = new AuthorityActionExecutionScope(
            intent.Identity.Scope.SessionId,
            intent.Identity.Scope.Life);
        if (!_executionAllocator.TryAllocate(intent, frame, out var execution) ||
            !execution.IsValid ||
            execution.Scope != expectedExecutionScope)
        {
            return new(AuthorityIntentApplication.BaselineRepairRequired, null);
        }
        var decision = journal.Resolve(
            identity,
            onPredictedFrame
                ? PredictedActionOutcome.Accepted
                : PredictedActionOutcome.Remapped,
            execution,
            frame,
            PredictedActionRejectionReason.None,
            frame,
            out var resolution);

        return Outcome(
            decision,
            onPredictedFrame
                ? AuthorityIntentApplication.AppliedOnPredictedFrame
                : AuthorityIntentApplication.AppliedOnRemappedFrame,
            resolution);
    }

    private static AuthorityIntentOutcome<PredictedActionResolution> Outcome(
        AuthorityActionResolveDecision decision,
        AuthorityIntentApplication application,
        PredictedActionResolution resolution) => decision switch
        {
            AuthorityActionResolveDecision.Resolved =>
                new(application, resolution),
            AuthorityActionResolveDecision.AlreadyResolved or
            AuthorityActionResolveDecision.ConflictingResolution =>
                new(AuthorityIntentApplication.AlreadyTerminal, null),
            AuthorityActionResolveDecision.UnknownAction =>
                new(AuthorityIntentApplication.IntentNotObserved, null),
            AuthorityActionResolveDecision.WrongScope =>
                new(AuthorityIntentApplication.ForeignScope, null),
            _ => new(AuthorityIntentApplication.BaselineRepairRequired, null),
        };
}

/// <summary>
/// What one authority frame did to every durable intent it touched.
/// </summary>
public readonly record struct AuthorityFrameIntentSummary
{
    internal AuthorityFrameIntentSummary(
        SimulationInstant frame,
        int transitionsApplied,
        int transitionResolutionsProduced,
        int actionsApplied,
        int actionResolutionsProduced,
        int referencesIgnored,
        bool requiresBaselineRepair)
    {
        Frame = frame;
        TransitionsApplied = transitionsApplied;
        TransitionResolutionsProduced = transitionResolutionsProduced;
        ActionsApplied = actionsApplied;
        ActionResolutionsProduced = actionResolutionsProduced;
        ReferencesIgnored = referencesIgnored;
        RequiresBaselineRepair = requiresBaselineRepair;
    }

    public SimulationInstant Frame { get; }
    public int TransitionsApplied { get; }
    public int TransitionResolutionsProduced { get; }
    public int ActionsApplied { get; }
    public int ActionResolutionsProduced { get; }

    /// <summary>
    /// References that produced no application and no terminal result: unobserved
    /// intents, not-yet-due intents, and repeats of already terminal intents.
    /// </summary>
    public int ReferencesIgnored { get; }
    public bool RequiresBaselineRepair { get; }
}

/// <summary>
/// Receives each committed authority frame decision exactly once, in order.
/// </summary>
/// <remarks>
/// The scheduler offers a frame to this sink inside the same call that consumes
/// it. That is what makes "referenced intents are resolved on the frame that
/// referenced them" structural rather than a convention a caller has to
/// remember.
/// </remarks>
public interface IAuthorityFrameIntentSink
{
    void OnFrameDecisionCommitted(in AuthorityInputFrameDecision decision);
}

/// <summary>
/// Composes the transition and action resolvers over one combatant's authority
/// journals and applies them to each committed frame.
/// </summary>
public sealed class AuthorityFrameIntentResolver : IAuthorityFrameIntentSink
{
    private readonly AuthorityMovementTransitionJournal _transitionJournal;
    private readonly AuthorityOwnerActionCommandJournal _actionJournal;
    private readonly MovementTransitionResolver _transitionResolver;
    private readonly PredictedActionResolver _actionResolver;
    private readonly MovementTransitionResolution[] _transitionResolutions;
    private readonly PredictedActionResolution[] _actionResolutions;

    private int _transitionResolutionCount;
    private int _actionResolutionCount;
    private AuthorityFrameIntentSummary _lastSummary;
    private bool _hasResolvedAnyFrame;

    public AuthorityFrameIntentResolver(
        AuthorityMovementTransitionJournal transitionJournal,
        AuthorityOwnerActionCommandJournal actionJournal,
        MovementTransitionResolver transitionResolver,
        PredictedActionResolver actionResolver)
    {
        ArgumentNullException.ThrowIfNull(transitionJournal);
        ArgumentNullException.ThrowIfNull(actionJournal);
        ArgumentNullException.ThrowIfNull(transitionResolver);
        ArgumentNullException.ThrowIfNull(actionResolver);
        if (transitionJournal.Scope != actionJournal.Scope)
        {
            throw new ArgumentException(
                "Both journals must belong to the same owner intent scope.",
                nameof(actionJournal));
        }

        _transitionJournal = transitionJournal;
        _actionJournal = actionJournal;
        _transitionResolver = transitionResolver;
        _actionResolver = actionResolver;
        // One frame can produce a terminal result for every reference the command
        // carried AND for every outstanding intent whose deadline falls on that
        // same frame. Sizing to references alone would starve the expiry sweep of
        // buffer space exactly when the journal is fullest, deferring terminal
        // results the client is waiting on. Journal capacity bounds the expiry
        // side, so this stays preallocated and bounded.
        _transitionResolutions = new MovementTransitionResolution[
            OwnerSimulationLimits.MaximumTransitionReferences + transitionJournal.Capacity];
        _actionResolutions = new PredictedActionResolution[
            OwnerSimulationLimits.MaximumActionReferences + actionJournal.Capacity];
    }

    public OwnerIntentScope Scope => _transitionJournal.Scope;
    public AuthorityFrameIntentSummary LastSummary => _lastSummary;
    public bool HasResolvedAnyFrame => _hasResolvedAnyFrame;

    /// <summary>Terminal transition results produced by the most recent frame only.</summary>
    public ReadOnlySpan<MovementTransitionResolution> LastTransitionResolutions =>
        _transitionResolutions.AsSpan(0, _transitionResolutionCount);

    /// <summary>Terminal action results produced by the most recent frame only.</summary>
    public ReadOnlySpan<PredictedActionResolution> LastActionResolutions =>
        _actionResolutions.AsSpan(0, _actionResolutionCount);

    public void OnFrameDecisionCommitted(in AuthorityInputFrameDecision decision)
    {
        // The scheduler only ever commits validated decisions, so this is a
        // contract assertion rather than a reachable input path.
        if (!decision.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(decision));
        }

        _transitionResolutionCount = 0;
        _actionResolutionCount = 0;
        var frame = decision.Identity.Frame;
        var transitionsApplied = 0;
        var actionsApplied = 0;
        var ignored = 0;

        // A fallback or override decision structurally carries no references, so
        // a frame the owner never supplied can never fire an edge. The loops
        // below simply do not execute for those kinds.
        var transitions = decision.AppliedInput.TransitionReferences;
        for (var i = 0; i < transitions.Count; i++)
        {
            var outcome = _transitionResolver.Resolve(_transitionJournal, transitions[i], frame);
            if (outcome.WasAppliedThisFrame)
            {
                transitionsApplied++;
            }

            if (outcome.Resolution is { } resolution)
            {
                _transitionResolutions[_transitionResolutionCount++] = resolution;
            }
            else
            {
                ignored++;
            }
        }

        var actions = decision.AppliedInput.ActionReferences;
        for (var i = 0; i < actions.Count; i++)
        {
            var outcome = _actionResolver.Resolve(_actionJournal, actions[i], frame);
            if (outcome.WasAppliedThisFrame)
            {
                actionsApplied++;
            }

            if (outcome.Resolution is { } resolution)
            {
                _actionResolutions[_actionResolutionCount++] = resolution;
            }
            else
            {
                ignored++;
            }
        }

        // Deadlines are swept every frame, not only when something is
        // referenced. An intent whose commands were all lost must still reach a
        // terminal result, or the client would advertise it forever.
        _transitionResolutionCount += _transitionJournal.ExpireThrough(
            frame,
            _transitionResolutions.AsSpan(_transitionResolutionCount));
        _actionResolutionCount += _actionJournal.ExpireThrough(
            frame,
            _actionResolutions.AsSpan(_actionResolutionCount));

        _lastSummary = new AuthorityFrameIntentSummary(
            frame,
            transitionsApplied,
            _transitionResolutionCount,
            actionsApplied,
            _actionResolutionCount,
            ignored,
            _transitionJournal.RequiresBaselineRepair || _actionJournal.RequiresBaselineRepair);
        _hasResolvedAnyFrame = true;
    }
}
