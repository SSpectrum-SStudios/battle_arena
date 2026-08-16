using BattleArena.Multiplayer.Protocol;
using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Tests.Protocol;

public sealed class AuthorityOwnerStateValidatorTests
{
    private readonly InboundMessageValidator _validator = new();

    [Fact]
    public void AWellFormedStateIsAccepted()
    {
        var result = _validator.ValidateAuthorityOwnerStateDraft(Valid(), Context());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void AMissingSubmessageIsAClassifiedViolationRatherThanAnException()
    {
        // proto3 omits an unset message, so this is the ordinary encoding of a
        // truncated or hostile packet — it must not reach a null dereference.
        var state = Valid();
        state.ConsumedInputs = null;

        var result = _validator.ValidateAuthorityOwnerStateDraft(state, Context());

        Assert.Equal(ProtocolViolationCode.MalformedPayload, result.Violation?.Code);
    }

    [Fact]
    public void ASpoofedLeadUpdateScopeIsRejected()
    {
        // The scope rides the wire four times. Checking only three leaves this
        // copy as a spoofing surface.
        var state = Valid();
        state.LeadUpdate.Scope.CombatantId = 999;

        var result = _validator.ValidateAuthorityOwnerStateDraft(state, Context());

        Assert.Equal(ProtocolViolationCode.InvalidSession, result.Violation?.Code);
    }

    [Theory]
    [InlineData("received")]
    [InlineData("consumed")]
    public void ASpoofedAcknowledgementScopeIsRejected(string which)
    {
        var state = Valid();
        if (which == "received")
        {
            state.ReceivedInputs.Scope.LifeId = 999;
        }
        else
        {
            state.ConsumedInputs.Scope.LifeId = 999;
        }

        var result = _validator.ValidateAuthorityOwnerStateDraft(state, Context());

        Assert.Equal(ProtocolViolationCode.InvalidSession, result.Violation?.Code);
    }

    [Fact]
    public void AReceiveAcknowledgementClaimingUnoriginatedInputIsRejected()
    {
        // The exact class of defect found twice during P04-08, and undetectable
        // from the message alone.
        var state = Valid();
        state.ReceivedInputs.ReceivedInputs.HighestContiguousSequence = 5_000;

        var result = _validator.ValidateAuthorityOwnerStateDraft(state, Context());

        Assert.Equal(ProtocolViolationCode.InvalidSequence, result.Violation?.Code);
    }

    [Fact]
    public void AResolutionNamingAnUnoriginatedIdentityIsRejected()
    {
        // Without this one forged resolution reaches the journal's unknown-identity
        // branch and forces a full baseline repair.
        var state = Valid();
        state.TransitionResolutions[0].TransitionId = 4_242;

        var result = _validator.ValidateAuthorityOwnerStateDraft(state, Context());

        Assert.Equal(ProtocolViolationCode.InvalidSequence, result.Violation?.Code);
    }

    [Fact]
    public void AnInflatedResolutionCursorIsRejected()
    {
        // Permanent poison: the client would discard every genuine resolution as
        // stale from here on.
        var state = Valid();
        state.LatestTransitionResolutionSequence = ulong.MaxValue;

        var result = _validator.ValidateAuthorityOwnerStateDraft(state, Context());

        Assert.Equal(ProtocolViolationCode.InvalidSequence, result.Violation?.Code);
    }

    [Fact]
    public void AResolutionCursorMovingBackwardIsRejected()
    {
        var state = Valid();
        state.LatestTransitionResolutionSequence = 1;
        state.TransitionResolutions[0].ResolutionSequence = 1;

        var result = _validator.ValidateAuthorityOwnerStateDraft(
            state,
            Context() with { AppliedTransitionResolutionCursor = 9 });

        Assert.Equal(ProtocolViolationCode.InvalidSequence, result.Violation?.Code);
    }

    [Fact]
    public void ATickBeyondPublishedAuthorityTimeIsRejected()
    {
        var state = Valid();
        state.AppliedInput.TargetSimulationTick = 5_000;

        var result = _validator.ValidateAuthorityOwnerStateDraft(state, Context());

        Assert.Equal(ProtocolViolationCode.InvalidNumericValue, result.Violation?.Code);
    }

    [Fact]
    public void ANonContiguousDispositionHistoryIsRejected()
    {
        // A gap could otherwise hide a frame that was never resolved.
        var state = Valid();
        state.ConsumedInputs.RecentDispositions[1].TargetSimulationTick = 80;

        var result = _validator.ValidateAuthorityOwnerStateDraft(state, Context());

        Assert.Equal(ProtocolViolationCode.InvalidNumericValue, result.Violation?.Code);
    }

    [Fact]
    public void ADispositionWhoseKindDisagreesWithItsSequenceIsRejected()
    {
        var state = Valid();
        state.AppliedInput.Kind = OwnerInputFrameDispositionKindDraft.NeutralFallback;

        var result = _validator.ValidateAuthorityOwnerStateDraft(state, Context());

        Assert.Equal(ProtocolViolationCode.InvalidSequence, result.Violation?.Code);
    }

    [Fact]
    public void AnUnspecifiedEnumMemberIsRejected()
    {
        var state = Valid();
        state.ActionResolutions[0].Outcome = PredictedActionOutcomeDraft.Unspecified;

        var result = _validator.ValidateAuthorityOwnerStateDraft(state, Context());

        Assert.Equal(ProtocolViolationCode.InvalidEnumValue, result.Violation?.Code);
    }

    [Fact]
    public void AnAcceptedActionWithoutAnExecutionIdentityIsRejected()
    {
        var state = Valid();
        state.ActionResolutions[0].ClearAuthorityExecutionId();

        var result = _validator.ValidateAuthorityOwnerStateDraft(state, Context());

        Assert.Equal(ProtocolViolationCode.InvalidSequence, result.Violation?.Code);
    }

    [Fact]
    public void ALeadOutsideTheNegotiatedPolicyIsRejected()
    {
        var state = Valid();
        state.LeadUpdate.TargetLeadFrames = 900;

        var result = _validator.ValidateAuthorityOwnerStateDraft(state, Context());

        Assert.Equal(ProtocolViolationCode.InvalidNumericValue, result.Violation?.Code);
    }

    [Fact]
    public void ALeadEffectiveTooSoonToScheduleIsRejected()
    {
        // Weaker than the P03-08 client gate would be worse than useless: the
        // validator would admit updates the gate then refuses.
        var state = Valid();
        state.LeadUpdate.EffectiveTick = 91;

        var result = _validator.ValidateAuthorityOwnerStateDraft(state, Context());

        Assert.Equal(ProtocolViolationCode.InvalidNumericValue, result.Violation?.Code);
    }

    [Fact]
    public void AnOversizedDispositionListIsRejected()
    {
        var state = Valid();
        state.ConsumedInputs.RecentDispositions.Clear();
        for (var i = 0; i <= ProtocolConstants.MaxOwnerRecentInputDispositions; i++)
        {
            state.ConsumedInputs.RecentDispositions.Add(new OwnerInputFrameDispositionDraft
            {
                TargetSimulationTick = (ulong)i,
                Kind = OwnerInputFrameDispositionKindDraft.NeutralFallback,
            });
        }

        var result = _validator.ValidateAuthorityOwnerStateDraft(state, Context());

        Assert.Equal(ProtocolViolationCode.InvalidCollectionCount, result.Violation?.Code);
    }

    private static AuthorityOwnerStateDraftValidationContext Context() => new(
        new OwnerPredictionDraftScopeExpectation(7UL, 1UL, 4UL, 1UL, 1UL, 1UL),
        HighestAuthorityFramePublished: 100UL,
        EarliestRetainedTargetTick: 0UL,
        HighestOriginatedInputSequence: 200UL,
        KnownTransitionIds: new OwnerKnownJournalIdentityWindow(10UL, 0UL),
        MaximumPermittedTransitionId: 64UL,
        KnownActionIds: new OwnerKnownJournalIdentityWindow(10UL, 0UL),
        MaximumPermittedActionId: 64UL,
        AppliedTransitionResolutionCursor: 0UL,
        MaximumPermittedTransitionResolutionSequence: 500UL,
        AppliedActionResolutionCursor: 0UL,
        MaximumPermittedActionResolutionSequence: 500UL,
        EarliestPermittedLeadEffectiveTick: 110UL,
        MinimumLeadFrames: 2U,
        MaximumLeadFrames: 24U);

    private static OwnerPredictionScopeDraft Scope() => new()
    {
        SessionId = 7UL,
        MatchFrameEpoch = 1UL,
        CombatantId = 4UL,
        LifeId = 1UL,
        AuthorityDiscontinuityId = 1UL,
        OwnerControlEpoch = 1UL,
    };

    private static AuthorityOwnerStateDraft Valid()
    {
        var state = new AuthorityOwnerStateDraft
        {
            Scope = Scope(),
            AppliedInput = new OwnerInputFrameDispositionDraft
            {
                TargetSimulationTick = 50UL,
                InputSequence = 51UL,
                Kind = OwnerInputFrameDispositionKindDraft.ReceivedCommand,
            },
            ReceivedInputs = new OwnerInputReceiveAcknowledgementDraft
            {
                Scope = Scope(),
                ReceivedInputs = new SelectiveSequenceAcknowledgementDraft
                {
                    HighestContiguousSequence = 51UL,
                    Following64ReceivedMask = 0UL,
                },
            },
            ConsumedInputs = new OwnerInputConsumptionAcknowledgementDraft
            {
                Scope = Scope(),
                ConsumedThroughSimulationTick = 50UL,
            },
            LatestTransitionResolutionSequence = 3UL,
            LatestActionResolutionSequence = 2UL,
            LeadUpdate = new OwnerPredictionLeadUpdateDraft
            {
                Scope = Scope(),
                TargetLeadFrames = 6U,
                LeadPolicyRevision = 4UL,
                EffectiveTick = 120UL,
            },
        };

        for (var tick = 48UL; tick <= 50UL; tick++)
        {
            state.ConsumedInputs.RecentDispositions.Add(new OwnerInputFrameDispositionDraft
            {
                TargetSimulationTick = tick,
                InputSequence = tick + 1UL,
                Kind = OwnerInputFrameDispositionKindDraft.ReceivedCommand,
            });
        }

        state.TransitionResolutions.Add(new MovementTransitionResolutionDraft
        {
            ResolutionSequence = 3UL,
            TransitionId = 2UL,
            Outcome = MovementTransitionOutcomeDraft.Accepted,
            DecisionTick = 49UL,
            RejectionReason = MovementTransitionRejectionReasonDraft.None,
        });
        state.ActionResolutions.Add(new PredictedActionResolutionDraft
        {
            ResolutionSequence = 2UL,
            ActionId = 3UL,
            Outcome = PredictedActionOutcomeDraft.Accepted,
            AuthorityExecutionId = 9_001UL,
            DecisionTick = 50UL,
            RejectionReason = PredictedActionRejectionReasonDraft.None,
        });
        return state;
    }
}
