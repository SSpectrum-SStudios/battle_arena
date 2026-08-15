using BattleArena.Core.Common;
using BattleArena.Core.Movement.Simulation;
using BattleArena.Multiplayer.AuthoritySimulation;
using BattleArena.Multiplayer.OwnerPrediction;
using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Protocol;

/// <summary>
/// What the wire told the client about one authority-consumed frame. Unlike
/// <see cref="AuthorityFrameTerminalDisposition"/>, which only the scheduler
/// that produced it can construct, this is the client's own read of the same
/// three facts: represented frame, applied input sequence, and application
/// kind. It carries no <c>ConsecutiveMissingFrames</c> or override reason
/// because <see cref="OwnerInputFrameDispositionDraft"/> does not wire them.
/// </summary>
public readonly record struct AuthorityFrameDispositionEcho(
    SimulationInstant Frame,
    InputSequence? AppliedInputSequence,
    AuthorityInputApplicationKind ApplicationKind);

/// <summary>
/// One combatant's exact owner-scheduling acknowledgement, composed from the
/// Phase 4 authority types this mapper translates. It deliberately excludes
/// <c>CharacterSimulationState</c>: the explicit rewind character state this
/// will eventually accompany is Phase 5 work.
/// </summary>
public readonly record struct AuthorityOwnerState
{
    public AuthorityOwnerState(
        CombatantAuthorityPredictionEpoch epoch,
        AuthorityFrameTerminalDisposition appliedInput,
        OwnerKnownJournalIdentityWindow receivedInputWindow,
        SimulationInstant? consumedThroughFrame,
        IReadOnlyList<AuthorityFrameTerminalDisposition> recentDispositions,
        TransitionResolutionSequence? latestTransitionResolutionSequence,
        IReadOnlyList<MovementTransitionResolution> transitionResolutions,
        ActionResolutionSequence? latestActionResolutionSequence,
        IReadOnlyList<PredictedActionResolution> actionResolutions,
        PredictionLeadUpdate? leadUpdate)
    {
        if (!epoch.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(epoch));
        }
        if (!receivedInputWindow.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(receivedInputWindow));
        }
        ArgumentNullException.ThrowIfNull(recentDispositions);
        ArgumentNullException.ThrowIfNull(transitionResolutions);
        ArgumentNullException.ThrowIfNull(actionResolutions);
        RequireRepresentableDisposition(appliedInput, nameof(appliedInput));

        // Bounded here rather than at encode time, so an over-long list is a
        // construction error at its source instead of a packet the peer's own
        // validator rejects. The scheduler's retention window is deliberately
        // larger than the wire allowance, so callers must slice.
        if (recentDispositions.Count > ProtocolConstants.MaxOwnerRecentInputDispositions)
        {
            throw new ArgumentException(
                $"At most {ProtocolConstants.MaxOwnerRecentInputDispositions} recent dispositions fit one acknowledgement.",
                nameof(recentDispositions));
        }
        if (transitionResolutions.Count > ProtocolConstants.MaxOwnerJournalEntriesPerBatch)
        {
            throw new ArgumentException(
                $"At most {ProtocolConstants.MaxOwnerJournalEntriesPerBatch} transition resolutions fit one batch.",
                nameof(transitionResolutions));
        }
        if (actionResolutions.Count > ProtocolConstants.MaxOwnerJournalEntriesPerBatch)
        {
            throw new ArgumentException(
                $"At most {ProtocolConstants.MaxOwnerJournalEntriesPerBatch} action resolutions fit one batch.",
                nameof(actionResolutions));
        }

        var scope = OwnerIntentScope.From(epoch);
        foreach (var disposition in recentDispositions)
        {
            RequireRepresentableDisposition(disposition, nameof(recentDispositions));
        }
        foreach (var resolution in transitionResolutions)
        {
            if (!resolution.IsValid || resolution.ResolutionIdentity.Scope != scope)
            {
                throw new ArgumentException(
                    "Every transition resolution must belong to this state's owner scope.",
                    nameof(transitionResolutions));
            }
        }
        foreach (var resolution in actionResolutions)
        {
            if (!resolution.IsValid || resolution.ResolutionIdentity.Scope != scope)
            {
                throw new ArgumentException(
                    "Every action resolution must belong to this state's owner scope.",
                    nameof(actionResolutions));
            }
        }
        if (leadUpdate is { } lead && (!lead.IsValid || lead.Scope != scope))
        {
            throw new ArgumentOutOfRangeException(nameof(leadUpdate));
        }

        Epoch = epoch;
        AppliedInput = appliedInput;
        ReceivedInputWindow = receivedInputWindow;
        ConsumedThroughFrame = consumedThroughFrame;
        // Copied, not aliased: the scope and bound checks above are the only
        // reason this type has a validating constructor, and a caller holding the
        // original list could otherwise append a foreign-scope entry afterwards.
        RecentDispositions = recentDispositions.ToArray();
        LatestTransitionResolutionSequence = latestTransitionResolutionSequence;
        TransitionResolutions = transitionResolutions.ToArray();
        LatestActionResolutionSequence = latestActionResolutionSequence;
        ActionResolutions = actionResolutions.ToArray();
        LeadUpdate = leadUpdate;
    }

    /// <summary>
    /// A <see langword="default"/> disposition reads as a
    /// <see cref="AuthorityInputApplicationKind.ReceivedCommand"/> at tick zero
    /// with no input sequence, because that kind is the zero enum value. That
    /// combination is self-contradictory and the peer's own validator rejects it,
    /// so it is refused here rather than encoded.
    /// </summary>
    internal static void RequireRepresentableDisposition(
        AuthorityFrameTerminalDisposition disposition,
        string parameterName)
    {
        if (disposition.UsedOwnerCommand && disposition.AppliedInputSequence is null)
        {
            throw new ArgumentException(
                "A received-command disposition must carry the input sequence it applied.",
                parameterName);
        }
    }

    public CombatantAuthorityPredictionEpoch Epoch { get; }
    public AuthorityFrameTerminalDisposition AppliedInput { get; }
    public OwnerKnownJournalIdentityWindow ReceivedInputWindow { get; }
    public SimulationInstant? ConsumedThroughFrame { get; }
    public IReadOnlyList<AuthorityFrameTerminalDisposition> RecentDispositions { get; }
    public TransitionResolutionSequence? LatestTransitionResolutionSequence { get; }
    public IReadOnlyList<MovementTransitionResolution> TransitionResolutions { get; }
    public ActionResolutionSequence? LatestActionResolutionSequence { get; }
    public IReadOnlyList<PredictedActionResolution> ActionResolutions { get; }
    public PredictionLeadUpdate? LeadUpdate { get; }
    public OwnerIntentScope Scope => OwnerIntentScope.From(Epoch);
}

/// <summary>
/// Translates Phase 4 authority scheduling/journal/lead types to and from the
/// protocol-next <c>*Draft</c> wire messages in <c>authority_state.proto</c>.
/// Every method here is a structural conversion: no admission, resolution, or
/// scheduling decision is made in this class. Where the wire schema omits a
/// value derivable from another field (an application/decision frame, or a
/// predicted action's start frame), the round trip recomputes it from the
/// same rule the originating domain constructor already enforces, rather than
/// carrying redundant bytes.
/// </summary>
public static class AuthorityOwnerStateMapper
{
    public static OwnerPredictionScopeDraft ToProtocol(
        CombatantAuthorityPredictionEpoch epoch)
    {
        if (!epoch.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(epoch));
        }

        return new OwnerPredictionScopeDraft
        {
            MatchFrameEpoch = epoch.MatchFrameEpoch.Value,
            // Both are validated positive by their own types, so these are
            // checked to match the decode direction rather than silently
            // reinterpreting a value the epoch guarantees cannot be negative.
            CombatantId = checked((ulong)epoch.CombatantId.Value),
            LifeId = checked((ulong)epoch.Life.Value),
            AuthorityDiscontinuityId = epoch.AuthorityDiscontinuity.Value,
            OwnerControlEpoch = epoch.OwnerControl.Value,
            SessionId = epoch.SessionId,
        };
    }

    public static CombatantAuthorityPredictionEpoch AuthorityEpochFromProtocol(
        OwnerPredictionScopeDraft scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return new CombatantAuthorityPredictionEpoch(
            scope.SessionId,
            new MatchFrameEpochId(scope.MatchFrameEpoch),
            new CombatantId(checked((long)scope.CombatantId)),
            new LifeGenerationId(checked((long)scope.LifeId)),
            new AuthorityDiscontinuityId(scope.AuthorityDiscontinuityId),
            new OwnerControlEpoch(scope.OwnerControlEpoch));
    }

    public static SelectiveSequenceAcknowledgementDraft ToProtocol(
        OwnerKnownJournalIdentityWindow window)
    {
        if (!window.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(window));
        }

        var draft = new SelectiveSequenceAcknowledgementDraft
        {
            Following64ReceivedMask = window.Following64KnownMask,
        };
        if (window.HighestContiguousId is { } highest)
        {
            draft.HighestContiguousSequence = highest;
        }

        return draft;
    }

    public static OwnerKnownJournalIdentityWindow ReceivedWindowFromProtocol(
        SelectiveSequenceAcknowledgementDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var window = new OwnerKnownJournalIdentityWindow(
            draft.HasHighestContiguousSequence ? draft.HighestContiguousSequence : null,
            draft.Following64ReceivedMask);
        if (!window.IsValid)
        {
            throw new ArgumentException(
                "The wire selective-acknowledgement window is malformed.",
                nameof(draft));
        }

        return window;
    }

    public static OwnerInputReceiveAcknowledgementDraft ToReceiveAcknowledgement(
        CombatantAuthorityPredictionEpoch epoch,
        OwnerKnownJournalIdentityWindow window) => new()
        {
            Scope = ToProtocol(epoch),
            ReceivedInputs = ToProtocol(window),
        };

    public static OwnerInputFrameDispositionDraft ToProtocol(
        AuthorityFrameTerminalDisposition disposition)
    {
        // Checked here as well as in the aggregate, because this and
        // ToConsumptionAcknowledgement are public entry points in their own
        // right and a default-valued disposition would otherwise encode as a
        // received command at tick zero carrying no sequence.
        AuthorityOwnerState.RequireRepresentableDisposition(disposition, nameof(disposition));
        var draft = new OwnerInputFrameDispositionDraft
        {
            TargetSimulationTick = checked((ulong)disposition.Frame.Tick),
            Kind = ToDraft(disposition.ApplicationKind),
        };
        if (disposition.AppliedInputSequence is { } sequence)
        {
            draft.InputSequence = sequence.Value;
        }

        return draft;
    }

    /// <summary>
    /// Re-encodes a decoded echo. This exists so the disposition round trip
    /// closes in both directions: without it the only encode path starts from
    /// <see cref="AuthorityFrameTerminalDisposition"/>, which only the scheduler
    /// that produced it can construct, and an idempotence check would have to
    /// re-use the original rather than the decoded value.
    /// </summary>
    public static OwnerInputFrameDispositionDraft ToProtocol(
        AuthorityFrameDispositionEcho echo)
    {
        if ((echo.ApplicationKind == AuthorityInputApplicationKind.ReceivedCommand) !=
            (echo.AppliedInputSequence is not null))
        {
            throw new ArgumentException(
                "Exactly the received-command kind carries an applied input sequence.",
                nameof(echo));
        }

        var draft = new OwnerInputFrameDispositionDraft
        {
            TargetSimulationTick = checked((ulong)echo.Frame.Tick),
            Kind = ToDraft(echo.ApplicationKind),
        };
        if (echo.AppliedInputSequence is { } sequence)
        {
            draft.InputSequence = sequence.Value;
        }

        return draft;
    }

    public static AuthorityFrameDispositionEcho DispositionEchoFromProtocol(
        OwnerInputFrameDispositionDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var kind = FromDraft(draft.Kind);
        InputSequence? sequence = draft.HasInputSequence
            ? new InputSequence(draft.InputSequence)
            : null;
        if ((kind == AuthorityInputApplicationKind.ReceivedCommand) != (sequence is not null))
        {
            throw new ArgumentException(
                "Exactly the received-command kind carries an applied input sequence.",
                nameof(draft));
        }

        return new AuthorityFrameDispositionEcho(
            new SimulationInstant(checked((long)draft.TargetSimulationTick)),
            sequence,
            kind);
    }

    public static OwnerInputConsumptionAcknowledgementDraft ToConsumptionAcknowledgement(
        CombatantAuthorityPredictionEpoch epoch,
        SimulationInstant? consumedThroughFrame,
        IReadOnlyList<AuthorityFrameTerminalDisposition> recentDispositions)
    {
        ArgumentNullException.ThrowIfNull(recentDispositions);
        var draft = new OwnerInputConsumptionAcknowledgementDraft
        {
            Scope = ToProtocol(epoch),
        };
        if (consumedThroughFrame is { } frame)
        {
            draft.ConsumedThroughSimulationTick = checked((ulong)frame.Tick);
        }
        foreach (var disposition in recentDispositions)
        {
            draft.RecentDispositions.Add(ToProtocol(disposition));
        }

        return draft;
    }

    public static MovementTransitionResolutionDraft ToProtocol(
        MovementTransitionResolution resolution)
    {
        if (!resolution.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(resolution));
        }

        return new MovementTransitionResolutionDraft
        {
            ResolutionSequence = resolution.ResolutionIdentity.Sequence.Value,
            TransitionId = resolution.TransitionIdentity.Id.Value,
            Outcome = ToDraft(resolution.Outcome),
            DecisionTick = checked((ulong)resolution.DecisionFrame.Tick),
            RejectionReason = ToDraft(resolution.RejectionReason),
        };
    }

    public static MovementTransitionResolution TransitionResolutionFromProtocol(
        OwnerIntentScope scope,
        MovementTransitionResolutionDraft draft)
    {
        if (!scope.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(scope));
        }
        ArgumentNullException.ThrowIfNull(draft);

        var outcome = FromDraft(draft.Outcome);
        var decisionFrame = new SimulationInstant(checked((long)draft.DecisionTick));
        var applied = outcome is MovementTransitionOutcome.Accepted or
            MovementTransitionOutcome.Remapped;
        return new MovementTransitionResolution(
            new TransitionResolutionIdentity(
                scope,
                new TransitionResolutionSequence(draft.ResolutionSequence)),
            new MovementTransitionIdentity(scope, new MovementTransitionId(draft.TransitionId)),
            outcome,
            applied,
            applied ? decisionFrame : default,
            decisionFrame,
            FromDraft(draft.RejectionReason));
    }

    public static PredictedActionResolutionDraft ToProtocol(
        PredictedActionResolution resolution)
    {
        if (!resolution.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(resolution));
        }

        var draft = new PredictedActionResolutionDraft
        {
            ResolutionSequence = resolution.ResolutionIdentity.Sequence.Value,
            ActionId = resolution.ActionIdentity.Id.Value,
            Outcome = ToDraft(resolution.Outcome),
            DecisionTick = checked((ulong)resolution.DecisionFrame.Tick),
            RejectionReason = ToDraft(resolution.RejectionReason),
        };
        if (resolution.HasAuthorityExecution)
        {
            draft.AuthorityExecutionId = resolution.AuthorityExecution.Id.Value;
        }

        return draft;
    }

    public static PredictedActionResolution ActionResolutionFromProtocol(
        OwnerIntentScope scope,
        PredictedActionResolutionDraft draft)
    {
        if (!scope.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(scope));
        }
        ArgumentNullException.ThrowIfNull(draft);

        var outcome = FromDraft(draft.Outcome);
        var decisionFrame = new SimulationInstant(checked((long)draft.DecisionTick));
        var applied = outcome is PredictedActionOutcome.Accepted or
            PredictedActionOutcome.Remapped;
        if (applied != draft.HasAuthorityExecutionId)
        {
            throw new ArgumentException(
                "An accepted or remapped action must carry an authority execution id; a terminal one must not.",
                nameof(draft));
        }

        var executionScope = new AuthorityActionExecutionScope(scope.SessionId, scope.Life);
        var execution = applied
            ? new AuthorityActionExecutionIdentity(
                executionScope,
                new AuthorityActionExecutionId(draft.AuthorityExecutionId))
            : default;
        return new PredictedActionResolution(
            new ActionResolutionIdentity(scope, new ActionResolutionSequence(draft.ResolutionSequence)),
            new PredictedActionIdentity(scope, new PredictedActionId(draft.ActionId)),
            outcome,
            applied,
            execution,
            applied ? decisionFrame : default,
            decisionFrame,
            FromDraft(draft.RejectionReason));
    }

    public static OwnerPredictionLeadUpdateDraft ToProtocol(
        CombatantAuthorityPredictionEpoch epoch,
        PredictionLeadUpdate update)
    {
        if (!epoch.IsValid || !update.IsValid || update.Scope != OwnerIntentScope.From(epoch))
        {
            throw new ArgumentException(
                "The lead update must belong to this epoch's owner scope.",
                nameof(update));
        }

        return new OwnerPredictionLeadUpdateDraft
        {
            Scope = ToProtocol(epoch),
            TargetLeadFrames = checked((uint)update.TargetLead.Value),
            LeadPolicyRevision = update.Revision.Value,
            EffectiveTick = checked((ulong)update.EffectiveFrame.Tick),
        };
    }

    public static PredictionLeadUpdate LeadUpdateFromProtocol(
        OwnerPredictionLeadUpdateDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var scope = OwnerIntentScope.From(AuthorityEpochFromProtocol(draft.Scope));
        return new PredictionLeadUpdate(
            scope,
            new PredictionLeadFrameCount(checked((int)draft.TargetLeadFrames)),
            new PredictionLeadPolicyRevision(draft.LeadPolicyRevision),
            new SimulationInstant(checked((long)draft.EffectiveTick)));
    }

    public static AuthorityOwnerStateDraft ToProtocol(AuthorityOwnerState state)
    {
        var draft = new AuthorityOwnerStateDraft
        {
            Scope = ToProtocol(state.Epoch),
            AppliedInput = ToProtocol(state.AppliedInput),
            ReceivedInputs = ToReceiveAcknowledgement(state.Epoch, state.ReceivedInputWindow),
            ConsumedInputs = ToConsumptionAcknowledgement(
                state.Epoch,
                state.ConsumedThroughFrame,
                state.RecentDispositions),
        };
        if (state.LatestTransitionResolutionSequence is { } transitionCursor)
        {
            draft.LatestTransitionResolutionSequence = transitionCursor.Value;
        }
        foreach (var resolution in state.TransitionResolutions)
        {
            draft.TransitionResolutions.Add(ToProtocol(resolution));
        }
        if (state.LatestActionResolutionSequence is { } actionCursor)
        {
            draft.LatestActionResolutionSequence = actionCursor.Value;
        }
        foreach (var resolution in state.ActionResolutions)
        {
            draft.ActionResolutions.Add(ToProtocol(resolution));
        }
        if (state.LeadUpdate is { } lead)
        {
            draft.LeadUpdate = ToProtocol(state.Epoch, lead);
        }

        return draft;
    }

    /// <summary>
    /// Reconstructs everything the wire message can carry back into domain
    /// values, except <see cref="AuthorityFrameTerminalDisposition"/> itself:
    /// that type is constructible only by the scheduler that produced it, so
    /// <c>applied_input</c> and each entry of <c>recent_dispositions</c> come
    /// back as <see cref="AuthorityFrameDispositionEcho"/> instead.
    /// </summary>
    public static (
        CombatantAuthorityPredictionEpoch Epoch,
        AuthorityFrameDispositionEcho AppliedInput,
        OwnerKnownJournalIdentityWindow ReceivedInputWindow,
        SimulationInstant? ConsumedThroughFrame,
        IReadOnlyList<AuthorityFrameDispositionEcho> RecentDispositions,
        TransitionResolutionSequence? LatestTransitionResolutionSequence,
        IReadOnlyList<MovementTransitionResolution> TransitionResolutions,
        ActionResolutionSequence? LatestActionResolutionSequence,
        IReadOnlyList<PredictedActionResolution> ActionResolutions,
        PredictionLeadUpdate? LeadUpdate) FromProtocol(AuthorityOwnerStateDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        // Every nested message is optional on the wire, so omitting one is
        // ordinary proto3 rather than a malformed packet. These are checked
        // explicitly because this path will eventually decode hostile bytes and
        // must fail with a stated reason instead of a null dereference.
        RequirePresent(draft.Scope, "scope");
        RequirePresent(draft.AppliedInput, "applied_input");
        RequirePresent(draft.ReceivedInputs, "received_inputs");
        RequirePresent(draft.ReceivedInputs.ReceivedInputs, "received_inputs.received_inputs");
        RequirePresent(draft.ConsumedInputs, "consumed_inputs");

        var epoch = AuthorityEpochFromProtocol(draft.Scope);
        var scope = OwnerIntentScope.From(epoch);

        // The scope rides the wire three times. The encode side treats their
        // agreement as an invariant, so the decode side enforces it rather than
        // silently preferring one copy and letting the others disagree.
        RequireSameScope(draft.ReceivedInputs.Scope, draft.Scope, "received_inputs.scope");
        RequireSameScope(draft.ConsumedInputs.Scope, draft.Scope, "consumed_inputs.scope");
        if (draft.LeadUpdate is not null)
        {
            RequireSameScope(draft.LeadUpdate.Scope, draft.Scope, "lead_update.scope");
        }

        if (draft.ConsumedInputs.RecentDispositions.Count >
            ProtocolConstants.MaxOwnerRecentInputDispositions)
        {
            throw new ArgumentException(
                "The acknowledgement carries more recent dispositions than the protocol permits.",
                nameof(draft));
        }
        if (draft.TransitionResolutions.Count > ProtocolConstants.MaxOwnerJournalEntriesPerBatch ||
            draft.ActionResolutions.Count > ProtocolConstants.MaxOwnerJournalEntriesPerBatch)
        {
            throw new ArgumentException(
                "The acknowledgement carries more journal resolutions than the protocol permits.",
                nameof(draft));
        }

        var recentDispositions = new List<AuthorityFrameDispositionEcho>(
            draft.ConsumedInputs.RecentDispositions.Count);
        foreach (var disposition in draft.ConsumedInputs.RecentDispositions)
        {
            recentDispositions.Add(DispositionEchoFromProtocol(disposition));
        }

        var transitionResolutions = new List<MovementTransitionResolution>(
            draft.TransitionResolutions.Count);
        foreach (var resolution in draft.TransitionResolutions)
        {
            transitionResolutions.Add(TransitionResolutionFromProtocol(scope, resolution));
        }

        var actionResolutions = new List<PredictedActionResolution>(
            draft.ActionResolutions.Count);
        foreach (var resolution in draft.ActionResolutions)
        {
            actionResolutions.Add(ActionResolutionFromProtocol(scope, resolution));
        }

        return (
            epoch,
            DispositionEchoFromProtocol(draft.AppliedInput),
            ReceivedWindowFromProtocol(draft.ReceivedInputs.ReceivedInputs),
            draft.ConsumedInputs.HasConsumedThroughSimulationTick
                ? new SimulationInstant(checked((long)draft.ConsumedInputs.ConsumedThroughSimulationTick))
                : null,
            recentDispositions,
            draft.HasLatestTransitionResolutionSequence
                ? new TransitionResolutionSequence(draft.LatestTransitionResolutionSequence)
                : null,
            transitionResolutions,
            draft.HasLatestActionResolutionSequence
                ? new ActionResolutionSequence(draft.LatestActionResolutionSequence)
                : null,
            actionResolutions,
            draft.LeadUpdate is null ? null : LeadUpdateFromProtocol(draft.LeadUpdate));
    }

    private static void RequirePresent(object? message, string fieldName)
    {
        if (message is null)
        {
            throw new ArgumentException(
                $"The authority owner state is missing its required '{fieldName}' message.",
                nameof(message));
        }
    }

    private static void RequireSameScope(
        OwnerPredictionScopeDraft? nested,
        OwnerPredictionScopeDraft expected,
        string fieldName)
    {
        if (nested is null || !nested.Equals(expected))
        {
            throw new ArgumentException(
                $"'{fieldName}' does not match the authority owner state's own scope.",
                nameof(nested));
        }
    }

    private static OwnerInputFrameDispositionKindDraft ToDraft(
        AuthorityInputApplicationKind kind) => kind switch
        {
            AuthorityInputApplicationKind.ReceivedCommand =>
                OwnerInputFrameDispositionKindDraft.ReceivedCommand,
            AuthorityInputApplicationKind.RepeatedContinuous =>
                OwnerInputFrameDispositionKindDraft.RepeatedContinuousFallback,
            AuthorityInputApplicationKind.NeutralFallback =>
                OwnerInputFrameDispositionKindDraft.NeutralFallback,
            AuthorityInputApplicationKind.AuthorityOverride =>
                OwnerInputFrameDispositionKindDraft.AuthorityOverride,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static AuthorityInputApplicationKind FromDraft(
        OwnerInputFrameDispositionKindDraft kind) => kind switch
        {
            OwnerInputFrameDispositionKindDraft.ReceivedCommand =>
                AuthorityInputApplicationKind.ReceivedCommand,
            OwnerInputFrameDispositionKindDraft.RepeatedContinuousFallback =>
                AuthorityInputApplicationKind.RepeatedContinuous,
            OwnerInputFrameDispositionKindDraft.NeutralFallback =>
                AuthorityInputApplicationKind.NeutralFallback,
            OwnerInputFrameDispositionKindDraft.AuthorityOverride =>
                AuthorityInputApplicationKind.AuthorityOverride,
            // REJECTED_COMMAND and LATE_COMMAND exist in the wire enum and the
            // existing validator accepts them, but they are arrival
            // classifications, not frame applications: a rejected or late packet
            // never supplies a frame's input. This message reports what the
            // authority applied, so those values are refused here. Publishing
            // arrival outcomes is separate work and needs its own carrier.
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                "Rejected and late arrivals are not input applications and cannot describe a consumed frame."),
        };

    private static MovementTransitionOutcomeDraft ToDraft(MovementTransitionOutcome outcome) =>
        outcome switch
        {
            MovementTransitionOutcome.Accepted => MovementTransitionOutcomeDraft.Accepted,
            MovementTransitionOutcome.Remapped => MovementTransitionOutcomeDraft.Remapped,
            MovementTransitionOutcome.Rejected => MovementTransitionOutcomeDraft.Rejected,
            MovementTransitionOutcome.Expired => MovementTransitionOutcomeDraft.Expired,
            MovementTransitionOutcome.Superseded => MovementTransitionOutcomeDraft.Superseded,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
        };

    private static MovementTransitionOutcome FromDraft(MovementTransitionOutcomeDraft outcome) =>
        outcome switch
        {
            MovementTransitionOutcomeDraft.Accepted => MovementTransitionOutcome.Accepted,
            MovementTransitionOutcomeDraft.Remapped => MovementTransitionOutcome.Remapped,
            MovementTransitionOutcomeDraft.Rejected => MovementTransitionOutcome.Rejected,
            MovementTransitionOutcomeDraft.Expired => MovementTransitionOutcome.Expired,
            MovementTransitionOutcomeDraft.Superseded => MovementTransitionOutcome.Superseded,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
        };

    private static MovementTransitionRejectionReasonDraft ToDraft(
        MovementTransitionRejectionReason reason) => reason switch
        {
            MovementTransitionRejectionReason.None =>
                MovementTransitionRejectionReasonDraft.None,
            MovementTransitionRejectionReason.AuthorityPolicyRejected =>
                MovementTransitionRejectionReasonDraft.AuthorityPolicyRejected,
            MovementTransitionRejectionReason.InvalidState =>
                MovementTransitionRejectionReasonDraft.InvalidState,
            MovementTransitionRejectionReason.CapabilityUnavailable =>
                MovementTransitionRejectionReasonDraft.CapabilityUnavailable,
            MovementTransitionRejectionReason.DeadlineExpired =>
                MovementTransitionRejectionReasonDraft.DeadlineExpired,
            MovementTransitionRejectionReason.Superseded =>
                MovementTransitionRejectionReasonDraft.Superseded,
            _ => throw new ArgumentOutOfRangeException(nameof(reason)),
        };

    private static MovementTransitionRejectionReason FromDraft(
        MovementTransitionRejectionReasonDraft reason) => reason switch
        {
            MovementTransitionRejectionReasonDraft.None =>
                MovementTransitionRejectionReason.None,
            MovementTransitionRejectionReasonDraft.AuthorityPolicyRejected =>
                MovementTransitionRejectionReason.AuthorityPolicyRejected,
            MovementTransitionRejectionReasonDraft.InvalidState =>
                MovementTransitionRejectionReason.InvalidState,
            MovementTransitionRejectionReasonDraft.CapabilityUnavailable =>
                MovementTransitionRejectionReason.CapabilityUnavailable,
            MovementTransitionRejectionReasonDraft.DeadlineExpired =>
                MovementTransitionRejectionReason.DeadlineExpired,
            MovementTransitionRejectionReasonDraft.Superseded =>
                MovementTransitionRejectionReason.Superseded,
            _ => throw new ArgumentOutOfRangeException(nameof(reason)),
        };

    private static PredictedActionOutcomeDraft ToDraft(PredictedActionOutcome outcome) =>
        outcome switch
        {
            PredictedActionOutcome.Accepted => PredictedActionOutcomeDraft.Accepted,
            PredictedActionOutcome.Remapped => PredictedActionOutcomeDraft.Remapped,
            PredictedActionOutcome.Rejected => PredictedActionOutcomeDraft.Rejected,
            PredictedActionOutcome.Expired => PredictedActionOutcomeDraft.Expired,
            PredictedActionOutcome.Superseded => PredictedActionOutcomeDraft.Superseded,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
        };

    private static PredictedActionOutcome FromDraft(PredictedActionOutcomeDraft outcome) =>
        outcome switch
        {
            PredictedActionOutcomeDraft.Accepted => PredictedActionOutcome.Accepted,
            PredictedActionOutcomeDraft.Remapped => PredictedActionOutcome.Remapped,
            PredictedActionOutcomeDraft.Rejected => PredictedActionOutcome.Rejected,
            PredictedActionOutcomeDraft.Expired => PredictedActionOutcome.Expired,
            PredictedActionOutcomeDraft.Superseded => PredictedActionOutcome.Superseded,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
        };

    private static PredictedActionRejectionReasonDraft ToDraft(
        PredictedActionRejectionReason reason) => reason switch
        {
            PredictedActionRejectionReason.None =>
                PredictedActionRejectionReasonDraft.None,
            PredictedActionRejectionReason.AuthorityPolicyRejected =>
                PredictedActionRejectionReasonDraft.AuthorityPolicyRejected,
            PredictedActionRejectionReason.InvalidState =>
                PredictedActionRejectionReasonDraft.InvalidState,
            PredictedActionRejectionReason.CooldownActive =>
                PredictedActionRejectionReasonDraft.CooldownActive,
            PredictedActionRejectionReason.CapabilityUnavailable =>
                PredictedActionRejectionReasonDraft.CapabilityUnavailable,
            PredictedActionRejectionReason.DeadlineExpired =>
                PredictedActionRejectionReasonDraft.DeadlineExpired,
            PredictedActionRejectionReason.Superseded =>
                PredictedActionRejectionReasonDraft.Superseded,
            _ => throw new ArgumentOutOfRangeException(nameof(reason)),
        };

    private static PredictedActionRejectionReason FromDraft(
        PredictedActionRejectionReasonDraft reason) => reason switch
        {
            PredictedActionRejectionReasonDraft.None =>
                PredictedActionRejectionReason.None,
            PredictedActionRejectionReasonDraft.AuthorityPolicyRejected =>
                PredictedActionRejectionReason.AuthorityPolicyRejected,
            PredictedActionRejectionReasonDraft.InvalidState =>
                PredictedActionRejectionReason.InvalidState,
            PredictedActionRejectionReasonDraft.CooldownActive =>
                PredictedActionRejectionReason.CooldownActive,
            PredictedActionRejectionReasonDraft.CapabilityUnavailable =>
                PredictedActionRejectionReason.CapabilityUnavailable,
            PredictedActionRejectionReasonDraft.DeadlineExpired =>
                PredictedActionRejectionReason.DeadlineExpired,
            PredictedActionRejectionReasonDraft.Superseded =>
                PredictedActionRejectionReason.Superseded,
            _ => throw new ArgumentOutOfRangeException(nameof(reason)),
        };
}
