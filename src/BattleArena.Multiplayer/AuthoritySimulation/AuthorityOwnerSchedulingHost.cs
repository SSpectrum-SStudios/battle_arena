using BattleArena.Core.Common;
using BattleArena.Core.Movement.Simulation;
using BattleArena.Multiplayer.OwnerPrediction;
using BattleArena.Multiplayer.Protocol;
using BattleArena.Multiplayer.Timing;

namespace BattleArena.Multiplayer.AuthoritySimulation;

/// <summary>
/// The engine-side half of one authority frame: supplies the per-combatant
/// policy inputs the scheduler needs, then integrates the decision it produced.
/// </summary>
/// <remarks>
/// <para>
/// This exists so <see cref="AuthorityOwnerSchedulingHost"/> can own the frame
/// loop rather than handing out steps a caller must remember to sequence. The
/// host drives every registered combatant through every due frame and calls back
/// here; the caller cannot skip a combatant, resolve one twice, complete a frame
/// nobody ran, or admit a command mid-frame, because it never gets the chance.
/// Those were four separate ordering hazards when the loop lived in the caller.
/// </para>
/// <para>
/// It also keeps Godot out of this assembly. <c>NetworkArena</c> implements this
/// one small interface; nothing here references an engine type.
/// </para>
/// </remarks>
public interface IAuthorityFrameSimulator
{
    /// <summary>
    /// Opens one authority frame, before any combatant resolves input on it.
    /// </summary>
    /// <remarks>
    /// Match-wide per-frame work belongs here — advancing combat timers, moving
    /// the caller's own frame counter — because it must happen once per frame
    /// rather than once per combatant or once per engine callback. Under
    /// catch-up a single callback runs several frames, and work placed in the
    /// callback instead of here would run once for all of them.
    /// </remarks>
    void BeginFrame(SimulationInstant frame);

    /// <summary>
    /// Closes one authority frame, after every combatant has resolved and
    /// integrated.
    /// </summary>
    /// <remarks>
    /// Cross-combatant resolution belongs here: pose history, melee hit
    /// resolution, damage, respawns, and outbound replication all need every
    /// combatant's post-state for this frame.
    /// </remarks>
    void EndFrame(SimulationInstant frame);

    /// <summary>
    /// Authority-resolved view and configuration revisions effective for this
    /// combatant on this frame. Fallback input is built from this rather than
    /// from a stale command, so a missed frame cannot replay a superseded
    /// movement revision.
    /// </summary>
    AuthorityFallbackInputBasis GetFallbackBasis(
        CombatantId combatantId,
        SimulationInstant frame);

    /// <summary>
    /// Whether authority policy — elimination, stun, teleport, freeze — governs
    /// this combatant on this frame instead of owner input.
    /// </summary>
    /// <remarks>
    /// An overridden frame is still consumed exactly once, so the timeline stays
    /// contiguous and a late command can never be applied to a frame that was
    /// skipped rather than resolved.
    /// </remarks>
    bool TryGetAuthorityOverride(
        CombatantId combatantId,
        SimulationInstant frame,
        out CharacterSimulationInput overrideInput,
        out AuthorityInputOverrideReason reason);

    /// <summary>
    /// Integrates one committed decision into simulation. Called exactly once
    /// per combatant per frame, in ascending frame order.
    /// </summary>
    void IntegrateFrame(CombatantId combatantId, in AuthorityInputFrameDecision decision);
}

/// <summary>
/// Latches a rebase demand so a persistent condition produces one rebase rather
/// than one per evaluation.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PredictionLeadController"/> returns
/// <see cref="PredictionLeadControllerDecision.RebaseRequired"/> on <em>every</em>
/// evaluation while sustained low clock confidence or starvation-at-maximum-lead
/// persists. That is correct for a pure control law — the condition really is
/// still true — but a rebase is a heavy reliable message, so re-emitting it every
/// frame turns a degraded link into its own amplification vector.
/// </para>
/// <para>
/// <see cref="AuthoritySimulationClock"/> already latches its own timeline reset.
/// This is the other half, and the two are deliberately separate: the clock's
/// reset is match-wide, while this one is per controlled combatant, because one
/// client's bad link must never rebase everyone.
/// </para>
/// </remarks>
public sealed class PredictionRebaseDebouncer
{
    private SimulationInstant? _claimedOnFrame;

    /// <summary>
    /// Whether a rebase has been claimed and not yet released. While true,
    /// <see cref="TryClaim"/> returns false no matter how many evaluations
    /// demand a rebase.
    /// </summary>
    public bool IsLatched => _claimedOnFrame is not null;

    /// <summary>
    /// The frame the outstanding rebase was claimed on, or null when not
    /// latched. Retained so the composition layer can report how long a client
    /// has been waiting for its rebase to be acknowledged.
    /// </summary>
    public SimulationInstant? ClaimedOnFrame => _claimedOnFrame;

    /// <summary>
    /// Attempts to take ownership of a rebase demand for
    /// <paramref name="frame"/>.
    /// </summary>
    /// <returns>
    /// True exactly once per latch cycle, for the caller that should actually
    /// emit the rebase. False while an earlier claim is still outstanding.
    /// </returns>
    public bool TryClaim(SimulationInstant frame)
    {
        if (_claimedOnFrame is not null)
        {
            return false;
        }

        _claimedOnFrame = frame;
        return true;
    }

    /// <summary>
    /// Records an evaluation that did <em>not</em> demand a rebase, clearing the
    /// latch so a later genuine demand can be claimed.
    /// </summary>
    /// <remarks>
    /// Without this the latch would be permanent in practice. Low clock
    /// confidence is transient — it can clear on its own and recur later with no
    /// epoch change in between — so releasing only on epoch rebuild would
    /// silently swallow every rebase demand after the first for the rest of the
    /// session.
    /// </remarks>
    public void ObserveRebaseNotRequired() => _claimedOnFrame = null;

    /// <summary>
    /// Clears the latch unconditionally, on acknowledgement or epoch rebuild.
    /// Releasing when not latched is a no-op rather than an error, so a rebuild
    /// does not have to know whether a claim was outstanding.
    /// </summary>
    public void Release() => _claimedOnFrame = null;
}

/// <summary>
/// Identity a combatant's scheduling group must carry across a rebuild that does
/// not change its <see cref="OwnerIntentScope"/>.
/// </summary>
/// <remarks>
/// <para>
/// This exists because <see cref="OwnerIntentScope"/> is deliberately narrower
/// than <see cref="CombatantAuthorityPredictionEpoch"/>: it excludes both the
/// match-frame epoch and the authority discontinuity. A teleport or a timeline
/// rebase therefore produces a new epoch while the owner's journal lifetime is
/// unchanged — the client keeps its transition and action IDs and its applied
/// resolution cursors straight through.
/// </para>
/// <para>
/// Rebuilding the journals from zero in that case is silently destructive.
/// Resolution sequences would restart at 1 while the client's applied cursor is
/// already ahead, so every new resolution is discarded as stale; and the lead
/// revision would restart, which the client's P03-08 receive gate treats as a
/// contradictory revision and locks on. Carrying these forward is what makes a
/// same-scope rebuild safe.
/// </para>
/// </remarks>
public readonly record struct AuthorityOwnerSchedulingContinuity
{
    public AuthorityOwnerSchedulingContinuity(
        PredictionLeadPolicyRevision leadRevisionSeed,
        TransitionResolutionSequence nextTransitionResolutionSequence,
        TransitionResolutionIdentity? acknowledgedTransitionThrough,
        MovementTransitionId? highestContiguousObservedTransition,
        ActionResolutionSequence nextActionResolutionSequence,
        ActionResolutionIdentity? acknowledgedActionThrough,
        PredictedActionId? highestContiguousObservedAction)
    {
        if (!leadRevisionSeed.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(leadRevisionSeed));
        }
        if (!nextTransitionResolutionSequence.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(nextTransitionResolutionSequence));
        }
        if (!nextActionResolutionSequence.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(nextActionResolutionSequence));
        }

        LeadRevisionSeed = leadRevisionSeed;
        NextTransitionResolutionSequence = nextTransitionResolutionSequence;
        AcknowledgedTransitionThrough = acknowledgedTransitionThrough;
        HighestContiguousObservedTransition = highestContiguousObservedTransition;
        NextActionResolutionSequence = nextActionResolutionSequence;
        AcknowledgedActionThrough = acknowledgedActionThrough;
        HighestContiguousObservedAction = highestContiguousObservedAction;
    }

    /// <summary>
    /// The revision the rebuilt controller is seeded with; its next emission is
    /// one past this, so revisions stay monotonic within an unchanged scope.
    /// </summary>
    public PredictionLeadPolicyRevision LeadRevisionSeed { get; }
    public TransitionResolutionSequence NextTransitionResolutionSequence { get; }
    public TransitionResolutionIdentity? AcknowledgedTransitionThrough { get; }
    public MovementTransitionId? HighestContiguousObservedTransition { get; }
    public ActionResolutionSequence NextActionResolutionSequence { get; }
    public ActionResolutionIdentity? AcknowledgedActionThrough { get; }
    public PredictedActionId? HighestContiguousObservedAction { get; }
}

/// <summary>
/// Everything the authority owns for one controlled combatant under V2 exact
/// scheduling: input storage, durable intent journals, intent resolution, lead
/// control, rebase debouncing, starvation accounting, and — for the listen host
/// only — the in-memory command publisher.
/// </summary>
/// <remarks>
/// <para>
/// These are grouped into one object because they share a lifetime. On any epoch
/// change they must be rebuilt <em>together</em>: rebuilding the scheduler while
/// leaving a publisher pointed at the old one produces a host command that is
/// hard-rejected for stale epoch — correct behaviour, but a bug on the
/// authority's own side rather than a client's.
/// </para>
/// <para>
/// The publisher is present only for the listen host. A remote client's commands
/// arrive over the wire and are admitted directly; the host has no network to
/// traverse, and the publisher is what forces its input through the identical
/// projection, validation, and admission rules anyway.
/// </para>
/// </remarks>
public sealed class AuthorityOwnerCombatantScheduling
{
    private readonly SimulationInstant _firstFrame;
    private int _starvedFramesSinceLastEvaluation;
    private SimulationInstant? _highestAdmittedTargetFrame;
    private PredictionLeadUpdate? _lastEmittedLeadUpdate;

    /// <param name="capacityFrames">
    /// Scheduler ring size, which is also the acceptance horizon and the flood
    /// bound. It must be at least the negotiated maximum prediction lead, or
    /// commands sent legally at the ceiling are refused as beyond horizon.
    /// </param>
    /// <param name="continuity">
    /// Identity carried from a previous group whose
    /// <see cref="OwnerIntentScope"/> was the same. Null for a genuinely new
    /// scope, which starts every sequence at its initial value.
    /// </param>
    /// <param name="isListenHost">
    /// When true, an <see cref="InMemoryAuthorityOwnerCommandPublisher"/> is
    /// created over this scheduler so host input takes the same path as a remote
    /// client's. When false the combatant is remote and admits over the wire.
    /// </param>
    public AuthorityOwnerCombatantScheduling(
        CombatantAuthorityPredictionEpoch epoch,
        SimulationInstant firstFrameToConsume,
        SimulationRate rate,
        int capacityFrames,
        AuthorityInputFallbackPolicy fallbackPolicy,
        bool isListenHost,
        AuthorityOwnerSchedulingContinuity? continuity)
    {
        if (!epoch.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(epoch));
        }

        Epoch = epoch;
        Scope = OwnerIntentScope.From(epoch);
        _firstFrame = firstFrameToConsume;

        Scheduler = new AuthorityOwnerInputScheduler(
            epoch,
            firstFrameToConsume,
            capacityFrames,
            fallbackPolicy,
            AuthorityOwnerInputScheduler.DefaultRetainedDispositionFrames);

        // A same-scope rebuild restores journals at the sequence numbering the
        // client is already tracking. A new scope starts at Initial.
        if (continuity is { } carried)
        {
            TransitionJournal = AuthorityMovementTransitionJournal.RestoreEmptyBaseline(
                Scope,
                MovementTransitionJournalPolicy.Default,
                carried.NextTransitionResolutionSequence,
                carried.AcknowledgedTransitionThrough,
                carried.HighestContiguousObservedTransition);
            ActionJournal = AuthorityOwnerActionCommandJournal.RestoreEmptyBaseline(
                Scope,
                OwnerActionJournalPolicy.Default,
                carried.NextActionResolutionSequence,
                carried.AcknowledgedActionThrough,
                carried.HighestContiguousObservedAction);
        }
        else
        {
            TransitionJournal = new AuthorityMovementTransitionJournal(Scope);
            ActionJournal = new AuthorityOwnerActionCommandJournal(Scope);
        }

        IntentResolver = new AuthorityFrameIntentResolver(
            TransitionJournal,
            ActionJournal,
            new MovementTransitionResolver(),
            new PredictedActionResolver(new MonotonicAuthorityActionExecutionAllocator()));

        var leadPolicy = PredictionLeadControllerPolicy.LeadPolicyForRate(rate);
        LeadController = new PredictionLeadController(
            Scope,
            rate,
            leadPolicy.MinimumLeadFrames,
            continuity?.LeadRevisionSeed ?? PredictionLeadPolicyRevision.Initial,
            leadPolicy,
            PredictionLeadControllerPolicy.Default);

        RebaseDebouncer = new PredictionRebaseDebouncer();
        HostPublisher = isListenHost
            ? new InMemoryAuthorityOwnerCommandPublisher(Scheduler)
            : null;
    }

    /// <summary>The epoch every member of this group is bound to.</summary>
    public CombatantAuthorityPredictionEpoch Epoch { get; }

    /// <summary>
    /// Journal/input lifetime derived from <see cref="Epoch"/>. Narrower than the
    /// epoch: two successive epochs can share one scope.
    /// </summary>
    public OwnerIntentScope Scope { get; }

    /// <summary>Exact target-frame command storage and ordered consumption.</summary>
    internal AuthorityOwnerInputScheduler Scheduler { get; }

    internal AuthorityMovementTransitionJournal TransitionJournal { get; }

    internal AuthorityOwnerActionCommandJournal ActionJournal { get; }

    /// <summary>
    /// Applies durable intents on the frame that referenced them. Passed to
    /// <see cref="AuthorityOwnerInputScheduler.ResolveNextFrame"/> as the sink,
    /// which is what makes that ordering structural rather than conventional.
    /// </summary>
    internal AuthorityFrameIntentResolver IntentResolver { get; }

    internal PredictionLeadController LeadController { get; }

    /// <summary>
    /// Present only for the listen host. Null for every remote combatant, whose
    /// commands are admitted straight from the validated wire batch.
    /// </summary>
    internal InMemoryAuthorityOwnerCommandPublisher? HostPublisher { get; }

    public PredictionRebaseDebouncer RebaseDebouncer { get; }

    /// <summary>Measured owner commands waiting ahead of the consumption cursor.</summary>
    public int BufferedFrameCount => Scheduler.BufferedFrameCount;

    /// <summary>
    /// Frames filled by fallback since the last lead evaluation, reset by that
    /// evaluation.
    /// </summary>
    /// <remarks>
    /// Owned here rather than by the engine caller because it is scheduler-side
    /// bookkeeping that steers an absolute control law: double- or under-counting
    /// silently mis-steers the lead, and the arena node has no business tracking
    /// it.
    /// </remarks>
    public int StarvedFramesSinceLastEvaluation => _starvedFramesSinceLastEvaluation;

    /// <summary>
    /// The most recent lead update actually emitted for this combatant, so owner
    /// state advertises the real current policy rather than one the caller
    /// remembered. Null before the first emission.
    /// </summary>
    public PredictionLeadUpdate? LastEmittedLeadUpdate => _lastEmittedLeadUpdate;

    /// <summary>Highest target frame any admitted command has named.</summary>
    internal SimulationInstant? HighestAdmittedTargetFrame => _highestAdmittedTargetFrame;

    /// <summary>
    /// The next sequence the listen host's own command will carry, derived from
    /// the target frame rather than from a caller-side counter.
    /// </summary>
    /// <remarks>
    /// The scheduler enforces a fixed <c>sequence - targetFrame</c> offset for
    /// its whole lifetime. A caller-owned counter that advanced on a frame the
    /// host did not publish — an eliminated frame taking the override path, say —
    /// would break that offset and wedge the host's own input with
    /// <see cref="AuthorityInputAdmissionFault.SequenceFrameSkew"/> for the rest
    /// of the epoch. Deriving it here makes that impossible.
    /// </remarks>
    internal InputSequence HostSequenceForFrame(SimulationInstant targetFrame)
    {
        var offset = targetFrame.Tick - _firstFrame.Tick;
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetFrame));
        }

        return new InputSequence(checked(InputSequence.Initial.Value + (ulong)offset));
    }

    internal void RecordAdmittedTargetFrame(SimulationInstant frame)
    {
        if (_highestAdmittedTargetFrame is not { } highest || frame > highest)
        {
            _highestAdmittedTargetFrame = frame;
        }
    }

    internal void RecordResolvedFrame(in AuthorityInputFrameDecision decision)
    {
        if (decision.ApplicationKind is
            AuthorityInputApplicationKind.RepeatedContinuous or
            AuthorityInputApplicationKind.NeutralFallback)
        {
            _starvedFramesSinceLastEvaluation++;
        }
    }

    internal void RecordLeadEvaluated(PredictionLeadUpdate? emitted)
    {
        _starvedFramesSinceLastEvaluation = 0;
        if (emitted is not null)
        {
            _lastEmittedLeadUpdate = emitted;
        }
    }

    /// <summary>
    /// Captures the continuity a same-scope rebuild must carry forward.
    /// </summary>
    internal AuthorityOwnerSchedulingContinuity? CaptureContinuity()
    {
        // The empty-baseline restore contract requires the acknowledged cursor to
        // be exactly one below the next sequence, so both are derived from the
        // same value rather than read independently.
        var nextTransition = TransitionJournal.NextResolutionSequenceValue;
        var nextAction = ActionJournal.NextResolutionSequenceValue;

        // Exhausted numbering cannot be carried; that journal already requires an
        // explicit baseline repair, and a rebuild is the natural place to take it.
        if (nextTransition == 0 || nextAction == 0)
        {
            return null;
        }

        return new AuthorityOwnerSchedulingContinuity(
            LeadController.CurrentRevision,
            new TransitionResolutionSequence(nextTransition),
            nextTransition == 1
                ? null
                : new TransitionResolutionIdentity(
                    Scope,
                    new TransitionResolutionSequence(nextTransition - 1)),
            TransitionJournal.HighestContiguousObservedTransitionId,
            new ActionResolutionSequence(nextAction),
            nextAction == 1
                ? null
                : new ActionResolutionIdentity(
                    Scope,
                    new ActionResolutionSequence(nextAction - 1)),
            ActionJournal.HighestContiguousObservedActionId);
    }
}

/// <summary>
/// What one call to <see cref="AuthorityOwnerSchedulingHost.RunDueFrames"/> did.
/// </summary>
public readonly record struct AuthorityFrameRunSummary
{
    internal AuthorityFrameRunSummary(
        int framesRun,
        SimulationInstant? lastCompletedFrame,
        AuthorityClockAdvanceDecision clockDecision,
        AuthorityTimelineReset? timelineReset)
    {
        FramesRun = framesRun;
        LastCompletedFrame = lastCompletedFrame;
        ClockDecision = clockDecision;
        TimelineReset = timelineReset;
    }

    /// <summary>
    /// Frames actually simulated. Zero is normal — wall time may not yet have
    /// accumulated a whole fixed step.
    /// </summary>
    public int FramesRun { get; }

    public SimulationInstant? LastCompletedFrame { get; }
    public AuthorityClockAdvanceDecision ClockDecision { get; }

    /// <summary>
    /// Set when the match timeline could not be recovered and must be rebased.
    /// The clock latches this, so it is reported once rather than every callback.
    /// The caller must send it reliably and then call
    /// <see cref="AuthorityOwnerSchedulingHost.ResumeAfterMatchEpochReset"/>.
    /// </summary>
    public AuthorityTimelineReset? TimelineReset { get; }
}

/// <summary>
/// Classified outcome of observing durable intents carried by an owner batch.
/// </summary>
public readonly record struct AuthorityIntentObservationSummary
{
    internal AuthorityIntentObservationSummary(
        int transitionsFirstSeen,
        int actionsFirstSeen,
        int duplicatesIgnored,
        int rejected,
        bool requiresBaselineRepair)
    {
        TransitionsFirstSeen = transitionsFirstSeen;
        ActionsFirstSeen = actionsFirstSeen;
        DuplicatesIgnored = duplicatesIgnored;
        Rejected = rejected;
        RequiresBaselineRepair = requiresBaselineRepair;
    }

    public int TransitionsFirstSeen { get; }
    public int ActionsFirstSeen { get; }

    /// <summary>Resends of intents already observed. Expected and harmless.</summary>
    public int DuplicatesIgnored { get; }

    /// <summary>
    /// Wrong scope, malformed window, past capacity, or conflicting with an
    /// already-observed intent of the same identity.
    /// </summary>
    public int Rejected { get; }

    public bool RequiresBaselineRepair { get; }
}

/// <summary>
/// Composition root for V2 exact authority scheduling: one match clock plus one
/// <see cref="AuthorityOwnerCombatantScheduling"/> per controlled combatant.
/// </summary>
/// <remarks>
/// <para>
/// This type deliberately lives in <c>BattleArena.Multiplayer</c> rather than in
/// the Godot arena node. It has no engine dependency, so the whole V2 scheduling
/// path — epoch rebuilds, admission, per-frame resolution, lead emission, and
/// owner state publication — is testable with plain unit tests instead of a
/// headless two-process smoke run. The arena node implements
/// <see cref="IAuthorityFrameSimulator"/> and hands itself to
/// <see cref="RunDueFrames"/>.
/// </para>
/// <para>
/// It owns pacing and input resolution but not movement integration; that stays
/// with the simulator callback, because the explicit motor which will perform it
/// is Phase 5 work and the legacy motor is still in place behind the flag.
/// </para>
/// </remarks>
public sealed class AuthorityOwnerSchedulingHost
{
    private readonly Dictionary<long, AuthorityOwnerCombatantScheduling> _combatants = [];
    private readonly List<long> _order = [];
    private readonly SimulationRate _rate;
    private readonly AuthoritySimulationClockPolicy _clockPolicy;

    private AuthoritySimulationClock _clock;
    private MatchFrameEpochId _matchFrameEpoch;
    private SimulationInstant _nextFrame;
    private SimulationInstant? _lastCompletedFrame;
    private bool _frameRunInProgress;
    private bool _frameOpenForHostPublication;
    private bool _awaitingTimelineResume;

    /// <param name="rate">
    /// The negotiated simulation rate. It reaches the lead controller's policy,
    /// whose ceiling is a duration rather than a frame count, so this cannot be
    /// defaulted without silently doubling the lead ceiling at 60 Hz.
    /// </param>
    public AuthorityOwnerSchedulingHost(
        SimulationRate rate,
        AuthoritySimulationClockPolicy clockPolicy,
        MatchFrameEpochId matchFrameEpoch,
        SimulationInstant firstFrame)
    {
        if (rate.TicksPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rate));
        }
        if (!clockPolicy.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(clockPolicy));
        }
        if (!matchFrameEpoch.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(matchFrameEpoch));
        }

        _rate = rate;
        _clockPolicy = clockPolicy;
        _matchFrameEpoch = matchFrameEpoch;
        _nextFrame = firstFrame;
        _clock = new AuthoritySimulationClock(matchFrameEpoch, rate, firstFrame, clockPolicy);
    }

    public SimulationRate Rate => _rate;

    /// <summary>Current match-frame epoch; changes only on a timeline reset.</summary>
    public MatchFrameEpochId MatchFrameEpoch => _matchFrameEpoch;

    /// <summary>
    /// The next frame <see cref="RunDueFrames"/> will retire for every registered
    /// combatant.
    /// </summary>
    public SimulationInstant NextFrame => _nextFrame;

    /// <summary>
    /// The newest frame every registered combatant has resolved and the clock has
    /// completed. Null before the first frame runs.
    /// </summary>
    public SimulationInstant? LastCompletedFrame => _lastCompletedFrame;

    /// <summary>
    /// True while the match timeline is frozen awaiting
    /// <see cref="ResumeAfterMatchEpochReset"/>. No frame runs in this state.
    /// </summary>
    public bool IsAwaitingTimelineResume => _awaitingTimelineResume;

    /// <summary>
    /// Every registered combatant, so the caller never has to maintain its own
    /// parallel list and cannot drift out of step with the host.
    /// </summary>
    public IReadOnlyCollection<CombatantId> Combatants =>
        _order.Select(id => new CombatantId(id)).ToArray();

    /// <summary>
    /// Begins V2 scheduling for one combatant. The listen host and every remote
    /// client register the same way and get the same rules; only
    /// <paramref name="isListenHost"/> decides whether a publisher is attached.
    /// </summary>
    /// <remarks>
    /// A combatant registered mid-match starts at the host's
    /// <see cref="NextFrame"/>, not at the match's first frame, so it does not
    /// owe a backlog of frames nobody could have sent input for.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The combatant is already registered, or the epoch's match-frame epoch
    /// disagrees with this host's current <see cref="MatchFrameEpoch"/>.
    /// </exception>
    public void RegisterCombatant(
        CombatantAuthorityPredictionEpoch epoch,
        int capacityFrames,
        AuthorityInputFallbackPolicy fallbackPolicy,
        bool isListenHost)
    {
        RequireNotRunningFrames();
        if (!epoch.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(epoch));
        }
        if (epoch.MatchFrameEpoch != _matchFrameEpoch)
        {
            throw new ArgumentException(
                "A combatant must be registered against the host's current match-frame epoch.",
                nameof(epoch));
        }
        if (_combatants.ContainsKey(epoch.CombatantId.Value))
        {
            throw new ArgumentException(
                "That combatant is already registered; use RebuildForEpoch to change its epoch.",
                nameof(epoch));
        }

        _combatants.Add(
            epoch.CombatantId.Value,
            new AuthorityOwnerCombatantScheduling(
                epoch,
                _nextFrame,
                _rate,
                capacityFrames,
                fallbackPolicy,
                isListenHost,
                continuity: null));
        _order.Add(epoch.CombatantId.Value);
    }

    /// <summary>
    /// Drops a combatant that has left the match. Its journals and any
    /// unacknowledged tombstones go with it.
    /// </summary>
    /// <remarks>
    /// This is departure, not reconnection. A client that reconnects or respawns
    /// keeps its combatant and goes through <see cref="RebuildForEpoch"/>, which
    /// preserves the journal identity a same-scope epoch change must not lose.
    /// </remarks>
    /// <returns>False when the combatant was not registered.</returns>
    public bool RemoveCombatant(CombatantId combatantId)
    {
        RequireNotRunningFrames();
        if (!_combatants.Remove(combatantId.Value))
        {
            return false;
        }

        _order.Remove(combatantId.Value);
        return true;
    }

    /// <summary>
    /// Replaces one combatant's scheduling group for a new epoch, on respawn,
    /// reconnect, owner-control renewal, or authority discontinuity.
    /// </summary>
    /// <remarks>
    /// Scheduler and publisher are rebuilt as one unit. When the new epoch's
    /// <see cref="OwnerIntentScope"/> matches the old one — an authority
    /// discontinuity such as a teleport, which does not end the owner's journal
    /// lifetime — lead revision, resolution sequences, and acknowledged cursors
    /// are carried forward, because restarting them makes the client discard
    /// every subsequent resolution as stale. A scope change starts fresh.
    /// </remarks>
    public void RebuildForEpoch(
        CombatantAuthorityPredictionEpoch epoch,
        int capacityFrames,
        AuthorityInputFallbackPolicy fallbackPolicy,
        bool isListenHost)
    {
        RequireNotRunningFrames();
        if (!epoch.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(epoch));
        }
        if (epoch.MatchFrameEpoch != _matchFrameEpoch)
        {
            throw new ArgumentException(
                "A rebuild must target the host's current match-frame epoch.",
                nameof(epoch));
        }

        var continuity = _combatants.TryGetValue(epoch.CombatantId.Value, out var existing) &&
            existing.Scope == OwnerIntentScope.From(epoch)
                ? existing.CaptureContinuity()
                : null;

        _combatants[epoch.CombatantId.Value] = new AuthorityOwnerCombatantScheduling(
            epoch,
            _nextFrame,
            _rate,
            capacityFrames,
            fallbackPolicy,
            isListenHost,
            continuity);
        if (!_order.Contains(epoch.CombatantId.Value))
        {
            _order.Add(epoch.CombatantId.Value);
        }
    }

    /// <summary>
    /// Resumes after a match timeline reset, adopting the new match-frame epoch
    /// and rebuilding <em>every</em> registered combatant against it.
    /// </summary>
    /// <remarks>
    /// The match-frame epoch is part of every combatant's epoch, so a timeline
    /// reset invalidates all of them at once. This is the match-wide half of the
    /// rule that scheduler and publisher are rebuilt together; without it the
    /// host is permanently unusable after the clock's first reset, since no
    /// combatant could be registered against the new epoch.
    /// </remarks>
    public void ResumeAfterMatchEpochReset(
        MatchFrameEpochId newEpoch,
        SimulationInstant resumeFrame)
    {
        RequireNotRunningFrames();
        if (!newEpoch.IsValid || newEpoch <= _matchFrameEpoch)
        {
            throw new ArgumentOutOfRangeException(
                nameof(newEpoch),
                "A timeline resume must adopt a strictly newer match-frame epoch.");
        }

        _clock.ResumeAfterReset(newEpoch, resumeFrame);
        _matchFrameEpoch = newEpoch;
        _nextFrame = resumeFrame;
        _lastCompletedFrame = null;
        _awaitingTimelineResume = false;

        // Every combatant's epoch embeds the match-frame epoch, so all of them
        // are rebuilt as one unit against the new one. Scope is unchanged by a
        // timeline reset, so journal and lead identity carry forward.
        foreach (var combatantId in _order.ToArray())
        {
            var existing = _combatants[combatantId];
            var rebuilt = new CombatantAuthorityPredictionEpoch(
                existing.Epoch.SessionId,
                newEpoch,
                existing.Epoch.CombatantId,
                existing.Epoch.Life,
                existing.Epoch.AuthorityDiscontinuity,
                existing.Epoch.OwnerControl);
            _combatants[combatantId] = new AuthorityOwnerCombatantScheduling(
                rebuilt,
                resumeFrame,
                _rate,
                existing.Scheduler.CapacityFrames,
                existing.Scheduler.FallbackPolicy,
                existing.HostPublisher is not null,
                existing.CaptureContinuity());
        }
    }

    /// <summary>
    /// Freezes the match and raises one reliable timeline reset, for conditions
    /// the clock cannot observe itself — required history overrun, or a session
    /// policy decision.
    /// </summary>
    public AuthorityTimelineReset DemandTimelineReset()
    {
        RequireNotRunningFrames();
        var advance = _clock.DemandTimelineReset();
        _awaitingTimelineResume = true;
        return advance.Reset
            ?? throw new InvalidOperationException(
                "The clock did not produce a timeline reset on demand.");
    }

    public bool TryGetScheduling(
        CombatantId combatantId,
        out AuthorityOwnerCombatantScheduling scheduling) =>
        _combatants.TryGetValue(combatantId.Value, out scheduling!);

    /// <summary>
    /// Records the durable transition and action intents carried by one owner
    /// batch, before the commands that reference them are admitted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Required, not optional. A command carries only intent <em>references</em>;
    /// the authority journals must have observed the intent itself or every
    /// reference resolves as not-observed and no transition or action is ever
    /// applied. The wire batch carries them in separate fields for exactly this
    /// reason.
    /// </para>
    /// <para>
    /// Ingress, so it classifies rather than throws: wrong scope, malformed
    /// window, capacity, and conflicting duplicates are all counted outcomes.
    /// </para>
    /// </remarks>
    public AuthorityIntentObservationSummary TryObserveRemoteIntents(
        CombatantId combatantId,
        ReadOnlySpan<MovementTransitionIntent> transitions,
        ReadOnlySpan<PredictedActionIntent> actions)
    {
        if (!_combatants.TryGetValue(combatantId.Value, out var scheduling))
        {
            return new AuthorityIntentObservationSummary(0, 0, 0,
                transitions.Length + actions.Length, requiresBaselineRepair: false);
        }

        var firstSeenTransitions = 0;
        var firstSeenActions = 0;
        var duplicates = 0;
        var rejected = 0;

        for (var i = 0; i < transitions.Length; i++)
        {
            switch (scheduling.TransitionJournal.Observe(transitions[i]))
            {
                case AuthorityTransitionObserveDecision.FirstSeen:
                    firstSeenTransitions++;
                    break;
                case AuthorityTransitionObserveDecision.Duplicate:
                    duplicates++;
                    break;
                default:
                    rejected++;
                    break;
            }
        }

        for (var i = 0; i < actions.Length; i++)
        {
            switch (scheduling.ActionJournal.Observe(actions[i]))
            {
                case AuthorityActionObserveDecision.FirstSeen:
                    firstSeenActions++;
                    break;
                case AuthorityActionObserveDecision.Duplicate:
                    duplicates++;
                    break;
                default:
                    rejected++;
                    break;
            }
        }

        return new AuthorityIntentObservationSummary(
            firstSeenTransitions,
            firstSeenActions,
            duplicates,
            rejected,
            scheduling.TransitionJournal.RequiresBaselineRepair ||
                scheduling.ActionJournal.RequiresBaselineRepair);
    }

    /// <summary>
    /// Applies the client's transition and action resolution cursors, retiring
    /// acknowledged tombstones.
    /// </summary>
    /// <remarks>
    /// Without this, terminal results are retained and resent forever until the
    /// bounded retention timeout latches a baseline repair. Ingress, so an
    /// out-of-scope or impossible cursor is refused rather than throwing.
    /// </remarks>
    /// <returns>False when either cursor was refused.</returns>
    public bool AcknowledgeResolutions(
        CombatantId combatantId,
        TransitionResolutionIdentity? transitionCursor,
        ActionResolutionIdentity? actionCursor)
    {
        if (!_combatants.TryGetValue(combatantId.Value, out var scheduling))
        {
            return false;
        }

        var accepted = true;
        if (transitionCursor is { } transition)
        {
            accepted &= scheduling.TransitionJournal.AcknowledgeResolutions(transition);
        }
        if (actionCursor is { } action)
        {
            accepted &= scheduling.ActionJournal.AcknowledgeResolutions(action);
        }

        return accepted;
    }

    /// <summary>
    /// Offers one validated remote owner command to its combatant's scheduler.
    /// </summary>
    /// <remarks>
    /// Never throws for hostile input: an unregistered combatant or a
    /// stale-epoch command is a classified rejection, because ingress pressure
    /// must be telemetry rather than control flow. Calling this while
    /// <see cref="RunDueFrames"/> is on the stack is itself refused, so whether a
    /// command lands before its target frame is consumed can never depend on
    /// callback ordering.
    /// </remarks>
    public AuthorityInputAdmission TryAdmitRemoteCommand(
        CombatantId combatantId,
        in OwnerSimulationCommand command)
    {
        if (_frameRunInProgress)
        {
            return AuthorityInputAdmission.Rejected(
                AuthorityInputAdmissionFault.FrameRunInProgress,
                arrival: null);
        }
        if (!_combatants.TryGetValue(combatantId.Value, out var scheduling))
        {
            return AuthorityInputAdmission.Rejected(
                AuthorityInputAdmissionFault.UnknownCombatant,
                arrival: null);
        }

        var admission = scheduling.Scheduler.TryAdmit(command);
        if (admission.WasStored)
        {
            scheduling.RecordAdmittedTargetFrame(command.TargetFrame);
        }

        return admission;
    }

    /// <summary>
    /// Routes one listen-host input through the in-memory publisher, and so
    /// through the same projection and admission rules a remote client's command
    /// passes. The host gets no relaxation of the late, horizon, duplicate, or
    /// conflicting-command rules.
    /// </summary>
    /// <remarks>
    /// Takes the input rather than a built command because the sequence is
    /// derived from <paramref name="targetFrame"/> by the scheduling group; see
    /// <see cref="AuthorityOwnerCombatantScheduling.HostSequenceForFrame"/>.
    /// </remarks>
    public OwnerCommandPublishResult PublishHostCommand(
        CombatantId combatantId,
        SimulationInstant targetFrame,
        in CharacterSimulationInput input)
    {
        // Permitted outside a run, and also from inside BeginFrame — which is
        // where the listen host naturally captures input for the frame about to
        // resolve. That window is safe precisely because it is a fixed point in
        // the ordering: BeginFrame runs before any combatant resolves the frame,
        // so the host's command lands in time and never depends on which
        // combatant was simulated first. Publishing from IntegrateFrame or
        // EndFrame would be order-dependent and is still refused.
        if (_frameRunInProgress && !_frameOpenForHostPublication)
        {
            throw new InvalidOperationException(
                "A host command may only be published outside a frame run or while the " +
                "frame is being opened, before any combatant has resolved it.");
        }
        if (!_combatants.TryGetValue(combatantId.Value, out var scheduling) ||
            scheduling.HostPublisher is not { } publisher)
        {
            throw new InvalidOperationException(
                "That combatant is not registered as the listen host.");
        }

        var command = new OwnerSimulationCommand(
            new OwnerInputIdentity(
                scheduling.Scope,
                scheduling.HostSequenceForFrame(targetFrame)),
            scheduling.Epoch.AuthorityDiscontinuity,
            scheduling.Epoch.MatchFrameEpoch,
            targetFrame,
            input);
        var result = publisher.Publish(command);
        if (result.Admission.WasStored)
        {
            scheduling.RecordAdmittedTargetFrame(targetFrame);
        }

        return result;
    }

    /// <summary>
    /// Advances match pacing by the elapsed wall time since the previous frame
    /// boundary and runs every due frame for every registered combatant.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the only way frames advance. For each due frame, every registered
    /// combatant resolves exactly one decision — owner command, fallback, or
    /// declared override — the decision is integrated through
    /// <paramref name="simulator"/>, and only then does the clock complete the
    /// frame. A combatant therefore cannot be skipped, and the clock cannot
    /// advance past a frame that was not simulated.
    /// </para>
    /// <para>
    /// Callers must pass elapsed-since-last-boundary, not elapsed-since-load. An
    /// interval beyond the per-callback bound is treated as unrecoverable and
    /// resets the timeline rather than being silently truncated.
    /// </para>
    /// </remarks>
    public AuthorityFrameRunSummary RunDueFrames(
        double elapsedMilliseconds,
        long monotonicTimestamp,
        IAuthorityFrameSimulator simulator)
    {
        ArgumentNullException.ThrowIfNull(simulator);
        RequireNotRunningFrames();

        var advance = _clock.Advance(elapsedMilliseconds);
        if (advance.Reset is { } reset)
        {
            _awaitingTimelineResume = true;
            return new AuthorityFrameRunSummary(0, _lastCompletedFrame, advance.Decision, reset);
        }
        if (advance.Decision is AuthorityClockAdvanceDecision.Frozen or
            AuthorityClockAdvanceDecision.NoStepDue ||
            advance.StepsToRun <= 0)
        {
            return new AuthorityFrameRunSummary(
                0, _lastCompletedFrame, advance.Decision, timelineReset: null);
        }

        var framesRun = 0;
        _frameRunInProgress = true;
        try
        {
            for (var step = 0; step < advance.StepsToRun; step++)
            {
                var frame = _nextFrame;

                // Host input for this frame is captured here, before any
                // combatant resolves it, so it is admitted in time to be the
                // frame's received input rather than arriving late.
                _frameOpenForHostPublication = true;
                try
                {
                    simulator.BeginFrame(frame);
                }
                finally
                {
                    _frameOpenForHostPublication = false;
                }
                foreach (var combatantId in _order)
                {
                    var scheduling = _combatants[combatantId];

                    // Fail closed rather than let a combatant silently drift from
                    // the match clock. Every registered combatant owes exactly
                    // this frame; anything else means a cursor moved outside the
                    // one path allowed to move it.
                    if (scheduling.Scheduler.NextFrameToConsume != frame)
                    {
                        throw new InvalidOperationException(
                            $"Combatant {combatantId} owes frame " +
                            $"{scheduling.Scheduler.NextFrameToConsume.Tick} but the match is " +
                            $"running frame {frame.Tick}.");
                    }

                    var id = new CombatantId(combatantId);
                    var decision = simulator.TryGetAuthorityOverride(
                        id, frame, out var overrideInput, out var reason)
                        ? scheduling.Scheduler.ResolveNextFrameAsAuthorityOverride(
                            overrideInput, reason, scheduling.IntentResolver)
                        : scheduling.Scheduler.ResolveNextFrame(
                            simulator.GetFallbackBasis(id, frame), scheduling.IntentResolver);

                    scheduling.RecordResolvedFrame(decision);
                    simulator.IntegrateFrame(id, decision);
                }

                simulator.EndFrame(frame);
                _lastCompletedFrame = _clock.CompleteFrame(monotonicTimestamp);
                _nextFrame = new SimulationInstant(frame.Tick + 1);
                framesRun++;
            }
        }
        finally
        {
            _frameRunInProgress = false;
        }

        return new AuthorityFrameRunSummary(
            framesRun, _lastCompletedFrame, advance.Decision, timelineReset: null);
    }

    /// <summary>
    /// Evaluates one combatant's lead control and, when a change is warranted,
    /// emits the absolute revisioned update.
    /// </summary>
    /// <remarks>
    /// Takes only the two facts the host cannot know — measured path and clock
    /// confidence. Buffer occupancy, starvation since the last evaluation, and
    /// the last scheduled command frame are all scheduler-side state this host
    /// already owns, and requiring the engine caller to accumulate them put
    /// stateful bookkeeping where a miscount silently mis-steers an absolute
    /// control law.
    /// </remarks>
    /// <returns>
    /// The controller's evaluation, plus whether this caller now owns an
    /// outstanding rebase. The update itself, when one is due, is carried by
    /// <see cref="PredictionLeadEvaluation.Update"/>; the controller allocates
    /// and stamps the revision, and nothing else may.
    /// </returns>
    public AuthorityLeadEmission EvaluateLead(
        CombatantId combatantId,
        NetworkPathEstimate path,
        double clockConfidence)
    {
        RequireNotRunningFrames();
        if (!_combatants.TryGetValue(combatantId.Value, out var scheduling))
        {
            throw new InvalidOperationException($"Combatant {combatantId} is not registered.");
        }

        var evaluation = scheduling.LeadController.Evaluate(
            new PredictionLeadObservation(
                _lastCompletedFrame ?? _nextFrame,
                path,
                clockConfidence,
                scheduling.BufferedFrameCount,
                scheduling.StarvedFramesSinceLastEvaluation,
                scheduling.HighestAdmittedTargetFrame));

        var rebaseClaimed = false;
        if (evaluation.Decision == PredictionLeadControllerDecision.RebaseRequired)
        {
            rebaseClaimed = scheduling.RebaseDebouncer.TryClaim(_lastCompletedFrame ?? _nextFrame);
        }
        else
        {
            scheduling.RebaseDebouncer.ObserveRebaseNotRequired();
        }

        scheduling.RecordLeadEvaluated(evaluation.Update);
        return new AuthorityLeadEmission(evaluation, rebaseClaimed);
    }

    /// <summary>
    /// Assembles the exact owner scheduling acknowledgement for one combatant:
    /// represented frame, applied input, application kind, received SACK,
    /// consumed cursor and bounded dispositions, unacknowledged journal
    /// resolutions, and the absolute lead currently in force.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Resolutions come from the journals' unacknowledged sets, not from the last
    /// frame's results, so a frame whose resolutions were produced while several
    /// frames ran in one callback is still reported. Each collection is sliced to
    /// its protocol bound; when more remains than fits,
    /// <see cref="AuthorityOwnerStatePublication.HasMoreToPublish"/> says so
    /// rather than silently truncating.
    /// </para>
    /// <para>
    /// Returns null before the combatant's first frame has been resolved, since
    /// there is no represented frame to report yet.
    /// </para>
    /// </remarks>
    public AuthorityOwnerStatePublication? BuildOwnerState(CombatantId combatantId)
    {
        if (!_combatants.TryGetValue(combatantId.Value, out var scheduling) ||
            scheduling.Scheduler.ConsumedThroughFrame is not { } representedFrame ||
            !scheduling.Scheduler.TryGetDisposition(representedFrame, out var appliedInput))
        {
            return null;
        }

        const int dispositionLimit = ProtocolConstants.MaxOwnerRecentInputDispositions;
        const int resolutionLimit = ProtocolConstants.MaxOwnerJournalEntriesPerBatch;

        // CopyRetainedDispositions returns the newest window that fits, which is
        // what repair wants: the client already has older ones or has moved past
        // them.
        var dispositionBuffer = new AuthorityFrameTerminalDisposition[dispositionLimit];
        var dispositionCount = scheduling.Scheduler.CopyRetainedDispositions(dispositionBuffer);

        // One extra slot detects "more remains" without a second pass.
        var transitionBuffer = new MovementTransitionResolution[resolutionLimit + 1];
        var transitionCount = scheduling.TransitionJournal
            .CopyUnacknowledgedResolutions(transitionBuffer);
        var actionBuffer = new PredictedActionResolution[resolutionLimit + 1];
        var actionCount = scheduling.ActionJournal
            .CopyUnacknowledgedResolutions(actionBuffer);

        var hasMore = transitionCount > resolutionLimit || actionCount > resolutionLimit;
        var transitions = transitionBuffer[..Math.Min(transitionCount, resolutionLimit)];
        var actions = actionBuffer[..Math.Min(actionCount, resolutionLimit)];

        var state = new AuthorityOwnerState(
            scheduling.Epoch,
            appliedInput,
            scheduling.Scheduler.ReceivedInputSequenceWindow,
            scheduling.Scheduler.ConsumedThroughFrame,
            dispositionBuffer[..dispositionCount],
            transitions.Length == 0
                ? null
                : transitions[^1].ResolutionIdentity.Sequence,
            transitions,
            actions.Length == 0
                ? null
                : actions[^1].ResolutionIdentity.Sequence,
            actions,
            scheduling.LastEmittedLeadUpdate);
        return new AuthorityOwnerStatePublication(state, hasMore);
    }

    private void RequireNotRunningFrames()
    {
        if (_frameRunInProgress)
        {
            throw new InvalidOperationException(
                "This operation cannot run while RunDueFrames is executing; it would make " +
                "the result depend on the order combatants were simulated in.");
        }
    }
}

/// <summary>
/// One combatant's owner state plus whether it exhausted what was pending.
/// </summary>
public readonly record struct AuthorityOwnerStatePublication
{
    internal AuthorityOwnerStatePublication(
        AuthorityOwnerState state,
        bool hasMoreToPublish)
    {
        State = state;
        HasMoreToPublish = hasMoreToPublish;
    }

    public AuthorityOwnerState State { get; }

    /// <summary>
    /// True when unacknowledged resolutions or dispositions remained after
    /// filling this message to its protocol bound, so the caller knows to publish
    /// again rather than assuming the client is caught up.
    /// </summary>
    public bool HasMoreToPublish { get; }
}

/// <summary>
/// The result of one lead evaluation: what the controller decided, and whether
/// this caller owns an outstanding rebase.
/// </summary>
public readonly record struct AuthorityLeadEmission
{
    internal AuthorityLeadEmission(
        PredictionLeadEvaluation evaluation,
        bool rebaseClaimed)
    {
        Evaluation = evaluation;
        RebaseClaimed = rebaseClaimed;
    }

    /// <summary>
    /// The controller's decision. Its <see cref="PredictionLeadEvaluation.Update"/>
    /// carries the absolute revisioned update when one is due, and null when the
    /// current lead still stands.
    /// </summary>
    public PredictionLeadEvaluation Evaluation { get; }

    /// <summary>
    /// True only on the evaluation that first observed a persistent rebase
    /// condition. Later evaluations of the same condition report false, so the
    /// caller emits one reliable rebase rather than one per frame.
    /// </summary>
    public bool RebaseClaimed { get; }
}
