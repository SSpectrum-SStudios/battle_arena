using BattleArena.Core.Common;
using BattleArena.Core.Movement.Simulation;
using BattleArena.Multiplayer.OwnerPrediction;

namespace BattleArena.Multiplayer.Tests.OwnerPrediction;

public sealed class PredictionLeadUpdateTests
{
    [Fact]
    public void TypedLeadRevisionAndPolicyBoundsFailClosed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PredictionLeadFrameCount(0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PredictionLeadFrameCount(PredictionLeadLimits.MaximumSupportedFrames + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PredictionLeadPolicyRevision(0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PredictionLeadUpdatePolicy(0, 4, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PredictionLeadUpdatePolicy(4, 3, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PredictionLeadUpdatePolicy(2, 4, 0));

        Assert.Throws<InvalidOperationException>(() =>
            new PredictionLeadPolicyRevision(ulong.MaxValue).Next());
        Assert.Throws<InvalidOperationException>(() =>
            default(PredictionLeadPolicyRevision).Next());
        Assert.Throws<InvalidOperationException>(() =>
            default(PredictionLeadPolicyRevision).CompareTo(
                PredictionLeadPolicyRevision.Initial));
        Assert.Throws<InvalidOperationException>(() =>
            PredictionLeadPolicyRevision.Initial.CompareTo(default));
        Assert.Throws<InvalidOperationException>(() =>
            default(PredictionLeadFrameCount).CompareTo(new PredictionLeadFrameCount(2)));
        Assert.Throws<InvalidOperationException>(() =>
            new PredictionLeadFrameCount(2).CompareTo(default));
        Assert.True(PredictionLeadUpdatePolicy.Default.IsValid);
        Assert.Equal(2, PredictionLeadUpdatePolicy.Default.MinimumLeadFrames);
        Assert.Equal(48, PredictionLeadUpdatePolicy.Default.MaximumLeadFrames);
    }

    [Fact]
    public void SafeUpdateUsesAbsoluteTargetAtFirstFrameBeyondBothFrontiers()
    {
        var scope = Scope();
        var gate = new PredictionLeadUpdateGate(
            scope,
            new PredictionLeadUpdatePolicy(2, 24, 1));
        var update = Update(scope, revision: 1, target: 7, effective: 106);

        Assert.Equal(
            PredictionLeadUpdateDecision.Applied,
            gate.Observe(update, Context(observed: 100, scheduled: 105)));
        Assert.Equal(update, gate.LatestAccepted);
        Assert.Equal(7, gate.LatestAccepted!.Value.TargetLead.Value);
        Assert.False(gate.RequiresTimelineRebase);
    }

    [Fact]
    public void DuplicateRemainsIdempotentAfterItsEffectiveFrameHasPassed()
    {
        var scope = Scope();
        var gate = new PredictionLeadUpdateGate(scope);
        var update = Update(scope, revision: 4, target: 8, effective: 20);
        Assert.Equal(PredictionLeadUpdateDecision.Applied,
            gate.Observe(update, Context(10, 15)));

        Assert.Equal(
            PredictionLeadUpdateDecision.IdempotentDuplicate,
            gate.Observe(update, Context(40, 40)));
        Assert.Equal(update, gate.LatestAccepted);
        Assert.False(gate.RequiresTimelineRebase);
    }

    [Fact]
    public void OlderAbsoluteRevisionIsIgnoredEvenWhenItsFrameIsNowUnsafe()
    {
        var scope = Scope();
        var gate = new PredictionLeadUpdateGate(scope);
        var latest = Update(scope, revision: 5, target: 9, effective: 30);
        Assert.Equal(PredictionLeadUpdateDecision.Applied,
            gate.Observe(latest, Context(10, 20)));

        Assert.Equal(
            PredictionLeadUpdateDecision.IgnoredStaleRevision,
            gate.Observe(
                Update(scope, revision: 4, target: 2, effective: 1),
                Context(100, 100)));
        Assert.Equal(latest, gate.LatestAccepted);
        Assert.False(gate.RequiresTimelineRebase);
    }

    [Theory]
    [InlineData(7, 31)]
    [InlineData(8, 30)]
    public void SameRevisionDifferentAbsolutePayloadRequiresTimelineRebase(
        int target,
        long effective)
    {
        var scope = Scope();
        var gate = new PredictionLeadUpdateGate(scope);
        var accepted = Update(scope, revision: 2, target: 7, effective: 30);
        gate.Observe(accepted, Context(10, 20));

        Assert.Equal(
            PredictionLeadUpdateDecision.ConflictingRevision,
            gate.Observe(
                Update(scope, revision: 2, target, effective),
                Context(10, 20)));
        Assert.True(gate.RequiresTimelineRebase);
        Assert.Equal(accepted, gate.LatestAccepted);
        Assert.Equal(
            PredictionLeadUpdateDecision.TimelineRebaseRequired,
            gate.Observe(Update(scope, 3, 8, 40), Context(10, 20)));
    }

    [Theory]
    [InlineData(100L, null, 100L)]
    [InlineData(100L, null, 99L)]
    [InlineData(100L, 110L, 110L)]
    [InlineData(100L, 110L, 105L)]
    public void NonFutureOrAlreadyScheduledEffectiveFrameRequiresRebase(
        long observed,
        long? scheduled,
        long effective)
    {
        var scope = Scope();
        var gate = new PredictionLeadUpdateGate(scope);

        Assert.Equal(
            PredictionLeadUpdateDecision.UnsafeEffectiveFrame,
            gate.Observe(
                Update(scope, 1, 5, effective),
                Context(observed, scheduled)));
        Assert.True(gate.RequiresTimelineRebase);
        Assert.Null(gate.LatestAccepted);
    }

    [Fact]
    public void NoticePolicyAndFrameOverflowRejectUnsafeScheduleDeterministically()
    {
        var scope = Scope();
        var policy = new PredictionLeadUpdatePolicy(2, 24, 3);
        var tooSoon = new PredictionLeadUpdateGate(scope, policy);
        Assert.Equal(
            PredictionLeadUpdateDecision.UnsafeEffectiveFrame,
            tooSoon.Observe(Update(scope, 1, 4, 102), Context(100, null)));

        var exhausted = new PredictionLeadUpdateGate(scope, policy);
        Assert.Equal(
            PredictionLeadUpdateDecision.UnsafeEffectiveFrame,
            exhausted.Observe(
                Update(scope, 1, 4, long.MaxValue),
                Context(long.MaxValue - 1, null)));

        var scheduledExhausted = new PredictionLeadUpdateGate(scope, policy);
        Assert.Equal(
            PredictionLeadUpdateDecision.UnsafeEffectiveFrame,
            scheduledExhausted.Observe(
                Update(scope, 1, 4, long.MaxValue),
                Context(10, long.MaxValue)));
    }

    [Fact]
    public void ExactNoticeBoundaryIsAcceptedAndOneFrameBeforeIsRejected()
    {
        var scope = Scope();
        var policy = new PredictionLeadUpdatePolicy(2, 24, 3);
        var exact = new PredictionLeadUpdateGate(scope, policy);
        Assert.Equal(
            PredictionLeadUpdateDecision.Applied,
            exact.Observe(Update(scope, 1, 4, 103), Context(100, null)));

        var before = new PredictionLeadUpdateGate(scope, policy);
        Assert.Equal(
            PredictionLeadUpdateDecision.UnsafeEffectiveFrame,
            before.Observe(Update(scope, 1, 4, 102), Context(100, null)));
    }

    [Fact]
    public void TargetOutsideNegotiatedBoundsRequiresRebaseWithoutMutation()
    {
        var scope = Scope();
        var gate = new PredictionLeadUpdateGate(
            scope,
            new PredictionLeadUpdatePolicy(2, 24, 1));

        Assert.Equal(
            PredictionLeadUpdateDecision.TargetOutsidePolicy,
            gate.Observe(Update(scope, 1, 25, 20), Context(10, 15)));
        Assert.True(gate.RequiresTimelineRebase);
        Assert.Null(gate.LatestAccepted);
    }

    [Fact]
    public void RejectedNewerEvidencePreservesPreviouslyAcceptedAbsolutePolicy()
    {
        var scope = Scope();
        var gate = new PredictionLeadUpdateGate(
            scope,
            new PredictionLeadUpdatePolicy(2, 24, 1));
        var accepted = Update(scope, 2, 8, 30);
        Assert.Equal(PredictionLeadUpdateDecision.Applied,
            gate.Observe(accepted, Context(10, 20)));

        Assert.Equal(
            PredictionLeadUpdateDecision.UnsafeEffectiveFrame,
            gate.Observe(Update(scope, 3, 9, 20), Context(15, 20)));
        Assert.Equal(accepted, gate.LatestAccepted);
        Assert.True(gate.RequiresTimelineRebase);
    }

    [Fact]
    public void RevisionGapIsSafeBecauseEveryUpdateCarriesAnAbsoluteTarget()
    {
        var scope = Scope();
        var gate = new PredictionLeadUpdateGate(scope);
        var first = Update(scope, 2, 5, 20);
        var replacement = Update(scope, 99, 11, 25);

        Assert.Equal(PredictionLeadUpdateDecision.Applied,
            gate.Observe(first, Context(10, 15)));
        Assert.Equal(PredictionLeadUpdateDecision.Applied,
            gate.Observe(replacement, Context(10, 20)));
        Assert.Equal(replacement, gate.LatestAccepted);
        Assert.Equal(11, gate.LatestAccepted!.Value.TargetLead.Value);
    }

    [Fact]
    public void WrongScopeCannotChangeOrLockCurrentPolicy()
    {
        var scope = Scope();
        var other = Scope(control: 2);
        var gate = new PredictionLeadUpdateGate(scope);

        Assert.Equal(
            PredictionLeadUpdateDecision.WrongScope,
            gate.Observe(Update(other, 1, 4, 20), Context(10, 15)));
        Assert.Null(gate.LatestAccepted);
        Assert.False(gate.RequiresTimelineRebase);
    }

    [Fact]
    public void InvalidAuthenticatedEvidenceFailsClosed()
    {
        var gate = new PredictionLeadUpdateGate(Scope());

        Assert.Equal(
            PredictionLeadUpdateDecision.InvalidUpdate,
            gate.Observe(default, Context(10, 15)));
        Assert.True(gate.RequiresTimelineRebase);
    }

    [Fact]
    public void ForwardControlAndRespawnBootstrapClearStateExactlyOnce()
    {
        var oldScope = Scope();
        var newScope = Scope(control: 2);
        var respawnScope = Scope(life: 4, control: 3);
        var gate = new PredictionLeadUpdateGate(oldScope);
        gate.Observe(Update(oldScope, 1, 4, 20), Context(10, 15));
        gate.Observe(Update(oldScope, 1, 5, 20), Context(10, 15));
        Assert.True(gate.RequiresTimelineRebase);

        Assert.Equal(
            PredictionLeadBootstrapDecision.Duplicate,
            gate.ApplyAuthorityBootstrapScope(oldScope));
        Assert.True(gate.RequiresTimelineRebase);
        Assert.Equal(
            PredictionLeadBootstrapDecision.AppliedNewControl,
            gate.ApplyAuthorityBootstrapScope(newScope));
        Assert.False(gate.RequiresTimelineRebase);
        Assert.Null(gate.LatestAccepted);
        Assert.Equal(newScope, gate.Scope);
        Assert.Equal(
            PredictionLeadUpdateDecision.Applied,
            gate.Observe(Update(newScope, 1, 6, 30), Context(20, 25)));

        var accepted = gate.LatestAccepted;
        gate.Observe(Update(newScope, 1, 7, 30), Context(20, 25));
        Assert.True(gate.RequiresTimelineRebase);
        Assert.Equal(
            PredictionLeadBootstrapDecision.RejectedStaleControl,
            gate.ApplyAuthorityBootstrapScope(Scope(life: 4, control: 2)));
        Assert.Equal(
            PredictionLeadBootstrapDecision.RejectedStaleControl,
            gate.ApplyAuthorityBootstrapScope(Scope(life: 4, control: 1)));
        Assert.Equal(newScope, gate.Scope);
        Assert.Equal(accepted, gate.LatestAccepted);
        Assert.True(gate.RequiresTimelineRebase);

        Assert.Equal(
            PredictionLeadBootstrapDecision.AppliedNewLife,
            gate.ApplyAuthorityBootstrapScope(respawnScope));
        Assert.Null(gate.LatestAccepted);
        Assert.Equal(respawnScope, gate.Scope);
        Assert.Equal(
            PredictionLeadBootstrapDecision.Duplicate,
            gate.ApplyAuthorityBootstrapScope(respawnScope));
    }

    [Fact]
    public void DelayedOrForeignBootstrapCannotRollBackOrRepurposeGate()
    {
        var initial = Scope();
        var current = Scope(control: 2);
        var gate = new PredictionLeadUpdateGate(initial);
        Assert.Equal(
            PredictionLeadBootstrapDecision.AppliedNewControl,
            gate.ApplyAuthorityBootstrapScope(current));
        var accepted = Update(current, 1, 6, 30);
        gate.Observe(accepted, Context(20, 25));
        gate.Observe(Update(current, 1, 7, 30), Context(20, 25));
        Assert.True(gate.RequiresTimelineRebase);

        Assert.Equal(
            PredictionLeadBootstrapDecision.RejectedStaleControl,
            gate.ApplyAuthorityBootstrapScope(initial));
        Assert.Equal(
            PredictionLeadBootstrapDecision.RejectedStaleLife,
            gate.ApplyAuthorityBootstrapScope(Scope(life: 2, control: 99)));
        Assert.Equal(
            PredictionLeadBootstrapDecision.RejectedDifferentOwner,
            gate.ApplyAuthorityBootstrapScope(Scope(session: 101, control: 3)));
        Assert.Equal(
            PredictionLeadBootstrapDecision.RejectedDifferentOwner,
            gate.ApplyAuthorityBootstrapScope(Scope(combatant: 8, control: 3)));

        Assert.Equal(current, gate.Scope);
        Assert.Equal(accepted, gate.LatestAccepted);
        Assert.True(gate.RequiresTimelineRebase);
    }

    [Fact]
    public void BootstrapPreviewIsPureForEveryDecisionAndApplyConflictLocks()
    {
        var scope = Scope();
        var baseline = Update(scope, 1, 4, 20);
        AssertPurePreview(
            new PredictionLeadUpdateGate(scope),
            baseline,
            PredictionLeadBootstrapBaselineDecision.SeededCurrentScope);

        var duplicate = new PredictionLeadUpdateGate(scope);
        duplicate.ApplyAuthorityBootstrapBaseline(baseline);
        AssertPurePreview(
            duplicate,
            baseline,
            PredictionLeadBootstrapBaselineDecision.IdempotentDuplicate);

        var conflict = new PredictionLeadUpdateGate(scope);
        conflict.ApplyAuthorityBootstrapBaseline(baseline);
        var conflicting = Update(scope, 1, 5, 20);
        AssertPurePreview(
            conflict,
            conflicting,
            PredictionLeadBootstrapBaselineDecision.ConflictingCurrentScope);
        Assert.False(conflict.RequiresTimelineRebase);
        Assert.Equal(
            PredictionLeadBootstrapBaselineDecision.ConflictingCurrentScope,
            conflict.ApplyAuthorityBootstrapBaseline(conflicting));
        Assert.True(conflict.RequiresTimelineRebase);
        Assert.Equal(baseline, conflict.LatestAccepted);

        AssertPurePreview(
            new PredictionLeadUpdateGate(scope),
            Update(Scope(control: 2), 1, 4, 20),
            PredictionLeadBootstrapBaselineDecision.SeededNewControl);
        AssertPurePreview(
            new PredictionLeadUpdateGate(scope),
            Update(Scope(control: 2, life: 4), 1, 4, 20),
            PredictionLeadBootstrapBaselineDecision.SeededNewLife);
        AssertPurePreview(
            new PredictionLeadUpdateGate(scope),
            Update(Scope(session: 101), 1, 4, 20),
            PredictionLeadBootstrapBaselineDecision.RejectedDifferentOwner);
        AssertPurePreview(
            new PredictionLeadUpdateGate(scope),
            Update(Scope(life: 2, control: 99), 1, 4, 20),
            PredictionLeadBootstrapBaselineDecision.RejectedStaleLife);
        AssertPurePreview(
            new PredictionLeadUpdateGate(Scope(control: 2)),
            Update(scope, 1, 4, 20),
            PredictionLeadBootstrapBaselineDecision.RejectedStaleControl);
        AssertPurePreview(
            new PredictionLeadUpdateGate(
                scope,
                new PredictionLeadUpdatePolicy(2, 4, 1)),
            Update(scope, 1, 5, 20),
            PredictionLeadBootstrapBaselineDecision.TargetOutsidePolicy);
    }

    [Fact]
    public void WarmedObserveAndBootstrapRenewalPathsAllocateNothing()
    {
        var gate = new PredictionLeadUpdateGate(Scope());
        for (ulong control = 2; control <= 2_001; control++)
        {
            var scope = Scope(control: control);
            gate.ApplyAuthorityBootstrapScope(scope);
            gate.Observe(Update(scope, 1, 4, 20), Context(10, 15));
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (ulong control = 2_002; control <= 3_001; control++)
        {
            var scope = Scope(control: control);
            gate.ApplyAuthorityBootstrapScope(scope);
            gate.Observe(Update(scope, 1, 4, 20), Context(10, 15));
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static PredictionLeadUpdate Update(
        OwnerIntentScope scope,
        ulong revision,
        int target,
        long effective) => new(
            scope,
            new PredictionLeadFrameCount(target),
            new PredictionLeadPolicyRevision(revision),
            new SimulationInstant(effective));

    private static PredictionLeadSafetyContext Context(
        long observed,
        long? scheduled) => new(
            new SimulationInstant(observed),
            scheduled is null ? null : new SimulationInstant(scheduled.Value));

    private static void AssertPurePreview(
        PredictionLeadUpdateGate gate,
        PredictionLeadUpdate baseline,
        PredictionLeadBootstrapBaselineDecision expected)
    {
        var before = (gate.Scope, gate.LatestAccepted, gate.RequiresTimelineRebase);
        Assert.Equal(expected, gate.PreviewAuthorityBootstrapBaseline(baseline));
        Assert.Equal(before,
            (gate.Scope, gate.LatestAccepted, gate.RequiresTimelineRebase));
    }

    private static OwnerIntentScope Scope(
        ulong control = 1,
        long session = 100,
        long combatant = 7,
        long life = 3) => new(
        checked((ulong)session),
        new LifeEpoch(new CombatantId(combatant), new LifeGenerationId(life)),
        new OwnerControlEpoch(control));
}
