using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Protocol;

public readonly record struct OwnerPredictionDraftScopeExpectation(
    ulong SessionId,
    ulong MatchFrameEpoch,
    ulong CombatantId,
    ulong LifeId,
    ulong AuthorityDiscontinuityId,
    ulong OwnerControlEpoch)
{
    public bool IsValid =>
        SessionId != 0 &&
        MatchFrameEpoch != 0 &&
        CombatantId != 0 &&
        LifeId != 0 &&
        AuthorityDiscontinuityId != 0 &&
        OwnerControlEpoch != 0;
}

public readonly record struct OwnerKnownJournalIdentityWindow(
    ulong? HighestContiguousId,
    ulong Following64KnownMask)
{
    public bool IsValid =>
        (HighestContiguousId is null or > 0) &&
        (HighestContiguousId != ulong.MaxValue || Following64KnownMask == 0);

    public bool Contains(ulong id)
    {
        if (!IsValid || id == 0)
        {
            return false;
        }
        var cursor = HighestContiguousId ?? 0;
        if (HighestContiguousId is not null && id <= cursor)
        {
            return true;
        }
        if (id <= cursor || id - cursor > 64)
        {
            return false;
        }
        return (Following64KnownMask & (1UL << ((int)(id - cursor) - 1))) != 0;
    }

    public bool IsBoundedBy(ulong maximumId)
    {
        if (!IsValid || (HighestContiguousId ?? 0) > maximumId)
        {
            return false;
        }
        var cursor = HighestContiguousId ?? 0;
        for (var bit = 0; bit < 64; bit++)
        {
            if ((Following64KnownMask & (1UL << bit)) == 0)
            {
                continue;
            }
            var offset = (ulong)bit + 1;
            if (cursor > ulong.MaxValue - offset || cursor + offset > maximumId)
            {
                return false;
            }
        }
        return true;
    }
}

public readonly record struct OwnerCommandDraftValidationContext(
    OwnerPredictionDraftScopeExpectation ExpectedScope,
    ulong EarliestRetainedTargetTick,
    ulong LatestPermittedTargetTick,
    ulong HighestAuthorityFramePublished,
    ulong HighestAuthorityStreamSequencePublished,
    ulong HighestKnownInputSequence,
    ulong HighestOriginatedInputSequence,
    ulong MaximumPermittedInputSequence,
    OwnerKnownJournalIdentityWindow KnownTransitionIds,
    ulong MaximumPermittedTransitionId,
    OwnerKnownJournalIdentityWindow KnownActionIds,
    ulong MaximumPermittedActionId,
    ulong MaximumMovementProfileRevision,
    ulong MaximumMovementCapabilityRevision,
    ulong? MaximumIssuedTransitionResolutionSequence,
    ulong? MaximumIssuedActionResolutionSequence)
{
    public bool IsValid =>
        ExpectedScope.IsValid &&
        EarliestRetainedTargetTick <= LatestPermittedTargetTick &&
        LatestPermittedTargetTick <= long.MaxValue &&
        HighestAuthorityFramePublished <= long.MaxValue &&
        HighestKnownInputSequence <= MaximumPermittedInputSequence &&
        HighestOriginatedInputSequence <= MaximumPermittedInputSequence &&
        KnownTransitionIds.IsBoundedBy(MaximumPermittedTransitionId) &&
        KnownActionIds.IsBoundedBy(MaximumPermittedActionId) &&
        MaximumMovementProfileRevision != 0 &&
        MaximumMovementCapabilityRevision != 0 &&
        (MaximumIssuedTransitionResolutionSequence is null or > 0) &&
        (MaximumIssuedActionResolutionSequence is null or > 0);
}

public readonly record struct OwnerControlDraftValidationContext(
    OwnerPredictionDraftScopeExpectation ExpectedScope,
    ulong HighestAuthorityFramePublished,
    ulong EarliestLeadEffectiveTick,
    uint NegotiatedSimulationTicksPerSecond,
    uint MinimumLeadFrames,
    uint MaximumLeadFrames,
    ulong MaximumMovementProfileRevision,
    ulong MaximumMovementCapabilityRevision,
    Google.Protobuf.ByteString ExpectedCollisionContentHash)
{
    public bool IsValid =>
        ExpectedScope.IsValid &&
        HighestAuthorityFramePublished <= long.MaxValue &&
        EarliestLeadEffectiveTick <= long.MaxValue &&
        EarliestLeadEffectiveTick > HighestAuthorityFramePublished &&
        NegotiatedSimulationTicksPerSecond != 0 &&
        MinimumLeadFrames != 0 &&
        MinimumLeadFrames <= MaximumLeadFrames &&
        MaximumLeadFrames <= ProtocolConstants.MaxOwnerPredictionLeadFrames &&
        MaximumMovementProfileRevision != 0 &&
        MaximumMovementCapabilityRevision != 0 &&
        ExpectedCollisionContentHash is not null &&
        ExpectedCollisionContentHash.Length ==
            ProtocolConstants.OwnerCollisionContentHashBytes;
}

public sealed class InboundMessageValidator
{
    private const float HalfPi = MathF.PI / 2f;

    public ProtocolValidationResult ValidateOwnerCommandBatchDraft(
        OwnerCommandBatchDraft batch,
        OwnerCommandDraftValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (!context.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(context));
        }

        var scope = ValidateOwnerScope(batch.Scope, context.ExpectedScope);
        if (!scope.IsValid)
        {
            return scope;
        }
        if (batch.PacketSequence == 0)
        {
            return Invalid(
                ProtocolViolationCode.InvalidSequence,
                "Owner command packet sequence must be positive.");
        }
        if (batch.Commands.Count > ProtocolConstants.MaxOwnerCommandsPerBatch ||
            batch.OutstandingTransitions.Count >
                ProtocolConstants.MaxOwnerJournalEntriesPerBatch ||
            batch.OutstandingActions.Count >
                ProtocolConstants.MaxOwnerJournalEntriesPerBatch)
        {
            return Invalid(
                ProtocolViolationCode.InvalidCollectionCount,
                "Owner command batch exceeds a bounded command or journal count.");
        }
        if (batch.CalculateSize() > ProtocolConstants.MaxOwnerCommandDraftBodyBytes)
        {
            return Invalid(
                ProtocolViolationCode.PacketTooLarge,
                "Owner command batch exceeds its conservative datagram body allowance.");
        }
        if (batch.LatestAuthorityFrameObserved >
                context.HighestAuthorityFramePublished ||
            batch.LatestAuthorityStreamSequenceObserved >
                context.HighestAuthorityStreamSequencePublished)
        {
            return Invalid(
                ProtocolViolationCode.InvalidNumericValue,
                "Owner command batch claims authority evidence that was not published.");
        }

        var transitionCursor = ValidateResolutionCursor(
            batch.HasTransitionResolutionCursorApplied,
            batch.TransitionResolutionCursorApplied,
            context.MaximumIssuedTransitionResolutionSequence,
            "transition");
        if (!transitionCursor.IsValid)
        {
            return transitionCursor;
        }
        var actionCursor = ValidateResolutionCursor(
            batch.HasActionResolutionCursorApplied,
            batch.ActionResolutionCursorApplied,
            context.MaximumIssuedActionResolutionSequence,
            "action");
        if (!actionCursor.IsValid)
        {
            return actionCursor;
        }

        var transitionIds = new HashSet<ulong>();
        var transitionOrigins = new Dictionary<ulong, ulong>();
        foreach (var intent in batch.OutstandingTransitions)
        {
            var result = ValidateMovementTransitionIntent(
                intent,
                batch,
                context,
                transitionIds);
            if (!result.IsValid)
            {
                return result;
            }
            transitionOrigins.Add(
                intent.TransitionId,
                intent.OriginatingInputSequence);
        }

        var actionIds = new HashSet<ulong>();
        var actionOrigins = new Dictionary<ulong, ulong>();
        foreach (var intent in batch.OutstandingActions)
        {
            var result = ValidatePredictedActionIntent(
                intent,
                batch,
                context,
                actionIds);
            if (!result.IsValid)
            {
                return result;
            }
            actionOrigins.Add(intent.ActionId, intent.OriginatingInputSequence);
        }

        var inputSequences = new HashSet<ulong>();
        var targetTicks = new HashSet<ulong>();
        var transitionReferences = new HashSet<ulong>();
        var actionReferences = new HashSet<ulong>();
        foreach (var command in batch.Commands)
        {
            var result = ValidateOwnerSimulationCommand(
                command,
                context,
                transitionOrigins,
                actionOrigins,
                inputSequences,
                targetTicks,
                transitionReferences,
                actionReferences);
            if (!result.IsValid)
            {
                return result;
            }
        }

        foreach (var intent in batch.OutstandingTransitions)
        {
            if (!context.KnownTransitionIds.Contains(intent.TransitionId) &&
                !transitionReferences.Contains(intent.TransitionId))
            {
                return Invalid(
                    ProtocolViolationCode.InvalidSequence,
                    "A first-seen transition must be attached to its originating command.");
            }
        }
        foreach (var intent in batch.OutstandingActions)
        {
            if (!context.KnownActionIds.Contains(intent.ActionId) &&
                !actionReferences.Contains(intent.ActionId))
            {
                return Invalid(
                    ProtocolViolationCode.InvalidSequence,
                    "A first-seen action must be attached to its originating command.");
            }
        }

        return ProtocolValidationResult.Valid;
    }

    public ProtocolValidationResult ValidateOwnerInputReceiveAcknowledgementDraft(
        OwnerInputReceiveAcknowledgementDraft acknowledgement,
        OwnerCommandDraftValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(acknowledgement);
        if (!context.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(context));
        }
        var scope = ValidateOwnerScope(
            acknowledgement.Scope,
            context.ExpectedScope);
        if (!scope.IsValid)
        {
            return scope;
        }
        if (acknowledgement.ReceivedInputs is null)
        {
            return Invalid(
                ProtocolViolationCode.InvalidNumericValue,
                "Owner receive acknowledgement is missing selective evidence.");
        }

        var selective = acknowledgement.ReceivedInputs;
        var cursor = selective.HasHighestContiguousSequence
            ? selective.HighestContiguousSequence
            : 0UL;
        if ((selective.HasHighestContiguousSequence && cursor == 0) ||
            cursor > context.HighestOriginatedInputSequence)
        {
            return Invalid(
                ProtocolViolationCode.InvalidSequence,
                "Owner receive cursor acknowledges an unoriginated input.");
        }
        for (var bit = 0; bit < 64; bit++)
        {
            if ((selective.Following64ReceivedMask & (1UL << bit)) == 0)
            {
                continue;
            }
            var offset = (ulong)bit + 1;
            if (cursor > ulong.MaxValue - offset ||
                cursor + offset > context.HighestOriginatedInputSequence)
            {
                return Invalid(
                    ProtocolViolationCode.InvalidSequence,
                    "Owner receive SACK acknowledges an unoriginated input.");
            }
        }
        return ProtocolValidationResult.Valid;
    }

    public ProtocolValidationResult ValidateOwnerInputConsumptionAcknowledgementDraft(
        OwnerInputConsumptionAcknowledgementDraft acknowledgement,
        OwnerCommandDraftValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(acknowledgement);
        if (!context.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(context));
        }
        var scope = ValidateOwnerScope(
            acknowledgement.Scope,
            context.ExpectedScope);
        if (!scope.IsValid)
        {
            return scope;
        }
        if (acknowledgement.RecentDispositions.Count >
            ProtocolConstants.MaxOwnerRecentInputDispositions)
        {
            return Invalid(
                ProtocolViolationCode.InvalidCollectionCount,
                "Owner consumption acknowledgement has too many dispositions.");
        }
        if (!acknowledgement.HasConsumedThroughSimulationTick)
        {
            return acknowledgement.RecentDispositions.Count == 0
                ? ProtocolValidationResult.Valid
                : Invalid(
                    ProtocolViolationCode.InvalidNumericValue,
                    "Input dispositions require a consumed-through cursor.");
        }
        if (!FitsSimulationInstant(acknowledgement.ConsumedThroughSimulationTick) ||
            acknowledgement.ConsumedThroughSimulationTick >
                context.HighestAuthorityFramePublished)
        {
            return Invalid(
                ProtocolViolationCode.InvalidNumericValue,
                "Consumed-through frame is outside published authority time.");
        }

        var targetTicks = new HashSet<ulong>();
        var inputSequences = new HashSet<ulong>();
        foreach (var disposition in acknowledgement.RecentDispositions)
        {
            if (!FitsSimulationInstant(disposition.TargetSimulationTick) ||
                disposition.TargetSimulationTick <
                    context.EarliestRetainedTargetTick ||
                disposition.TargetSimulationTick >
                    acknowledgement.ConsumedThroughSimulationTick ||
                !targetTicks.Add(disposition.TargetSimulationTick))
            {
                return Invalid(
                    ProtocolViolationCode.InvalidNumericValue,
                    "Input disposition target frames must be unique and consumed.");
            }
            if (!Enum.IsDefined(disposition.Kind) ||
                disposition.Kind == OwnerInputFrameDispositionKindDraft.Unspecified)
            {
                return Invalid(
                    ProtocolViolationCode.InvalidEnumValue,
                    "Input disposition kind is not recognized.");
            }
            if (disposition.HasInputSequence &&
                (disposition.InputSequence == 0 ||
                 disposition.InputSequence > context.HighestOriginatedInputSequence ||
                 !inputSequences.Add(disposition.InputSequence)))
            {
                return Invalid(
                    ProtocolViolationCode.InvalidSequence,
                    "Input disposition names an unoriginated input sequence.");
            }

            var requiresInput = disposition.Kind is
                OwnerInputFrameDispositionKindDraft.ReceivedCommand or
                OwnerInputFrameDispositionKindDraft.RejectedCommand or
                OwnerInputFrameDispositionKindDraft.LateCommand;
            var forbidsInput = disposition.Kind is
                OwnerInputFrameDispositionKindDraft.RepeatedContinuousFallback or
                OwnerInputFrameDispositionKindDraft.NeutralFallback;
            if ((requiresInput && !disposition.HasInputSequence) ||
                (forbidsInput && disposition.HasInputSequence))
            {
                return Invalid(
                    ProtocolViolationCode.InvalidNumericValue,
                    "Input disposition sequence presence contradicts its kind.");
            }
        }
        return ProtocolValidationResult.Valid;
    }

    public ProtocolValidationResult ValidateOwnerPredictionBootstrapPlanDraft(
        OwnerPredictionBootstrapPlanDraft plan,
        OwnerControlDraftValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!context.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(context));
        }
        var scope = ValidateOwnerScope(plan.Scope, context.ExpectedScope);
        if (!scope.IsValid)
        {
            return scope;
        }
        if (plan.PlanId == 0 || plan.LeadPolicyRevision == 0)
        {
            return Invalid(
                ProtocolViolationCode.InvalidSequence,
                "Bootstrap plan and lead policy revisions must be positive.");
        }
        if (!Enum.IsDefined(plan.Kind) ||
            plan.Kind == OwnerPredictionBootstrapKindDraft.Unspecified ||
            !Enum.IsDefined(plan.Preparation) ||
            plan.Preparation == OwnerPredictionBaselinePreparationDraft.Unspecified)
        {
            return Invalid(
                ProtocolViolationCode.InvalidEnumValue,
                "Bootstrap kind or baseline preparation is not recognized.");
        }
        if (plan.SimulationTicksPerSecond !=
                context.NegotiatedSimulationTicksPerSecond ||
            plan.AuthorityFrameBoundaryTimestampMicroseconds == 0 ||
            plan.MovementProfileRevision == 0 ||
            plan.MovementProfileRevision >
                context.MaximumMovementProfileRevision ||
            plan.MovementCapabilityRevision == 0 ||
            plan.MovementCapabilityRevision >
                context.MaximumMovementCapabilityRevision ||
            plan.CollisionContentHash.Length !=
                ProtocolConstants.OwnerCollisionContentHashBytes ||
            plan.CollisionContentHash != context.ExpectedCollisionContentHash)
        {
            return Invalid(
                ProtocolViolationCode.InvalidNumericValue,
                "Bootstrap simulation, configuration, or collision identity is invalid.");
        }
        if (!FitsSimulationInstant(plan.PublishedAuthorityTick) ||
            !FitsSimulationInstant(plan.BaselineTick) ||
            !FitsSimulationInstant(plan.LocalInputEnableTick) ||
            !FitsSimulationInstant(plan.FirstCommandTargetTick) ||
            plan.PublishedAuthorityTick > context.HighestAuthorityFramePublished ||
            plan.TargetLeadFrames < context.MinimumLeadFrames ||
            plan.TargetLeadFrames > context.MaximumLeadFrames ||
            plan.LocalInputEnableTick <= plan.PublishedAuthorityTick ||
            plan.LocalInputEnableTick > ulong.MaxValue - plan.TargetLeadFrames ||
            plan.FirstCommandTargetTick !=
                plan.LocalInputEnableTick + plan.TargetLeadFrames ||
            plan.LocalInputEnableTick - plan.PublishedAuthorityTick >
                ProtocolConstants.MaxOwnerBootstrapEnableNoticeFrames)
        {
            return Invalid(
                ProtocolViolationCode.InvalidNumericValue,
                "Bootstrap frame schedule is outside negotiated bounds.");
        }

        var requiresFrozen = plan.Kind is
            OwnerPredictionBootstrapKindDraft.PreMatch or
            OwnerPredictionBootstrapKindDraft.Respawn;
        if (requiresFrozen !=
            (plan.Preparation ==
                OwnerPredictionBaselinePreparationDraft.FrozenCommandPredecessor))
        {
            return Invalid(
                ProtocolViolationCode.InvalidNumericValue,
                "Bootstrap kind and baseline preparation contradict each other.");
        }
        if (requiresFrozen)
        {
            if (plan.FirstCommandTargetTick == 0 ||
                plan.BaselineTick != plan.FirstCommandTargetTick - 1)
            {
                return Invalid(
                    ProtocolViolationCode.InvalidNumericValue,
                    "Frozen bootstrap baseline must represent T-1.");
            }
        }
        else if (plan.BaselineTick > plan.PublishedAuthorityTick ||
                 plan.BaselineTick >= plan.FirstCommandTargetTick ||
                 plan.FirstCommandTargetTick - 1 - plan.BaselineTick >
                    ProtocolConstants.MaxOwnerBootstrapNeutralPrerollFrames)
        {
            return Invalid(
                ProtocolViolationCode.InvalidNumericValue,
                "Neutral bootstrap preroll is outside negotiated bounds.");
        }
        return ProtocolValidationResult.Valid;
    }

    public ProtocolValidationResult ValidateOwnerPredictionBaselineReceiptDraft(
        OwnerPredictionBaselineReceiptDraft receipt,
        OwnerPredictionDraftScopeExpectation expectedScope,
        ulong expectedPlanId,
        ulong expectedBaselineTick)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (!expectedScope.IsValid || expectedPlanId == 0 ||
            !FitsSimulationInstant(expectedBaselineTick))
        {
            throw new ArgumentOutOfRangeException(nameof(expectedScope));
        }
        var scope = ValidateOwnerScope(receipt.Scope, expectedScope);
        if (!scope.IsValid)
        {
            return scope;
        }
        if (receipt.PlanId != expectedPlanId ||
            receipt.BaselineTick != expectedBaselineTick)
        {
            return Invalid(
                ProtocolViolationCode.InvalidSequence,
                "Baseline receipt does not match the pending bootstrap plan.");
        }
        return Enum.IsDefined(receipt.Source) &&
               receipt.Source != OwnerPredictionBaselineSourceDraft.Unspecified
            ? ProtocolValidationResult.Valid
            : Invalid(
                ProtocolViolationCode.InvalidEnumValue,
                "Baseline receipt source is not recognized.");
    }

    public ProtocolValidationResult ValidateOwnerPredictionLeadUpdateDraft(
        OwnerPredictionLeadUpdateDraft update,
        OwnerControlDraftValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (!context.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(context));
        }
        var scope = ValidateOwnerScope(update.Scope, context.ExpectedScope);
        if (!scope.IsValid)
        {
            return scope;
        }
        return update.TargetLeadFrames >= context.MinimumLeadFrames &&
               update.TargetLeadFrames <= context.MaximumLeadFrames &&
               update.LeadPolicyRevision != 0 &&
               FitsSimulationInstant(update.EffectiveTick) &&
               update.EffectiveTick >= context.EarliestLeadEffectiveTick
            ? ProtocolValidationResult.Valid
            : Invalid(
                ProtocolViolationCode.InvalidNumericValue,
                "Prediction lead update is outside negotiated bounds.");
    }

    public ProtocolValidationResult Validate(
        PacketEnvelope envelope,
        ProtocolValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (envelope.ProtocolVersion != ProtocolConstants.CurrentVersion)
        {
            return Invalid(
                ProtocolViolationCode.UnsupportedProtocolVersion,
                $"Protocol version {envelope.ProtocolVersion} is not supported.");
        }

        if (envelope.Sequence == 0)
        {
            return Invalid(ProtocolViolationCode.InvalidSequence, "Envelope sequence must be positive.");
        }

        if (envelope.PayloadCase == PacketEnvelope.PayloadOneofCase.None)
        {
            return Invalid(ProtocolViolationCode.MissingPayload, "Envelope does not contain a payload.");
        }

        var directionResult = ValidateDirection(envelope.PayloadCase, context.RemoteRole);
        if (!directionResult.IsValid)
        {
            return directionResult;
        }

        var sessionResult = ValidateSession(envelope, context);
        if (!sessionResult.IsValid)
        {
            return sessionResult;
        }

        return envelope.PayloadCase switch
        {
            PacketEnvelope.PayloadOneofCase.ClientInputBatch => ValidateInputBatch(envelope.ClientInputBatch),
            PacketEnvelope.PayloadOneofCase.ClientActionRequest => ValidateAction(envelope.ClientActionRequest),
            PacketEnvelope.PayloadOneofCase.AuthoritySnapshot => ValidateSnapshot(envelope.AuthoritySnapshot),
            PacketEnvelope.PayloadOneofCase.AuthorityEventBatch => ValidateEventBatch(envelope.AuthorityEventBatch),
            PacketEnvelope.PayloadOneofCase.AuthorityCheckpoint => ValidateCheckpoint(envelope.AuthorityCheckpoint),
            PacketEnvelope.PayloadOneofCase.AuthorityMovementFrameBatch =>
                ValidateMovementFrameBatch(envelope.AuthorityMovementFrameBatch),
            PacketEnvelope.PayloadOneofCase.AuthorityAcceptedMovementBatch =>
                ValidateAcceptedMovementBatch(envelope.AuthorityAcceptedMovementBatch),
            PacketEnvelope.PayloadOneofCase.MovementPredictionBundle =>
                ValidateMovementPredictionBundle(envelope.MovementPredictionBundle),
            PacketEnvelope.PayloadOneofCase.PredictionRouteAdvertisement =>
                ValidateRouteAdvertisement(envelope.PredictionRouteAdvertisement),
            PacketEnvelope.PayloadOneofCase.PeerRosterUpdate =>
                ValidatePeerRoster(envelope.PeerRosterUpdate),
            PacketEnvelope.PayloadOneofCase.PredictionRouteAuthorization =>
                ValidateRouteAuthorization(
                    envelope.PredictionRouteAuthorization,
                    envelope.SimulationTick),
            PacketEnvelope.PayloadOneofCase.PredictionRouteRevoked =>
                ValidateRouteRevocation(envelope.PredictionRouteRevoked),
            PacketEnvelope.PayloadOneofCase.AuthorityMovementConfigurationBatch =>
                ValidateMovementConfigurationBatch(envelope.AuthorityMovementConfigurationBatch),
            PacketEnvelope.PayloadOneofCase.JoinRequest => ValidateJoinRequest(envelope.JoinRequest),
            PacketEnvelope.PayloadOneofCase.JoinAccepted => ValidateJoinAccepted(envelope.JoinAccepted),
            PacketEnvelope.PayloadOneofCase.ReconnectRequest => ValidateReconnectRequest(envelope.ReconnectRequest),
            PacketEnvelope.PayloadOneofCase.StateBaseline => ValidateBaseline(envelope.StateBaseline),
            PacketEnvelope.PayloadOneofCase.MatchStart => ValidateMatchStart(envelope.MatchStart),
            PacketEnvelope.PayloadOneofCase.ClockSyncProbe => ValidateClockSyncProbe(envelope.ClockSyncProbe),
            PacketEnvelope.PayloadOneofCase.ClockSyncReply => ValidateClockSyncReply(envelope.ClockSyncReply),
            _ => Invalid(ProtocolViolationCode.MissingPayload, "Envelope payload type is unsupported."),
        };
    }

    private static ProtocolValidationResult ValidateDirection(
        PacketEnvelope.PayloadOneofCase payload,
        RemoteEndpointRole remoteRole)
    {
        var allowed = remoteRole switch
        {
            RemoteEndpointRole.Client => payload is
                PacketEnvelope.PayloadOneofCase.ClientInputBatch or
                PacketEnvelope.PayloadOneofCase.ClientActionRequest or
                PacketEnvelope.PayloadOneofCase.MovementPredictionBundle or
                PacketEnvelope.PayloadOneofCase.PredictionRouteAdvertisement or
                PacketEnvelope.PayloadOneofCase.ClockSyncProbe or
                PacketEnvelope.PayloadOneofCase.JoinRequest or
                PacketEnvelope.PayloadOneofCase.ReconnectRequest,
            RemoteEndpointRole.Authority => payload is
                PacketEnvelope.PayloadOneofCase.AuthoritySnapshot or
                PacketEnvelope.PayloadOneofCase.AuthorityEventBatch or
                PacketEnvelope.PayloadOneofCase.AuthorityCheckpoint or
                PacketEnvelope.PayloadOneofCase.AuthorityMovementFrameBatch or
                PacketEnvelope.PayloadOneofCase.AuthorityAcceptedMovementBatch or
                PacketEnvelope.PayloadOneofCase.PeerRosterUpdate or
                PacketEnvelope.PayloadOneofCase.PredictionRouteAuthorization or
                PacketEnvelope.PayloadOneofCase.PredictionRouteRevoked or
                PacketEnvelope.PayloadOneofCase.AuthorityMovementConfigurationBatch or
                PacketEnvelope.PayloadOneofCase.JoinAccepted or
                PacketEnvelope.PayloadOneofCase.StateBaseline or
                PacketEnvelope.PayloadOneofCase.MatchStart or
                PacketEnvelope.PayloadOneofCase.ClockSyncReply,
            _ => false,
        };

        return allowed
            ? ProtocolValidationResult.Valid
            : Invalid(
                ProtocolViolationCode.UnexpectedMessageDirection,
                $"A remote {remoteRole} cannot send payload type {payload}.");
    }

    private static ProtocolValidationResult ValidateSession(
        PacketEnvelope envelope,
        ProtocolValidationContext context)
    {
        if (envelope.PayloadCase == PacketEnvelope.PayloadOneofCase.JoinRequest)
        {
            return !context.SessionEstablished && envelope.SessionId == 0
                ? ProtocolValidationResult.Valid
                : Invalid(ProtocolViolationCode.InvalidSession, "Initial join packets must not claim a session.");
        }

        if (envelope.PayloadCase == PacketEnvelope.PayloadOneofCase.JoinAccepted)
        {
            var acceptedSession = envelope.JoinAccepted.SessionId;
            return !context.SessionEstablished &&
                   envelope.SessionId != 0 &&
                   envelope.SessionId == acceptedSession
                ? ProtocolValidationResult.Valid
                : Invalid(
                    ProtocolViolationCode.InvalidSession,
                    "Join acceptance must establish one consistent nonzero session.");
        }

        if (envelope.PayloadCase == PacketEnvelope.PayloadOneofCase.ReconnectRequest)
        {
            var sessionMatchesEnvelope =
                envelope.SessionId != 0 && envelope.SessionId == envelope.ReconnectRequest.SessionId;
            var sessionMatchesKnownIdentity =
                context.ExpectedSessionId is null || envelope.SessionId == context.ExpectedSessionId.Value;

            return !context.SessionEstablished && sessionMatchesEnvelope && sessionMatchesKnownIdentity
                ? ProtocolValidationResult.Valid
                : Invalid(
                    ProtocolViolationCode.InvalidSession,
                    "Reconnect request does not identify a consistent known session.");
        }

        if (!context.SessionEstablished || context.ExpectedSessionId is null)
        {
            return Invalid(
                ProtocolViolationCode.InvalidSession,
                "Only an initial join request is accepted before a session is established.");
        }

        return envelope.SessionId == context.ExpectedSessionId.Value
            ? ProtocolValidationResult.Valid
            : Invalid(ProtocolViolationCode.InvalidSession, "Packet session does not match the connection session.");
    }

    private static ProtocolValidationResult ValidateInputBatch(ClientInputBatch batch)
    {
        if (batch.Frames.Count is < 1 or > ProtocolConstants.MaxInputFramesPerBatch)
        {
            return Invalid(
                ProtocolViolationCode.InvalidCollectionCount,
                $"An input batch must contain 1-{ProtocolConstants.MaxInputFramesPerBatch} frames.");
        }

        ulong previousSequence = 0;
        foreach (var frame in batch.Frames)
        {
            if (frame.InputSequence == 0 || frame.InputSequence <= previousSequence)
            {
                return Invalid(
                    ProtocolViolationCode.InvalidSequence,
                    "Input frame sequences must be positive and strictly increasing within a batch.");
            }

            var frameValidation = ValidateInputFrame(frame);
            if (!frameValidation.IsValid)
            {
                return frameValidation;
            }

            previousSequence = frame.InputSequence;
        }

        return ProtocolValidationResult.Valid;
    }

    internal static ProtocolValidationResult ValidateInputFrame(ClientInputFrame frame)
    {
        if (frame.InputSequence == 0)
        {
            return Invalid(
                ProtocolViolationCode.InvalidSequence,
                "Input frame sequence must be positive.");
        }

        if (frame.ClientTick == 0 ||
            frame.EstimatedAuthorityTick == 0 ||
            frame.MovementProfileRevision == 0 ||
            frame.MovementCapabilityRevision == 0)
        {
            return Invalid(
                ProtocolViolationCode.InvalidNumericValue,
                "Input frame requires client/authority ticks and movement revisions.");
        }

        if (!IsFiniteInRange(frame.MoveX, -1f, 1f) ||
            !IsFiniteInRange(frame.MoveZ, -1f, 1f) ||
            !IsFiniteInRange(frame.ViewYawRadians, -MathF.PI, MathF.PI) ||
            !IsFiniteInRange(frame.ViewPitchRadians, -HalfPi, HalfPi))
        {
            return Invalid(
                ProtocolViolationCode.InvalidNumericValue,
                "Input axes or view angles are non-finite or outside their allowed range.");
        }

        if ((frame.ButtonBits & ~ProtocolConstants.KnownInputButtonMask) != 0 ||
            (frame.PressedButtonBits & ~ProtocolConstants.KnownInputButtonMask) != 0 ||
            (frame.ReleasedButtonBits & ~ProtocolConstants.KnownInputButtonMask) != 0)
        {
            return Invalid(
                ProtocolViolationCode.InvalidNumericValue,
                "Input frame contains unknown button bits.");
        }

        return ProtocolValidationResult.Valid;
    }

    private static ProtocolValidationResult ValidateAction(ClientActionRequest action)
    {
        if (action.ActionSequence == 0)
        {
            return Invalid(ProtocolViolationCode.InvalidSequence, "Action sequence must be positive.");
        }

        if (action.SourceLifeId == 0)
        {
            return Invalid(
                ProtocolViolationCode.InvalidNumericValue,
                "Action request requires a positive source life identity.");
        }

        if (!Enum.IsDefined(action.Kind) || action.Kind == ClientActionKind.Unspecified)
        {
            return Invalid(ProtocolViolationCode.InvalidEnumValue, "Action kind is not recognized.");
        }

        if (action.EquipmentSlot >= ProtocolConstants.EquipmentSlotCount)
        {
            return Invalid(ProtocolViolationCode.InvalidNumericValue, "Equipment slot is outside the allowed range.");
        }

        return ProtocolValidationResult.Valid;
    }

    private static ProtocolValidationResult ValidateSnapshot(AuthoritySnapshot snapshot)
    {
        if (snapshot.Combatants.Count > ProtocolConstants.MaxCombatants)
        {
            return Invalid(ProtocolViolationCode.InvalidCollectionCount, "Snapshot contains too many combatants.");
        }

        foreach (var combatant in snapshot.Combatants)
        {
            if (combatant.CombatantId == 0 || combatant.LifeId == 0 ||
                combatant.MovementProfileRevision == 0 || combatant.MovementCapabilityRevision == 0)
            {
                return Invalid(
                    ProtocolViolationCode.InvalidNumericValue,
                    "Combatant snapshot is missing life or movement revision identity.");
            }

            var traversalValidation = ValidateTraversalState(combatant.Traversal);
            if (!traversalValidation.IsValid)
            {
                return traversalValidation;
            }
        }

        return ProtocolValidationResult.Valid;
    }

    private static ProtocolValidationResult ValidateMovementFrameBatch(
        AuthorityMovementFrameBatch batch)
    {
        if (batch.StreamSequence == 0)
        {
            return Invalid(
                ProtocolViolationCode.InvalidSequence,
                "Movement stream sequence must be positive.");
        }

        if (batch.Combatants.Count > ProtocolConstants.MaxCombatants)
        {
            return Invalid(
                ProtocolViolationCode.InvalidCollectionCount,
                "Movement frame contains too many combatants.");
        }

        foreach (var state in batch.Combatants)
        {
            if (state.CombatantId == 0 || state.LifeId == 0 ||
                state.MovementProfileRevision == 0 || state.MovementCapabilityRevision == 0)
            {
                return Invalid(
                    ProtocolViolationCode.InvalidNumericValue,
                    "Movement state requires positive combatant and life identities.");
            }

            if (!IsFiniteVector(state.Position) ||
                !IsFiniteVector(state.Velocity) ||
                !IsFiniteVector(state.RollDirection) ||
                !IsFiniteInRange(state.ViewYawRadians, -MathF.PI, MathF.PI) ||
                !IsFiniteInRange(state.ViewPitchRadians, -HalfPi, HalfPi) ||
                !IsFiniteInRange(state.BodyFacingYawRadians, -MathF.PI, MathF.PI) ||
                !float.IsFinite(state.RollEntrySpeed) ||
                !float.IsFinite(state.RollBoostDistance))
            {
                return Invalid(
                    ProtocolViolationCode.InvalidNumericValue,
                    "Movement state contains a non-finite or invalid spatial value.");
            }

            if (!Enum.IsDefined(state.LocomotionMode) ||
                state.LocomotionMode == ReplicatedLocomotionMode.Unspecified ||
                !Enum.IsDefined(state.PostureMode) ||
                state.PostureMode == ReplicatedPostureMode.Unspecified ||
                !Enum.IsDefined(state.MovementActionMode) ||
                state.MovementActionMode == ReplicatedMovementActionMode.Unspecified ||
                !Enum.IsDefined(state.JumpPhase) ||
                state.JumpPhase == ReplicatedJumpPhase.Unspecified)
            {
                return Invalid(
                    ProtocolViolationCode.InvalidEnumValue,
                    "Movement state contains an unsupported mode.");
            }

            var traversalValidation = ValidateTraversalState(state.Traversal);
            if (!traversalValidation.IsValid)
            {
                return traversalValidation;
            }
        }

        return ProtocolValidationResult.Valid;
    }

    private static ProtocolValidationResult ValidateAcceptedMovementBatch(
        AuthorityAcceptedMovementBatch batch)
    {
        if (batch.StreamSequence == 0)
        {
            return Invalid(
                ProtocolViolationCode.InvalidSequence,
                "Accepted movement stream sequence must be positive.");
        }

        if (batch.Commands.Count is < 1 or > ProtocolConstants.MaxAcceptedMovementCommandsPerBatch)
        {
            return Invalid(
                ProtocolViolationCode.InvalidCollectionCount,
                "Accepted movement batch contains an invalid number of commands.");
        }

        foreach (var command in batch.Commands)
        {
            if (command.SourceSessionPeerId == 0 ||
                command.PeerSessionGeneration == 0 ||
                command.CombatantId == 0 ||
                command.LifeId == 0 ||
                command.AppliedAuthorityTick == 0 ||
                command.AppliedMovementProfileRevision == 0 ||
                command.AppliedMovementCapabilityRevision == 0 ||
                command.Input is null)
            {
                return Invalid(
                    ProtocolViolationCode.InvalidNumericValue,
                    "Accepted movement command is missing authority identity or timing fields.");
            }

            var inputValidation = ValidateInputFrame(command.Input);
            if (!inputValidation.IsValid)
            {
                return inputValidation;
            }
        }

        return ProtocolValidationResult.Valid;
    }

    internal static ProtocolValidationResult ValidateMovementPredictionBundle(
        MovementPredictionBundle bundle)
    {
        if (bundle.BundleSequence == 0 ||
            bundle.SourceCombatantId == 0 ||
            bundle.SourceLifeId == 0)
        {
            return Invalid(
                ProtocolViolationCode.InvalidNumericValue,
                "Movement prediction bundle is missing identity, sequence, or revision fields.");
        }

        if (bundle.Commands.Count is < 1 or > ProtocolConstants.MaxPredictionCommandsPerBundle)
        {
            return Invalid(
                ProtocolViolationCode.InvalidCollectionCount,
                $"Movement prediction bundle must contain 1-{ProtocolConstants.MaxPredictionCommandsPerBundle} commands.");
        }

        ulong previousSequence = 0;
        foreach (var command in bundle.Commands)
        {
            var validation = ValidateInputFrame(command);
            if (!validation.IsValid)
            {
                return validation;
            }

            if (command.InputSequence <= previousSequence)
            {
                return Invalid(
                    ProtocolViolationCode.InvalidSequence,
                    "Prediction command sequences must be strictly increasing.");
            }

            if (((command.ButtonBits |
                  command.PressedButtonBits |
                  command.ReleasedButtonBits) &
                 ~ProtocolConstants.KnownPredictionMovementButtonMask) != 0)
            {
                return Invalid(
                    ProtocolViolationCode.InvalidNumericValue,
                    "Prediction commands may contain movement buttons only.");
            }

            previousSequence = command.InputSequence;
        }

        if (bundle.RollbackState is null)
        {
            return ProtocolValidationResult.Valid;
        }

        var baselineCommand = bundle.Commands.FirstOrDefault(
            command => command.InputSequence == bundle.RollbackState.LastIncludedInputSequence);
        return baselineCommand is null
            ? Invalid(
                ProtocolViolationCode.InvalidSequence,
                "Rollback state must identify a command included in the same bundle.")
            : ValidatePredictedMovementState(bundle.RollbackState, baselineCommand);
    }

    private static ProtocolValidationResult ValidatePredictedMovementState(
        PredictedMovementState state,
        ClientInputFrame baselineCommand)
    {
        if (state.ClientTick == 0 ||
            state.EstimatedAuthorityTick == 0 ||
            state.LastIncludedInputSequence == 0 ||
            state.ClientTick != baselineCommand.ClientTick ||
            state.EstimatedAuthorityTick != baselineCommand.EstimatedAuthorityTick ||
            state.MovementProfileRevision != baselineCommand.MovementProfileRevision ||
            state.MovementCapabilityRevision != baselineCommand.MovementCapabilityRevision)
        {
            return Invalid(
                ProtocolViolationCode.InvalidNumericValue,
                "Predicted rollback state contains invalid timing or revisions.");
        }

        if (!IsFiniteBoundedVector(
                state.Position,
                ProtocolConstants.MaxPredictionCoordinateMagnitude) ||
            !IsFiniteBoundedVector(
                state.Velocity,
                ProtocolConstants.MaxPredictionVelocityMagnitude) ||
            !IsFiniteBoundedVector(state.RollDirection, 1.001f) ||
            !IsFiniteInRange(state.ViewYawRadians, -MathF.PI, MathF.PI) ||
            !IsFiniteInRange(state.ViewPitchRadians, -HalfPi, HalfPi) ||
            !IsFiniteInRange(state.BodyFacingYawRadians, -MathF.PI, MathF.PI) ||
            !float.IsFinite(state.RollEntrySpeed) ||
            !float.IsFinite(state.RollBoostDistance) ||
            state.RollEntrySpeed is < 0 or > ProtocolConstants.MaxPredictionVelocityMagnitude ||
            state.RollBoostDistance is < 0 or > ProtocolConstants.MaxPredictionRollBoostDistance ||
            state.MovementModeElapsedTicks > ProtocolConstants.MaxPredictionTimerTicks ||
            state.TicksSinceGrounded > ProtocolConstants.MaxPredictionTimerTicks ||
            (state.HasBufferedJumpRemainingTicks &&
             state.BufferedJumpRemainingTicks > ProtocolConstants.MaxPredictionTimerTicks) ||
            state.RollDurationTicks > ProtocolConstants.MaxPredictionTimerTicks ||
            state.RollCooldownRemainingTicks > ProtocolConstants.MaxPredictionTimerTicks)
        {
            return Invalid(
                ProtocolViolationCode.InvalidNumericValue,
                "Predicted rollback state contains invalid spatial values.");
        }

        if (!Enum.IsDefined(state.LocomotionMode) ||
            state.LocomotionMode == ReplicatedLocomotionMode.Unspecified ||
            !Enum.IsDefined(state.PostureMode) ||
            state.PostureMode == ReplicatedPostureMode.Unspecified ||
            !Enum.IsDefined(state.MovementActionMode) ||
            state.MovementActionMode == ReplicatedMovementActionMode.Unspecified ||
            !Enum.IsDefined(state.JumpPhase) ||
            state.JumpPhase == ReplicatedJumpPhase.Unspecified)
        {
            return Invalid(
                ProtocolViolationCode.InvalidEnumValue,
                "Predicted rollback state contains an unsupported movement mode.");
        }

        return ValidateTraversalState(state.Traversal);
    }

    private static ProtocolValidationResult ValidateRouteAdvertisement(
        PredictionRouteAdvertisement advertisement)
    {
        return ValidateRouteDescriptor(advertisement.RouteDescriptor);
    }

    private static ProtocolValidationResult ValidatePeerRoster(PeerRosterUpdate roster)
    {
        if (roster.RosterRevision == 0 ||
            roster.Peers.Count is < 1 or > ProtocolConstants.MaxSessionPeers)
        {
            return Invalid(
                ProtocolViolationCode.InvalidCollectionCount,
                "Prediction peer roster has an invalid revision or peer count.");
        }

        var peerIds = new HashSet<ulong>();
        var authorityCount = 0;
        foreach (var peer in roster.Peers)
        {
            if (peer.SessionPeerId == 0 ||
                peer.PeerSessionGeneration == 0 ||
                peer.PlayerId == 0 ||
                peer.CombatantId == 0 ||
                string.IsNullOrWhiteSpace(peer.DisplayName) ||
                peer.DisplayName.Length > ProtocolConstants.MaxDisplayNameCharacters ||
                !peerIds.Add(peer.SessionPeerId))
            {
                return Invalid(
                    ProtocolViolationCode.InvalidSession,
                    "Prediction peer roster contains an invalid or duplicate peer.");
            }

            if (peer.IsAuthority)
            {
                authorityCount++;
            }
        }

        return authorityCount == 1
            ? ProtocolValidationResult.Valid
            : Invalid(
                ProtocolViolationCode.InvalidSession,
                "Prediction peer roster must identify exactly one authority.");
    }

    private static ProtocolValidationResult ValidateRouteAuthorization(
        PredictionRouteAuthorization authorization,
        ulong receivedAuthorityTick)
    {
        if (authorization.LocalSessionPeerId == 0 ||
            authorization.RemoteSessionPeerId == 0 ||
            authorization.LocalSessionPeerId == authorization.RemoteSessionPeerId ||
            authorization.LocalPeerSessionGeneration == 0 ||
            authorization.RemotePeerSessionGeneration == 0 ||
            authorization.PredictionRouteGeneration == 0 ||
            authorization.ExpiresAuthorityTick <= receivedAuthorityTick)
        {
            return Invalid(
                ProtocolViolationCode.InvalidSession,
                "Prediction route authorization contains invalid identity, generation, or expiry fields.");
        }

        if (authorization.RouteCredential.Length != ProtocolConstants.PredictionRouteCredentialBytes)
        {
            return Invalid(
                ProtocolViolationCode.InvalidCredential,
                "Prediction route credential has an invalid length.");
        }

        if (authorization.RouteCredential.ToByteArray().All(value => value == 0))
        {
            return Invalid(
                ProtocolViolationCode.InvalidCredential,
                "Prediction route credential cannot be the all-zero sentinel.");
        }

        return ValidateRouteDescriptor(authorization.RemoteDescriptor);
    }

    private static ProtocolValidationResult ValidateRouteRevocation(
        PredictionRouteRevoked revocation)
    {
        if (revocation.LocalSessionPeerId == 0 ||
            revocation.RemoteSessionPeerId == 0 ||
            revocation.LocalSessionPeerId == revocation.RemoteSessionPeerId ||
            revocation.PredictionRouteGeneration == 0 ||
            revocation.LocalPeerSessionGeneration == 0 ||
            revocation.RemotePeerSessionGeneration == 0)
        {
            return Invalid(
                ProtocolViolationCode.InvalidSession,
                "Prediction route revocation contains invalid identity or generation fields.");
        }

        return Enum.IsDefined(revocation.Reason) &&
               revocation.Reason != PredictionRouteRevocationReason.Unspecified
            ? ProtocolValidationResult.Valid
            : Invalid(
                ProtocolViolationCode.InvalidEnumValue,
                "Prediction route revocation reason is not recognized.");
    }

    internal static ProtocolValidationResult ValidateRouteDescriptor(
        PredictionRouteDescriptor? descriptor)
    {
        if (descriptor is null ||
            descriptor.DescriptorVersion == 0 ||
            !Enum.IsDefined(descriptor.TransportKind) ||
            descriptor.TransportKind == PredictionTransportKind.Unspecified)
        {
            return Invalid(
                ProtocolViolationCode.InvalidEnumValue,
                "Prediction route descriptor has an invalid version or transport kind.");
        }

        return descriptor.Payload.Length is > 0 and <= ProtocolConstants.MaxPredictionRouteDescriptorBytes
            ? ProtocolValidationResult.Valid
            : Invalid(
                ProtocolViolationCode.InvalidCollectionCount,
                "Prediction route descriptor payload has an invalid size.");
    }

    private static ProtocolValidationResult ValidateEventBatch(AuthorityEventBatch batch)
    {
        if (batch.Events.Count > ProtocolConstants.MaxEventsPerBatch)
        {
            return Invalid(ProtocolViolationCode.InvalidCollectionCount, "Event batch contains too many events.");
        }

        foreach (var authorityEvent in batch.Events)
        {
            if (authorityEvent.EventSequence == 0 || authorityEvent.AuthorityTick == 0 ||
                authorityEvent.EventCase == AuthorityEvent.EventOneofCase.None)
            {
                return Invalid(
                    ProtocolViolationCode.InvalidSequence,
                    "Authority event requires sequence, tick, and payload.");
            }

            if (authorityEvent.EventCase == AuthorityEvent.EventOneofCase.Damage &&
                (authorityEvent.Damage.SourceCombatantId == 0 ||
                 authorityEvent.Damage.SourceLifeId == 0 ||
                 authorityEvent.Damage.TargetCombatantId == 0 ||
                 authorityEvent.Damage.TargetLifeId == 0 ||
                 authorityEvent.Damage.NetDamage < 0 ||
                 authorityEvent.Damage.Overkill < 0 ||
                 authorityEvent.Damage.MaximumHealth <= 0 ||
                 authorityEvent.Damage.CurrentHealth < 0 ||
                 authorityEvent.Damage.CurrentHealth > authorityEvent.Damage.MaximumHealth))
            {
                return Invalid(
                    ProtocolViolationCode.InvalidSession,
                    "Damage event must identify the target life that received it.");
            }

            if (authorityEvent.EventCase == AuthorityEvent.EventOneofCase.ActionState)
            {
                var action = authorityEvent.ActionState;
                if (action.SourceCombatantId == 0 || action.SourceLifeId == 0 ||
                    action.AttackExecutionId == 0 || action.AttackPolicyRevision == 0 ||
                    string.IsNullOrWhiteSpace(action.WeaponDefinitionId) ||
                    action.WeaponDefinitionId.Length > ProtocolConstants.MaxDefinitionIdCharacters ||
                    !Enum.IsDefined(action.Lifecycle) ||
                    action.Lifecycle == ReplicatedActionLifecycleKind.Unspecified ||
                    !Enum.IsDefined(action.Phase) ||
                    action.Phase == ReplicatedAttackPhase.Unspecified)
                {
                    return Invalid(
                        ProtocolViolationCode.InvalidNumericValue,
                        "Action event is missing lifecycle, policy, or life identity.");
                }
            }
        }

        return ProtocolValidationResult.Valid;
    }

    private static ProtocolValidationResult ValidateMovementConfigurationBatch(
        AuthorityMovementConfigurationBatch batch)
    {
        if (batch.StreamSequence == 0 ||
            batch.Updates.Count is < 1 or > ProtocolConstants.MaxMovementConfigurationUpdatesPerBatch)
        {
            return Invalid(
                ProtocolViolationCode.InvalidCollectionCount,
                "Movement configuration batch has an invalid sequence or update count.");
        }

        foreach (var update in batch.Updates)
        {
            var validation = ValidateMovementConfiguration(update);
            if (!validation.IsValid)
            {
                return validation;
            }
        }

        return ProtocolValidationResult.Valid;
    }

    internal static ProtocolValidationResult ValidateMovementConfiguration(
        AuthorityMovementConfigurationUpdate update)
    {
        if (update.CombatantId == 0 || update.LifeId == 0 ||
            update.MovementProfileRevision == 0 || update.MovementCapabilityRevision == 0 ||
            update.EffectiveAuthorityTick == 0 || update.Ground is null || update.Air is null ||
            update.Jump is null || update.CrouchRoll is null || update.Capabilities is null)
        {
            return Invalid(
                ProtocolViolationCode.InvalidNumericValue,
                "Resolved movement configuration is missing identity, timing, or a required attribute group.");
        }

        var ground = update.Ground;
        var air = update.Air;
        var jump = update.Jump;
        var roll = update.CrouchRoll;
        var capabilities = update.Capabilities;
        var allFinite = new[]
        {
            ground.MaximumRunSpeed, ground.MaximumSprintSpeed, ground.RunAcceleration,
            ground.SprintAcceleration, ground.BrakingDeceleration, ground.ReversalDeceleration,
            ground.LowSpeedTurnRateRadians, ground.HighSpeedTurnRateRadians,
            ground.ReversalDotThreshold, ground.MaximumStepHeight, ground.FloorSnapDistance,
            ground.MaximumFloorAngleRadians, ground.StepForwardAssistDistance,
            air.ForwardAirAcceleration, air.LateralAirAcceleration, air.MaximumRunAirSpeed,
            air.MaximumAirSpeed, air.HighSpeedForwardControlMultiplier,
            air.HighSpeedLateralControlMultiplier, air.TurnRateRadians,
            jump.JumpVelocity, jump.JumpVelocityPerHorizontalSpeed, jump.RisingGravity,
            jump.ApexGravity, jump.FallingGravity, jump.MaximumFallSpeed,
            jump.JumpReleaseGravity, jump.ApexVelocityThreshold,
            roll.RollEntrySpeed, roll.MaximumCrouchSpeed, roll.MinimumRollBoostDistance,
            roll.MaximumRollBoostDistance, roll.LowSpeedSteeringRateRadians,
            roll.HighSpeedSteeringRateRadians, roll.StandingCapsuleHeight,
            roll.CrouchingCapsuleHeight, roll.RollingCapsuleHeight, roll.CapsuleRadius,
        };
        if (allFinite.Any(value => !double.IsFinite(value)) ||
            ground.MaximumRunSpeed <= 0 || ground.MaximumSprintSpeed < ground.MaximumRunSpeed ||
            ground.RunAcceleration <= 0 || ground.SprintAcceleration <= 0 ||
            ground.BrakingDeceleration <= 0 || ground.ReversalDeceleration <= 0 ||
            ground.LowSpeedTurnRateRadians < ground.HighSpeedTurnRateRadians ||
            ground.HighSpeedTurnRateRadians <= 0 || ground.ReversalDotThreshold is < -1 or > 1 ||
            ground.MaximumStepHeight <= 0 || ground.FloorSnapDistance <= 0 ||
            ground.MaximumFloorAngleRadians is <= 0 or >= Math.PI / 2 ||
            ground.StepForwardAssistDistance <= 0 ||
            air.ForwardAirAcceleration <= 0 || air.LateralAirAcceleration <= 0 ||
            air.MaximumRunAirSpeed <= 0 || air.MaximumAirSpeed < air.MaximumRunAirSpeed ||
            air.HighSpeedForwardControlMultiplier is < 0 or > 1 ||
            air.HighSpeedLateralControlMultiplier is < 0 or > 1 || air.TurnRateRadians <= 0 ||
            jump.JumpVelocity <= 0 || jump.JumpVelocityPerHorizontalSpeed < 0 ||
            jump.RisingGravity <= 0 || jump.ApexGravity <= 0 || jump.FallingGravity <= 0 ||
            jump.MaximumFallSpeed <= 0 || jump.JumpReleaseGravity <= 0 ||
            jump.ApexVelocityThreshold <= 0 ||
            roll.RollEntrySpeed <= 0 || roll.MaximumCrouchSpeed <= 0 ||
            roll.MinimumRollBoostDistance <= 0 ||
            roll.MaximumRollBoostDistance < roll.MinimumRollBoostDistance ||
            roll.MinimumRollDurationTicks == 0 ||
            roll.MaximumRollDurationTicks < roll.MinimumRollDurationTicks ||
            roll.RollCooldownTicks == 0 || roll.LowSpeedSteeringRateRadians <= 0 ||
            roll.HighSpeedSteeringRateRadians <= 0 || roll.StandingCapsuleHeight <= 0 ||
            roll.CrouchingCapsuleHeight > roll.StandingCapsuleHeight ||
            roll.RollingCapsuleHeight > roll.CrouchingCapsuleHeight ||
            roll.CapsuleRadius <= 0 || roll.RollingCapsuleHeight < roll.CapsuleRadius * 2 ||
            capabilities.MaximumJumpCount > ProtocolConstants.MaxAuthoredMovementCount ||
            capabilities.MaximumAirRollCount > ProtocolConstants.MaxAuthoredMovementCount ||
            (!capabilities.CanJump && capabilities.MaximumJumpCount != 0) ||
            (!capabilities.CanRoll && capabilities.MaximumAirRollCount != 0))
        {
            return Invalid(
                ProtocolViolationCode.InvalidNumericValue,
                "Resolved movement configuration contains values outside simulator bounds.");
        }

        return ProtocolValidationResult.Valid;
    }

    private static ProtocolValidationResult ValidateCheckpoint(AuthorityCheckpoint checkpoint)
    {
        if (checkpoint.Combatants.Count > ProtocolConstants.MaxCombatants ||
            checkpoint.ActiveEffects.Count > ProtocolConstants.MaxActiveEffects ||
            checkpoint.WorldObjects.Count > ProtocolConstants.MaxWorldObjects)
        {
            return Invalid(
                ProtocolViolationCode.InvalidCollectionCount,
                "Checkpoint contains more replicated objects than the protocol permits.");
        }

        return ProtocolValidationResult.Valid;
    }

    private static ProtocolValidationResult ValidateJoinRequest(JoinRequest request)
    {
        if (request.ClientProtocolVersion != ProtocolConstants.CurrentVersion)
        {
            return Invalid(
                ProtocolViolationCode.UnsupportedProtocolVersion,
                "Join request protocol version is not supported.");
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName) ||
            request.DisplayName.Length > ProtocolConstants.MaxDisplayNameCharacters)
        {
            return Invalid(ProtocolViolationCode.InvalidTextValue, "Display name is empty or too long.");
        }

        return request.ClientNonce.Length == ProtocolConstants.ClientNonceBytes
            ? ProtocolValidationResult.Valid
            : Invalid(ProtocolViolationCode.InvalidCredential, "Client nonce has an invalid length.");
    }

    private static ProtocolValidationResult ValidateJoinAccepted(JoinAccepted accepted)
    {
        if (accepted.SessionId == 0 ||
            accepted.SessionPeerId == 0 ||
            accepted.ConnectionGeneration == 0 ||
            accepted.PlayerId == 0 ||
            accepted.CombatantId == 0)
        {
            return Invalid(ProtocolViolationCode.InvalidSession, "Join acceptance contains an invalid identity.");
        }

        if (accepted.ReconnectToken.Length != ProtocolConstants.ReconnectTokenBytes)
        {
            return Invalid(ProtocolViolationCode.InvalidCredential, "Reconnect token has an invalid length.");
        }

        if (accepted.SimulationTicksPerSecond == 0 ||
            accepted.SnapshotRate == 0 ||
            accepted.CheckpointIntervalTicks == 0)
        {
            return Invalid(ProtocolViolationCode.InvalidNumericValue, "Join timing values must be positive.");
        }

        return ProtocolValidationResult.Valid;
    }

    private static ProtocolValidationResult ValidateReconnectRequest(ReconnectRequest request)
    {
        if (request.SessionId == 0 || request.PlayerId == 0)
        {
            return Invalid(ProtocolViolationCode.InvalidSession, "Reconnect request contains an invalid identity.");
        }

        return request.ReconnectToken.Length == ProtocolConstants.ReconnectTokenBytes
            ? ProtocolValidationResult.Valid
            : Invalid(ProtocolViolationCode.InvalidCredential, "Reconnect token has an invalid length.");
    }

    private static ProtocolValidationResult ValidateBaseline(StateBaseline baseline)
    {
        if (baseline.PlayerId == 0 || baseline.ControlledCombatantId == 0 || baseline.Checkpoint is null)
        {
            return Invalid(ProtocolViolationCode.InvalidSession, "State baseline is missing required identity or state.");
        }

        return ValidateCheckpoint(baseline.Checkpoint);
    }

    private static ProtocolValidationResult ValidateMatchStart(MatchStart matchStart)
    {
        if (string.IsNullOrWhiteSpace(matchStart.ArenaDefinitionId) ||
            matchStart.ArenaDefinitionId.Length > ProtocolConstants.MaxDefinitionIdCharacters)
        {
            return Invalid(
                ProtocolViolationCode.InvalidTextValue,
                "Match start contains an invalid arena definition ID.");
        }

        return matchStart.MatchSeed != 0
            ? ProtocolValidationResult.Valid
            : Invalid(ProtocolViolationCode.InvalidNumericValue, "Match seed must be nonzero.");
    }

    private static ProtocolValidationResult ValidateClockSyncProbe(ClockSyncProbe probe) =>
        probe.ProbeSequence != 0 && probe.ClientSendTimestampMicroseconds != 0
            ? ProtocolValidationResult.Valid
            : Invalid(
                ProtocolViolationCode.InvalidSequence,
                "Clock synchronization probe requires a sequence and timestamp.");

    private static ProtocolValidationResult ValidateClockSyncReply(ClockSyncReply reply) =>
        reply.ProbeSequence != 0 &&
        reply.ClientSendTimestampMicroseconds != 0 &&
        reply.AuthorityReceiveTimestampMicroseconds != 0 &&
        reply.AuthoritySendTimestampMicroseconds >= reply.AuthorityReceiveTimestampMicroseconds
            ? ProtocolValidationResult.Valid
            : Invalid(
                ProtocolViolationCode.InvalidNumericValue,
                "Clock synchronization reply contains invalid timestamps.");

    private static ProtocolValidationResult ValidateOwnerScope(
        OwnerPredictionScopeDraft? scope,
        OwnerPredictionDraftScopeExpectation expected)
    {
        if (scope is null || !expected.IsValid ||
            scope.SessionId == 0 ||
            scope.MatchFrameEpoch == 0 ||
            scope.CombatantId == 0 ||
            scope.LifeId == 0 ||
            scope.AuthorityDiscontinuityId == 0 ||
            scope.OwnerControlEpoch == 0 ||
            scope.SessionId != expected.SessionId ||
            scope.MatchFrameEpoch != expected.MatchFrameEpoch ||
            scope.CombatantId != expected.CombatantId ||
            scope.LifeId != expected.LifeId ||
            scope.AuthorityDiscontinuityId != expected.AuthorityDiscontinuityId ||
            scope.OwnerControlEpoch != expected.OwnerControlEpoch)
        {
            return Invalid(
                ProtocolViolationCode.InvalidSession,
                "Owner prediction scope is incomplete, stale, or not owned by this peer.");
        }
        return ProtocolValidationResult.Valid;
    }

    private static ProtocolValidationResult ValidateResolutionCursor(
        bool hasCursor,
        ulong cursor,
        ulong? maximumIssuedCursor,
        string streamName)
    {
        if (!hasCursor)
        {
            return ProtocolValidationResult.Valid;
        }
        return cursor != 0 &&
               maximumIssuedCursor is { } maximum &&
               cursor <= maximum
            ? ProtocolValidationResult.Valid
            : Invalid(
                ProtocolViolationCode.InvalidSequence,
                $"Owner {streamName} resolution cursor acknowledges an unissued result.");
    }

    private static ProtocolValidationResult ValidateMovementTransitionIntent(
        MovementTransitionIntentDraft intent,
        OwnerCommandBatchDraft batch,
        OwnerCommandDraftValidationContext context,
        HashSet<ulong> identities)
    {
        if (intent is null || intent.TransitionId == 0 ||
            intent.TransitionId > context.MaximumPermittedTransitionId ||
            !identities.Add(intent.TransitionId) ||
            intent.OriginatingInputSequence == 0 ||
            intent.OriginatingInputSequence > context.MaximumPermittedInputSequence ||
            (intent.OriginatingInputSequence > context.HighestKnownInputSequence &&
             !BatchContainsInputSequence(batch, intent.OriginatingInputSequence)))
        {
            return Invalid(
                ProtocolViolationCode.InvalidSequence,
                "Movement transition identity or originating input is invalid.");
        }
        if (!Enum.IsDefined(intent.Kind) ||
            intent.Kind == OwnerMovementTransitionKindDraft.Unspecified)
        {
            return Invalid(
                ProtocolViolationCode.InvalidEnumValue,
                "Movement transition kind is not recognized.");
        }
        if (!FitsSimulationInstant(intent.FirstPredictedTick) ||
            !FitsSimulationInstant(intent.LastValidTick) ||
            intent.LastValidTick < intent.FirstPredictedTick ||
            intent.LastValidTick - intent.FirstPredictedTick >
                ProtocolConstants.MaxOwnerIntentValidityTicks ||
            intent.FirstPredictedTick > context.LatestPermittedTargetTick ||
            intent.LastValidTick < context.EarliestRetainedTargetTick)
        {
            return Invalid(
                ProtocolViolationCode.InvalidNumericValue,
                "Movement transition frame window is invalid or stale.");
        }
        if (TryFindBatchCommandTarget(
                batch,
                intent.OriginatingInputSequence,
                out var originatingTarget) &&
            intent.FirstPredictedTick != originatingTarget)
        {
            return Invalid(
                ProtocolViolationCode.InvalidNumericValue,
                "Movement transition does not start on its originating command frame.");
        }
        return ProtocolValidationResult.Valid;
    }

    private static ProtocolValidationResult ValidatePredictedActionIntent(
        PredictedActionIntentDraft intent,
        OwnerCommandBatchDraft batch,
        OwnerCommandDraftValidationContext context,
        HashSet<ulong> identities)
    {
        if (intent is null || intent.ActionId == 0 ||
            intent.ActionId > context.MaximumPermittedActionId ||
            !identities.Add(intent.ActionId) ||
            intent.OriginatingInputSequence == 0 ||
            intent.OriginatingInputSequence > context.MaximumPermittedInputSequence ||
            (intent.OriginatingInputSequence > context.HighestKnownInputSequence &&
             !BatchContainsInputSequence(batch, intent.OriginatingInputSequence)))
        {
            return Invalid(
                ProtocolViolationCode.InvalidSequence,
                "Predicted action identity or originating input is invalid.");
        }
        if (!Enum.IsDefined(intent.Trigger) ||
            intent.Trigger == OwnerActionTriggerDraft.Unspecified)
        {
            return Invalid(
                ProtocolViolationCode.InvalidEnumValue,
                "Predicted action trigger is not recognized.");
        }
        if (!FitsSimulationInstant(intent.PredictedStartTick) ||
            !FitsSimulationInstant(intent.LastValidStartTick) ||
            !FitsSimulationInstant(intent.RenderedAuthorityTick) ||
            intent.LastValidStartTick < intent.PredictedStartTick ||
            intent.LastValidStartTick - intent.PredictedStartTick >
                ProtocolConstants.MaxOwnerIntentValidityTicks ||
            intent.PredictedStartTick > context.LatestPermittedTargetTick ||
            intent.LastValidStartTick < context.EarliestRetainedTargetTick ||
            intent.RenderedAuthorityTick > intent.PredictedStartTick ||
            intent.RenderedAuthorityTick > context.HighestAuthorityFramePublished)
        {
            return Invalid(
                ProtocolViolationCode.InvalidNumericValue,
                "Predicted action frame window or rendered evidence is invalid.");
        }
        if (TryFindBatchCommandTarget(
                batch,
                intent.OriginatingInputSequence,
                out var originatingTarget) &&
            intent.PredictedStartTick != originatingTarget)
        {
            return Invalid(
                ProtocolViolationCode.InvalidNumericValue,
                "Predicted action does not start on its originating command frame.");
        }
        return ProtocolValidationResult.Valid;
    }

    private static ProtocolValidationResult ValidateOwnerSimulationCommand(
        OwnerSimulationCommandDraft command,
        OwnerCommandDraftValidationContext context,
        IReadOnlyDictionary<ulong, ulong> batchTransitionOrigins,
        IReadOnlyDictionary<ulong, ulong> batchActionOrigins,
        HashSet<ulong> inputSequences,
        HashSet<ulong> targetTicks,
        HashSet<ulong> transitionReferences,
        HashSet<ulong> actionReferences)
    {
        if (command is null || command.InputSequence == 0 ||
            command.InputSequence > context.MaximumPermittedInputSequence ||
            !inputSequences.Add(command.InputSequence))
        {
            return Invalid(
                ProtocolViolationCode.InvalidSequence,
                "Owner command input sequence is zero, duplicate, or unpermitted.");
        }
        if (!FitsSimulationInstant(command.TargetSimulationTick) ||
            command.TargetSimulationTick < context.EarliestRetainedTargetTick ||
            command.TargetSimulationTick > context.LatestPermittedTargetTick ||
            !targetTicks.Add(command.TargetSimulationTick))
        {
            return Invalid(
                ProtocolViolationCode.InvalidNumericValue,
                "Owner command target frame is duplicate, stale, or too far ahead.");
        }
        if (command.MoveXQ15 is < -ProtocolConstants.OwnerAxisQ15Magnitude or
                > ProtocolConstants.OwnerAxisQ15Magnitude ||
            command.MoveZQ15 is < -ProtocolConstants.OwnerAxisQ15Magnitude or
                > ProtocolConstants.OwnerAxisQ15Magnitude ||
            command.ViewYawU16 > ProtocolConstants.OwnerViewYawU16Maximum ||
            command.ViewPitchI16 is < -ProtocolConstants.OwnerViewPitchI16Magnitude or
                > ProtocolConstants.OwnerViewPitchI16Magnitude ||
            (command.HeldMovementBits &
                ~ProtocolConstants.KnownOwnerHeldMovementMask) != 0 ||
            (command.HeldCombatBits &
                ~ProtocolConstants.KnownOwnerHeldCombatMask) != 0)
        {
            return Invalid(
                ProtocolViolationCode.InvalidNumericValue,
                "Owner command quantized input or held-state bits are invalid.");
        }
        if (command.MovementProfileRevision == 0 ||
            command.MovementProfileRevision >
                context.MaximumMovementProfileRevision ||
            command.MovementCapabilityRevision == 0 ||
            command.MovementCapabilityRevision >
                context.MaximumMovementCapabilityRevision)
        {
            return Invalid(
                ProtocolViolationCode.InvalidNumericValue,
                "Owner command movement revision is unavailable.");
        }
        if (command.TransitionReferences.Count >
                ProtocolConstants.MaxOwnerTransitionReferencesPerCommand ||
            command.ActionReferences.Count >
                ProtocolConstants.MaxOwnerActionReferencesPerCommand)
        {
            return Invalid(
                ProtocolViolationCode.InvalidCollectionCount,
                "Owner command has too many durable intent references.");
        }
        foreach (var reference in command.TransitionReferences)
        {
            var known = context.KnownTransitionIds.Contains(reference);
            var hasBatchIntent = batchTransitionOrigins.TryGetValue(
                reference,
                out var originatingInput);
            if (reference == 0 ||
                reference > context.MaximumPermittedTransitionId ||
                !transitionReferences.Add(reference) ||
                (!known && !hasBatchIntent) ||
                (!known && originatingInput != command.InputSequence))
            {
                return Invalid(
                    ProtocolViolationCode.InvalidSequence,
                    "Owner command transition reference is duplicate or unresolved.");
            }
        }
        foreach (var reference in command.ActionReferences)
        {
            var known = context.KnownActionIds.Contains(reference);
            var hasBatchIntent = batchActionOrigins.TryGetValue(
                reference,
                out var originatingInput);
            if (reference == 0 ||
                reference > context.MaximumPermittedActionId ||
                !actionReferences.Add(reference) ||
                (!known && !hasBatchIntent) ||
                (!known && originatingInput != command.InputSequence))
            {
                return Invalid(
                    ProtocolViolationCode.InvalidSequence,
                    "Owner command action reference is duplicate or unresolved.");
            }
        }
        return ProtocolValidationResult.Valid;
    }

    private static bool BatchContainsInputSequence(
        OwnerCommandBatchDraft batch,
        ulong sequence)
    {
        foreach (var command in batch.Commands)
        {
            if (command.InputSequence == sequence)
            {
                return true;
            }
        }
        return false;
    }

    private static bool TryFindBatchCommandTarget(
        OwnerCommandBatchDraft batch,
        ulong sequence,
        out ulong targetTick)
    {
        foreach (var command in batch.Commands)
        {
            if (command.InputSequence == sequence)
            {
                targetTick = command.TargetSimulationTick;
                return true;
            }
        }
        targetTick = 0;
        return false;
    }

    private static bool FitsSimulationInstant(ulong tick) => tick <= long.MaxValue;

    private static bool IsFiniteVector(Vector3Value? vector) =>
        vector is not null &&
        float.IsFinite(vector.X) &&
        float.IsFinite(vector.Y) &&
        float.IsFinite(vector.Z);

    private static ProtocolValidationResult ValidateTraversalState(
        ReplicatedTraversalState? traversal)
    {
        if (traversal is null)
        {
            return ProtocolValidationResult.Valid;
        }

        return traversal.TraversalInstanceId != 0 &&
               Enum.IsDefined(traversal.Phase) &&
               traversal.Phase != ReplicatedTraversalPhase.Unspecified &&
               traversal.PhaseElapsedTicks <= ProtocolConstants.MaxPredictionTimerTicks &&
               traversal.PhaseDurationTicks <= ProtocolConstants.MaxPredictionTimerTicks &&
               IsFiniteBoundedVector(
                   traversal.LedgeAnchor,
                   ProtocolConstants.MaxPredictionCoordinateMagnitude) &&
               IsFiniteBoundedVector(traversal.LedgeNormal, 1.001f)
            ? ProtocolValidationResult.Valid
            : Invalid(
                ProtocolViolationCode.InvalidNumericValue,
                "Traversal state is incomplete or invalid.");
    }

    private static bool IsFiniteInRange(float value, float minimum, float maximum) =>
        float.IsFinite(value) && value >= minimum && value <= maximum;

    private static bool IsFiniteBoundedVector(Vector3Value? vector, float magnitude) =>
        IsFiniteVector(vector) &&
        MathF.Abs(vector!.X) <= magnitude &&
        MathF.Abs(vector.Y) <= magnitude &&
        MathF.Abs(vector.Z) <= magnitude;

    private static ProtocolValidationResult Invalid(ProtocolViolationCode code, string message) =>
        ProtocolValidationResult.Invalid(code, message);
}
