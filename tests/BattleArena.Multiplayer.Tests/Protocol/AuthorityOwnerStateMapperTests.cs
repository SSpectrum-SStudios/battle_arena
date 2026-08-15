using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Core.Movement.Simulation;
using BattleArena.Multiplayer.AuthoritySimulation;
using BattleArena.Multiplayer.OwnerPrediction;
using BattleArena.Multiplayer.Protocol;
using BattleArena.Protocol.V1;
using Google.Protobuf;

namespace BattleArena.Multiplayer.Tests.Protocol;

public sealed class AuthorityOwnerStateMapperTests
{
    [Fact]
    public void ScopeRoundTripsThroughTheWireDraft()
    {
        var epoch = Fixture.CreateEpoch();

        var draft = AuthorityOwnerStateMapper.ToProtocol(epoch);
        var decoded = AuthorityOwnerStateMapper.AuthorityEpochFromProtocol(
            OwnerPredictionScopeDraft.Parser.ParseFrom(draft.ToByteArray()));

        Assert.Equal(epoch, decoded);
    }

    [Fact]
    public void ReceivedWindowWithNoContiguousCursorRoundTripsAsNull()
    {
        var window = new OwnerKnownJournalIdentityWindow(null, 0);

        var draft = AuthorityOwnerStateMapper.ToProtocol(window);
        var decoded = AuthorityOwnerStateMapper.ReceivedWindowFromProtocol(
            SelectiveSequenceAcknowledgementDraft.Parser.ParseFrom(draft.ToByteArray()));

        Assert.False(draft.HasHighestContiguousSequence);
        Assert.Equal(window, decoded);
    }

    [Fact]
    public void ReceivedWindowWithContiguousCursorAndFollowingMaskRoundTrips()
    {
        var window = new OwnerKnownJournalIdentityWindow(41, 0b0000_0101UL);

        var draft = AuthorityOwnerStateMapper.ToProtocol(window);
        var decoded = AuthorityOwnerStateMapper.ReceivedWindowFromProtocol(
            SelectiveSequenceAcknowledgementDraft.Parser.ParseFrom(draft.ToByteArray()));

        Assert.True(draft.HasHighestContiguousSequence);
        Assert.Equal(41UL, draft.HighestContiguousSequence);
        Assert.Equal(window, decoded);
    }

    [Fact]
    public void SchedulerReceivedWindowDistinguishesReceiptFromConsumption()
    {
        // Frame 12 is admitted three frames ahead of the cursor: it is received
        // (occupies a cell) well before it is ever consumed. The mapped SACK must
        // reflect that gap rather than collapsing to the consumed cursor.
        var fixture = new Fixture(firstFrame: 10);
        fixture.Admit(frame: 10);
        fixture.Admit(frame: 11);
        fixture.Admit(frame: 12);
        fixture.ResolveFrame(); // consumes frame 10 only

        var window = fixture.Scheduler.ReceivedInputSequenceWindow;
        var consumedThrough = fixture.Scheduler.ConsumedThroughFrame;

        Assert.Equal(3UL, window.HighestContiguousId);
        Assert.Equal(new SimulationInstant(10), consumedThrough);

        var draft = AuthorityOwnerStateMapper.ToProtocol(window);
        var decoded = AuthorityOwnerStateMapper.ReceivedWindowFromProtocol(draft);
        Assert.Equal(window, decoded);
    }

    [Fact]
    public void AppliedInputEchoRoundTripsRepresentedFrameSequenceAndKind()
    {
        var fixture = new Fixture(firstFrame: 0);
        fixture.Admit(frame: 0);

        var decision = fixture.ResolveFrame();
        Assert.True(fixture.Scheduler.TryGetDisposition(new SimulationInstant(0), out var disposition));

        var draft = AuthorityOwnerStateMapper.ToProtocol(disposition);
        var echo = AuthorityOwnerStateMapper.DispositionEchoFromProtocol(
            OwnerInputFrameDispositionDraft.Parser.ParseFrom(draft.ToByteArray()));

        Assert.Equal(new SimulationInstant(0), echo.Frame);
        Assert.Equal(decision.AppliedInputSequence, echo.AppliedInputSequence);
        Assert.Equal(AuthorityInputApplicationKind.ReceivedCommand, echo.ApplicationKind);
    }

    [Fact]
    public void AppliedInputEchoRoundTripsAFallbackFrameWithNoInputSequence()
    {
        var fixture = new Fixture(firstFrame: 0);

        fixture.ResolveFrame(); // nothing admitted: neutral fallback, no sequence

        Assert.True(fixture.Scheduler.TryGetDisposition(new SimulationInstant(0), out var disposition));
        Assert.Null(disposition.AppliedInputSequence);

        var draft = AuthorityOwnerStateMapper.ToProtocol(disposition);
        Assert.False(draft.HasInputSequence);

        var echo = AuthorityOwnerStateMapper.DispositionEchoFromProtocol(draft);
        Assert.Null(echo.AppliedInputSequence);
        Assert.Equal(AuthorityInputApplicationKind.NeutralFallback, echo.ApplicationKind);
    }

    [Theory]
    [InlineData(MovementTransitionOutcome.Accepted, MovementTransitionRejectionReason.None)]
    [InlineData(MovementTransitionOutcome.Rejected, MovementTransitionRejectionReason.AuthorityPolicyRejected)]
    [InlineData(MovementTransitionOutcome.Rejected, MovementTransitionRejectionReason.InvalidState)]
    [InlineData(MovementTransitionOutcome.Rejected, MovementTransitionRejectionReason.CapabilityUnavailable)]
    [InlineData(MovementTransitionOutcome.Expired, MovementTransitionRejectionReason.DeadlineExpired)]
    [InlineData(MovementTransitionOutcome.Superseded, MovementTransitionRejectionReason.Superseded)]
    public void TransitionResolutionRoundTripsEveryTerminalOutcome(
        MovementTransitionOutcome outcome,
        MovementTransitionRejectionReason reason)
    {
        var scope = Fixture.CreateScope();
        var applied = outcome is MovementTransitionOutcome.Accepted;
        var decisionFrame = new SimulationInstant(50);
        var resolution = new MovementTransitionResolution(
            new TransitionResolutionIdentity(scope, new TransitionResolutionSequence(3)),
            new MovementTransitionIdentity(scope, new MovementTransitionId(9)),
            outcome,
            applied,
            applied ? decisionFrame : default,
            decisionFrame,
            reason);

        var draft = AuthorityOwnerStateMapper.ToProtocol(resolution);
        var decoded = AuthorityOwnerStateMapper.TransitionResolutionFromProtocol(
            scope,
            MovementTransitionResolutionDraft.Parser.ParseFrom(draft.ToByteArray()));

        Assert.Equal(resolution, decoded);
    }

    [Fact]
    public void RemappedTransitionResolutionRoundTripsItsLaterApplicationFrame()
    {
        var scope = Fixture.CreateScope();
        var resolution = new MovementTransitionResolution(
            new TransitionResolutionIdentity(scope, new TransitionResolutionSequence(4)),
            new MovementTransitionIdentity(scope, new MovementTransitionId(11)),
            MovementTransitionOutcome.Remapped,
            hasApplicationFrame: true,
            applicationFrame: new SimulationInstant(120),
            decisionFrame: new SimulationInstant(120),
            MovementTransitionRejectionReason.None);

        var draft = AuthorityOwnerStateMapper.ToProtocol(resolution);
        var decoded = AuthorityOwnerStateMapper.TransitionResolutionFromProtocol(scope, draft);

        Assert.Equal(resolution, decoded);
    }

    [Theory]
    [InlineData(PredictedActionOutcome.Accepted, PredictedActionRejectionReason.None)]
    [InlineData(PredictedActionOutcome.Remapped, PredictedActionRejectionReason.None)]
    [InlineData(PredictedActionOutcome.Rejected, PredictedActionRejectionReason.AuthorityPolicyRejected)]
    [InlineData(PredictedActionOutcome.Rejected, PredictedActionRejectionReason.InvalidState)]
    [InlineData(PredictedActionOutcome.Rejected, PredictedActionRejectionReason.CooldownActive)]
    [InlineData(PredictedActionOutcome.Rejected, PredictedActionRejectionReason.CapabilityUnavailable)]
    [InlineData(PredictedActionOutcome.Expired, PredictedActionRejectionReason.DeadlineExpired)]
    [InlineData(PredictedActionOutcome.Superseded, PredictedActionRejectionReason.Superseded)]
    public void ActionResolutionRoundTripsEveryTerminalOutcome(
        PredictedActionOutcome outcome,
        PredictedActionRejectionReason reason)
    {
        var scope = Fixture.CreateScope();
        var applied = outcome is PredictedActionOutcome.Accepted or
            PredictedActionOutcome.Remapped;
        var decisionFrame = new SimulationInstant(75);
        var execution = applied
            ? new AuthorityActionExecutionIdentity(
                new AuthorityActionExecutionScope(scope.SessionId, scope.Life),
                new AuthorityActionExecutionId(9_500))
            : default;
        var resolution = new PredictedActionResolution(
            new ActionResolutionIdentity(scope, new ActionResolutionSequence(2)),
            new PredictedActionIdentity(scope, new PredictedActionId(6)),
            outcome,
            applied,
            execution,
            applied ? decisionFrame : default,
            decisionFrame,
            reason);

        var draft = AuthorityOwnerStateMapper.ToProtocol(resolution);
        var decoded = AuthorityOwnerStateMapper.ActionResolutionFromProtocol(
            scope,
            PredictedActionResolutionDraft.Parser.ParseFrom(draft.ToByteArray()));

        Assert.Equal(resolution, decoded);
    }

    [Fact]
    public void ActionResolutionCarriesTheAllocatedAuthorityExecutionIdWhenAccepted()
    {
        var scope = Fixture.CreateScope();
        var execution = new AuthorityActionExecutionIdentity(
            new AuthorityActionExecutionScope(scope.SessionId, scope.Life),
            new AuthorityActionExecutionId(12_345));
        var resolution = new PredictedActionResolution(
            new ActionResolutionIdentity(scope, new ActionResolutionSequence(1)),
            new PredictedActionIdentity(scope, new PredictedActionId(2)),
            PredictedActionOutcome.Accepted,
            hasAuthorityExecution: true,
            execution,
            startFrame: new SimulationInstant(30),
            decisionFrame: new SimulationInstant(30),
            PredictedActionRejectionReason.None);

        var draft = AuthorityOwnerStateMapper.ToProtocol(resolution);

        Assert.True(draft.HasAuthorityExecutionId);
        Assert.Equal(12_345UL, draft.AuthorityExecutionId);
    }

    [Fact]
    public void LeadUpdateRoundTripsThroughTheWireDraft()
    {
        var epoch = Fixture.CreateEpoch();
        var update = new PredictionLeadUpdate(
            OwnerIntentScope.From(epoch),
            new PredictionLeadFrameCount(9),
            new PredictionLeadPolicyRevision(4),
            new SimulationInstant(500));

        var draft = AuthorityOwnerStateMapper.ToProtocol(epoch, update);
        var decoded = AuthorityOwnerStateMapper.LeadUpdateFromProtocol(
            OwnerPredictionLeadUpdateDraft.Parser.ParseFrom(draft.ToByteArray()));

        Assert.Equal(update, decoded);
    }

    [Fact]
    public void LeadUpdateMappingRejectsAScopeThatDoesNotMatchTheEpoch()
    {
        var epoch = Fixture.CreateEpoch();
        var foreignScope = new OwnerIntentScope(
            epoch.SessionId,
            new LifeEpoch(epoch.CombatantId, new LifeGenerationId(epoch.Life.Value + 1)),
            epoch.OwnerControl);
        var update = new PredictionLeadUpdate(
            foreignScope,
            new PredictionLeadFrameCount(9),
            new PredictionLeadPolicyRevision(1),
            new SimulationInstant(10));

        Assert.Throws<ArgumentException>(() => AuthorityOwnerStateMapper.ToProtocol(epoch, update));
    }

    [Fact]
    public void FullAggregateRoundTripsThroughEncodedProtobufBytes()
    {
        var fixture = new Fixture(firstFrame: 0);
        fixture.ObserveTransition(id: 1, firstPredicted: 0, lastValid: 10);
        fixture.ObserveAction(id: 1, predictedStart: 0, lastValidStart: 10);
        fixture.Admit(frame: 0, transitions: [1], actions: [1]);
        fixture.Admit(frame: 1);
        fixture.Admit(frame: 2);

        var decision = fixture.ResolveFrame();
        Assert.True(
            fixture.Scheduler.TryGetDisposition(new SimulationInstant(0), out var disposition));

        var recentDispositions = new AuthorityFrameTerminalDisposition[
            AuthorityOwnerInputScheduler.DefaultRetainedDispositionFrames];
        var count = fixture.Scheduler.CopyRetainedDispositions(recentDispositions);

        var transitionResolutions = fixture.Resolver.LastTransitionResolutions.ToArray();
        var actionResolutions = fixture.Resolver.LastActionResolutions.ToArray();
        Assert.Single(transitionResolutions);
        Assert.Single(actionResolutions);

        var leadUpdate = new PredictionLeadUpdate(
            fixture.Scope,
            new PredictionLeadFrameCount(6),
            new PredictionLeadPolicyRevision(2),
            new SimulationInstant(50));

        var state = new AuthorityOwnerState(
            fixture.Epoch,
            disposition,
            fixture.Scheduler.ReceivedInputSequenceWindow,
            fixture.Scheduler.ConsumedThroughFrame,
            recentDispositions[..count],
            transitionResolutions[0].ResolutionIdentity.Sequence,
            transitionResolutions,
            actionResolutions[0].ResolutionIdentity.Sequence,
            actionResolutions,
            leadUpdate);

        var encoded = AuthorityOwnerStateMapper.ToProtocol(state).ToByteArray();
        var decodedDraft = AuthorityOwnerStateDraft.Parser.ParseFrom(encoded);
        var decoded = AuthorityOwnerStateMapper.FromProtocol(decodedDraft);

        Assert.Equal(fixture.Epoch, decoded.Epoch);
        Assert.Equal(new SimulationInstant(0), decoded.AppliedInput.Frame);
        Assert.Equal(decision.AppliedInputSequence, decoded.AppliedInput.AppliedInputSequence);
        Assert.Equal(AuthorityInputApplicationKind.ReceivedCommand, decoded.AppliedInput.ApplicationKind);
        Assert.Equal(fixture.Scheduler.ReceivedInputSequenceWindow, decoded.ReceivedInputWindow);
        Assert.Equal(fixture.Scheduler.ConsumedThroughFrame, decoded.ConsumedThroughFrame);
        Assert.Equal(count, decoded.RecentDispositions.Count);
        Assert.Equal(transitionResolutions[0], Assert.Single(decoded.TransitionResolutions));
        Assert.Equal(
            transitionResolutions[0].ResolutionIdentity.Sequence,
            decoded.LatestTransitionResolutionSequence);
        Assert.Equal(actionResolutions[0], Assert.Single(decoded.ActionResolutions));
        Assert.Equal(
            actionResolutions[0].ResolutionIdentity.Sequence,
            decoded.LatestActionResolutionSequence);
        Assert.Equal(leadUpdate, decoded.LeadUpdate);

        // Re-encoding must reproduce the same bytes, and every field must be
        // rebuilt from a DECODED value rather than reusing the original. Reusing
        // the originals would compare the encoder to itself and prove nothing
        // about the decode direction.
        var reEncoded = new AuthorityOwnerStateDraft
        {
            Scope = AuthorityOwnerStateMapper.ToProtocol(decoded.Epoch),
            AppliedInput = AuthorityOwnerStateMapper.ToProtocol(decoded.AppliedInput),
            ReceivedInputs = AuthorityOwnerStateMapper.ToReceiveAcknowledgement(
                decoded.Epoch,
                decoded.ReceivedInputWindow),
            ConsumedInputs = new OwnerInputConsumptionAcknowledgementDraft
            {
                Scope = AuthorityOwnerStateMapper.ToProtocol(decoded.Epoch),
            },
        };
        if (decoded.ConsumedThroughFrame is { } consumed)
        {
            reEncoded.ConsumedInputs.ConsumedThroughSimulationTick =
                checked((ulong)consumed.Tick);
        }
        foreach (var echo in decoded.RecentDispositions)
        {
            reEncoded.ConsumedInputs.RecentDispositions.Add(
                AuthorityOwnerStateMapper.ToProtocol(echo));
        }
        reEncoded.LatestTransitionResolutionSequence =
            decoded.LatestTransitionResolutionSequence!.Value.Value;
        foreach (var resolution in decoded.TransitionResolutions)
        {
            reEncoded.TransitionResolutions.Add(
                AuthorityOwnerStateMapper.ToProtocol(resolution));
        }
        reEncoded.LatestActionResolutionSequence =
            decoded.LatestActionResolutionSequence!.Value.Value;
        foreach (var resolution in decoded.ActionResolutions)
        {
            reEncoded.ActionResolutions.Add(AuthorityOwnerStateMapper.ToProtocol(resolution));
        }
        reEncoded.LeadUpdate = AuthorityOwnerStateMapper.ToProtocol(
            decoded.Epoch,
            decoded.LeadUpdate!.Value);

        Assert.Equal(encoded, reEncoded.ToByteArray());
    }

    [Fact]
    public void ADispositionEchoReEncodesToTheExactBytesItWasDecodedFrom()
    {
        var fixture = new Fixture(firstFrame: 0);
        fixture.Admit(frame: 0);
        fixture.ResolveFrame();
        fixture.ResolveFrame(); // frame 1 has no command: neutral fallback

        foreach (var tick in new[] { 0L, 1L })
        {
            Assert.True(
                fixture.Scheduler.TryGetDisposition(new SimulationInstant(tick), out var disposition));
            var encoded = AuthorityOwnerStateMapper.ToProtocol(disposition).ToByteArray();

            var echo = AuthorityOwnerStateMapper.DispositionEchoFromProtocol(
                OwnerInputFrameDispositionDraft.Parser.ParseFrom(encoded));
            var reEncoded = AuthorityOwnerStateMapper.ToProtocol(echo).ToByteArray();

            Assert.Equal(encoded, reEncoded);
        }
    }

    [Fact]
    public void AggregateConstructionRejectsAResolutionFromAForeignScope()
    {
        var fixture = new Fixture(firstFrame: 0);
        fixture.Admit(frame: 0);
        var decision = fixture.ResolveFrame();
        Assert.True(fixture.Scheduler.TryGetDisposition(new SimulationInstant(0), out var disposition));
        _ = decision;

        var foreignScope = new OwnerIntentScope(
            fixture.Epoch.SessionId,
            new LifeEpoch(fixture.Epoch.CombatantId, new LifeGenerationId(fixture.Epoch.Life.Value + 1)),
            fixture.Epoch.OwnerControl);
        var foreignResolution = new MovementTransitionResolution(
            new TransitionResolutionIdentity(foreignScope, new TransitionResolutionSequence(1)),
            new MovementTransitionIdentity(foreignScope, new MovementTransitionId(1)),
            MovementTransitionOutcome.Accepted,
            hasApplicationFrame: true,
            applicationFrame: new SimulationInstant(0),
            decisionFrame: new SimulationInstant(0),
            MovementTransitionRejectionReason.None);

        Assert.Throws<ArgumentException>(() => new AuthorityOwnerState(
            fixture.Epoch,
            disposition,
            fixture.Scheduler.ReceivedInputSequenceWindow,
            fixture.Scheduler.ConsumedThroughFrame,
            Array.Empty<AuthorityFrameTerminalDisposition>(),
            null,
            [foreignResolution],
            null,
            [],
            null));
    }

    [Fact]
    public void MoreRecentDispositionsThanTheProtocolPermitsIsRefusedAtConstruction()
    {
        // The scheduler retains more frames than one acknowledgement may carry,
        // so an unsliced CopyRetainedDispositions buffer would otherwise produce
        // a message the peer's own validator rejects.
        var fixture = new Fixture(firstFrame: 0);
        var overLimit = ProtocolConstants.MaxOwnerRecentInputDispositions + 1;
        for (var frame = 0; frame < overLimit; frame++)
        {
            fixture.ResolveFrame();
        }

        var buffer = new AuthorityFrameTerminalDisposition[
            AuthorityOwnerInputScheduler.DefaultRetainedDispositionFrames];
        var count = fixture.Scheduler.CopyRetainedDispositions(buffer);
        Assert.True(count > ProtocolConstants.MaxOwnerRecentInputDispositions);
        Assert.True(fixture.Scheduler.TryGetDisposition(new SimulationInstant(0), out var applied));

        var error = Assert.Throws<ArgumentException>(() => new AuthorityOwnerState(
            fixture.Epoch,
            applied,
            fixture.Scheduler.ReceivedInputSequenceWindow,
            fixture.Scheduler.ConsumedThroughFrame,
            buffer[..count],
            null,
            [],
            null,
            [],
            null));
        Assert.Contains("recent dispositions", error.Message, StringComparison.Ordinal);

        // Sliced to the wire allowance, the same data is accepted.
        _ = new AuthorityOwnerState(
            fixture.Epoch,
            applied,
            fixture.Scheduler.ReceivedInputSequenceWindow,
            fixture.Scheduler.ConsumedThroughFrame,
            buffer[(count - ProtocolConstants.MaxOwnerRecentInputDispositions)..count],
            null,
            [],
            null,
            [],
            null);
    }

    [Fact]
    public void AnAggregateMissingARequiredNestedMessageFailsClosed()
    {
        var epoch = Fixture.CreateEpoch();
        var scope = AuthorityOwnerStateMapper.ToProtocol(epoch);

        // Omitting a plain message field is ordinary proto3, not a malformed
        // packet, so this must be a stated refusal rather than a null dereference.
        var missingConsumed = new AuthorityOwnerStateDraft
        {
            Scope = scope,
            AppliedInput = new OwnerInputFrameDispositionDraft
            {
                TargetSimulationTick = 0,
                Kind = OwnerInputFrameDispositionKindDraft.NeutralFallback,
            },
            ReceivedInputs = AuthorityOwnerStateMapper.ToReceiveAcknowledgement(
                epoch,
                new OwnerKnownJournalIdentityWindow(null, 0)),
        };

        Assert.Throws<ArgumentException>(
            () => AuthorityOwnerStateMapper.FromProtocol(missingConsumed));
    }

    [Fact]
    public void AnAggregateWhoseNestedScopesDisagreeIsRefusedOnDecode()
    {
        var epoch = Fixture.CreateEpoch();
        var foreignEpoch = new CombatantAuthorityPredictionEpoch(
            epoch.SessionId,
            epoch.MatchFrameEpoch,
            epoch.CombatantId,
            new LifeGenerationId(epoch.Life.Value + 1),
            epoch.AuthorityDiscontinuity,
            epoch.OwnerControl);

        var draft = new AuthorityOwnerStateDraft
        {
            Scope = AuthorityOwnerStateMapper.ToProtocol(epoch),
            AppliedInput = new OwnerInputFrameDispositionDraft
            {
                TargetSimulationTick = 0,
                Kind = OwnerInputFrameDispositionKindDraft.NeutralFallback,
            },
            ReceivedInputs = AuthorityOwnerStateMapper.ToReceiveAcknowledgement(
                epoch,
                new OwnerKnownJournalIdentityWindow(null, 0)),
            ConsumedInputs = AuthorityOwnerStateMapper.ToConsumptionAcknowledgement(
                epoch,
                null,
                []),
            // The lead update claims a different life than the aggregate does.
            LeadUpdate = AuthorityOwnerStateMapper.ToProtocol(
                foreignEpoch,
                new PredictionLeadUpdate(
                    OwnerIntentScope.From(foreignEpoch),
                    new PredictionLeadFrameCount(5),
                    new PredictionLeadPolicyRevision(1),
                    new SimulationInstant(10))),
        };

        var error = Assert.Throws<ArgumentException>(
            () => AuthorityOwnerStateMapper.FromProtocol(draft));
        Assert.Contains("lead_update.scope", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADispositionWhoseKindAndInputSequenceDisagreeIsRefused()
    {
        // AuthorityInputApplicationKind.ReceivedCommand is the zero enum value,
        // so a default-constructed or truncated message reads as a received
        // command at tick zero carrying no sequence, which is self-contradictory.
        var fabricated = new OwnerInputFrameDispositionDraft
        {
            TargetSimulationTick = 0,
            Kind = OwnerInputFrameDispositionKindDraft.ReceivedCommand,
        };
        Assert.Throws<ArgumentException>(
            () => AuthorityOwnerStateMapper.DispositionEchoFromProtocol(fabricated));

        var fallbackClaimingASequence = new OwnerInputFrameDispositionDraft
        {
            TargetSimulationTick = 4,
            Kind = OwnerInputFrameDispositionKindDraft.NeutralFallback,
            InputSequence = 9,
        };
        Assert.Throws<ArgumentException>(
            () => AuthorityOwnerStateMapper.DispositionEchoFromProtocol(fallbackClaimingASequence));
    }

    [Theory]
    [InlineData(OwnerInputFrameDispositionKindDraft.Unspecified)]
    [InlineData(OwnerInputFrameDispositionKindDraft.RejectedCommand)]
    [InlineData(OwnerInputFrameDispositionKindDraft.LateCommand)]
    public void ArrivalOnlyDispositionKindsCannotDescribeAConsumedFrame(
        OwnerInputFrameDispositionKindDraft kind)
    {
        // Rejected and late are arrival classifications: neither ever supplied a
        // frame's input, so neither can describe what the authority applied.
        var draft = new OwnerInputFrameDispositionDraft
        {
            TargetSimulationTick = 3,
            Kind = kind,
        };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => AuthorityOwnerStateMapper.DispositionEchoFromProtocol(draft));
    }

    [Fact]
    public void HostileScalarsFailClosedRatherThanProducingCorruptDomainValues()
    {
        var scope = Fixture.CreateScope();

        // Zero identities, where the domain types require positive values.
        Assert.ThrowsAny<ArgumentException>(() =>
            AuthorityOwnerStateMapper.TransitionResolutionFromProtocol(
                scope,
                new MovementTransitionResolutionDraft
                {
                    ResolutionSequence = 0,
                    TransitionId = 1,
                    Outcome = MovementTransitionOutcomeDraft.Expired,
                    DecisionTick = 5,
                    RejectionReason = MovementTransitionRejectionReasonDraft.DeadlineExpired,
                }));

        // An unspecified enum is not a terminal outcome.
        Assert.ThrowsAny<ArgumentException>(() =>
            AuthorityOwnerStateMapper.TransitionResolutionFromProtocol(
                scope,
                new MovementTransitionResolutionDraft
                {
                    ResolutionSequence = 1,
                    TransitionId = 1,
                    Outcome = MovementTransitionOutcomeDraft.Unspecified,
                    DecisionTick = 5,
                    RejectionReason = MovementTransitionRejectionReasonDraft.None,
                }));

        // A decision tick past long.MaxValue cannot be a SimulationInstant.
        Assert.Throws<OverflowException>(() =>
            AuthorityOwnerStateMapper.TransitionResolutionFromProtocol(
                scope,
                new MovementTransitionResolutionDraft
                {
                    ResolutionSequence = 1,
                    TransitionId = 1,
                    Outcome = MovementTransitionOutcomeDraft.Expired,
                    DecisionTick = ulong.MaxValue,
                    RejectionReason = MovementTransitionRejectionReasonDraft.DeadlineExpired,
                }));

        // An accepted action must carry its authority execution, and a terminal
        // one must not claim a fabricated execution it never received.
        Assert.Throws<ArgumentException>(() =>
            AuthorityOwnerStateMapper.ActionResolutionFromProtocol(
                scope,
                new PredictedActionResolutionDraft
                {
                    ResolutionSequence = 1,
                    ActionId = 1,
                    Outcome = PredictedActionOutcomeDraft.Accepted,
                    DecisionTick = 5,
                    RejectionReason = PredictedActionRejectionReasonDraft.None,
                }));
        Assert.Throws<ArgumentException>(() =>
            AuthorityOwnerStateMapper.ActionResolutionFromProtocol(
                scope,
                new PredictedActionResolutionDraft
                {
                    ResolutionSequence = 1,
                    ActionId = 1,
                    Outcome = PredictedActionOutcomeDraft.Expired,
                    AuthorityExecutionId = 77,
                    DecisionTick = 5,
                    RejectionReason = PredictedActionRejectionReasonDraft.DeadlineExpired,
                }));
    }

    private sealed class Fixture
    {
        private long _firstFrame;

        public Fixture(long firstFrame, int capacity = 16)
        {
            Epoch = CreateEpoch();
            Scope = OwnerIntentScope.From(Epoch);
            _firstFrame = firstFrame;

            Scheduler = new AuthorityOwnerInputScheduler(
                Epoch,
                new SimulationInstant(firstFrame),
                capacity,
                new AuthorityInputFallbackPolicy(2),
                AuthorityOwnerInputScheduler.DefaultRetainedDispositionFrames);
            TransitionJournal = new AuthorityMovementTransitionJournal(Scope, 32);
            ActionJournal = new AuthorityOwnerActionCommandJournal(Scope, 32);
            Resolver = new AuthorityFrameIntentResolver(
                TransitionJournal,
                ActionJournal,
                new MovementTransitionResolver(PermissiveMovementTransitionAdmissionPolicy.Instance),
                new PredictedActionResolver(new MonotonicAuthorityActionExecutionAllocator(9_000)));
        }

        public CombatantAuthorityPredictionEpoch Epoch { get; }
        public OwnerIntentScope Scope { get; }
        public AuthorityOwnerInputScheduler Scheduler { get; }
        public AuthorityMovementTransitionJournal TransitionJournal { get; }
        public AuthorityOwnerActionCommandJournal ActionJournal { get; }
        public AuthorityFrameIntentResolver Resolver { get; }

        public static CombatantAuthorityPredictionEpoch CreateEpoch() => new(
            10,
            new MatchFrameEpochId(1),
            new CombatantId(4),
            new LifeGenerationId(1),
            new AuthorityDiscontinuityId(1),
            new OwnerControlEpoch(3));

        public static OwnerIntentScope CreateScope() => OwnerIntentScope.From(CreateEpoch());

        public void ObserveTransition(ulong id, long firstPredicted, long lastValid)
        {
            var decision = TransitionJournal.Observe(new MovementTransitionIntent(
                new MovementTransitionIdentity(Scope, new MovementTransitionId(id)),
                new OwnerInputIdentity(Scope, new InputSequence(id)),
                MovementTransitionKind.JumpPressed,
                new SimulationInstant(firstPredicted),
                new SimulationInstant(lastValid)));
            Assert.Equal(AuthorityTransitionObserveDecision.FirstSeen, decision);
        }

        public void ObserveAction(ulong id, long predictedStart, long lastValidStart)
        {
            var decision = ActionJournal.Observe(new PredictedActionIntent(
                new PredictedActionIdentity(Scope, new PredictedActionId(id)),
                new OwnerInputIdentity(Scope, new InputSequence(id)),
                OwnerActionTrigger.Attack,
                new SimulationInstant(predictedStart),
                new SimulationInstant(lastValidStart),
                new SimulationInstant(predictedStart)));
            Assert.Equal(AuthorityActionObserveDecision.FirstSeen, decision);
        }

        public void Admit(long frame, ulong[]? transitions = null, ulong[]? actions = null)
        {
            var input = new CharacterSimulationInput(
                MovementAxes.FromUnitVector(new HorizontalVector(0.6d, -0.4d)),
                ViewOrientation.FromRadians(1.25d, -0.15d),
                new MovementHeldState(MovementHeldButtons.Sprint),
                transitions is null
                    ? default
                    : new TransitionReferenceBuffer(
                        transitions.Select(id => new MovementTransitionId(id)).ToArray()),
                new CombatInputState(CombatHeldButtons.None),
                actions is null
                    ? default
                    : new ActionReferenceBuffer(
                        actions.Select(id => new PredictedActionId(id)).ToArray()),
                new MovementConfigurationRevision(7),
                new MovementCapabilityRevision(8));

            var admission = Scheduler.TryAdmit(new OwnerSimulationCommand(
                new OwnerInputIdentity(
                    Scope,
                    new InputSequence((ulong)(frame - _firstFrame) + 1)),
                Epoch.AuthorityDiscontinuity,
                Epoch.MatchFrameEpoch,
                new SimulationInstant(frame),
                input));
            Assert.True(admission.WasStored);
        }

        public AuthorityInputFrameDecision ResolveFrame() =>
            Scheduler.ResolveNextFrame(
                new AuthorityFallbackInputBasis(
                    ViewOrientation.FromRadians(1.25d, -0.15d),
                    new MovementConfigurationRevision(7),
                    new MovementCapabilityRevision(8)),
                Resolver);
    }
}
