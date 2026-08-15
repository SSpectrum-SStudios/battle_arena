using BattleArena.Core.Common;
using BattleArena.Core.Movement.Simulation;
using BattleArena.Multiplayer.OwnerPrediction;

namespace BattleArena.Multiplayer.Tests.OwnerPrediction;

public sealed class OwnerPredictionBootstrapTests
{
    [Fact]
    public void PlanRequiresFutureEnableExactAbsoluteLeadAndMatchingBaselinePolicy()
    {
        var epoch = Epoch();
        Assert.Throws<ArgumentOutOfRangeException>(() => new OwnerPredictionBootstrapPlan(
            PredictionBootstrapPlanId.Initial, epoch, OwnerPredictionBootstrapKind.PreMatch,
            OwnerPredictionBaselinePreparation.FrozenCommandPredecessor,
            new SimulationInstant(100), new SimulationInstant(115),
            new SimulationInstant(100), new SimulationInstant(116), Lead(6), Revision(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OwnerPredictionBootstrapPlan(
            PredictionBootstrapPlanId.Initial, epoch, OwnerPredictionBootstrapKind.PreMatch,
            OwnerPredictionBaselinePreparation.FrozenCommandPredecessor,
            new SimulationInstant(100), new SimulationInstant(115),
            new SimulationInstant(110), new SimulationInstant(117), Lead(6), Revision(1)));
        Assert.Throws<ArgumentException>(() => new OwnerPredictionBootstrapPlan(
            PredictionBootstrapPlanId.Initial, epoch, OwnerPredictionBootstrapKind.Reconnect,
            OwnerPredictionBaselinePreparation.FrozenCommandPredecessor,
            new SimulationInstant(100), new SimulationInstant(115),
            new SimulationInstant(110), new SimulationInstant(116), Lead(6), Revision(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OwnerPredictionBootstrapPlan(
            PredictionBootstrapPlanId.Initial, epoch, OwnerPredictionBootstrapKind.PreMatch,
            OwnerPredictionBaselinePreparation.FrozenCommandPredecessor,
            new SimulationInstant(100), new SimulationInstant(114),
            new SimulationInstant(110), new SimulationInstant(116), Lead(6), Revision(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OwnerPredictionBootstrapPlan(
            PredictionBootstrapPlanId.Initial, epoch, OwnerPredictionBootstrapKind.Reconnect,
            OwnerPredictionBaselinePreparation.NeutralPreroll,
            new SimulationInstant(100), new SimulationInstant(101),
            new SimulationInstant(110), new SimulationInstant(116), Lead(6), Revision(1)));

        Assert.True(Plan(epoch, OwnerPredictionBootstrapKind.PreMatch).IsValid);
        Assert.True(Plan(epoch, OwnerPredictionBootstrapKind.Reconnect).IsValid);
    }

    [Fact]
    public void PreMatchCannotEnableFromLocalDelayUnstableClockOrEarlyAuthorityFrame()
    {
        var coordinator = Coordinator();
        var plan = Plan(Epoch(), OwnerPredictionBootstrapKind.PreMatch);
        Assert.Equal(
            OwnerPredictionBootstrapApplyDecision.AppliedInitial,
            coordinator.ApplyBootstrap(plan));
        Assert.Equal(OwnerPredictionBootstrapState.AwaitingBaselineRestore,
            coordinator.State);
        Assert.Equal(
            OwnerPredictionBaselineRestoreDecision.WrongEpoch,
            coordinator.CommitStateBearingBaseline(new OwnerPredictionBaselineReceipt(
                plan.PlanId,
                Epoch(control: 2),
                plan.BaselineFrame,
                ClientPredictionStateBaselineSource.Spawn)));
        Assert.Equal(
            OwnerPredictionClockDecision.BaselineNotRestored,
            coordinator.ObserveClock(Sample(109, PredictionClockConfidence.Stable)));
        Restore(coordinator, plan);
        Assert.Equal(
            OwnerPredictionBaselineRestoreDecision.Duplicate,
            coordinator.CommitStateBearingBaseline(Receipt(plan)));
        Assert.Equal(OwnerPredictionBootstrapState.WaitingForSynchronizedEnable,
            coordinator.State);

        // A client may have waited arbitrarily long locally; without a stable
        // synchronized authority frame, local input remains disabled.
        Assert.Equal(
            OwnerPredictionClockDecision.WaitingForConfidence,
            coordinator.ObserveClock(Sample(1_000, PredictionClockConfidence.Acquiring)));
        Assert.Equal(
            OwnerPredictionClockDecision.BeforeEnableFrame,
            coordinator.ObserveClock(Sample(109, PredictionClockConfidence.Stable)));
        Assert.Null(coordinator.NextCommandTargetFrame);

        Assert.Equal(
            OwnerPredictionClockDecision.InputEnabled,
            coordinator.ObserveClock(Sample(110, PredictionClockConfidence.Stable)));
        Assert.Equal(new SimulationInstant(116), coordinator.NextCommandTargetFrame);
        Assert.Equal(OwnerPredictionBootstrapState.Active, coordinator.State);
    }

    [Fact]
    public void ScheduleAndStatelessLifeNotificationCannotCommitEpochBeforeStateBaseline()
    {
        var resets = new List<ClientPredictionEpochTransition>();
        var router = new ClientPredictionLifecycleRouter(100, resets.Add);
        var coordinator = new OwnerPredictionBootstrapCoordinator(
            router, 100, new CombatantId(7));
        var initial = Plan(Epoch(), OwnerPredictionBootstrapKind.PreMatch);

        Assert.Equal(
            OwnerPredictionBootstrapApplyDecision.AppliedInitial,
            coordinator.ApplyBootstrap(initial));
        Assert.Null(coordinator.CommittedEpoch);
        Assert.Empty(resets);
        Restore(coordinator, initial);
        Assert.Empty(resets); // First registration has no old epoch to reset.

        var reconnect = Plan(
            Epoch(control: 2),
            OwnerPredictionBootstrapKind.Reconnect,
            published: 200,
            enable: 210,
            lead: 6,
            baseline: 200);
        Assert.Equal(
            OwnerPredictionBootstrapApplyDecision.AppliedReconnect,
            coordinator.ApplyBootstrap(reconnect));
        Assert.Equal(initial.AuthorityEpoch, coordinator.CommittedEpoch!.Value.Authority);
        Assert.Empty(resets);
        Assert.Equal(
            ClientLifeNotificationDecision.PendingStateBaseline,
            router.ObserveLifeNotification(reconnect.AuthorityEpoch));
        Assert.Equal(initial.AuthorityEpoch, coordinator.CommittedEpoch!.Value.Authority);
        Assert.Empty(resets);
        Assert.Equal(
            OwnerPredictionClockDecision.BaselineNotRestored,
            coordinator.ObserveClock(Sample(209, PredictionClockConfidence.Stable)));

        Assert.Equal(
            OwnerPredictionBaselineRestoreDecision.Committed,
            coordinator.CommitStateBearingBaseline(Receipt(reconnect)));
        Assert.Single(resets);
        Assert.Equal(reconnect.AuthorityEpoch, coordinator.CommittedEpoch!.Value.Authority);
        Assert.Equal(
            OwnerPredictionBaselineRestoreDecision.Duplicate,
            coordinator.CommitStateBearingBaseline(Receipt(reconnect)));
        Assert.Single(resets);
    }

    [Fact]
    public void RouterAdvanceBetweenScheduleAndReceiptCannotPartiallyCommitAggregate()
    {
        var router = new ClientPredictionLifecycleRouter(100, _ => { });
        var coordinator = new OwnerPredictionBootstrapCoordinator(
            router, 100, new CombatantId(7));
        var initial = Plan(Epoch(), OwnerPredictionBootstrapKind.PreMatch);
        coordinator.ApplyBootstrap(initial);
        Restore(coordinator, initial);
        var initialLead = coordinator.LeadPolicy;
        var reconnect = Plan(
            Epoch(control: 2), OwnerPredictionBootstrapKind.Reconnect,
            published: 200, enable: 210, lead: 6, baseline: 200,
            leadRevision: 2);
        coordinator.ApplyBootstrap(reconnect);

        Assert.True(router.ObserveStateBaseline(
            Epoch(control: 3),
            ClientPredictionStateBaselineSource.Snapshot).IsAccepted);
        var exception = Record.Exception(() =>
        {
            Assert.Equal(
                OwnerPredictionBaselineRestoreDecision.RejectedLifecycle,
                coordinator.CommitStateBearingBaseline(Receipt(reconnect)));
        });

        Assert.Null(exception);
        Assert.Equal(initial, coordinator.CurrentPlan);
        Assert.Equal(reconnect, coordinator.PendingPlan);
        Assert.Equal(initialLead, coordinator.LeadPolicy);
        Assert.Equal(Epoch(control: 3), coordinator.CommittedEpoch!.Value.Authority);
        Assert.True(coordinator.RequiresTimelineRebase);
    }

    [Fact]
    public void LeadDivergenceDuringPendingBootstrapFailsClosedWithoutEpochAdvance()
    {
        var coordinator = Coordinator();
        var initial = Plan(Epoch(), OwnerPredictionBootstrapKind.PreMatch);
        coordinator.ApplyBootstrap(initial);
        Restore(coordinator, initial);
        var reconnect = Plan(
            Epoch(control: 2), OwnerPredictionBootstrapKind.Reconnect,
            published: 200, enable: 210, lead: 6, baseline: 200,
            leadRevision: 2);
        coordinator.ApplyBootstrap(reconnect);
        var context = new PredictionLeadSafetyContext(
            new SimulationInstant(100), null);

        Assert.Equal(
            PredictionLeadUpdateDecision.ConflictingRevision,
            coordinator.ObserveLeadUpdate(new PredictionLeadUpdate(
                initial.Scope,
                Lead(7),
                initial.LeadRevision,
                new SimulationInstant(110)), context));
        Assert.Equal(
            OwnerPredictionBaselineRestoreDecision.RebaseRequired,
            coordinator.CommitStateBearingBaseline(Receipt(reconnect)));
        Assert.Equal(initial, coordinator.CurrentPlan);
        Assert.Equal(reconnect, coordinator.PendingPlan);
        Assert.Equal(initial.AuthorityEpoch, coordinator.CommittedEpoch!.Value.Authority);
        Assert.Equal(initial.LeadRevision,
            coordinator.LeadPolicy!.Value.LatestAccepted!.Value.Revision);
        Assert.True(coordinator.RequiresTimelineRebase);
    }

    [Fact]
    public void BaselineReceiptMustExactlyNamePendingPlanWithoutPartialMutation()
    {
        var resetCount = 0;
        var coordinator = Coordinator(resetSink: _ => resetCount++);
        var plan = Plan(Epoch(), OwnerPredictionBootstrapKind.PreMatch);
        coordinator.ApplyBootstrap(plan);

        Assert.Equal(
            OwnerPredictionBaselineRestoreDecision.WrongPlan,
            coordinator.CommitStateBearingBaseline(new OwnerPredictionBaselineReceipt(
                new PredictionBootstrapPlanId(2),
                plan.AuthorityEpoch,
                plan.BaselineFrame,
                ClientPredictionStateBaselineSource.Spawn)));
        Assert.Equal(
            OwnerPredictionBaselineRestoreDecision.WrongEpoch,
            coordinator.CommitStateBearingBaseline(new OwnerPredictionBaselineReceipt(
                plan.PlanId,
                Epoch(control: 2),
                plan.BaselineFrame,
                ClientPredictionStateBaselineSource.Spawn)));
        Assert.Equal(
            OwnerPredictionBaselineRestoreDecision.WrongFrame,
            coordinator.CommitStateBearingBaseline(new OwnerPredictionBaselineReceipt(
                plan.PlanId,
                plan.AuthorityEpoch,
                new SimulationInstant(plan.BaselineFrame.Tick - 1),
                ClientPredictionStateBaselineSource.AuthorityMovement)));

        Assert.Null(coordinator.CommittedEpoch);
        Assert.Equal(plan, coordinator.PendingPlan);
        Assert.Equal(0, resetCount);
        Restore(coordinator, plan);
        Assert.Equal(0, resetCount);
    }

    [Fact]
    public void CommandFramesCommitOnlyAsOneContiguousTargetTimeline()
    {
        var coordinator = ActiveCoordinator();
        Assert.Equal(
            OwnerPredictionCommandFrameDecision.WrongFrame,
            coordinator.CommitCommandTargetFrame(new SimulationInstant(117)));
        Assert.Equal(new SimulationInstant(116), coordinator.NextCommandTargetFrame);
        for (long frame = 116; frame <= 130; frame++)
        {
            Assert.Equal(
                OwnerPredictionCommandFrameDecision.Committed,
                coordinator.CommitCommandTargetFrame(new SimulationInstant(frame)));
            Assert.Equal(new SimulationInstant(frame + 1),
                coordinator.NextCommandTargetFrame);
        }
    }

    [Fact]
    public void ReconnectNeutralPrerollIsExplicitContiguousAndMustFinishBeforeEnable()
    {
        var coordinator = ActiveCoordinator();
        var reconnect = Plan(
            Epoch(control: 2),
            OwnerPredictionBootstrapKind.Reconnect,
            published: 200,
            enable: 210,
            lead: 6,
            baseline: 200);
        Assert.Equal(
            OwnerPredictionBootstrapApplyDecision.AppliedReconnect,
            coordinator.ApplyBootstrap(reconnect));
        Assert.Equal(
            OwnerPredictionBaselineRestoreDecision.WrongFrame,
            coordinator.CommitStateBearingBaseline(new OwnerPredictionBaselineReceipt(
                reconnect.PlanId,
                reconnect.AuthorityEpoch,
                new SimulationInstant(reconnect.BaselineFrame.Tick + 1),
                ClientPredictionStateBaselineSource.Snapshot)));
        Assert.False(coordinator.TryGetNextNeutralFrame(out _));
        Assert.Equal(
            OwnerPredictionNeutralFrameDecision.BaselineNotRestored,
            coordinator.CommitNeutralFrame(new SimulationInstant(201)));
        Restore(coordinator, reconnect);
        Assert.Equal(15, coordinator.NeutralFramesRemaining);
        Assert.Equal(
            OwnerPredictionClockDecision.NeutralPrerollIncomplete,
            coordinator.ObserveClock(Sample(210, PredictionClockConfidence.Stable)));

        Assert.True(coordinator.TryGetNextNeutralFrame(out var first));
        Assert.Equal(new SimulationInstant(201), first);
        Assert.Equal(
            OwnerPredictionNeutralFrameDecision.WrongFrame,
            coordinator.CommitNeutralFrame(new SimulationInstant(202)));
        Assert.Equal(15, coordinator.NeutralFramesRemaining);
        for (long frame = 201; frame <= 215; frame++)
        {
            Assert.True(coordinator.TryGetNextNeutralFrame(out var next));
            Assert.Equal(new SimulationInstant(frame), next);
            Assert.Equal(
                OwnerPredictionNeutralFrameDecision.Committed,
                coordinator.CommitNeutralFrame(next));
        }
        Assert.False(coordinator.TryGetNextNeutralFrame(out _));
        Assert.Equal(0, coordinator.NeutralFramesRemaining);
        Assert.Equal(
            OwnerPredictionClockDecision.InputEnabled,
            coordinator.ObserveClock(Sample(210, PredictionClockConfidence.Stable)));
        Assert.Equal(new SimulationInstant(216), coordinator.NextCommandTargetFrame);
    }

    [Fact]
    public void PendingPlanSupersessionRejectsDelayedReceiptAndCommitsNewestOnce()
    {
        var resetCount = 0;
        var coordinator = Coordinator(resetSink: _ => resetCount++);
        var initial = Plan(Epoch(), OwnerPredictionBootstrapKind.PreMatch);
        coordinator.ApplyBootstrap(initial);
        Restore(coordinator, initial);
        var second = Plan(
            Epoch(control: 2), OwnerPredictionBootstrapKind.Reconnect,
            published: 200, enable: 210, lead: 6, baseline: 200,
            planId: 2, leadRevision: 2);
        var third = Plan(
            Epoch(control: 3), OwnerPredictionBootstrapKind.Reconnect,
            published: 220, enable: 230, lead: 7, baseline: 220,
            planId: 3, leadRevision: 3);

        Assert.Equal(OwnerPredictionBootstrapApplyDecision.AppliedReconnect,
            coordinator.ApplyBootstrap(second));
        Assert.Equal(OwnerPredictionBootstrapApplyDecision.AppliedReconnect,
            coordinator.ApplyBootstrap(third));
        Assert.Equal(third, coordinator.PendingPlan);
        Assert.Equal(
            OwnerPredictionBaselineRestoreDecision.WrongPlan,
            coordinator.CommitStateBearingBaseline(Receipt(second)));
        Assert.Equal(initial, coordinator.CurrentPlan);
        Assert.Equal(third, coordinator.PendingPlan);
        Assert.Equal(0, resetCount);

        Restore(coordinator, third);
        Assert.Equal(1, resetCount);
        Assert.Equal(third, coordinator.CurrentPlan);
        Assert.Equal(Revision(3),
            coordinator.LeadPolicy!.Value.LatestAccepted!.Value.Revision);
        Assert.Equal(
            OwnerPredictionBaselineRestoreDecision.Duplicate,
            coordinator.CommitStateBearingBaseline(Receipt(third)));
        Assert.Equal(1, resetCount);
    }

    [Fact]
    public void RespawnLoadsFrozenPredecessorAndStartsFreshContiguousTarget()
    {
        var coordinator = ActiveCoordinator();
        var respawn = Plan(
            Epoch(life: 2, discontinuity: 2, control: 2),
            OwnerPredictionBootstrapKind.Respawn,
            published: 300,
            enable: 310,
            lead: 6);

        Assert.Equal(
            OwnerPredictionBootstrapApplyDecision.AppliedRespawn,
            coordinator.ApplyBootstrap(respawn));
        Restore(coordinator, respawn);
        Assert.Equal(0, coordinator.NeutralFramesRemaining);
        Assert.False(coordinator.TryGetNextNeutralFrame(out _));
        Assert.Equal(
            OwnerPredictionClockDecision.InputEnabled,
            coordinator.ObserveClock(Sample(310, PredictionClockConfidence.Stable)));
        Assert.Equal(new SimulationInstant(316), coordinator.NextCommandTargetFrame);
    }

    [Fact]
    public void ClockLossSuspendsCommandsUntilNewControlTimelineRebaseArrives()
    {
        var coordinator = ActiveCoordinator();
        Assert.Equal(
            OwnerPredictionClockDecision.ClockConfidenceLost,
            coordinator.ObserveClock(Sample(111, PredictionClockConfidence.Lost)));
        Assert.True(coordinator.RequiresTimelineRebase);
        Assert.Null(coordinator.NextCommandTargetFrame);
        Assert.Equal(
            OwnerPredictionCommandFrameDecision.RebaseRequired,
            coordinator.CommitCommandTargetFrame(new SimulationInstant(116)));

        var oldPlan = coordinator.CurrentPlan!.Value;
        Assert.Equal(
            OwnerPredictionBootstrapApplyDecision.Duplicate,
            coordinator.ApplyBootstrap(oldPlan));
        Assert.True(coordinator.RequiresTimelineRebase);

        var rebase = Plan(
            Epoch(matchFrame: 2, control: 2),
            OwnerPredictionBootstrapKind.TimelineRebase,
            published: 200,
            enable: 210,
            lead: 6,
            baseline: 200);
        Assert.Equal(
            OwnerPredictionBootstrapApplyDecision.AppliedTimelineRebase,
            coordinator.ApplyBootstrap(rebase));
        Restore(coordinator, rebase);
        Assert.False(coordinator.RequiresTimelineRebase);
        Assert.Equal(15, coordinator.NeutralFramesRemaining);
    }

    [Fact]
    public void LifecycleKindMismatchCannotMutateEpochBeforeValidReplacement()
    {
        var coordinator = ActiveCoordinator();
        var controlOnly = Epoch(control: 2);
        Assert.Equal(
            OwnerPredictionBootstrapApplyDecision.RejectedInvalidLifecycle,
            coordinator.ApplyBootstrap(Plan(
                controlOnly,
                OwnerPredictionBootstrapKind.Respawn,
                published: 200,
                enable: 210,
                lead: 6)));

        Assert.Equal(
            OwnerPredictionBootstrapApplyDecision.AppliedReconnect,
            coordinator.ApplyBootstrap(Plan(
                controlOnly,
                OwnerPredictionBootstrapKind.Reconnect,
                published: 200,
                enable: 210,
                lead: 6,
                baseline: 200)));
    }

    [Fact]
    public void StaleForeignAndInvalidLifecyclePlansNeverReplaceCurrentPlan()
    {
        var coordinator = ActiveCoordinator();
        var reconnect = Plan(
            Epoch(control: 2), OwnerPredictionBootstrapKind.Reconnect,
            published: 200, enable: 210, lead: 6, baseline: 200);
        coordinator.ApplyBootstrap(reconnect);
        Restore(coordinator, reconnect);

        Assert.Equal(
            OwnerPredictionBootstrapApplyDecision.RejectedPlanIdRegression,
            coordinator.ApplyBootstrap(Plan(
                Epoch(control: 1), OwnerPredictionBootstrapKind.PreMatch,
                published: 300, enable: 310, lead: 6)));
        Assert.Equal(
            OwnerPredictionBootstrapApplyDecision.RejectedDifferentScope,
            coordinator.ApplyBootstrap(Plan(
                Epoch(session: 101, control: 3),
                OwnerPredictionBootstrapKind.Reconnect,
                published: 300, enable: 310, lead: 6, baseline: 300)));
        Assert.Equal(reconnect, coordinator.CurrentPlan);
    }

    [Fact]
    public void EveryAuthorityEpochComponentIsIndependentlyMonotonic()
    {
        var coordinator = Coordinator();
        var initial = Plan(
            Epoch(matchFrame: 2, life: 2, discontinuity: 2, control: 2),
            OwnerPredictionBootstrapKind.PreMatch,
            planId: 2);
        coordinator.ApplyBootstrap(initial);
        Restore(coordinator, initial);
        coordinator.ObserveClock(Sample(
            110, PredictionClockConfidence.Stable, matchFrame: 2));

        var regressions = new[]
        {
            Plan(
                Epoch(matchFrame: 1, life: 2, discontinuity: 2, control: 3),
                OwnerPredictionBootstrapKind.TimelineRebase,
                published: 200, enable: 210, lead: 6, baseline: 200, planId: 3),
            Plan(
                Epoch(matchFrame: 2, life: 1, discontinuity: 2, control: 3),
                OwnerPredictionBootstrapKind.TimelineRebase,
                published: 200, enable: 210, lead: 6, baseline: 200, planId: 4),
            Plan(
                Epoch(matchFrame: 2, life: 2, discontinuity: 1, control: 3),
                OwnerPredictionBootstrapKind.TimelineRebase,
                published: 200, enable: 210, lead: 6, baseline: 200, planId: 5),
            Plan(
                Epoch(matchFrame: 2, life: 2, discontinuity: 2, control: 1),
                OwnerPredictionBootstrapKind.TimelineRebase,
                published: 200, enable: 210, lead: 6, baseline: 200, planId: 6),
        };

        foreach (var regression in regressions)
        {
            Assert.Equal(
                OwnerPredictionBootstrapApplyDecision.RejectedRegression,
                coordinator.ApplyBootstrap(regression));
            Assert.Equal(initial, coordinator.CurrentPlan);
            Assert.Null(coordinator.PendingPlan);
            Assert.False(coordinator.RequiresTimelineRebase);
        }
    }

    [Fact]
    public void DuplicateDoesNotResetProgressButSameEpochConflictFailsClosed()
    {
        var coordinator = Coordinator();
        var plan = Plan(Epoch(), OwnerPredictionBootstrapKind.PreMatch);
        coordinator.ApplyBootstrap(plan);
        Restore(coordinator, plan);
        coordinator.ObserveClock(Sample(110, PredictionClockConfidence.Stable));
        coordinator.CommitCommandTargetFrame(new SimulationInstant(116));

        Assert.Equal(
            OwnerPredictionBootstrapApplyDecision.Duplicate,
            coordinator.ApplyBootstrap(plan));
        Assert.Equal(new SimulationInstant(117), coordinator.NextCommandTargetFrame);

        var conflict = Plan(
            Epoch(), OwnerPredictionBootstrapKind.PreMatch,
            published: 101, enable: 111, lead: 6);
        Assert.Equal(
            OwnerPredictionBootstrapApplyDecision.ConflictingCurrentEpoch,
            coordinator.ApplyBootstrap(conflict));
        Assert.True(coordinator.RequiresTimelineRebase);
        Assert.Equal(plan, coordinator.CurrentPlan);
    }

    [Fact]
    public void LateStableEstimateCannotStartCommandWhoseDeadlineWasReached()
    {
        var coordinator = Coordinator();
        var plan = Plan(Epoch(), OwnerPredictionBootstrapKind.PreMatch);
        coordinator.ApplyBootstrap(plan);
        Restore(coordinator, plan);

        Assert.Equal(
            OwnerPredictionClockDecision.MissedFirstCommandDeadline,
            coordinator.ObserveClock(Sample(116, PredictionClockConfidence.Stable)));
        Assert.True(coordinator.RequiresTimelineRebase);
        Assert.Null(coordinator.NextCommandTargetFrame);
    }

    [Fact]
    public void PreMatchLateStartUsesFirstCommandTargetAsUniformDeadline()
    {
        foreach (var frame in new[] { 109L, 110L, 111L, 115L, 116L })
        {
            var coordinator = Coordinator();
            var plan = Plan(Epoch(), OwnerPredictionBootstrapKind.PreMatch);
            coordinator.ApplyBootstrap(plan);
            Restore(coordinator, plan);

            var decision = coordinator.ObserveClock(Sample(
                frame, PredictionClockConfidence.Stable));
            if (frame < plan.LocalInputEnableFrame.Tick)
            {
                Assert.Equal(OwnerPredictionClockDecision.BeforeEnableFrame, decision);
            }
            else if (frame < plan.FirstCommandTargetFrame.Tick)
            {
                Assert.Equal(OwnerPredictionClockDecision.InputEnabled, decision);
            }
            else
            {
                Assert.Equal(
                    OwnerPredictionClockDecision.MissedFirstCommandDeadline,
                    decision);
            }
        }
    }

    [Fact]
    public void SameMatchPlanAndReceiptUseFirstCommandTargetAsUniformDeadline()
    {
        foreach (var kind in new[]
        {
            OwnerPredictionBootstrapKind.Reconnect,
            OwnerPredictionBootstrapKind.Respawn,
        })
        {
            foreach (var frame in new[] { 209L, 210L, 211L, 215L, 216L })
            {
                var coordinator = ActiveCoordinator();
                var epoch = kind == OwnerPredictionBootstrapKind.Respawn
                    ? Epoch(life: 2, discontinuity: 2, control: 2)
                    : Epoch(control: 2);
                var plan = Plan(
                    epoch,
                    kind,
                    published: 200,
                    enable: 210,
                    lead: 6,
                    baseline: kind == OwnerPredictionBootstrapKind.Reconnect
                        ? 200
                        : null);
                Assert.Equal(
                    kind == OwnerPredictionBootstrapKind.Reconnect
                        ? OwnerPredictionBootstrapApplyDecision.AppliedReconnect
                        : OwnerPredictionBootstrapApplyDecision.AppliedRespawn,
                    coordinator.ApplyBootstrap(plan));
                Assert.Equal(
                    OwnerPredictionClockDecision.BaselineNotRestored,
                    coordinator.ObserveClock(Sample(
                        frame, PredictionClockConfidence.Stable)));
                Assert.Equal(
                    frame < plan.FirstCommandTargetFrame.Tick
                        ? OwnerPredictionBaselineRestoreDecision.Committed
                        : OwnerPredictionBaselineRestoreDecision.RebaseRequired,
                    coordinator.CommitStateBearingBaseline(Receipt(plan)));
            }
        }

        foreach (var kind in new[]
        {
            OwnerPredictionBootstrapKind.Reconnect,
            OwnerPredictionBootstrapKind.Respawn,
        })
        {
            foreach (var frame in new[] { 209L, 210L, 211L, 215L, 216L })
            {
                var coordinator = ActiveCoordinator();
                coordinator.ObserveClock(Sample(frame, PredictionClockConfidence.Stable));
                var epoch = kind == OwnerPredictionBootstrapKind.Respawn
                    ? Epoch(life: 2, discontinuity: 2, control: 2)
                    : Epoch(control: 2);
                var plan = Plan(
                    epoch,
                    kind,
                    published: 200,
                    enable: 210,
                    lead: 6,
                    baseline: kind == OwnerPredictionBootstrapKind.Reconnect
                        ? 200
                        : null);
                Assert.Equal(
                    frame < plan.FirstCommandTargetFrame.Tick
                        ? kind == OwnerPredictionBootstrapKind.Reconnect
                            ? OwnerPredictionBootstrapApplyDecision.AppliedReconnect
                            : OwnerPredictionBootstrapApplyDecision.AppliedRespawn
                        : OwnerPredictionBootstrapApplyDecision.ScheduleNotFutureOfKnownClock,
                    coordinator.ApplyBootstrap(plan));
            }
        }
    }

    [Fact]
    public void StableClockEvidenceNeverRegressesToEnableEarly()
    {
        var coordinator = Coordinator();
        var plan = Plan(Epoch(), OwnerPredictionBootstrapKind.PreMatch);
        coordinator.ApplyBootstrap(plan);
        Restore(coordinator, plan);
        Assert.Equal(OwnerPredictionClockDecision.BeforeEnableFrame,
            coordinator.ObserveClock(Sample(109, PredictionClockConfidence.Stable)));
        Assert.Equal(OwnerPredictionClockDecision.BeforeEnableFrame,
            coordinator.ObserveClock(Sample(50, PredictionClockConfidence.Stable)));
        Assert.Null(coordinator.NextCommandTargetFrame);
    }

    [Fact]
    public void ClockScopeCannotLeakAcrossMatchFrameEpochs()
    {
        var coordinator = ActiveCoordinator();
        Assert.Equal(
            OwnerPredictionClockDecision.Active,
            coordinator.ObserveClock(Sample(180, PredictionClockConfidence.Stable)));
        var rebase = Plan(
            Epoch(matchFrame: 2, control: 2),
            OwnerPredictionBootstrapKind.TimelineRebase,
            published: 0,
            enable: 10,
            lead: 6,
            baseline: 0);
        Assert.Equal(
            OwnerPredictionBootstrapApplyDecision.AppliedTimelineRebase,
            coordinator.ApplyBootstrap(rebase));
        Restore(coordinator, rebase);

        Assert.Equal(
            OwnerPredictionClockDecision.RejectedClockScope,
            coordinator.ObserveClock(Sample(
                500, PredictionClockConfidence.Stable, matchFrame: 1)));
        Assert.Equal(
            OwnerPredictionClockDecision.RejectedClockScope,
            coordinator.ObserveClock(Sample(
                500, PredictionClockConfidence.Lost, matchFrame: 1)));
        Assert.False(coordinator.RequiresTimelineRebase);
        Assert.Equal(
            OwnerPredictionClockDecision.NeutralPrerollIncomplete,
            coordinator.ObserveClock(Sample(
                10, PredictionClockConfidence.Stable, matchFrame: 2)));
    }

    [Fact]
    public void PendingNewEpochClockHighWaterSurvivesReceiptAndCannotEnableLate()
    {
        foreach (var frame in new[] { 9L, 10L, 15L, 16L })
        {
            var coordinator = ActiveCoordinator();
            var rebase = Plan(
                Epoch(matchFrame: 2, control: 2),
                OwnerPredictionBootstrapKind.TimelineRebase,
                published: 0,
                enable: 10,
                lead: 6,
                baseline: 0,
                planId: 2);
            coordinator.ApplyBootstrap(rebase);
            Assert.Equal(
                OwnerPredictionClockDecision.BaselineNotRestored,
                coordinator.ObserveClock(Sample(
                    frame, PredictionClockConfidence.Stable, matchFrame: 2)));

            var receipt = coordinator.CommitStateBearingBaseline(Receipt(rebase));
            if (frame >= rebase.FirstCommandTargetFrame.Tick)
            {
                Assert.Equal(
                    OwnerPredictionBaselineRestoreDecision.RebaseRequired,
                    receipt);
                Assert.True(coordinator.RequiresTimelineRebase);
                continue;
            }

            Assert.Equal(OwnerPredictionBaselineRestoreDecision.Committed, receipt);
            DrainNeutral(coordinator);
            var afterRegressedSample = coordinator.ObserveClock(Sample(
                0, PredictionClockConfidence.Stable, matchFrame: 2));
            Assert.Equal(
                frame < rebase.LocalInputEnableFrame.Tick
                    ? OwnerPredictionClockDecision.BeforeEnableFrame
                    : OwnerPredictionClockDecision.InputEnabled,
                afterRegressedSample);
        }
    }

    [Fact]
    public void PendingClockEvidenceFollowsOnlySameMatchEpochSupersession()
    {
        var sameScope = ActiveCoordinator();
        var second = Plan(
            Epoch(matchFrame: 2, control: 2),
            OwnerPredictionBootstrapKind.TimelineRebase,
            published: 0, enable: 10, lead: 6, baseline: 0,
            planId: 2);
        var thirdSameScope = Plan(
            Epoch(matchFrame: 2, control: 3),
            OwnerPredictionBootstrapKind.TimelineRebase,
            published: 0, enable: 14, lead: 6, baseline: 0,
            planId: 3);
        sameScope.ApplyBootstrap(second);
        sameScope.ObserveClock(Sample(
            15, PredictionClockConfidence.Stable, matchFrame: 2));
        sameScope.ApplyBootstrap(thirdSameScope);
        Restore(sameScope, thirdSameScope);
        DrainNeutral(sameScope);
        Assert.Equal(
            OwnerPredictionClockDecision.InputEnabled,
            sameScope.ObserveClock(Sample(
                0, PredictionClockConfidence.Stable, matchFrame: 2)));

        var changedScope = ActiveCoordinator();
        var thirdDifferentScope = Plan(
            Epoch(matchFrame: 3, control: 3),
            OwnerPredictionBootstrapKind.TimelineRebase,
            published: 0, enable: 10, lead: 6, baseline: 0,
            planId: 3);
        changedScope.ApplyBootstrap(second);
        changedScope.ObserveClock(Sample(
            15, PredictionClockConfidence.Stable, matchFrame: 2));
        changedScope.ApplyBootstrap(thirdDifferentScope);
        Restore(changedScope, thirdDifferentScope);
        DrainNeutral(changedScope);
        Assert.Equal(
            OwnerPredictionClockDecision.BeforeEnableFrame,
            changedScope.ObserveClock(Sample(
                9, PredictionClockConfidence.Stable, matchFrame: 3)));
        Assert.Equal(
            OwnerPredictionClockDecision.InputEnabled,
            changedScope.ObserveClock(Sample(
                10, PredictionClockConfidence.Stable, matchFrame: 3)));
    }

    [Fact]
    public void SameMatchRenewalsPreserveStableClockHighWater()
    {
        var coordinator = ActiveCoordinator();
        coordinator.ObserveClock(Sample(180, PredictionClockConfidence.Stable));

        var staleReconnect = Plan(
            Epoch(control: 2),
            OwnerPredictionBootstrapKind.Reconnect,
            published: 150,
            enable: 170,
            lead: 6,
            baseline: 150);
        Assert.Equal(
            OwnerPredictionBootstrapApplyDecision.ScheduleNotFutureOfKnownClock,
            coordinator.ApplyBootstrap(staleReconnect));
        Assert.True(coordinator.RequiresTimelineRebase);

        var fresh = ActiveCoordinator();
        fresh.ObserveClock(Sample(180, PredictionClockConfidence.Stable));
        var staleRespawn = Plan(
            Epoch(life: 2, discontinuity: 2, control: 2),
            OwnerPredictionBootstrapKind.Respawn,
            published: 160,
            enable: 170,
            lead: 6);
        Assert.Equal(
            OwnerPredictionBootstrapApplyDecision.ScheduleNotFutureOfKnownClock,
            fresh.ApplyBootstrap(staleRespawn));
        Assert.True(fresh.RequiresTimelineRebase);
    }

    [Fact]
    public void RepeatedClockLossIsIdempotentlySuspendedUntilValidRebase()
    {
        var coordinator = ActiveCoordinator();
        Assert.Equal(
            OwnerPredictionClockDecision.ClockConfidenceLost,
            coordinator.ObserveClock(Sample(120, PredictionClockConfidence.Lost)));
        Assert.Equal(
            OwnerPredictionClockDecision.RebaseRequired,
            coordinator.ObserveClock(Sample(121, PredictionClockConfidence.Lost)));

        var rebase = Plan(
            Epoch(control: 2),
            OwnerPredictionBootstrapKind.TimelineRebase,
            published: 200,
            enable: 210,
            lead: 6,
            baseline: 200);
        Assert.Equal(
            OwnerPredictionBootstrapApplyDecision.AppliedTimelineRebase,
            coordinator.ApplyBootstrap(rebase));
        Restore(coordinator, rebase);
        Assert.False(coordinator.RequiresTimelineRebase);
    }

    [Fact]
    public void NeutralPrerollAndLeadPolicyAreBoundedBeforeEpochMutation()
    {
        var policy = new OwnerPredictionBootstrapPolicy(
            new PredictionLeadUpdatePolicy(2, 8, 1),
            maximumNeutralPrerollFrames: 4);
        var coordinator = Coordinator(policy);
        var initial = Plan(
            Epoch(), OwnerPredictionBootstrapKind.PreMatch,
            published: 100, enable: 110, lead: 6);
        coordinator.ApplyBootstrap(initial);
        Restore(coordinator, initial);
        var current = coordinator.CurrentPlan;

        Assert.Equal(
            OwnerPredictionBootstrapApplyDecision.RejectedDifferentScope,
            coordinator.ApplyBootstrap(Plan(
                Epoch(session: 101, control: 2),
                OwnerPredictionBootstrapKind.Reconnect,
                published: 200, enable: 210, lead: 9, baseline: 200)));
        Assert.Equal(current, coordinator.CurrentPlan);
        Assert.False(coordinator.RequiresTimelineRebase);

        Assert.Equal(
            OwnerPredictionBootstrapApplyDecision.NeutralPrerollTooLarge,
            coordinator.ApplyBootstrap(Plan(
                Epoch(control: 2), OwnerPredictionBootstrapKind.Reconnect,
                published: 200, enable: 210, lead: 6, baseline: 200)));
        Assert.Equal(current, coordinator.CurrentPlan);

        var fresh = Coordinator(policy);
        Assert.Equal(
            OwnerPredictionBootstrapApplyDecision.TargetOutsidePolicy,
            fresh.ApplyBootstrap(Plan(
                Epoch(), OwnerPredictionBootstrapKind.PreMatch,
                published: 100, enable: 110, lead: 9)));
        Assert.Null(fresh.CurrentPlan);
        Assert.Equal(OwnerPredictionBootstrapState.AwaitingBootstrap, fresh.State);
        Assert.Equal(
            OwnerPredictionBootstrapApplyDecision.AppliedInitial,
            fresh.ApplyBootstrap(Plan(
                Epoch(), OwnerPredictionBootstrapKind.PreMatch,
                published: 100, enable: 110, lead: 6)));
    }

    [Fact]
    public void EnableNoticeAndNeutralPrerollBoundariesAreExact()
    {
        var noticePolicy = new OwnerPredictionBootstrapPolicy(
            new PredictionLeadUpdatePolicy(2, 8, 1),
            maximumNeutralPrerollFrames: 8,
            maximumEnableNoticeFrames: 10);
        var exactNotice = Coordinator(noticePolicy);
        Assert.Equal(
            OwnerPredictionBootstrapApplyDecision.AppliedInitial,
            exactNotice.ApplyBootstrap(Plan(
                Epoch(), OwnerPredictionBootstrapKind.PreMatch,
                published: 100, enable: 110, lead: 2)));
        var overNotice = Coordinator(noticePolicy);
        Assert.Equal(
            OwnerPredictionBootstrapApplyDecision.EnableWindowTooLarge,
            overNotice.ApplyBootstrap(Plan(
                Epoch(), OwnerPredictionBootstrapKind.PreMatch,
                published: 100, enable: 111, lead: 2)));

        var initial = Plan(
            Epoch(), OwnerPredictionBootstrapKind.PreMatch,
            published: 0, enable: 1, lead: 2);
        var exactPreroll = Coordinator(noticePolicy);
        exactPreroll.ApplyBootstrap(initial);
        Restore(exactPreroll, initial);
        var reconnectAtMax = Plan(
            Epoch(control: 2), OwnerPredictionBootstrapKind.Reconnect,
            published: 100, enable: 107, lead: 2, baseline: 100);
        Assert.Equal(8, reconnectAtMax.NeutralPrerollFrameCount);
        Assert.Equal(
            OwnerPredictionBootstrapApplyDecision.AppliedReconnect,
            exactPreroll.ApplyBootstrap(reconnectAtMax));

        var beyondPreroll = Coordinator(noticePolicy);
        beyondPreroll.ApplyBootstrap(initial);
        Restore(beyondPreroll, initial);
        var reconnectOverMax = Plan(
            Epoch(control: 2), OwnerPredictionBootstrapKind.Reconnect,
            published: 100, enable: 108, lead: 2, baseline: 100);
        Assert.Equal(9, reconnectOverMax.NeutralPrerollFrameCount);
        Assert.Equal(
            OwnerPredictionBootstrapApplyDecision.NeutralPrerollTooLarge,
            beyondPreroll.ApplyBootstrap(reconnectOverMax));
    }

    [Fact]
    public void BootstrapSeedsAbsoluteLeadRevisionBeforeIncrementalUpdates()
    {
        var coordinator = Coordinator();
        var plan = Plan(
            Epoch(), OwnerPredictionBootstrapKind.PreMatch,
            leadRevision: 10);
        coordinator.ApplyBootstrap(plan);
        Restore(coordinator, plan);
        var leadPolicy = Assert.IsType<PredictionLeadPolicySnapshot>(
            coordinator.LeadPolicy);
        var seeded = Assert.IsType<PredictionLeadUpdate>(leadPolicy.LatestAccepted);
        Assert.Equal(Revision(10), seeded.Revision);
        Assert.Equal(Lead(6), seeded.TargetLead);
        var context = new PredictionLeadSafetyContext(
            new SimulationInstant(100), null);

        Assert.Equal(
            PredictionLeadUpdateDecision.IgnoredStaleRevision,
            coordinator.ObserveLeadUpdate(new PredictionLeadUpdate(
                plan.Scope, Lead(5), Revision(9), new SimulationInstant(110)), context));
        Assert.Equal(
            PredictionLeadUpdateDecision.IdempotentDuplicate,
            coordinator.ObserveLeadUpdate(seeded, context));
        Assert.Equal(
            PredictionLeadUpdateDecision.Applied,
            coordinator.ObserveLeadUpdate(new PredictionLeadUpdate(
                plan.Scope, Lead(7), Revision(11), new SimulationInstant(111)), context));

        var conflictingCoordinator = Coordinator();
        conflictingCoordinator.ApplyBootstrap(plan);
        Restore(conflictingCoordinator, plan);
        Assert.Equal(
            PredictionLeadUpdateDecision.ConflictingRevision,
            conflictingCoordinator.ObserveLeadUpdate(new PredictionLeadUpdate(
                plan.Scope, Lead(7), Revision(10), new SimulationInstant(110)), context));
        Assert.True(conflictingCoordinator.LeadPolicy!.Value.RequiresTimelineRebase);
    }

    [Fact]
    public void WrongCommandFramesOnEitherSideNeverAdvanceCursor()
    {
        var coordinator = ActiveCoordinator();
        Assert.Equal(new SimulationInstant(116), coordinator.NextCommandTargetFrame);
        Assert.Equal(
            OwnerPredictionCommandFrameDecision.WrongFrame,
            coordinator.CommitCommandTargetFrame(new SimulationInstant(115)));
        Assert.Equal(
            OwnerPredictionCommandFrameDecision.WrongFrame,
            coordinator.CommitCommandTargetFrame(new SimulationInstant(117)));
        Assert.Equal(new SimulationInstant(116), coordinator.NextCommandTargetFrame);
    }

    [Fact]
    public void RejectedReorderedOperationsPreserveWholeAggregateState()
    {
        var coordinator = ActiveCoordinator();
        var active = Capture(coordinator);
        Assert.Equal(
            OwnerPredictionBootstrapApplyDecision.RejectedDifferentScope,
            coordinator.ApplyBootstrap(Plan(
                Epoch(session: 101, control: 2),
                OwnerPredictionBootstrapKind.Reconnect,
                published: 200, enable: 210, lead: 6, baseline: 200,
                planId: 2)));
        Assert.Equal(active, Capture(coordinator));
        Assert.Equal(
            OwnerPredictionClockDecision.RejectedClockScope,
            coordinator.ObserveClock(Sample(
                1_000, PredictionClockConfidence.Lost, session: 101)));
        Assert.Equal(active, Capture(coordinator));
        Assert.Equal(
            OwnerPredictionCommandFrameDecision.WrongFrame,
            coordinator.CommitCommandTargetFrame(new SimulationInstant(115)));
        Assert.Equal(active, Capture(coordinator));

        var reconnect = Plan(
            Epoch(control: 2), OwnerPredictionBootstrapKind.Reconnect,
            published: 200, enable: 210, lead: 6, baseline: 200,
            planId: 2, leadRevision: 2);
        coordinator.ApplyBootstrap(reconnect);
        var pending = Capture(coordinator);
        Assert.Equal(
            OwnerPredictionBaselineRestoreDecision.WrongPlan,
            coordinator.CommitStateBearingBaseline(new OwnerPredictionBaselineReceipt(
                new PredictionBootstrapPlanId(3),
                reconnect.AuthorityEpoch,
                reconnect.BaselineFrame,
                ClientPredictionStateBaselineSource.Snapshot)));
        Assert.Equal(pending, Capture(coordinator));
        Assert.Equal(
            OwnerPredictionBootstrapApplyDecision.RejectedPlanIdRegression,
            coordinator.ApplyBootstrap(Plan(
                Epoch(control: 3), OwnerPredictionBootstrapKind.Reconnect,
                published: 220, enable: 230, lead: 6, baseline: 220,
                planId: 1)));
        Assert.Equal(pending, Capture(coordinator));
    }

    [Fact]
    public void MaximumTargetCommitsOnceThenRequiresExplicitTimelineRebase()
    {
        var lead = 2;
        var enable = long.MaxValue - lead;
        var coordinator = Coordinator();
        coordinator.ApplyBootstrap(Plan(
            Epoch(), OwnerPredictionBootstrapKind.PreMatch,
            published: enable - 1, enable: enable, lead: lead));
        var plan = coordinator.PendingPlan!.Value;
        coordinator.CommitStateBearingBaseline(Receipt(plan));
        Assert.Equal(
            OwnerPredictionClockDecision.InputEnabled,
            coordinator.ObserveClock(Sample(enable, PredictionClockConfidence.Stable)));
        Assert.Equal(new SimulationInstant(long.MaxValue),
            coordinator.NextCommandTargetFrame);
        Assert.Equal(
            OwnerPredictionCommandFrameDecision.CommittedTimelineExhausted,
            coordinator.CommitCommandTargetFrame(new SimulationInstant(long.MaxValue)));
        Assert.True(coordinator.RequiresTimelineRebase);
    }

    [Fact]
    public void WarmedBootstrapClockSeedAndCommandPathsAllocateNothing()
    {
        var coordinator = ActiveCoordinator();
        for (ulong control = 2; control <= 2_001; control++)
        {
            ExerciseRenewal(coordinator, control);
        }
        var minimumAllocated = long.MaxValue;
        ulong nextControl = 2_002;
        for (var sample = 0; sample < 4; sample++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var finalControl = nextControl + 999;
            for (; nextControl <= finalControl; nextControl++)
            {
                ExerciseRenewal(coordinator, nextControl);
            }
            minimumAllocated = Math.Min(
                minimumAllocated,
                GC.GetAllocatedBytesForCurrentThread() - before);
        }
        // Tiered JIT/event-source initialization can add a one-time allocation
        // when this runs amid the full parallel suite. A real hot-path
        // allocation appears in every retained sample; steady state must be 0.
        Assert.Equal(0, minimumAllocated);
    }

    private static void ExerciseRenewal(
        OwnerPredictionBootstrapCoordinator coordinator,
        ulong control)
    {
        var published = checked(100 + (long)control * 10);
        coordinator.ApplyBootstrap(Plan(
            Epoch(control: control),
            OwnerPredictionBootstrapKind.Reconnect,
            published: published,
            enable: published + 1,
            lead: 2,
            baseline: published));
        var plan = coordinator.PendingPlan!.Value;
        coordinator.CommitStateBearingBaseline(Receipt(plan));
        while (coordinator.TryGetNextNeutralFrame(out var frame))
        {
            coordinator.CommitNeutralFrame(frame);
        }
        coordinator.ObserveClock(Sample(
            published + 1,
            PredictionClockConfidence.Stable));
        coordinator.CommitCommandTargetFrame(new SimulationInstant(published + 3));
    }

    private static OwnerPredictionBootstrapCoordinator ActiveCoordinator()
    {
        var coordinator = Coordinator();
        var plan = Plan(Epoch(), OwnerPredictionBootstrapKind.PreMatch);
        coordinator.ApplyBootstrap(plan);
        Restore(coordinator, plan);
        coordinator.ObserveClock(Sample(110, PredictionClockConfidence.Stable));
        return coordinator;
    }

    private static void Restore(
        OwnerPredictionBootstrapCoordinator coordinator,
        OwnerPredictionBootstrapPlan plan)
    {
        Assert.Equal(
            OwnerPredictionBaselineRestoreDecision.Committed,
            coordinator.CommitStateBearingBaseline(Receipt(plan)));
    }

    private static void DrainNeutral(
        OwnerPredictionBootstrapCoordinator coordinator)
    {
        while (coordinator.TryGetNextNeutralFrame(out var frame))
        {
            Assert.Equal(
                OwnerPredictionNeutralFrameDecision.Committed,
                coordinator.CommitNeutralFrame(frame));
        }
    }

    private static OwnerPredictionBootstrapCoordinator Coordinator(
        OwnerPredictionBootstrapPolicy? policy = null,
        Action<ClientPredictionEpochTransition>? resetSink = null)
    {
        var router = new ClientPredictionLifecycleRouter(
            100,
            resetSink ?? (_ => { }));
        return policy is { } configured
            ? new OwnerPredictionBootstrapCoordinator(
                router, 100, new CombatantId(7), configured)
            : new OwnerPredictionBootstrapCoordinator(
                router, 100, new CombatantId(7));
    }

    private static OwnerPredictionBaselineReceipt Receipt(
        OwnerPredictionBootstrapPlan plan) => new(
            plan.PlanId,
            plan.AuthorityEpoch,
            plan.BaselineFrame,
            plan.Kind is OwnerPredictionBootstrapKind.PreMatch or
                OwnerPredictionBootstrapKind.Respawn
                    ? ClientPredictionStateBaselineSource.Spawn
                    : ClientPredictionStateBaselineSource.Snapshot);

    private static OwnerPredictionBootstrapPlan Plan(
        CombatantAuthorityPredictionEpoch epoch,
        OwnerPredictionBootstrapKind kind,
        long published = 100,
        long enable = 110,
        int lead = 6,
        long? baseline = null,
        ulong? planId = null,
        ulong leadRevision = 1)
    {
        var preparation = kind is OwnerPredictionBootstrapKind.PreMatch or
            OwnerPredictionBootstrapKind.Respawn
                ? OwnerPredictionBaselinePreparation.FrozenCommandPredecessor
                : OwnerPredictionBaselinePreparation.NeutralPreroll;
        var target = checked(enable + lead);
        var baselineFrame = baseline ?? (preparation ==
            OwnerPredictionBaselinePreparation.FrozenCommandPredecessor
                ? target - 1
                : published);
        return new OwnerPredictionBootstrapPlan(
            new PredictionBootstrapPlanId(planId ?? epoch.OwnerControl.Value),
            epoch,
            kind,
            preparation,
            new SimulationInstant(published),
            new SimulationInstant(baselineFrame),
            new SimulationInstant(enable),
            new SimulationInstant(target),
            Lead(lead),
            Revision(leadRevision));
    }

    private static CombatantAuthorityPredictionEpoch Epoch(
        ulong session = 100,
        ulong matchFrame = 1,
        long combatant = 7,
        long life = 1,
        ulong discontinuity = 1,
        ulong control = 1) => new(
            session,
            new MatchFrameEpochId(matchFrame),
            new CombatantId(combatant),
            new LifeGenerationId(life),
            new AuthorityDiscontinuityId(discontinuity),
            new OwnerControlEpoch(control));

    private static PredictionLeadFrameCount Lead(int value) => new(value);
    private static PredictionLeadPolicyRevision Revision(ulong value) => new(value);
    private static SynchronizedAuthorityFrameSample Sample(
        long frame,
        PredictionClockConfidence confidence,
        ulong session = 100,
        ulong matchFrame = 1) => new(
            session,
            new MatchFrameEpochId(matchFrame),
            new SimulationInstant(frame),
            confidence);

    private static AggregateSnapshot Capture(
        OwnerPredictionBootstrapCoordinator coordinator) => new(
            coordinator.CurrentPlan,
            coordinator.PendingPlan,
            coordinator.CommittedEpoch,
            coordinator.LeadPolicy,
            coordinator.State,
            coordinator.NextCommandTargetFrame,
            coordinator.NeutralFramesRemaining,
            coordinator.RequiresTimelineRebase);

    private readonly record struct AggregateSnapshot(
        OwnerPredictionBootstrapPlan? CurrentPlan,
        OwnerPredictionBootstrapPlan? PendingPlan,
        CombatantLocalPredictionEpoch? CommittedEpoch,
        PredictionLeadPolicySnapshot? LeadPolicy,
        OwnerPredictionBootstrapState State,
        SimulationInstant? NextCommandTargetFrame,
        long NeutralFramesRemaining,
        bool RequiresTimelineRebase);
}
