using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Core.Movement.Simulation;
using BattleArena.Multiplayer.OwnerPrediction;

namespace BattleArena.Multiplayer.Tests.OwnerPrediction;

public sealed class OwnerCommandStepBuilderTests
{
    [Fact]
    public void ZeroStepQuickTapSurvivesWithNewestContinuousAndCameraSample()
    {
        var builder = Builder(out var transitions, out _);
        var firstSample = Sample(yaw: 0.25, movementX: 0.2);
        Assert.Equal(
            new OwnerCommandBuildResult(
                OwnerCommandBuildDecision.CapturedWithoutSimulation, 0),
            builder.Build(firstSample, OwnerCommandStepCount.Zero, []));
        Assert.Equal(OwnerIntentQueueDecision.Added,
            builder.QueueTransition(
                MovementTransitionKind.JumpPressed,
                new SimulationDuration(10)));
        Assert.Equal(OwnerIntentQueueDecision.Added,
            builder.QueueTransition(
                MovementTransitionKind.JumpReleased,
                new SimulationDuration(10)));

        var newestSample = Sample(yaw: 1.5, movementX: -0.75);
        builder.Build(newestSample, OwnerCommandStepCount.Zero, []);
        var commands = new OwnerSimulationCommand[1];
        Assert.Equal(
            new OwnerCommandBuildResult(OwnerCommandBuildDecision.Built, 1),
            builder.Build(newestSample, OwnerCommandStepCount.One, commands));

        var command = commands[0];
        Assert.Equal(new InputSequence(1), command.Sequence);
        Assert.Equal(new SimulationInstant(100), command.TargetFrame);
        Assert.Equal(newestSample.View, command.Input.View);
        Assert.Equal(newestSample.Movement, command.Input.Movement);
        Assert.Equal(2, command.Input.TransitionReferences.Count);
        Assert.Equal(new MovementTransitionId(1),
            command.Input.TransitionReferences[0]);
        Assert.Equal(new MovementTransitionId(2),
            command.Input.TransitionReferences[1]);

        var intents = new MovementTransitionIntent[2];
        Assert.Equal(2, transitions.CopyOutstanding(intents));
        Assert.All(intents, intent =>
        {
            Assert.Equal(new InputSequence(1),
                intent.OriginatingInput.Sequence);
            Assert.Equal(new SimulationInstant(100), intent.FirstPredictedFrame);
        });
        Assert.Equal(MovementTransitionKind.JumpPressed, intents[0].Kind);
        Assert.Equal(MovementTransitionKind.JumpReleased, intents[1].Kind);
    }

    [Fact]
    public void TwoStepCallbackSamplesOnceAndNeverInventsSecondStepEdges()
    {
        var builder = Builder(out var transitions, out var actions);
        Assert.Equal(OwnerIntentQueueDecision.Added,
            builder.QueueTransition(
                MovementTransitionKind.CrouchOrRollPressed,
                new SimulationDuration(6)));
        Assert.Equal(OwnerIntentQueueDecision.Added,
            builder.QueueAction(
                OwnerActionTrigger.Attack,
                new SimulationDuration(8),
                new SimulationInstant(95)));
        var sample = Sample(
            yaw: 2.25,
            movementX: 0.6,
            movementHeld: MovementHeldButtons.Sprint,
            combatHeld: CombatHeldButtons.Attack);
        var commands = new OwnerSimulationCommand[2];

        Assert.Equal(
            new OwnerCommandBuildResult(OwnerCommandBuildDecision.Built, 2),
            builder.Build(sample, OwnerCommandStepCount.Two, commands));

        Assert.Equal(new InputSequence(1), commands[0].Sequence);
        Assert.Equal(new InputSequence(2), commands[1].Sequence);
        Assert.Equal(new SimulationInstant(100), commands[0].TargetFrame);
        Assert.Equal(new SimulationInstant(101), commands[1].TargetFrame);
        Assert.Equal(sample.Movement, commands[0].Input.Movement);
        Assert.Equal(sample.Movement, commands[1].Input.Movement);
        Assert.Equal(sample.View, commands[0].Input.View);
        Assert.Equal(sample.View, commands[1].Input.View);
        Assert.Equal(sample.MovementHeld, commands[1].Input.MovementHeld);
        Assert.Equal(sample.CombatInput, commands[1].Input.CombatInput);
        Assert.Equal(1, commands[0].Input.TransitionReferences.Count);
        Assert.Equal(1, commands[0].Input.ActionReferences.Count);
        Assert.Equal(0, commands[1].Input.TransitionReferences.Count);
        Assert.Equal(0, commands[1].Input.ActionReferences.Count);
        Assert.Equal(new OwnerInputIdentity(Scope(), new InputSequence(3)),
            builder.NextInputIdentity);
        Assert.Equal(new SimulationInstant(102), builder.NextTargetFrame);

        var transitionIntents = new MovementTransitionIntent[1];
        Assert.Equal(1, transitions.CopyOutstanding(transitionIntents));
        Assert.Equal(Scope(), transitionIntents[0].Identity.Scope);
        Assert.Equal(commands[0].Identity,
            transitionIntents[0].OriginatingInput);
        Assert.Equal(MovementTransitionKind.CrouchOrRollPressed,
            transitionIntents[0].Kind);
        Assert.Equal(new SimulationInstant(100),
            transitionIntents[0].FirstPredictedFrame);
        Assert.Equal(new SimulationInstant(106),
            transitionIntents[0].LastValidFrame);

        var actionIntents = new PredictedActionIntent[1];
        Assert.Equal(1, actions.CopyOutstanding(actionIntents));
        Assert.Equal(Scope(), actionIntents[0].Identity.Scope);
        Assert.Equal(commands[0].Identity,
            actionIntents[0].OriginatingInput);
        Assert.Equal(OwnerActionTrigger.Attack, actionIntents[0].Trigger);
        Assert.Equal(new SimulationInstant(100),
            actionIntents[0].PredictedStartFrame);
        Assert.Equal(new SimulationInstant(108),
            actionIntents[0].LastValidStartFrame);
        Assert.Equal(new SimulationInstant(95),
            actionIntents[0].RenderedAuthorityFrame);
        Assert.Equal(new AuthorityDiscontinuityId(5),
            commands[0].AuthorityDiscontinuity);
        Assert.Equal(commands[0].AuthorityDiscontinuity,
            commands[1].AuthorityDiscontinuity);
        Assert.Equal(new MovementConfigurationRevision(3),
            commands[0].Input.MovementRevision);
        Assert.Equal(commands[0].Input.MovementRevision,
            commands[1].Input.MovementRevision);
        Assert.Equal(new MovementCapabilityRevision(4),
            commands[0].Input.CapabilityRevision);
        Assert.Equal(commands[0].Input.CapabilityRevision,
            commands[1].Input.CapabilityRevision);
    }

    [Fact]
    public void MixedPacingTraceKeepsIdentityFrameAndReferenceContinuity()
    {
        var builder = Builder(out _, out _);
        var first = Sample(yaw: 0.2, movementX: 0.1, movementRevision: 3);
        builder.Build(first, OwnerCommandStepCount.Zero, []);
        builder.QueueTransition(
            MovementTransitionKind.JumpPressed,
            new SimulationDuration(4));
        builder.QueueTransition(
            MovementTransitionKind.JumpReleased,
            new SimulationDuration(4));
        builder.QueueAction(
            OwnerActionTrigger.Attack,
            new SimulationDuration(5),
            new SimulationInstant(90));
        var newest = Sample(
            yaw: 1.7,
            movementX: -0.4,
            movementHeld: MovementHeldButtons.Sprint,
            movementRevision: 8,
            capabilityRevision: 9);
        builder.Build(newest, OwnerCommandStepCount.Zero, []);

        var two = new OwnerSimulationCommand[2];
        builder.Build(newest, OwnerCommandStepCount.Two, two);
        var finalSample = Sample(
            yaw: 2.4,
            movementX: 0.9,
            movementRevision: 10,
            capabilityRevision: 11);
        var one = new OwnerSimulationCommand[1];
        builder.Build(finalSample, OwnerCommandStepCount.One, one);

        Assert.Equal(new ulong[] { 1, 2, 3 },
            new[]
            {
                two[0].Sequence.Value,
                two[1].Sequence.Value,
                one[0].Sequence.Value,
            });
        Assert.Equal(new long[] { 100, 101, 102 },
            new[]
            {
                two[0].TargetFrame.Tick,
                two[1].TargetFrame.Tick,
                one[0].TargetFrame.Tick,
            });
        Assert.Equal(2, two[0].Input.TransitionReferences.Count);
        Assert.Equal(1, two[0].Input.ActionReferences.Count);
        Assert.Equal(0, two[1].Input.TransitionReferences.Count);
        Assert.Equal(0, two[1].Input.ActionReferences.Count);
        Assert.Equal(0, one[0].Input.TransitionReferences.Count);
        Assert.Equal(0, one[0].Input.ActionReferences.Count);
        Assert.Equal(new MovementConfigurationRevision(8),
            two[0].Input.MovementRevision);
        Assert.Equal(two[0].Input.Movement, two[1].Input.Movement);
        Assert.Equal(two[0].Input.View, two[1].Input.View);
        Assert.Equal(two[0].Input.MovementHeld, two[1].Input.MovementHeld);
        Assert.Equal(two[0].Input.CombatInput, two[1].Input.CombatInput);
        Assert.Equal(two[0].Input.MovementRevision,
            two[1].Input.MovementRevision);
        Assert.Equal(two[0].Input.CapabilityRevision,
            two[1].Input.CapabilityRevision);
        Assert.Equal(new MovementConfigurationRevision(10),
            one[0].Input.MovementRevision);
        Assert.Equal(finalSample.View, one[0].Input.View);
    }

    [Fact]
    public void HeldInputRepeatsButNewEdgesMoveToNextActualCommand()
    {
        var builder = Builder(out _, out _);
        var held = Sample(
            movementHeld: MovementHeldButtons.Jump | MovementHeldButtons.Sprint,
            combatHeld: CombatHeldButtons.Attack |
                CombatHeldButtons.ActivateSelectedFlexibleItem);
        var first = new OwnerSimulationCommand[1];
        builder.Build(held, OwnerCommandStepCount.One, first);
        Assert.Equal(0, first[0].Input.TransitionReferences.Count);

        builder.QueueTransition(
            MovementTransitionKind.JumpReleased,
            new SimulationDuration(3));
        var second = new OwnerSimulationCommand[1];
        builder.Build(held, OwnerCommandStepCount.One, second);
        Assert.Equal(new InputSequence(2), second[0].Sequence);
        Assert.Equal(new SimulationInstant(101), second[0].TargetFrame);
        Assert.Equal(held.MovementHeld, second[0].Input.MovementHeld);
        Assert.Equal(held.CombatInput, second[0].Input.CombatInput);
        Assert.Equal(1, second[0].Input.TransitionReferences.Count);
    }

    [Fact]
    public void PendingReferenceLimitsRejectBeforeJournalMutation()
    {
        var builder = Builder(out var transitions, out var actions);
        for (var index = 0;
             index < OwnerSimulationLimits.MaximumTransitionReferences;
             index++)
        {
            Assert.Equal(OwnerIntentQueueDecision.Added,
                builder.QueueTransition(
                    MovementTransitionKind.JumpPressed,
                    SimulationDuration.Zero));
        }
        Assert.Equal(
            OwnerIntentQueueDecision.PendingReferenceCapacityExceeded,
            builder.QueueTransition(
                MovementTransitionKind.JumpReleased,
                SimulationDuration.Zero));
        Assert.Equal(OwnerSimulationLimits.MaximumTransitionReferences,
            transitions.OutstandingCount);

        for (var index = 0;
             index < OwnerSimulationLimits.MaximumActionReferences;
             index++)
        {
            Assert.Equal(OwnerIntentQueueDecision.Added,
                builder.QueueAction(
                    OwnerActionTrigger.Attack,
                    SimulationDuration.Zero,
                    new SimulationInstant(90)));
        }
        Assert.Equal(
            OwnerIntentQueueDecision.PendingReferenceCapacityExceeded,
            builder.QueueAction(
                OwnerActionTrigger.Block,
                SimulationDuration.Zero,
                new SimulationInstant(90)));
        Assert.Equal(OwnerSimulationLimits.MaximumActionReferences,
            actions.OutstandingCount);
    }

    [Fact]
    public void JournalBackpressureIsReportedWithoutAddingCommandReference()
    {
        var scope = Scope();
        var transitions = new MovementTransitionJournal(scope, capacity: 1);
        var actions = new OwnerActionCommandJournal(scope, capacity: 1);
        var builder = new OwnerCommandStepBuilder(
            Seed(),
            new OwnerCommandIntentJournalAdapter(transitions, actions));
        builder.QueueTransition(
            MovementTransitionKind.JumpPressed,
            SimulationDuration.Zero);
        var command = new OwnerSimulationCommand[1];
        builder.Build(Sample(), OwnerCommandStepCount.One, command);

        Assert.Equal(
            OwnerIntentQueueDecision.JournalCapacityExceeded,
            builder.QueueTransition(
                MovementTransitionKind.JumpReleased,
                SimulationDuration.Zero));
        var next = new OwnerSimulationCommand[1];
        builder.Build(Sample(), OwnerCommandStepCount.One, next);
        Assert.Equal(0, next[0].Input.TransitionReferences.Count);
    }

    [Fact]
    public void TwoStepExhaustionIsAtomicAndOneFinalStepCanStillCommit()
    {
        var builder = Builder(
            out _,
            out _,
            firstSequence: new InputSequence(ulong.MaxValue));
        builder.QueueTransition(
            MovementTransitionKind.JumpPressed,
            SimulationDuration.Zero);
        var commands = new OwnerSimulationCommand[2];

        Assert.Equal(
            new OwnerCommandBuildResult(
                OwnerCommandBuildDecision.TimelineExhausted, 0),
            builder.Build(Sample(), OwnerCommandStepCount.Two, commands));
        Assert.Equal(1, builder.PendingTransitionCount);
        Assert.Equal(default, commands[0]);
        Assert.Equal(
            new OwnerCommandBuildResult(
                OwnerCommandBuildDecision.BuiltTimelineExhausted, 1),
            builder.Build(Sample(), OwnerCommandStepCount.One, commands));
        Assert.Equal(new InputSequence(ulong.MaxValue), commands[0].Sequence);
        Assert.Equal(1, commands[0].Input.TransitionReferences.Count);
        Assert.True(builder.RequiresTimelineRebase);
        Assert.Null(builder.NextInputIdentity);
        Assert.Null(builder.NextTargetFrame);
        Assert.Equal(
            new OwnerCommandBuildResult(
                OwnerCommandBuildDecision.CapturedWithoutSimulation, 0),
            builder.Build(Sample(yaw: 2), OwnerCommandStepCount.Zero, []));
    }

    [Fact]
    public void TargetFrameExhaustionMatchesIdentityExhaustion()
    {
        var builder = Builder(
            out _,
            out _,
            firstTarget: new SimulationInstant(long.MaxValue));
        var command = new OwnerSimulationCommand[1];
        Assert.Equal(
            OwnerCommandBuildDecision.BuiltTimelineExhausted,
            builder.Build(Sample(), OwnerCommandStepCount.One, command).Decision);
        Assert.Equal(new SimulationInstant(long.MaxValue), command[0].TargetFrame);
        Assert.True(builder.RequiresTimelineRebase);
    }

    [Fact]
    public void BuildValidationNeverConsumesCursorOrPendingEdges()
    {
        var builder = Builder(out _, out _);
        builder.QueueTransition(
            MovementTransitionKind.JumpPressed,
            SimulationDuration.Zero);
        var identity = builder.NextInputIdentity;
        var frame = builder.NextTargetFrame;

        Assert.Throws<ArgumentException>(() => builder.Build(
            Sample(), OwnerCommandStepCount.Two, new OwnerSimulationCommand[1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Build(
            default, OwnerCommandStepCount.One, new OwnerSimulationCommand[1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Build(
            Sample(), (OwnerCommandStepCount)99, new OwnerSimulationCommand[2]));
        Assert.Equal(identity, builder.NextInputIdentity);
        Assert.Equal(frame, builder.NextTargetFrame);
        Assert.Equal(1, builder.PendingTransitionCount);
    }

    [Fact]
    public void AdapterAndBuilderRejectMismatchedOwnership()
    {
        var transitions = new MovementTransitionJournal(Scope());
        var otherActions = new OwnerActionCommandJournal(Scope(control: 2));
        Assert.Throws<ArgumentException>(() =>
            new OwnerCommandIntentJournalAdapter(transitions, otherActions));

        var origin = new OwnerCommandIntentJournalAdapter(
            transitions,
            new OwnerActionCommandJournal(Scope()));
        Assert.Throws<ArgumentException>(() => new OwnerCommandStepBuilder(
            Seed(),
            new OwnerCommandIntentJournalAdapter(
                new MovementTransitionJournal(Scope(control: 2)),
                new OwnerActionCommandJournal(Scope(control: 2)))));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OwnerCommandStepBuilder(
                default,
                origin));
    }

    [Fact]
    public void ActiveBootstrapDerivesEveryCommandTimelineIdentityComponent()
    {
        var seed = Seed(new InputSequence(9), new SimulationInstant(100));
        Assert.Equal(100UL, seed.AuthorityEpoch.SessionId);
        Assert.Equal(new MatchFrameEpochId(1),
            seed.AuthorityEpoch.MatchFrameEpoch);
        Assert.Equal(new CombatantId(7), seed.AuthorityEpoch.CombatantId);
        Assert.Equal(new LifeGenerationId(2), seed.AuthorityEpoch.Life);
        Assert.Equal(new AuthorityDiscontinuityId(5),
            seed.AuthorityEpoch.AuthorityDiscontinuity);
        Assert.Equal(new OwnerControlEpoch(1),
            seed.AuthorityEpoch.OwnerControl);
        Assert.Equal(new InputSequence(9), seed.FirstSequence);
        Assert.Equal(new SimulationInstant(100), seed.FirstTargetFrame);

        var transitions = new MovementTransitionJournal(seed.Scope);
        var actions = new OwnerActionCommandJournal(seed.Scope);
        var builder = new OwnerCommandStepBuilder(
            seed,
            new OwnerCommandIntentJournalAdapter(transitions, actions));
        var command = new OwnerSimulationCommand[1];
        builder.Build(Sample(), OwnerCommandStepCount.One, command);
        Assert.Equal(seed.Scope, command[0].Identity.Scope);
        Assert.Equal(seed.AuthorityEpoch.AuthorityDiscontinuity,
            command[0].AuthorityDiscontinuity);
        Assert.Equal(seed.FirstSequence, command[0].Sequence);
        Assert.Equal(seed.FirstTargetFrame, command[0].TargetFrame);

        var inactive = new OwnerPredictionBootstrapCoordinator(
            new ClientPredictionLifecycleRouter(100, _ => { }),
            100,
            new CombatantId(7));
        Assert.Throws<InvalidOperationException>(() =>
            inactive.CreateCommandTimelineSeed(InputSequence.Initial));
    }

    [Theory]
    [InlineData(IntentFault.ReturnDefault)]
    [InlineData(IntentFault.ForeignScope)]
    [InlineData(IntentFault.WrongOriginatingInput)]
    [InlineData(IntentFault.WrongKindOrTrigger)]
    [InlineData(IntentFault.WrongFirstFrame)]
    [InlineData(IntentFault.WrongDeadline)]
    [InlineData(IntentFault.ThrowAfterMutation)]
    [InlineData(IntentFault.UnknownDecision)]
    [InlineData(IntentFault.DuplicateOrDecreasingId)]
    public void MalformedTransitionOriginFailsClosed(IntentFault fault)
    {
        var seed = Seed();
        var origin = new FakeIntentOrigin(seed.Scope);
        var builder = new OwnerCommandStepBuilder(seed, origin);
        if (fault == IntentFault.DuplicateOrDecreasingId)
        {
            Assert.Equal(OwnerIntentQueueDecision.Added,
                builder.QueueTransition(
                    MovementTransitionKind.JumpPressed,
                    new SimulationDuration(5)));
        }
        origin.Fault = fault;
        var identity = builder.NextInputIdentity;
        var frame = builder.NextTargetFrame;
        var pending = builder.PendingTransitionCount;

        Assert.Equal(
            OwnerIntentQueueDecision.IntentOriginContractFault,
            builder.QueueTransition(
                MovementTransitionKind.JumpReleased,
                new SimulationDuration(5)));
        Assert.True(builder.RequiresTimelineRebase);
        Assert.Equal(identity, builder.NextInputIdentity);
        Assert.Equal(frame, builder.NextTargetFrame);
        Assert.Equal(pending, builder.PendingTransitionCount);
        var destination = new OwnerSimulationCommand[1];
        Assert.Equal(
            new OwnerCommandBuildResult(
                OwnerCommandBuildDecision.IntentOriginContractFault, 0),
            builder.Build(Sample(), OwnerCommandStepCount.One, destination));
        Assert.Equal(default, destination[0]);
        Assert.Equal(
            OwnerIntentQueueDecision.IntentOriginContractFault,
            builder.QueueAction(
                OwnerActionTrigger.Attack,
                SimulationDuration.Zero,
                new SimulationInstant(90)));
    }

    [Theory]
    [InlineData(IntentFault.ReturnDefault)]
    [InlineData(IntentFault.ForeignScope)]
    [InlineData(IntentFault.WrongOriginatingInput)]
    [InlineData(IntentFault.WrongKindOrTrigger)]
    [InlineData(IntentFault.WrongFirstFrame)]
    [InlineData(IntentFault.WrongDeadline)]
    [InlineData(IntentFault.WrongRenderedFrame)]
    [InlineData(IntentFault.ThrowAfterMutation)]
    [InlineData(IntentFault.UnknownDecision)]
    [InlineData(IntentFault.DuplicateOrDecreasingId)]
    public void MalformedActionOriginFailsClosed(IntentFault fault)
    {
        var seed = Seed();
        var origin = new FakeIntentOrigin(seed.Scope);
        var builder = new OwnerCommandStepBuilder(seed, origin);
        if (fault == IntentFault.DuplicateOrDecreasingId)
        {
            Assert.Equal(OwnerIntentQueueDecision.Added,
                builder.QueueAction(
                    OwnerActionTrigger.Attack,
                    new SimulationDuration(5),
                    new SimulationInstant(90)));
        }
        origin.Fault = fault;
        var identity = builder.NextInputIdentity;
        var frame = builder.NextTargetFrame;
        var pending = builder.PendingActionCount;

        Assert.Equal(
            OwnerIntentQueueDecision.IntentOriginContractFault,
            builder.QueueAction(
                OwnerActionTrigger.Block,
                new SimulationDuration(5),
                new SimulationInstant(90)));
        Assert.True(builder.RequiresTimelineRebase);
        Assert.Equal(identity, builder.NextInputIdentity);
        Assert.Equal(frame, builder.NextTargetFrame);
        Assert.Equal(pending, builder.PendingActionCount);
        Assert.Equal(
            OwnerCommandBuildDecision.IntentOriginContractFault,
            builder.Build(
                Sample(),
                OwnerCommandStepCount.One,
                new OwnerSimulationCommand[1]).Decision);
    }

    [Fact]
    public void JournalBaselineRepairStopsFurtherCommandConstruction()
    {
        var seed = Seed();
        var origin = new FakeIntentOrigin(seed.Scope)
        {
            TransitionDecision =
                MovementTransitionOriginDecision.BaselineRepairRequired,
        };
        var builder = new OwnerCommandStepBuilder(seed, origin);
        Assert.Equal(
            OwnerIntentQueueDecision.BaselineRepairRequired,
            builder.QueueTransition(
                MovementTransitionKind.JumpPressed,
                SimulationDuration.Zero));
        Assert.True(builder.RequiresTimelineRebase);
        Assert.Equal(
            OwnerCommandBuildDecision.IntentJournalBaselineRepairRequired,
            builder.Build(
                Sample(),
                OwnerCommandStepCount.One,
                new OwnerSimulationCommand[1]).Decision);
    }

    [Theory]
    [InlineData(MovementTransitionOriginDecision.CapacityExceeded)]
    [InlineData(MovementTransitionOriginDecision.IdentityExhausted)]
    public void NonAddedTransitionWithPayloadIsContractFault(
        MovementTransitionOriginDecision decision)
    {
        var seed = Seed();
        var origin = new FakeIntentOrigin(seed.Scope)
        {
            Fault = IntentFault.NonAddedPayload,
            TransitionDecision = decision,
        };
        var builder = new OwnerCommandStepBuilder(seed, origin);
        var identity = builder.NextInputIdentity;
        var frame = builder.NextTargetFrame;
        Assert.Equal(
            OwnerIntentQueueDecision.IntentOriginContractFault,
            builder.QueueTransition(
                MovementTransitionKind.JumpPressed,
                SimulationDuration.Zero));
        Assert.Equal(identity, builder.NextInputIdentity);
        Assert.Equal(frame, builder.NextTargetFrame);
        Assert.Equal(0, builder.PendingTransitionCount);
        var destination = new OwnerSimulationCommand[1];
        Assert.Equal(
            OwnerCommandBuildDecision.IntentOriginContractFault,
            builder.Build(
                Sample(),
                OwnerCommandStepCount.One,
                destination).Decision);
        Assert.Equal(default, destination[0]);
        Assert.Equal(
            OwnerIntentQueueDecision.IntentOriginContractFault,
            builder.QueueAction(
                OwnerActionTrigger.Attack,
                SimulationDuration.Zero,
                new SimulationInstant(90)));
    }

    [Theory]
    [InlineData(OwnerActionOriginDecision.CapacityExceeded)]
    [InlineData(OwnerActionOriginDecision.IdentityExhausted)]
    public void NonAddedActionWithPayloadIsContractFault(
        OwnerActionOriginDecision decision)
    {
        var seed = Seed();
        var origin = new FakeIntentOrigin(seed.Scope)
        {
            Fault = IntentFault.NonAddedPayload,
            ActionDecision = decision,
        };
        var builder = new OwnerCommandStepBuilder(seed, origin);
        Assert.Equal(
            OwnerIntentQueueDecision.IntentOriginContractFault,
            builder.QueueAction(
                OwnerActionTrigger.Attack,
                SimulationDuration.Zero,
                new SimulationInstant(90)));
        Assert.Equal(0, builder.PendingActionCount);
        Assert.Equal(
            OwnerIntentQueueDecision.IntentOriginContractFault,
            builder.QueueTransition(
                MovementTransitionKind.JumpPressed,
                SimulationDuration.Zero));
        var destination = new OwnerSimulationCommand[1];
        Assert.Equal(
            OwnerCommandBuildDecision.IntentOriginContractFault,
            builder.Build(
                Sample(),
                OwnerCommandStepCount.One,
                destination).Decision);
        Assert.Equal(default, destination[0]);
    }

    [Fact]
    public void ActionJournalBaselineRepairAlsoStopsCommandConstruction()
    {
        var seed = Seed();
        var origin = new FakeIntentOrigin(seed.Scope)
        {
            ActionDecision = OwnerActionOriginDecision.BaselineRepairRequired,
        };
        var builder = new OwnerCommandStepBuilder(seed, origin);
        Assert.Equal(
            OwnerIntentQueueDecision.BaselineRepairRequired,
            builder.QueueAction(
                OwnerActionTrigger.Attack,
                SimulationDuration.Zero,
                new SimulationInstant(90)));
        Assert.Equal(
            OwnerCommandBuildDecision.IntentJournalBaselineRepairRequired,
            builder.Build(
                Sample(),
                OwnerCommandStepCount.One,
                new OwnerSimulationCommand[1]).Decision);
    }

    [Fact]
    public void LiveJournalScopeDivergenceAndForeignResetFailBeforeOutput()
    {
        AssertJournalResetFailsClosed(resetBoth: false);
        AssertJournalResetFailsClosed(resetBoth: true);
    }

    [Fact]
    public void ThrowingOriginStateGettersFailBeforeOutput()
    {
        foreach (var throwScope in new[] { true, false })
        {
            var seed = Seed();
            var origin = new FakeIntentOrigin(seed.Scope);
            var builder = new OwnerCommandStepBuilder(seed, origin);
            builder.QueueTransition(
                MovementTransitionKind.JumpPressed,
                SimulationDuration.Zero);
            origin.ThrowOnScopeGet = throwScope;
            origin.ThrowOnBaselineRepairGet = !throwScope;
            var identity = builder.NextInputIdentity;
            var frame = builder.NextTargetFrame;
            var destination = new OwnerSimulationCommand[1];

            Assert.Equal(
                OwnerCommandBuildDecision.IntentOriginContractFault,
                builder.Build(
                    Sample(), OwnerCommandStepCount.One, destination).Decision);
            Assert.Equal(default, destination[0]);
            Assert.Equal(identity, builder.NextInputIdentity);
            Assert.Equal(frame, builder.NextTargetFrame);
            Assert.Equal(1, builder.PendingTransitionCount);
            Assert.True(builder.RequiresTimelineRebase);
        }
    }

    [Fact]
    public void WarmedZeroOneAndTwoStepPathsAllocateNothing()
    {
        var builder = Builder(out _, out _);
        var commands = new OwnerSimulationCommand[2];
        var sample = Sample();
        for (var index = 0; index < 2_000; index++)
        {
            builder.Build(sample, (OwnerCommandStepCount)(index % 3), commands);
        }

        var minimumAllocated = long.MaxValue;
        for (var retained = 0; retained < 4; retained++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < 1_000; index++)
            {
                builder.Build(sample, (OwnerCommandStepCount)(index % 3), commands);
            }
            minimumAllocated = Math.Min(
                minimumAllocated,
                GC.GetAllocatedBytesForCurrentThread() - before);
        }
        Assert.Equal(0, minimumAllocated);
    }

    [Fact]
    public void WarmedSuccessfulOriginBuildAndBackpressurePathsAllocateNothing()
    {
        var seed = Seed();
        var origin = new FakeIntentOrigin(seed.Scope);
        var builder = new OwnerCommandStepBuilder(seed, origin);
        var commands = new OwnerSimulationCommand[1];
        var sample = Sample();
        for (var index = 0; index < 2_000; index++)
        {
            builder.QueueTransition(
                MovementTransitionKind.JumpPressed,
                SimulationDuration.Zero);
            builder.QueueAction(
                OwnerActionTrigger.Attack,
                SimulationDuration.Zero,
                new SimulationInstant(90));
            builder.Build(sample, OwnerCommandStepCount.One, commands);
        }

        var minimumAllocated = long.MaxValue;
        for (var retained = 0; retained < 4; retained++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < 1_000; index++)
            {
                builder.QueueTransition(
                    MovementTransitionKind.JumpPressed,
                    SimulationDuration.Zero);
                builder.QueueAction(
                    OwnerActionTrigger.Attack,
                    SimulationDuration.Zero,
                    new SimulationInstant(90));
                builder.Build(sample, OwnerCommandStepCount.One, commands);
            }
            minimumAllocated = Math.Min(
                minimumAllocated,
                GC.GetAllocatedBytesForCurrentThread() - before);
        }
        Assert.Equal(0, minimumAllocated);

        origin.TransitionDecision =
            MovementTransitionOriginDecision.CapacityExceeded;
        origin.ActionDecision = OwnerActionOriginDecision.CapacityExceeded;
        minimumAllocated = long.MaxValue;
        var lastTransitionDecision = default(OwnerIntentQueueDecision);
        var lastActionDecision = default(OwnerIntentQueueDecision);
        for (var retained = 0; retained < 4; retained++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < 1_000; index++)
            {
                lastTransitionDecision = builder.QueueTransition(
                    MovementTransitionKind.JumpPressed,
                    SimulationDuration.Zero);
                lastActionDecision = builder.QueueAction(
                    OwnerActionTrigger.Attack,
                    SimulationDuration.Zero,
                    new SimulationInstant(90));
            }
            minimumAllocated = Math.Min(
                minimumAllocated,
                GC.GetAllocatedBytesForCurrentThread() - before);
        }
        Assert.Equal(0, minimumAllocated);
        Assert.Equal(OwnerIntentQueueDecision.JournalCapacityExceeded,
            lastTransitionDecision);
        Assert.Equal(OwnerIntentQueueDecision.JournalCapacityExceeded,
            lastActionDecision);
    }

    private static OwnerCommandStepBuilder Builder(
        out MovementTransitionJournal transitions,
        out OwnerActionCommandJournal actions,
        InputSequence? firstSequence = null,
        SimulationInstant? firstTarget = null)
    {
        var scope = Scope();
        transitions = new MovementTransitionJournal(scope);
        actions = new OwnerActionCommandJournal(scope);
        return new OwnerCommandStepBuilder(
            Seed(
                firstSequence ?? InputSequence.Initial,
                firstTarget ?? new SimulationInstant(100)),
            new OwnerCommandIntentJournalAdapter(transitions, actions));
    }

    private static void AssertJournalResetFailsClosed(bool resetBoth)
    {
        var seed = Seed();
        var transitions = new MovementTransitionJournal(seed.Scope);
        var actions = new OwnerActionCommandJournal(seed.Scope);
        var builder = new OwnerCommandStepBuilder(
            seed,
            new OwnerCommandIntentJournalAdapter(transitions, actions));
        builder.QueueTransition(
            MovementTransitionKind.JumpPressed,
            SimulationDuration.Zero);
        var foreignScope = Scope(control: 2);
        Assert.True(transitions.Reset(foreignScope));
        if (resetBoth)
        {
            Assert.True(actions.Reset(foreignScope));
        }
        var identity = builder.NextInputIdentity;
        var frame = builder.NextTargetFrame;
        var destination = new OwnerSimulationCommand[1];

        Assert.Equal(
            OwnerCommandBuildDecision.IntentOriginContractFault,
            builder.Build(
                Sample(), OwnerCommandStepCount.One, destination).Decision);
        Assert.Equal(default, destination[0]);
        Assert.Equal(identity, builder.NextInputIdentity);
        Assert.Equal(frame, builder.NextTargetFrame);
        Assert.Equal(1, builder.PendingTransitionCount);
        Assert.True(builder.RequiresTimelineRebase);
    }

    private static OwnerCommandTimelineSeed Seed(
        InputSequence? firstSequence = null,
        SimulationInstant? firstTarget = null)
    {
        var target = firstTarget ?? new SimulationInstant(100);
        const int lead = 2;
        var enable = target.Tick - lead;
        var published = enable - 1;
        var epoch = new CombatantAuthorityPredictionEpoch(
            100,
            new MatchFrameEpochId(1),
            new CombatantId(7),
            new LifeGenerationId(2),
            new AuthorityDiscontinuityId(5),
            new OwnerControlEpoch(1));
        var router = new ClientPredictionLifecycleRouter(100, _ => { });
        var coordinator = new OwnerPredictionBootstrapCoordinator(
            router, 100, new CombatantId(7));
        var plan = new OwnerPredictionBootstrapPlan(
            PredictionBootstrapPlanId.Initial,
            epoch,
            OwnerPredictionBootstrapKind.PreMatch,
            OwnerPredictionBaselinePreparation.FrozenCommandPredecessor,
            new SimulationInstant(published),
            new SimulationInstant(target.Tick - 1),
            new SimulationInstant(enable),
            target,
            new PredictionLeadFrameCount(lead),
            PredictionLeadPolicyRevision.Initial);
        Assert.Equal(
            OwnerPredictionBootstrapApplyDecision.AppliedInitial,
            coordinator.ApplyBootstrap(plan));
        Assert.Equal(
            OwnerPredictionBaselineRestoreDecision.Committed,
            coordinator.CommitStateBearingBaseline(new OwnerPredictionBaselineReceipt(
                plan.PlanId,
                plan.AuthorityEpoch,
                plan.BaselineFrame,
                ClientPredictionStateBaselineSource.Spawn)));
        Assert.Equal(
            OwnerPredictionClockDecision.InputEnabled,
            coordinator.ObserveClock(new SynchronizedAuthorityFrameSample(
                100,
                new MatchFrameEpochId(1),
                new SimulationInstant(enable),
                PredictionClockConfidence.Stable)));
        return coordinator.CreateCommandTimelineSeed(
            firstSequence ?? InputSequence.Initial);
    }

    private static OwnerCommandInputSample Sample(
        double yaw = 0,
        double movementX = 0,
        MovementHeldButtons movementHeld = MovementHeldButtons.None,
        CombatHeldButtons combatHeld = CombatHeldButtons.None,
        ulong movementRevision = 3,
        ulong capabilityRevision = 4) => new(
            MovementAxes.FromUnitVector(new HorizontalVector(movementX, 0)),
            ViewOrientation.FromRadians(yaw, 0),
            new MovementHeldState(movementHeld),
            new CombatInputState(combatHeld),
            new MovementConfigurationRevision(movementRevision),
            new MovementCapabilityRevision(capabilityRevision));

    private static OwnerIntentScope Scope(
        ulong control = 1) => new(
            100,
            new LifeEpoch(new CombatantId(7), new LifeGenerationId(2)),
            new OwnerControlEpoch(control));

    public enum IntentFault
    {
        None,
        ReturnDefault,
        ForeignScope,
        WrongOriginatingInput,
        WrongKindOrTrigger,
        WrongFirstFrame,
        WrongDeadline,
        WrongRenderedFrame,
        ThrowAfterMutation,
        UnknownDecision,
        DuplicateOrDecreasingId,
        NonAddedPayload,
    }

    private sealed class FakeIntentOrigin : IOwnerCommandIntentOrigin
    {
        private ulong _nextTransitionId = 1;
        private ulong _nextActionId = 1;

        private readonly OwnerIntentScope _scope;
        private bool _requiresBaselineRepair;

        public FakeIntentOrigin(OwnerIntentScope scope) => _scope = scope;

        public OwnerIntentScope Scope => ThrowOnScopeGet
            ? throw new InvalidOperationException("Synthetic scope getter failure.")
            : _scope;
        public bool RequiresBaselineRepair
        {
            get => ThrowOnBaselineRepairGet
                ? throw new InvalidOperationException(
                    "Synthetic baseline getter failure.")
                : _requiresBaselineRepair;
            set => _requiresBaselineRepair = value;
        }
        public bool ThrowOnScopeGet { get; set; }
        public bool ThrowOnBaselineRepairGet { get; set; }
        public IntentFault Fault { get; set; }
        public MovementTransitionOriginDecision TransitionDecision { get; set; } =
            MovementTransitionOriginDecision.Added;
        public OwnerActionOriginDecision ActionDecision { get; set; } =
            OwnerActionOriginDecision.Added;

        public MovementTransitionOriginDecision TryOriginateTransition(
            OwnerInputIdentity originatingInput,
            MovementTransitionKind kind,
            SimulationInstant firstPredictedFrame,
            SimulationInstant lastValidFrame,
            out MovementTransitionIntent intent)
        {
            if (Fault == IntentFault.ThrowAfterMutation)
            {
                _nextTransitionId++;
                throw new InvalidOperationException("Synthetic post-mutation failure.");
            }
            if (Fault == IntentFault.UnknownDecision)
            {
                intent = default;
                return (MovementTransitionOriginDecision)255;
            }
            if (TransitionDecision != MovementTransitionOriginDecision.Added)
            {
                intent = Fault == IntentFault.NonAddedPayload
                    ? new MovementTransitionIntent(
                        new MovementTransitionIdentity(
                            _scope,
                            new MovementTransitionId(_nextTransitionId++)),
                        originatingInput,
                        kind,
                        firstPredictedFrame,
                        lastValidFrame)
                    : default;
                return TransitionDecision;
            }
            if (Fault == IntentFault.ReturnDefault)
            {
                intent = default;
                return MovementTransitionOriginDecision.Added;
            }

            var returnedScope = Fault == IntentFault.ForeignScope
                ? OwnerCommandStepBuilderTests.Scope(control: 2)
                : _scope;
            var sequence = Fault == IntentFault.WrongOriginatingInput
                ? new InputSequence(originatingInput.Sequence.Value + 1)
                : originatingInput.Sequence;
            var returnedInput = new OwnerInputIdentity(returnedScope, sequence);
            var returnedKind = Fault == IntentFault.WrongKindOrTrigger
                ? MovementTransitionKind.LedgeDrop
                : kind;
            var returnedFirst = Fault == IntentFault.WrongFirstFrame
                ? new SimulationInstant(firstPredictedFrame.Tick + 1)
                : firstPredictedFrame;
            var returnedLast = Fault == IntentFault.WrongDeadline
                ? new SimulationInstant(lastValidFrame.Tick + 1)
                : Fault == IntentFault.WrongFirstFrame &&
                    lastValidFrame < returnedFirst
                    ? returnedFirst
                    : lastValidFrame;
            var id = Fault == IntentFault.DuplicateOrDecreasingId
                ? _nextTransitionId - 1
                : _nextTransitionId++;
            intent = new MovementTransitionIntent(
                new MovementTransitionIdentity(
                    returnedScope,
                    new MovementTransitionId(id)),
                returnedInput,
                returnedKind,
                returnedFirst,
                returnedLast);
            return MovementTransitionOriginDecision.Added;
        }

        public OwnerActionOriginDecision TryOriginateAction(
            OwnerInputIdentity originatingInput,
            OwnerActionTrigger trigger,
            SimulationInstant predictedStartFrame,
            SimulationInstant lastValidStartFrame,
            SimulationInstant renderedAuthorityFrame,
            out PredictedActionIntent intent)
        {
            if (Fault == IntentFault.ThrowAfterMutation)
            {
                _nextActionId++;
                throw new InvalidOperationException("Synthetic post-mutation failure.");
            }
            if (Fault == IntentFault.UnknownDecision)
            {
                intent = default;
                return (OwnerActionOriginDecision)255;
            }
            if (ActionDecision != OwnerActionOriginDecision.Added)
            {
                intent = Fault == IntentFault.NonAddedPayload
                    ? new PredictedActionIntent(
                        new PredictedActionIdentity(
                            _scope,
                            new PredictedActionId(_nextActionId++)),
                        originatingInput,
                        trigger,
                        predictedStartFrame,
                        lastValidStartFrame,
                        renderedAuthorityFrame)
                    : default;
                return ActionDecision;
            }
            if (Fault == IntentFault.ReturnDefault)
            {
                intent = default;
                return OwnerActionOriginDecision.Added;
            }

            var returnedScope = Fault == IntentFault.ForeignScope
                ? OwnerCommandStepBuilderTests.Scope(control: 2)
                : _scope;
            var sequence = Fault == IntentFault.WrongOriginatingInput
                ? new InputSequence(originatingInput.Sequence.Value + 1)
                : originatingInput.Sequence;
            var returnedInput = new OwnerInputIdentity(returnedScope, sequence);
            var returnedTrigger = Fault == IntentFault.WrongKindOrTrigger
                ? trigger == OwnerActionTrigger.Attack
                    ? OwnerActionTrigger.Block
                    : OwnerActionTrigger.Attack
                : trigger;
            var returnedFirst = Fault == IntentFault.WrongFirstFrame
                ? new SimulationInstant(predictedStartFrame.Tick + 1)
                : predictedStartFrame;
            var returnedLast = Fault == IntentFault.WrongDeadline
                ? new SimulationInstant(lastValidStartFrame.Tick + 1)
                : Fault == IntentFault.WrongFirstFrame &&
                    lastValidStartFrame < returnedFirst
                    ? returnedFirst
                    : lastValidStartFrame;
            var returnedRendered = Fault == IntentFault.WrongRenderedFrame
                ? new SimulationInstant(renderedAuthorityFrame.Tick - 1)
                : renderedAuthorityFrame;
            var id = Fault == IntentFault.DuplicateOrDecreasingId
                ? _nextActionId - 1
                : _nextActionId++;
            intent = new PredictedActionIntent(
                new PredictedActionIdentity(
                    returnedScope,
                    new PredictedActionId(id)),
                returnedInput,
                returnedTrigger,
                returnedFirst,
                returnedLast,
                returnedRendered);
            return OwnerActionOriginDecision.Added;
        }
    }
}
