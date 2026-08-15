using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Core.Movement.Simulation;
using BattleArena.Multiplayer.AuthoritySimulation;
using BattleArena.Multiplayer.OwnerPrediction;

namespace BattleArena.Multiplayer.Tests.AuthoritySimulation;

public sealed class AuthorityInputFrameDecisionTests
{
    [Fact]
    public void ReceivedDecisionRetainsExactCommandAndAllDurableReferences()
    {
        var epoch = Epoch();
        var command = Command(
            epoch,
            frame: 20,
            sequence: 7,
            input: Input(
                transitions: new TransitionReferenceBuffer(
                    [new MovementTransitionId(3)]),
                actions: new ActionReferenceBuffer([new PredictedActionId(4)])));

        var decision = AuthorityInputFrameDecision.ApplyReceived(
            Frame(epoch, 20),
            command);

        Assert.True(decision.IsValid);
        Assert.Equal(AuthorityInputApplicationKind.ReceivedCommand, decision.ApplicationKind);
        Assert.Equal(command, decision.ReceivedCommand);
        Assert.Equal(new InputSequence(7), decision.AppliedInputSequence);
        Assert.Equal(command.Input, decision.AppliedInput);
        Assert.Equal(1, decision.AppliedInput.TransitionReferences.Count);
        Assert.Equal(1, decision.AppliedInput.ActionReferences.Count);
        Assert.Equal(0u, decision.ConsecutiveMissingFrames);
        Assert.Null(decision.OverrideReason);
    }

    [Fact]
    public void ReceivedDecisionRequiresExactFrameScopeAndDiscontinuity()
    {
        var epoch = Epoch();
        var command = Command(epoch, frame: 20);

        Assert.Throws<ArgumentException>(() =>
            AuthorityInputFrameDecision.ApplyReceived(Frame(epoch, 21), command));
        Assert.Throws<ArgumentException>(() =>
            AuthorityInputFrameDecision.ApplyReceived(
                Frame(Epoch(life: 2), 20),
                command));
        Assert.Throws<ArgumentException>(() =>
            AuthorityInputFrameDecision.ApplyReceived(
                Frame(Epoch(discontinuity: 2), 20),
                command));
    }

    [Fact]
    public void RepeatedContinuousKeepsContinuousHeldAndViewButNeverEdges()
    {
        var epoch = Epoch();
        var previous = AuthorityInputFrameDecision.ApplyReceived(
            Frame(epoch, 20),
            Command(
                epoch,
                frame: 20,
                input: Input(
                    transitions: new TransitionReferenceBuffer(
                        [new MovementTransitionId(3)]),
                    actions: new ActionReferenceBuffer([new PredictedActionId(4)]))));
        var policy = new AuthorityInputFallbackPolicy(2);
        var currentBasis = new AuthorityFallbackInputBasis(
            ViewOrientation.FromRadians(2d, 0.2d),
            new MovementConfigurationRevision(90),
            new MovementCapabilityRevision(91));

        var repeated = policy.ResolveMissingFrame(
            Frame(epoch, 21),
            previous,
            currentBasis);

        Assert.True(repeated.IsValid);
        Assert.Equal(
            AuthorityInputApplicationKind.RepeatedContinuous,
            repeated.ApplicationKind);
        Assert.Equal(previous.AppliedInput.Movement, repeated.AppliedInput.Movement);
        Assert.Equal(previous.AppliedInput.View, repeated.AppliedInput.View);
        Assert.Equal(previous.AppliedInput.MovementHeld, repeated.AppliedInput.MovementHeld);
        Assert.Equal(previous.AppliedInput.CombatInput, repeated.AppliedInput.CombatInput);
        Assert.Equal(0, repeated.AppliedInput.TransitionReferences.Count);
        Assert.Equal(0, repeated.AppliedInput.ActionReferences.Count);
        Assert.Equal(90UL, repeated.AppliedInput.MovementRevision.Value);
        Assert.Equal(91UL, repeated.AppliedInput.CapabilityRevision.Value);
        Assert.Null(repeated.ReceivedCommand);
        Assert.Equal(1u, repeated.ConsecutiveMissingFrames);
    }

    [Fact]
    public void PolicyBecomesNeutralAfterBoundAndPreservesOnlySafeCurrentBasis()
    {
        var epoch = Epoch();
        var policy = new AuthorityInputFallbackPolicy(2);
        var basis = new AuthorityFallbackInputBasis(
            ViewOrientation.FromRadians(2.5d, -0.25d),
            new MovementConfigurationRevision(30),
            new MovementCapabilityRevision(31));
        var first = AuthorityInputFrameDecision.ApplyReceived(
            Frame(epoch, 10),
            Command(epoch, frame: 10));
        var missing1 = policy.ResolveMissingFrame(Frame(epoch, 11), first, basis);
        var missing2 = policy.ResolveMissingFrame(Frame(epoch, 12), missing1, basis);

        var neutral = policy.ResolveMissingFrame(Frame(epoch, 13), missing2, basis);

        Assert.True(neutral.IsValid);
        Assert.Equal(AuthorityInputApplicationKind.NeutralFallback, neutral.ApplicationKind);
        Assert.Equal(default, neutral.AppliedInput.Movement);
        Assert.Equal(MovementHeldButtons.None, neutral.AppliedInput.MovementHeld.Buttons);
        Assert.Equal(CombatHeldButtons.None, neutral.AppliedInput.CombatInput.HeldButtons);
        Assert.Equal(0, neutral.AppliedInput.TransitionReferences.Count);
        Assert.Equal(0, neutral.AppliedInput.ActionReferences.Count);
        Assert.Equal(basis.SafeView, neutral.AppliedInput.View);
        Assert.Equal(basis.MovementRevision, neutral.AppliedInput.MovementRevision);
        Assert.Equal(basis.CapabilityRevision, neutral.AppliedInput.CapabilityRevision);
        Assert.Equal(3u, neutral.ConsecutiveMissingFrames);

        var remainsNeutral = policy.ResolveMissingFrame(
            Frame(epoch, 14),
            neutral,
            basis);
        Assert.Equal(
            AuthorityInputApplicationKind.NeutralFallback,
            remainsNeutral.ApplicationKind);
        Assert.Equal(4u, remainsNeutral.ConsecutiveMissingFrames);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(256)]
    public void FallbackPolicyAcceptsAllDeclaredBoundaries(int repeatedFrames)
    {
        var policy = new AuthorityInputFallbackPolicy(repeatedFrames);

        Assert.True(policy.IsValid);
        Assert.Equal(repeatedFrames, policy.MaximumRepeatedContinuousFrames);
    }

    [Fact]
    public void ZeroRepeatPolicyAndMissingHistoryChooseNeutralImmediately()
    {
        var epoch = Epoch();
        var basis = AuthorityFallbackInputBasis.From(Input());
        var received = AuthorityInputFrameDecision.ApplyReceived(
            Frame(epoch, 0),
            Command(epoch, frame: 0));

        var noRepeat = new AuthorityInputFallbackPolicy(0).ResolveMissingFrame(
            Frame(epoch, 1),
            received,
            basis);
        var noHistory = new AuthorityInputFallbackPolicy(2).ResolveMissingFrame(
            Frame(epoch, 0),
            previousDecision: null,
            basis);

        Assert.Equal(AuthorityInputApplicationKind.NeutralFallback, noRepeat.ApplicationKind);
        Assert.Equal(AuthorityInputApplicationKind.NeutralFallback, noHistory.ApplicationKind);
        Assert.Equal(1u, noRepeat.ConsecutiveMissingFrames);
        Assert.Equal(1u, noHistory.ConsecutiveMissingFrames);
    }

    [Fact]
    public void FallbackRejectsNonAdjacentForeignOrInvalidPredecessors()
    {
        var epoch = Epoch();
        var policy = new AuthorityInputFallbackPolicy(2);
        var basis = AuthorityFallbackInputBasis.From(Input());
        var prior = AuthorityInputFrameDecision.ApplyReceived(
            Frame(epoch, 5),
            Command(epoch, frame: 5));
        var foreign = AuthorityInputFrameDecision.ApplyReceived(
            Frame(Epoch(combatant: 8), 5),
            Command(Epoch(combatant: 8), frame: 5));

        Assert.Throws<ArgumentException>(() => policy.ResolveMissingFrame(
            Frame(epoch, 7), prior, basis));
        Assert.Throws<ArgumentException>(() => policy.ResolveMissingFrame(
            Frame(epoch, 6), foreign, basis));
        Assert.Throws<ArgumentException>(() => policy.ResolveMissingFrame(
            Frame(epoch, 6), default(AuthorityInputFrameDecision), basis));
    }

    [Fact]
    public void AuthorityOverrideIsOneApplicationAndCannotInventOwnerEdges()
    {
        var epoch = Epoch();
        var cleanInput = Input(transitions: default, actions: default);

        var decision = AuthorityInputFrameDecision.ApplyAuthorityOverride(
            Frame(epoch, 9),
            cleanInput,
            AuthorityInputOverrideReason.Stunned);

        Assert.True(decision.IsValid);
        Assert.Equal(AuthorityInputApplicationKind.AuthorityOverride, decision.ApplicationKind);
        Assert.Equal(AuthorityInputOverrideReason.Stunned, decision.OverrideReason);
        Assert.Null(decision.ReceivedCommand);
        Assert.Null(decision.AppliedInputSequence);
        Assert.Equal(0u, decision.ConsecutiveMissingFrames);

        Assert.Throws<ArgumentException>(() =>
            AuthorityInputFrameDecision.ApplyAuthorityOverride(
                Frame(epoch, 10),
                Input(
                    transitions: new TransitionReferenceBuffer(
                        [new MovementTransitionId(1)])),
                AuthorityInputOverrideReason.Stunned));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AuthorityInputFrameDecision.ApplyAuthorityOverride(
                Frame(epoch, 10),
                cleanInput,
                (AuthorityInputOverrideReason)byte.MaxValue));
    }

    [Fact]
    public void ArrivalClassificationKeepsLateAndRejectedSeparateFromApplication()
    {
        var epoch = Epoch();
        var command = Command(epoch, frame: 12);

        var late = new AuthorityInputArrivalDecision(
            epoch,
            command,
            OwnerInputArrivalDisposition.LateCommand);
        var rejected = new AuthorityInputArrivalDecision(
            epoch,
            command,
            OwnerInputArrivalDisposition.RejectedCommand);
        var accepted = new AuthorityInputArrivalDecision(
            epoch,
            command,
            OwnerInputArrivalDisposition.NewCommandAccepted);

        Assert.True(late.IsValid);
        Assert.True(rejected.IsValid);
        Assert.True(accepted.IsValid);
        Assert.True(late.IsTerminalWithoutApplication);
        Assert.True(rejected.IsTerminalWithoutApplication);
        Assert.False(accepted.IsTerminalWithoutApplication);
        Assert.Equal(command, late.Command);
        Assert.False(default(AuthorityInputArrivalDecision).IsValid);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AuthorityInputArrivalDecision(
                epoch,
                command,
                (OwnerInputArrivalDisposition)byte.MaxValue));
    }

    [Fact]
    public void ArrivalClassificationRejectsForeignLifecycleEvidence()
    {
        var epoch = Epoch();
        var command = Command(epoch, frame: 12);

        Assert.Throws<ArgumentException>(() => new AuthorityInputArrivalDecision(
            Epoch(life: 2),
            command,
            OwnerInputArrivalDisposition.LateCommand));
        Assert.Throws<ArgumentException>(() => new AuthorityInputArrivalDecision(
            Epoch(discontinuity: 2),
            command,
            OwnerInputArrivalDisposition.RejectedCommand));
    }

    [Fact]
    public void DecisionFactoriesMakeMutuallyExclusiveRepresentations()
    {
        var epoch = Epoch();
        var received = AuthorityInputFrameDecision.ApplyReceived(
            Frame(epoch, 1),
            Command(epoch, frame: 1));
        var repeated = new AuthorityInputFallbackPolicy(1).ResolveMissingFrame(
            Frame(epoch, 2),
            received,
            AuthorityFallbackInputBasis.From(Input()));
        var neutral = new AuthorityInputFallbackPolicy(0).ResolveMissingFrame(
            Frame(epoch, 2),
            received,
            AuthorityFallbackInputBasis.From(Input()));
        var overridden = AuthorityInputFrameDecision.ApplyAuthorityOverride(
            Frame(epoch, 2),
            Input(transitions: default, actions: default),
            AuthorityInputOverrideReason.Eliminated);

        Assert.All(
            new[] { received, repeated, neutral, overridden },
            decision => Assert.True(decision.IsValid));
        Assert.Equal(4, new[] { received, repeated, neutral, overridden }
            .Select(decision => decision.ApplicationKind)
            .Distinct()
            .Count());
        Assert.False(default(AuthorityInputFrameDecision).IsValid);
    }

    [Fact]
    public void PolicyBoundsAndDefaultValuesFailClosed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AuthorityInputFallbackPolicy(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AuthorityInputFallbackPolicy(
                AuthorityInputFallbackPolicy.MaximumSupportedRepeatedContinuousFrames + 1));
        Assert.False(default(AuthorityFallbackInputBasis).IsValid);
        Assert.False(default(AuthorityInputFrameIdentity).IsValid);
    }

    [Fact]
    public void FrameSlotCommitsExactlyOneApplicationAndCannotBeRewritten()
    {
        var epoch = Epoch();
        var identity = Frame(epoch, 8);
        var received = AuthorityInputFrameDecision.ApplyReceived(
            identity,
            Command(epoch, frame: 8));
        var conflicting = AuthorityInputFrameDecision.ApplyAuthorityOverride(
            identity,
            Input(transitions: default, actions: default),
            AuthorityInputOverrideReason.Stunned);
        var differentFrame = AuthorityInputFrameDecision.ApplyReceived(
            Frame(epoch, 9),
            Command(epoch, frame: 9, sequence: 2));
        var slot = new AuthorityInputFrameDecisionSlot(identity);

        Assert.False(slot.HasDecision);
        Assert.Throws<InvalidOperationException>(() => slot.Decision);
        Assert.Equal(
            AuthorityInputFrameCommitResult.Committed,
            AuthorityInputFrameDecisionSlot.TryCommit(ref slot, received));
        Assert.Equal(
            AuthorityInputFrameCommitResult.ExactDuplicate,
            AuthorityInputFrameDecisionSlot.TryCommit(ref slot, received));
        Assert.Equal(
            AuthorityInputFrameCommitResult.RejectedConflictingDecision,
            AuthorityInputFrameDecisionSlot.TryCommit(ref slot, conflicting));
        Assert.Equal(
            AuthorityInputFrameCommitResult.RejectedDifferentFrame,
            AuthorityInputFrameDecisionSlot.TryCommit(ref slot, differentFrame));
        Assert.True(slot.HasDecision);
        Assert.Equal(received, slot.Decision);

        var uninitialized = default(AuthorityInputFrameDecisionSlot);
        Assert.Throws<InvalidOperationException>(() =>
            AuthorityInputFrameDecisionSlot.TryCommit(ref uninitialized, received));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AuthorityInputFrameDecisionSlot.TryCommit(ref slot, default));
    }

    [Fact]
    public void SlotCommitSurvivesArrayAndSpanStorageUsedByABoundedScheduler()
    {
        // The scheduler keeps slots inline in a bounded ring. Committing through
        // a by-value copy would report success and lose the decision, so commit
        // is only reachable by reference. Array elements and CollectionsMarshal
        // spans are real references; a List<T> indexer is not, and passing one
        // by ref does not compile.
        var epoch = Epoch();
        var identity = Frame(epoch, 30);
        var decision = AuthorityInputFrameDecision.ApplyReceived(
            identity,
            Command(epoch, frame: 30));

        var ring = new AuthorityInputFrameDecisionSlot[4];
        ring[1] = new AuthorityInputFrameDecisionSlot(identity);
        Assert.Equal(
            AuthorityInputFrameCommitResult.Committed,
            AuthorityInputFrameDecisionSlot.TryCommit(ref ring[1], decision));
        Assert.True(ring[1].HasDecision);
        Assert.Equal(decision, ring[1].Decision);

        var list = new List<AuthorityInputFrameDecisionSlot>
        {
            new AuthorityInputFrameDecisionSlot(identity),
        };
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
        Assert.Equal(
            AuthorityInputFrameCommitResult.Committed,
            AuthorityInputFrameDecisionSlot.TryCommit(ref span[0], decision));
        Assert.True(list[0].HasDecision);
    }

    [Fact]
    public void MatchFrameEpochDisambiguatesAReusedTickAfterATimelineReset()
    {
        // A timeline reset restarts frame numbering. Session, combatant, life,
        // control, and authority discontinuity can all legitimately be unchanged
        // across that reset, so only the match-frame epoch separates tick 40 of
        // the old timeline from tick 40 of the new one.
        var priorEpoch = Epoch(matchFrameEpoch: 1);
        var currentEpoch = Epoch(matchFrameEpoch: 2);
        Assert.Equal(
            OwnerIntentScope.From(priorEpoch),
            OwnerIntentScope.From(currentEpoch));
        Assert.Equal(
            priorEpoch.AuthorityDiscontinuity,
            currentEpoch.AuthorityDiscontinuity);

        var staleCommand = Command(priorEpoch, frame: 40);

        Assert.Throws<ArgumentException>(() =>
            AuthorityInputFrameDecision.ApplyReceived(
                Frame(currentEpoch, 40),
                staleCommand));
        Assert.Throws<ArgumentException>(() =>
            new AuthorityInputArrivalDecision(
                currentEpoch,
                staleCommand,
                OwnerInputArrivalDisposition.LateCommand));

        var currentCommand = Command(currentEpoch, frame: 40);
        var applied = AuthorityInputFrameDecision.ApplyReceived(
            Frame(currentEpoch, 40),
            currentCommand);
        Assert.True(applied.IsValid);
        Assert.Equal(
            new MatchFrameEpochId(2),
            applied.ReceivedCommand!.Value.MatchFrameEpoch);
    }

    [Fact]
    public void CommandRequiresAValidMatchFrameEpoch()
    {
        var epoch = Epoch();
        Assert.Throws<ArgumentOutOfRangeException>(() => Command(
            epoch,
            frame: 12,
            matchFrameEpoch: default(MatchFrameEpochId)));
    }

    private static AuthorityInputFrameIdentity Frame(
        CombatantAuthorityPredictionEpoch epoch,
        long tick) => new(epoch, new SimulationInstant(tick));

    private static CombatantAuthorityPredictionEpoch Epoch(
        ulong session = 10,
        ulong matchFrameEpoch = 1,
        long combatant = 4,
        long life = 1,
        ulong discontinuity = 1,
        ulong control = 3) => new(
            session,
            new MatchFrameEpochId(matchFrameEpoch),
            new CombatantId(combatant),
            new LifeGenerationId(life),
            new AuthorityDiscontinuityId(discontinuity),
            new OwnerControlEpoch(control));

    private static OwnerSimulationCommand Command(
        CombatantAuthorityPredictionEpoch epoch,
        long frame,
        ulong sequence = 1,
        CharacterSimulationInput? input = null,
        MatchFrameEpochId? matchFrameEpoch = null) => new(
            new OwnerInputIdentity(
                OwnerIntentScope.From(epoch),
                new InputSequence(sequence)),
            epoch.AuthorityDiscontinuity,
            matchFrameEpoch ?? epoch.MatchFrameEpoch,
            new SimulationInstant(frame),
            input ?? Input());

    private static CharacterSimulationInput Input(
        TransitionReferenceBuffer? transitions = null,
        ActionReferenceBuffer? actions = null) => new(
            MovementAxes.FromUnitVector(new HorizontalVector(0.6d, -0.4d)),
            ViewOrientation.FromRadians(1.25d, -0.15d),
            new MovementHeldState(
                MovementHeldButtons.Jump | MovementHeldButtons.Sprint),
            transitions ?? default,
            new CombatInputState(
                CombatHeldButtons.Attack | CombatHeldButtons.ActivateWeapon),
            actions ?? default,
            new MovementConfigurationRevision(7),
            new MovementCapabilityRevision(8));
}
