using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Core.Movement.Simulation;
using BattleArena.Multiplayer.AuthoritySimulation;
using BattleArena.Multiplayer.OwnerPrediction;

namespace BattleArena.Multiplayer.Tests.AuthoritySimulation;

public sealed class AuthorityInputFallbackTests
{
    [Fact]
    public void EveryActiveFrameCommitsExactlyOneDecisionInAscendingOrder()
    {
        var epoch = Epoch();
        var scheduler = Scheduler(epoch, firstFrame: 0, capacity: 16);

        Assert.Null(scheduler.ConsumedThroughFrame);

        Assert.True(scheduler.TryAdmit(Command(epoch, 0)).WasStored);
        Assert.True(scheduler.TryAdmit(Command(epoch, 2)).WasStored);

        var kinds = new List<AuthorityInputApplicationKind>();
        for (var frame = 0; frame < 4; frame++)
        {
            var decision = scheduler.ResolveNextFrame(Basis());
            Assert.Equal(new SimulationInstant(frame), decision.Identity.Frame);
            Assert.True(decision.IsValid);
            kinds.Add(decision.ApplicationKind);
            Assert.Equal(new SimulationInstant(frame), scheduler.ConsumedThroughFrame);
        }

        Assert.Equal(
            new[]
            {
                AuthorityInputApplicationKind.ReceivedCommand,
                AuthorityInputApplicationKind.RepeatedContinuous,
                AuthorityInputApplicationKind.ReceivedCommand,
                AuthorityInputApplicationKind.RepeatedContinuous,
            },
            kinds);
    }

    [Fact]
    public void RepeatedContinuousCarriesHeldStateButNeverAnEdge()
    {
        var epoch = Epoch();
        var scheduler = Scheduler(epoch, firstFrame: 0, capacity: 16);

        var withEdges = Input(
            transitions: new TransitionReferenceBuffer([new MovementTransitionId(11)]),
            actions: new ActionReferenceBuffer([new PredictedActionId(12)]));
        Assert.True(scheduler.TryAdmit(Command(epoch, 0, input: withEdges)).WasStored);

        var received = scheduler.ResolveNextFrame(Basis());
        Assert.Equal(1, received.AppliedInput.TransitionReferences.Count);
        Assert.Equal(1, received.AppliedInput.ActionReferences.Count);

        var repeated = scheduler.ResolveNextFrame(Basis());
        Assert.Equal(
            AuthorityInputApplicationKind.RepeatedContinuous,
            repeated.ApplicationKind);

        // The press survives nothing: continuous state is reused, the edge is not.
        Assert.Equal(withEdges.Movement, repeated.AppliedInput.Movement);
        Assert.Equal(withEdges.MovementHeld, repeated.AppliedInput.MovementHeld);
        Assert.Equal(withEdges.CombatInput, repeated.AppliedInput.CombatInput);
        Assert.Equal(0, repeated.AppliedInput.TransitionReferences.Count);
        Assert.Equal(0, repeated.AppliedInput.ActionReferences.Count);
        Assert.Null(repeated.AppliedInputSequence);
    }

    [Fact]
    public void HeldInputDecaysToNeutralOnceTheRepeatBoundIsExceeded()
    {
        var epoch = Epoch();
        var scheduler = Scheduler(
            epoch,
            firstFrame: 0,
            capacity: 16,
            maximumRepeatedFrames: 2);

        Assert.True(scheduler.TryAdmit(Command(epoch, 0)).WasStored);
        Assert.Equal(
            AuthorityInputApplicationKind.ReceivedCommand,
            scheduler.ResolveNextFrame(Basis()).ApplicationKind);

        var first = scheduler.ResolveNextFrame(Basis());
        Assert.Equal(AuthorityInputApplicationKind.RepeatedContinuous, first.ApplicationKind);
        Assert.Equal(1u, first.ConsecutiveMissingFrames);

        var second = scheduler.ResolveNextFrame(Basis());
        Assert.Equal(AuthorityInputApplicationKind.RepeatedContinuous, second.ApplicationKind);
        Assert.Equal(2u, second.ConsecutiveMissingFrames);

        var third = scheduler.ResolveNextFrame(Basis());
        Assert.Equal(AuthorityInputApplicationKind.NeutralFallback, third.ApplicationKind);
        Assert.Equal(3u, third.ConsecutiveMissingFrames);
        Assert.Equal(default, third.AppliedInput.Movement);
        Assert.Equal(MovementHeldButtons.None, third.AppliedInput.MovementHeld.Buttons);
        Assert.Equal(CombatHeldButtons.None, third.AppliedInput.CombatInput.HeldButtons);

        // Once neutral, it stays neutral until a real command arrives again.
        var fourth = scheduler.ResolveNextFrame(Basis());
        Assert.Equal(AuthorityInputApplicationKind.NeutralFallback, fourth.ApplicationKind);
        Assert.Equal(4u, fourth.ConsecutiveMissingFrames);
    }

    [Fact]
    public void ARealCommandResetsTheMissingRunToZero()
    {
        var epoch = Epoch();
        var scheduler = Scheduler(epoch, firstFrame: 0, capacity: 16, maximumRepeatedFrames: 2);

        Assert.True(scheduler.TryAdmit(Command(epoch, 0)).WasStored);
        Assert.True(scheduler.TryAdmit(Command(epoch, 4)).WasStored);

        scheduler.ResolveNextFrame(Basis());
        for (var i = 0; i < 3; i++)
        {
            scheduler.ResolveNextFrame(Basis());
        }

        var recovered = scheduler.ResolveNextFrame(Basis());
        Assert.Equal(AuthorityInputApplicationKind.ReceivedCommand, recovered.ApplicationKind);
        Assert.Equal(0u, recovered.ConsecutiveMissingFrames);

        var afterRecovery = scheduler.ResolveNextFrame(Basis());
        Assert.Equal(
            AuthorityInputApplicationKind.RepeatedContinuous,
            afterRecovery.ApplicationKind);
        Assert.Equal(1u, afterRecovery.ConsecutiveMissingFrames);
    }

    [Fact]
    public void FallbackUsesCurrentAuthorityRevisionsNotTheStaleCommandRevisions()
    {
        // A missed frame must not resurrect an old movement revision, or a
        // correction would replay against configuration the authority has
        // already superseded.
        var epoch = Epoch();
        var scheduler = Scheduler(epoch, firstFrame: 0, capacity: 16);

        Assert.True(scheduler.TryAdmit(
            Command(epoch, 0, input: Input(movementRevision: 7, capabilityRevision: 8)))
            .WasStored);
        scheduler.ResolveNextFrame(Basis(movementRevision: 7, capabilityRevision: 8));

        var repeated = scheduler.ResolveNextFrame(
            Basis(movementRevision: 9, capabilityRevision: 10));
        Assert.Equal(
            new MovementConfigurationRevision(9),
            repeated.AppliedInput.MovementRevision);
        Assert.Equal(
            new MovementCapabilityRevision(10),
            repeated.AppliedInput.CapabilityRevision);

        var neutral = scheduler.ResolveNextFrame(
            Basis(movementRevision: 11, capabilityRevision: 12));
        neutral = scheduler.ResolveNextFrame(
            Basis(movementRevision: 11, capabilityRevision: 12));
        Assert.Equal(AuthorityInputApplicationKind.NeutralFallback, neutral.ApplicationKind);
        Assert.Equal(
            new MovementConfigurationRevision(11),
            neutral.AppliedInput.MovementRevision);
    }

    [Fact]
    public void NeutralFallbackPreservesSafeViewSoFacingDoesNotSnap()
    {
        var epoch = Epoch();
        var scheduler = Scheduler(epoch, firstFrame: 0, capacity: 16, maximumRepeatedFrames: 0);

        var safeView = ViewOrientation.FromRadians(2.0d, 0.3d);
        var decision = scheduler.ResolveNextFrame(Basis(view: safeView));

        Assert.Equal(AuthorityInputApplicationKind.NeutralFallback, decision.ApplicationKind);
        Assert.Equal(safeView, decision.AppliedInput.View);
    }

    [Fact]
    public void ALateCommandNeverRunsAfterItsFrameWasResolvedByFallback()
    {
        var epoch = Epoch();
        var scheduler = Scheduler(epoch, firstFrame: 30, capacity: 16);

        var fallback = scheduler.ResolveNextFrame(Basis());
        Assert.Equal(AuthorityInputApplicationKind.NeutralFallback, fallback.ApplicationKind);

        var late = scheduler.TryAdmit(Command(epoch, 30));
        Assert.Equal(OwnerInputArrivalDisposition.LateCommand, late.Disposition);
        Assert.Equal(1, scheduler.LateCommandCount);

        // The frame's record is unchanged, and the store never held the command.
        Assert.True(scheduler.TryGetDisposition(new SimulationInstant(30), out var record));
        Assert.Equal(AuthorityInputApplicationKind.NeutralFallback, record.ApplicationKind);
        Assert.False(record.UsedOwnerCommand);
        Assert.False(scheduler.TryPeek(new SimulationInstant(30), out _));
        Assert.Equal(0, scheduler.StoredCommandCount);
    }

    [Fact]
    public void TerminalDispositionsRecordApplicationKindAndAppliedSequence()
    {
        var epoch = Epoch();
        var scheduler = Scheduler(epoch, firstFrame: 0, capacity: 16, maximumRepeatedFrames: 1);

        Assert.True(scheduler.TryAdmit(Command(epoch, 0, sequence: 41)).WasStored);
        Assert.True(scheduler.TryAdmit(Command(epoch, 3, sequence: 44)).WasStored);

        for (var i = 0; i < 4; i++)
        {
            scheduler.ResolveNextFrame(Basis());
        }

        Assert.True(scheduler.TryGetDisposition(new SimulationInstant(0), out var zero));
        Assert.Equal(AuthorityInputApplicationKind.ReceivedCommand, zero.ApplicationKind);
        Assert.Equal(new InputSequence(41), zero.AppliedInputSequence);
        Assert.True(zero.UsedOwnerCommand);

        Assert.True(scheduler.TryGetDisposition(new SimulationInstant(1), out var one));
        Assert.Equal(AuthorityInputApplicationKind.RepeatedContinuous, one.ApplicationKind);
        Assert.Null(one.AppliedInputSequence);
        Assert.Equal(1u, one.ConsecutiveMissingFrames);

        Assert.True(scheduler.TryGetDisposition(new SimulationInstant(2), out var two));
        Assert.Equal(AuthorityInputApplicationKind.NeutralFallback, two.ApplicationKind);

        Assert.True(scheduler.TryGetDisposition(new SimulationInstant(3), out var three));
        Assert.Equal(new InputSequence(44), three.AppliedInputSequence);

        var buffer = new AuthorityFrameTerminalDisposition[8];
        var copied = scheduler.CopyRetainedDispositions(buffer);
        Assert.Equal(4, copied);
        Assert.Equal(new SimulationInstant(0), buffer[0].Frame);
        Assert.Equal(new SimulationInstant(3), buffer[3].Frame);
    }

    [Fact]
    public void DispositionRetentionIsBoundedAndDropsOldestFirst()
    {
        var epoch = Epoch();
        var scheduler = Scheduler(
            epoch,
            firstFrame: 0,
            capacity: 8,
            maximumRepeatedFrames: 0,
            retainedDispositions: 4);

        for (var i = 0; i < 10; i++)
        {
            scheduler.ResolveNextFrame(Basis());
        }

        Assert.Equal(new SimulationInstant(9), scheduler.ConsumedThroughFrame);
        Assert.False(scheduler.TryGetDisposition(new SimulationInstant(5), out _));
        Assert.True(scheduler.TryGetDisposition(new SimulationInstant(6), out _));
        Assert.True(scheduler.TryGetDisposition(new SimulationInstant(9), out _));

        var buffer = new AuthorityFrameTerminalDisposition[16];
        var copied = scheduler.CopyRetainedDispositions(buffer);
        Assert.Equal(4, copied);
        Assert.Equal(new SimulationInstant(6), buffer[0].Frame);
        Assert.Equal(new SimulationInstant(9), buffer[3].Frame);

        var small = new AuthorityFrameTerminalDisposition[2];
        Assert.Equal(2, scheduler.CopyRetainedDispositions(small));
        Assert.Equal(new SimulationInstant(8), small[0].Frame);
        Assert.Equal(new SimulationInstant(9), small[1].Frame);
    }

    [Fact]
    public void RetentionArithmeticIsCorrectFromANonZeroFirstFrame()
    {
        // A scheduler rarely starts at frame zero: bootstrap picks a future
        // first command frame. The retention window must be relative to that
        // start, not to tick zero.
        var epoch = Epoch();
        var scheduler = Scheduler(
            epoch,
            firstFrame: 1_000_003,
            capacity: 8,
            maximumRepeatedFrames: 0,
            retainedDispositions: 4);

        var buffer = new AuthorityFrameTerminalDisposition[8];
        Assert.Equal(0, scheduler.CopyRetainedDispositions(buffer));

        scheduler.ResolveNextFrame(Basis());
        scheduler.ResolveNextFrame(Basis());

        Assert.Equal(2, scheduler.CopyRetainedDispositions(buffer));
        Assert.Equal(new SimulationInstant(1_000_003), buffer[0].Frame);
        Assert.Equal(new SimulationInstant(1_000_004), buffer[1].Frame);

        for (var i = 0; i < 5; i++)
        {
            scheduler.ResolveNextFrame(Basis());
        }

        Assert.Equal(new SimulationInstant(1_000_009), scheduler.ConsumedThroughFrame);
        Assert.Equal(4, scheduler.CopyRetainedDispositions(buffer));
        Assert.Equal(new SimulationInstant(1_000_006), buffer[0].Frame);
        Assert.Equal(new SimulationInstant(1_000_009), buffer[3].Frame);
        Assert.False(scheduler.TryGetDisposition(new SimulationInstant(1_000_005), out _));
    }

    [Fact]
    public void AuthorityOverrideConsumesTheFrameAndDiscardsOwnerIntent()
    {
        var epoch = Epoch();
        var scheduler = Scheduler(epoch, firstFrame: 0, capacity: 16);

        Assert.True(scheduler.TryAdmit(Command(epoch, 0)).WasStored);

        var decision = scheduler.ResolveNextFrameAsAuthorityOverride(
            Input(movement: default, held: MovementHeldButtons.None),
            AuthorityInputOverrideReason.Eliminated);

        Assert.Equal(AuthorityInputApplicationKind.AuthorityOverride, decision.ApplicationKind);
        Assert.Equal(AuthorityInputOverrideReason.Eliminated, decision.OverrideReason);
        Assert.Null(decision.ReceivedCommand);
        Assert.Equal(new SimulationInstant(0), scheduler.ConsumedThroughFrame);
        Assert.Equal(new SimulationInstant(1), scheduler.NextFrameToConsume);
        Assert.Equal(0, scheduler.StoredCommandCount);
    }

    [Fact]
    public void AnOverrideDoesNotSeedRepeatedHeldInputForTheNextMissingFrame()
    {
        var epoch = Epoch();
        var scheduler = Scheduler(epoch, firstFrame: 0, capacity: 16, maximumRepeatedFrames: 4);

        scheduler.ResolveNextFrameAsAuthorityOverride(
            Input(movement: default, held: MovementHeldButtons.None),
            AuthorityInputOverrideReason.Stunned);

        var next = scheduler.ResolveNextFrame(Basis());
        Assert.Equal(AuthorityInputApplicationKind.NeutralFallback, next.ApplicationKind);
        Assert.Equal(1u, next.ConsecutiveMissingFrames);
    }

    [Fact]
    public void MixingRawConsumptionWithDecisionResolutionFailsClosed()
    {
        var epoch = Epoch();
        var scheduler = Scheduler(epoch, firstFrame: 0, capacity: 16);

        scheduler.ResolveNextFrame(Basis());
        scheduler.ConsumeNextFrame();

        Assert.Throws<InvalidOperationException>(() => scheduler.ResolveNextFrame(Basis()));
        Assert.Throws<InvalidOperationException>(() =>
            scheduler.ResolveNextFrameAsAuthorityOverride(
                Input(),
                AuthorityInputOverrideReason.GameplayPolicy));
    }

    [Fact]
    public void ResolutionFailsClosedOnInvalidBasisAndUndefinedOverride()
    {
        var epoch = Epoch();
        var scheduler = Scheduler(epoch, firstFrame: 0, capacity: 16);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            scheduler.ResolveNextFrame(default));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            scheduler.ResolveNextFrameAsAuthorityOverride(
                Input(),
                (AuthorityInputOverrideReason)200));
        Assert.Throws<ArgumentException>(() =>
            scheduler.ResolveNextFrameAsAuthorityOverride(
                Input(transitions: new TransitionReferenceBuffer(
                    [new MovementTransitionId(3)])),
                AuthorityInputOverrideReason.Teleported));

        // None of the rejected calls advanced the timeline.
        Assert.Equal(new SimulationInstant(0), scheduler.NextFrameToConsume);
        Assert.Null(scheduler.ConsumedThroughFrame);
    }

    [Fact]
    public void EveryFrameIsResolvedExactlyOnceUnderLossyTraffic()
    {
        var epoch = Epoch();
        var scheduler = Scheduler(epoch, firstFrame: 0, capacity: 32, maximumRepeatedFrames: 2);
        var random = new Random(815);

        var resolved = new List<long>();
        var lateAfterResolution = 0;

        for (var step = 0; step < 3000; step++)
        {
            if (random.Next(0, 10) < 7)
            {
                // Jitter around the cursor so some commands genuinely arrive
                // after their frame was already resolved.
                var target = scheduler.NextFrameToConsume.Tick + random.Next(-3, 5);
                if (target >= 0)
                {
                    var admission = scheduler.TryAdmit(
                        Command(epoch, target, sequence: (ulong)target + 1));
                    if (admission.Disposition == OwnerInputArrivalDisposition.LateCommand)
                    {
                        lateAfterResolution++;
                        Assert.NotNull(scheduler.ConsumedThroughFrame);
                        Assert.True(target <= scheduler.ConsumedThroughFrame!.Value.Tick);
                    }
                }
            }

            if (random.Next(0, 10) < 6)
            {
                var decision = scheduler.ResolveNextFrame(Basis());
                resolved.Add(decision.Identity.Frame.Tick);

                // A frame resolved by fallback can never later report an owner sequence.
                Assert.True(scheduler.TryGetDisposition(decision.Identity.Frame, out var record));
                Assert.Equal(decision.ApplicationKind, record.ApplicationKind);
                Assert.Equal(
                    decision.ApplicationKind == AuthorityInputApplicationKind.ReceivedCommand,
                    record.AppliedInputSequence is not null);

                // Repeated continuous never invents an edge, whatever the history.
                if (decision.ApplicationKind != AuthorityInputApplicationKind.ReceivedCommand)
                {
                    Assert.Equal(0, decision.AppliedInput.TransitionReferences.Count);
                    Assert.Equal(0, decision.AppliedInput.ActionReferences.Count);
                }
            }
        }

        Assert.Equal(resolved.Count, resolved.Distinct().Count());
        Assert.Equal(Enumerable.Range(0, resolved.Count).Select(i => (long)i), resolved);
        Assert.Equal(
            new SimulationInstant(resolved.Count - 1),
            scheduler.ConsumedThroughFrame);
        Assert.True(lateAfterResolution > 0);
    }

    private static AuthorityOwnerInputScheduler Scheduler(
        CombatantAuthorityPredictionEpoch epoch,
        long firstFrame,
        int capacity,
        int maximumRepeatedFrames =
            AuthorityInputFallbackPolicy.DefaultMaximumRepeatedContinuousFrames,
        int retainedDispositions =
            AuthorityOwnerInputScheduler.DefaultRetainedDispositionFrames) => new(
            epoch,
            new SimulationInstant(firstFrame),
            capacity,
            new AuthorityInputFallbackPolicy(maximumRepeatedFrames),
            retainedDispositions);

    private static AuthorityFallbackInputBasis Basis(
        ViewOrientation? view = null,
        ulong movementRevision = 7,
        ulong capabilityRevision = 8) => new(
            view ?? ViewOrientation.FromRadians(1.25d, -0.15d),
            new MovementConfigurationRevision(movementRevision),
            new MovementCapabilityRevision(capabilityRevision));

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
        ulong? sequence = null,
        CharacterSimulationInput? input = null) => new(
            new OwnerInputIdentity(
                OwnerIntentScope.From(epoch),
                new InputSequence(sequence ?? (ulong)frame + 1)),
            epoch.AuthorityDiscontinuity,
            epoch.MatchFrameEpoch,
            new SimulationInstant(frame),
            input ?? Input());

    private static CharacterSimulationInput Input(
        TransitionReferenceBuffer? transitions = null,
        ActionReferenceBuffer? actions = null,
        MovementAxes? movement = null,
        MovementHeldButtons held = MovementHeldButtons.Sprint,
        ulong movementRevision = 7,
        ulong capabilityRevision = 8) => new(
            movement ?? MovementAxes.FromUnitVector(new HorizontalVector(0.6d, -0.4d)),
            ViewOrientation.FromRadians(1.25d, -0.15d),
            new MovementHeldState(held),
            transitions ?? default,
            new CombatInputState(CombatHeldButtons.Attack),
            actions ?? default,
            new MovementConfigurationRevision(movementRevision),
            new MovementCapabilityRevision(capabilityRevision));
}
