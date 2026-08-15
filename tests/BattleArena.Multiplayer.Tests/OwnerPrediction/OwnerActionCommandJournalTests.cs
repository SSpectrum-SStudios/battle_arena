using BattleArena.Core.Common;
using BattleArena.Core.Movement.Simulation;
using BattleArena.Multiplayer.OwnerPrediction;

namespace BattleArena.Multiplayer.Tests.OwnerPrediction;

public sealed class OwnerActionCommandJournalTests
{
    [Fact]
    public void UnresolvedActionRepeatsExactlyAcrossLossAndEveryTriggerIsRepresentable()
    {
        Span<PredictedActionIntent> first = stackalloc PredictedActionIntent[1];
        Span<PredictedActionIntent> retry = stackalloc PredictedActionIntent[1];
        foreach (var trigger in Enum.GetValues<OwnerActionTrigger>())
        {
            var scope = Scope();
            var journal = new OwnerActionCommandJournal(scope);
            Assert.Equal(
                OwnerActionOriginDecision.Added,
                journal.TryOriginate(
                    Input(scope, 1),
                    trigger,
                    new SimulationInstant(10),
                    new SimulationInstant(15),
                    new SimulationInstant(8),
                    out var intent));
            Assert.Equal(1, journal.CopyOutstanding(first));
            Assert.Equal(1, journal.CopyOutstanding(retry));
            Assert.Equal(intent, first[0]);
            Assert.Equal(first[0], retry[0]);
            Assert.Equal(trigger, intent.Trigger);
            Assert.Equal(new SimulationInstant(8), intent.RenderedAuthorityFrame);
        }
    }

    [Fact]
    public void BoundedPacketsRotateAndRemovalDoesNotStarveSurvivors()
    {
        var scope = Scope();
        var journal = new OwnerActionCommandJournal(scope);
        journal.TryOriginate(Input(scope, 1), OwnerActionTrigger.Attack,
            new SimulationInstant(10), new SimulationInstant(15),
            new SimulationInstant(8), out var first);
        journal.TryOriginate(Input(scope, 2), OwnerActionTrigger.ActivateWeapon,
            new SimulationInstant(11), new SimulationInstant(16),
            new SimulationInstant(8), out var second);
        journal.TryOriginate(Input(scope, 3), OwnerActionTrigger.ActivateHelmet,
            new SimulationInstant(12), new SimulationInstant(17),
            new SimulationInstant(8), out var third);
        Span<PredictedActionIntent> packet = stackalloc PredictedActionIntent[2];

        Assert.Equal(2, journal.CopyOutstanding(packet));
        Assert.Equal(first, packet[0]);
        Assert.Equal(second, packet[1]);
        Assert.Equal(2, journal.CopyOutstanding(packet));
        Assert.Equal(third, packet[0]);
        Assert.Equal(first, packet[1]);

        var terminal = Resolution(
            scope, 1, second.Identity, PredictedActionOutcome.Rejected,
            default, PredictedActionRejectionReason.InvalidState,
            second.PredictedStartFrame);
        Assert.Equal(
            ClientActionResolutionDecision.Applied,
            journal.ApplyResolution(terminal, out _));
        Assert.Equal(2, journal.CopyOutstanding(packet));
        Assert.Contains(first, packet.ToArray());
        Assert.Contains(third, packet.ToArray());
    }

    [Fact]
    public void AuthorityDeduplicatesReorderedConflictingAndRetiredActionIds()
    {
        var scope = Scope();
        var authority = new AuthorityOwnerActionCommandJournal(scope);
        var first = Intent(scope, 1, OwnerActionTrigger.Attack, 10, 15, 8);
        var second = Intent(scope, 2, OwnerActionTrigger.ActivateWeapon, 11, 16, 8);
        var conflict = Intent(scope, 1, OwnerActionTrigger.Block, 10, 15, 8);

        Assert.Equal(AuthorityActionObserveDecision.FirstSeen, authority.Observe(second));
        Assert.Equal(AuthorityActionObserveDecision.Duplicate, authority.Observe(second));
        Assert.Equal(AuthorityActionObserveDecision.FirstSeen, authority.Observe(first));
        Assert.Equal(
            AuthorityActionObserveDecision.ConflictingDuplicate,
            authority.Observe(conflict));
        var result = AssertResolution(
            authority, first, PredictedActionOutcome.Accepted,
            first.PredictedStartFrame, PredictedActionRejectionReason.None);
        Assert.True(authority.AcknowledgeResolutions(result.ResolutionIdentity));
        Assert.Equal(AuthorityActionObserveDecision.Duplicate, authority.Observe(first));
        Assert.Equal(1, authority.PendingCount);
    }

    [Fact]
    public void ClientAppliesReorderedResultsAndAdvancesOnlyContiguousCursor()
    {
        var scope = Scope();
        var client = new OwnerActionCommandJournal(scope);
        client.TryOriginate(Input(scope, 1), OwnerActionTrigger.Attack,
            new SimulationInstant(10), new SimulationInstant(15),
            new SimulationInstant(8), out var first);
        client.TryOriginate(Input(scope, 2), OwnerActionTrigger.ActivateWeapon,
            new SimulationInstant(11), new SimulationInstant(16),
            new SimulationInstant(8), out var second);
        var one = Resolution(
            scope, 1, first.Identity, PredictedActionOutcome.Accepted,
            first.PredictedStartFrame, executionId: 21);
        var two = Resolution(
            scope, 2, second.Identity, PredictedActionOutcome.Rejected,
            default, PredictedActionRejectionReason.CooldownActive,
            second.PredictedStartFrame);

        Assert.Equal(
            ClientActionResolutionDecision.Applied,
            client.ApplyResolution(two, out var resolvedSecond));
        Assert.Equal(second, resolvedSecond);
        Assert.Null(client.AppliedResolutionCursor);
        Assert.Equal(
            ClientActionResolutionDecision.Duplicate,
            client.ApplyResolution(two, out _));
        Assert.Equal(
            ClientActionResolutionDecision.Applied,
            client.ApplyResolution(one, out var resolvedFirst));
        Assert.Equal(first, resolvedFirst);
        Assert.Equal(
            new ActionResolutionIdentity(scope, new ActionResolutionSequence(2)),
            client.AppliedResolutionCursor);
        Assert.Equal(0, client.OutstandingCount);
    }

    [Fact]
    public void ClientRejectsImpossibleTerminalFramesWithoutMutation()
    {
        foreach (var impossible in new[]
                 {
                     (PredictedActionOutcome.Accepted, new SimulationInstant(9),
                         new SimulationInstant(9)),
                     (PredictedActionOutcome.Remapped, new SimulationInstant(10),
                         new SimulationInstant(10)),
                     (PredictedActionOutcome.Remapped, new SimulationInstant(16),
                         new SimulationInstant(16)),
                     (PredictedActionOutcome.Rejected, SimulationInstant.Zero,
                         new SimulationInstant(16)),
                     (PredictedActionOutcome.Expired, SimulationInstant.Zero,
                         new SimulationInstant(15)),
                     (PredictedActionOutcome.Superseded, SimulationInstant.Zero,
                         new SimulationInstant(9)),
                 })
        {
            var scope = Scope();
            var client = new OwnerActionCommandJournal(scope);
            client.TryOriginate(Input(scope, 1), OwnerActionTrigger.Attack,
                new SimulationInstant(10), new SimulationInstant(15),
                new SimulationInstant(8), out var intent);
            var reason = impossible.Item1 switch
            {
                PredictedActionOutcome.Rejected =>
                    PredictedActionRejectionReason.InvalidState,
                PredictedActionOutcome.Expired =>
                    PredictedActionRejectionReason.DeadlineExpired,
                PredictedActionOutcome.Superseded =>
                    PredictedActionRejectionReason.Superseded,
                _ => PredictedActionRejectionReason.None,
            };
            var resolution = Resolution(
                scope, 1, intent.Identity, impossible.Item1,
                impossible.Item2, reason, impossible.Item3);

            Assert.Equal(
                ClientActionResolutionDecision.InvalidResolution,
                client.ApplyResolution(resolution, out _));
            Assert.True(client.RequiresBaselineRepair);
            Assert.Equal(1, client.OutstandingCount);
            Assert.Null(client.AppliedResolutionCursor);
        }
    }

    [Fact]
    public void ClientAcceptsExactExpiredAndSupersededBoundaries()
    {
        static void AssertApplied(PredictedActionOutcome outcome, long decisionTick)
        {
            var scope = Scope();
            var client = new OwnerActionCommandJournal(scope);
            client.TryOriginate(Input(scope, 1), OwnerActionTrigger.Attack,
                new SimulationInstant(10), new SimulationInstant(15),
                new SimulationInstant(8), out var intent);
            var reason = outcome == PredictedActionOutcome.Expired
                ? PredictedActionRejectionReason.DeadlineExpired
                : PredictedActionRejectionReason.Superseded;
            Assert.Equal(
                ClientActionResolutionDecision.Applied,
                client.ApplyResolution(Resolution(
                    scope, 1, intent.Identity, outcome, default, reason,
                    new SimulationInstant(decisionTick)), out _));
        }

        AssertApplied(PredictedActionOutcome.Expired, 16);
        AssertApplied(PredictedActionOutcome.Superseded, 10);
        AssertApplied(PredictedActionOutcome.Superseded, 12);
        AssertApplied(PredictedActionOutcome.Superseded, 15);
    }

    [Fact]
    public void ConflictingResolutionFingerprintOrExecutionRequiresRepair()
    {
        var scope = Scope();
        var client = new OwnerActionCommandJournal(scope);
        client.TryOriginate(Input(scope, 1), OwnerActionTrigger.Attack,
            new SimulationInstant(10), new SimulationInstant(15),
            new SimulationInstant(8), out var intent);
        var original = Resolution(
            scope, 1, intent.Identity, PredictedActionOutcome.Accepted,
            intent.PredictedStartFrame, executionId: 10);
        var conflict = Resolution(
            scope, 1, intent.Identity, PredictedActionOutcome.Accepted,
            intent.PredictedStartFrame, executionId: 11);

        Assert.Equal(
            ClientActionResolutionDecision.Applied,
            client.ApplyResolution(original, out _));
        Assert.Equal(
            ClientActionResolutionDecision.ConflictingResolution,
            client.ApplyResolution(conflict, out _));
        Assert.True(client.RequiresBaselineRepair);
    }

    [Fact]
    public void AllTerminalOutcomesAreExplicitAndAuthorityResolutionIsIdempotent()
    {
        var scope = Scope();
        var authority = new AuthorityOwnerActionCommandJournal(scope);
        var accepted = Intent(scope, 1, OwnerActionTrigger.Attack, 10, 15, 8);
        var remapped = Intent(scope, 2, OwnerActionTrigger.ActivateWeapon, 11, 18, 8);
        var rejected = Intent(scope, 3, OwnerActionTrigger.ActivateHelmet, 12, 19, 8);
        var superseded = Intent(scope, 4, OwnerActionTrigger.Block, 13, 20, 8);
        authority.Observe(accepted);
        authority.Observe(remapped);
        authority.Observe(rejected);
        authority.Observe(superseded);

        var acceptedResult = AssertResolution(
            authority, accepted, PredictedActionOutcome.Accepted,
            accepted.PredictedStartFrame, PredictedActionRejectionReason.None);
        Assert.True(acceptedResult.HasAuthorityExecution);
        var remappedResult = AssertResolution(
            authority, remapped, PredictedActionOutcome.Remapped,
            new SimulationInstant(14), PredictedActionRejectionReason.None);
        Assert.Equal(new SimulationInstant(14), remappedResult.StartFrame);
        AssertResolution(
            authority, rejected, PredictedActionOutcome.Rejected,
            default, PredictedActionRejectionReason.CapabilityUnavailable);
        AssertResolution(
            authority, superseded, PredictedActionOutcome.Superseded,
            default, PredictedActionRejectionReason.Superseded);

        Assert.Equal(
            AuthorityActionResolveDecision.AlreadyResolved,
            authority.Resolve(
                remapped.Identity,
                PredictedActionOutcome.Remapped,
                remappedResult.AuthorityExecution,
                remappedResult.StartFrame,
                PredictedActionRejectionReason.None,
                remappedResult.StartFrame,
                out var duplicate));
        Assert.Equal(remappedResult, duplicate);
        Assert.Equal(
            AuthorityActionResolveDecision.ConflictingResolution,
            authority.Resolve(
                remapped.Identity,
                PredictedActionOutcome.Rejected,
                default,
                default,
                PredictedActionRejectionReason.InvalidState,
                new SimulationInstant(15),
                out _));
    }

    [Fact]
    public void AuthorityExpiresInActionIdOrderAndRejectsRetroactiveDecisions()
    {
        var scope = Scope();
        var authority = new AuthorityOwnerActionCommandJournal(scope);
        var second = Intent(scope, 2, OwnerActionTrigger.Block, 10, 12, 8);
        var first = Intent(scope, 1, OwnerActionTrigger.Attack, 10, 12, 8);
        authority.Observe(second);
        authority.Observe(first);
        Span<PredictedActionResolution> expired = stackalloc PredictedActionResolution[2];

        Assert.Equal(0, authority.ExpireThrough(new SimulationInstant(12), expired));
        Assert.Equal(2, authority.ExpireThrough(new SimulationInstant(13), expired));
        Assert.Equal(first.Identity, expired[0].ActionIdentity);
        Assert.Equal(second.Identity, expired[1].ActionIdentity);
        Assert.All(expired.ToArray(), result =>
        {
            Assert.Equal(PredictedActionOutcome.Expired, result.Outcome);
            Assert.False(result.HasAuthorityExecution);
        });

        foreach (var outcome in new[]
                 {
                     PredictedActionOutcome.Accepted,
                     PredictedActionOutcome.Remapped,
                     PredictedActionOutcome.Rejected,
                     PredictedActionOutcome.Superseded,
                 })
        {
            var separate = new AuthorityOwnerActionCommandJournal(scope);
            var intent = Intent(scope, 1, OwnerActionTrigger.Attack, 10, 15, 8);
            separate.Observe(intent);
            var applied = outcome is PredictedActionOutcome.Accepted or
                PredictedActionOutcome.Remapped;
            var start = outcome == PredictedActionOutcome.Accepted
                ? intent.PredictedStartFrame
                : outcome == PredictedActionOutcome.Remapped
                    ? new SimulationInstant(14)
                    : default;
            var reason = outcome switch
            {
                PredictedActionOutcome.Rejected =>
                    PredictedActionRejectionReason.InvalidState,
                PredictedActionOutcome.Superseded =>
                    PredictedActionRejectionReason.Superseded,
                _ => PredictedActionRejectionReason.None,
            };
            Assert.ThrowsAny<ArgumentException>(() => separate.Resolve(
                intent.Identity,
                outcome,
                applied ? Execution(scope, 1) : default,
                start,
                reason,
                new SimulationInstant(100),
                out _));
            Assert.Equal(1, separate.PendingCount);
        }
    }

    [Fact]
    public void CursorCleanupIsScopedAndRepairStartsWithOldestSequence()
    {
        var scope = Scope(control: 2);
        var oldScope = Scope(control: 1);
        var authority = new AuthorityOwnerActionCommandJournal(scope);
        var first = Intent(scope, 1, OwnerActionTrigger.Attack, 10, 15, 8);
        var second = Intent(scope, 2, OwnerActionTrigger.Block, 11, 16, 8);
        authority.Observe(first);
        authority.Observe(second);
        var one = AssertResolution(
            authority, second, PredictedActionOutcome.Rejected,
            default, PredictedActionRejectionReason.InvalidState);
        var two = AssertResolution(
            authority, first, PredictedActionOutcome.Accepted,
            first.PredictedStartFrame, PredictedActionRejectionReason.None);
        Span<PredictedActionResolution> outbound = stackalloc PredictedActionResolution[1];

        Assert.Equal(1, authority.CopyUnacknowledgedResolutions(outbound));
        Assert.Equal(one, outbound[0]);
        Assert.False(authority.AcknowledgeResolutions(
            new ActionResolutionIdentity(oldScope, one.ResolutionIdentity.Sequence)));
        Assert.Equal(2, authority.TombstoneCount);
        Assert.True(authority.AcknowledgeResolutions(one.ResolutionIdentity));
        Assert.Equal(1, authority.CopyUnacknowledgedResolutions(outbound));
        Assert.Equal(two, outbound[0]);
        Assert.True(authority.AcknowledgeResolutions(two.ResolutionIdentity));
        Assert.Equal(0, authority.EntryCount);
        Assert.Equal(AuthorityActionObserveDecision.Duplicate, authority.Observe(first));
    }

    [Fact]
    public void ClientAndAuthorityCapacityFailuresDoNotConsumeActionIds()
    {
        var scope = Scope();
        var client = new OwnerActionCommandJournal(scope, capacity: 1);
        Assert.Equal(
            OwnerActionOriginDecision.Added,
            client.TryOriginate(Input(scope, 1), OwnerActionTrigger.Attack,
                new SimulationInstant(1), new SimulationInstant(3),
                SimulationInstant.Zero, out var first));
        Assert.Equal(
            OwnerActionOriginDecision.CapacityExceeded,
            client.TryOriginate(Input(scope, 2), OwnerActionTrigger.Block,
                new SimulationInstant(2), new SimulationInstant(4),
                SimulationInstant.Zero, out _));
        client.ApplyResolution(Resolution(
            scope, 1, first.Identity, PredictedActionOutcome.Rejected,
            default, PredictedActionRejectionReason.InvalidState,
            first.PredictedStartFrame), out _);
        Assert.Equal(
            OwnerActionOriginDecision.Added,
            client.TryOriginate(Input(scope, 2), OwnerActionTrigger.Block,
                new SimulationInstant(2), new SimulationInstant(4),
                SimulationInstant.Zero, out var second));
        Assert.Equal(2UL, second.Identity.Id.Value);

        var authority = new AuthorityOwnerActionCommandJournal(scope, capacity: 1);
        Assert.Equal(AuthorityActionObserveDecision.FirstSeen, authority.Observe(first));
        Assert.Equal(AuthorityActionObserveDecision.CapacityExceeded, authority.Observe(second));
        var terminal = AssertResolution(
            authority, first, PredictedActionOutcome.Rejected,
            default, PredictedActionRejectionReason.InvalidState);
        Assert.True(authority.AcknowledgeResolutions(terminal.ResolutionIdentity));
        Assert.Equal(AuthorityActionObserveDecision.FirstSeen, authority.Observe(second));
    }

    [Fact]
    public void ReorderWindowsAcceptExactEdgeAndRejectFirstValueBeyondWithoutMutation()
    {
        var scope = Scope();
        var authorityAtEdge = new AuthorityOwnerActionCommandJournal(scope);
        Assert.Equal(
            AuthorityActionObserveDecision.FirstSeen,
            authorityAtEdge.Observe(Intent(
                scope, 64, OwnerActionTrigger.Attack, 10, 15, 8)));
        var authorityBeyond = new AuthorityOwnerActionCommandJournal(scope);
        Assert.Equal(
            AuthorityActionObserveDecision.TooFarAhead,
            authorityBeyond.Observe(Intent(
                scope, 65, OwnerActionTrigger.Attack, 10, 15, 8)));
        Assert.Equal(0, authorityBeyond.EntryCount);

        var clientAtEdge = new OwnerActionCommandJournal(scope);
        clientAtEdge.TryOriginate(Input(scope, 1), OwnerActionTrigger.Attack,
            new SimulationInstant(10), new SimulationInstant(15),
            new SimulationInstant(8), out var edge);
        Assert.Equal(
            ClientActionResolutionDecision.Applied,
            clientAtEdge.ApplyResolution(Resolution(
                scope, 64, edge.Identity, PredictedActionOutcome.Rejected,
                default, PredictedActionRejectionReason.InvalidState,
                edge.PredictedStartFrame), out _));
        Assert.Null(clientAtEdge.AppliedResolutionCursor);

        var clientBeyond = new OwnerActionCommandJournal(scope);
        clientBeyond.TryOriginate(Input(scope, 1), OwnerActionTrigger.Attack,
            new SimulationInstant(10), new SimulationInstant(15),
            new SimulationInstant(8), out var beyond);
        Assert.Equal(
            ClientActionResolutionDecision.TooFarAhead,
            clientBeyond.ApplyResolution(Resolution(
                scope, 65, beyond.Identity, PredictedActionOutcome.Rejected,
                default, PredictedActionRejectionReason.InvalidState,
                beyond.PredictedStartFrame), out _));
        Assert.True(clientBeyond.RequiresBaselineRepair);
        Assert.Equal(1, clientBeyond.OutstandingCount);
    }

    [Fact]
    public void ScopeResetClearsStateAndSameScopeResetIsIdempotent()
    {
        var oldScope = Scope(control: 1);
        var newScope = Scope(control: 2);
        var client = new OwnerActionCommandJournal(oldScope);
        var authority = new AuthorityOwnerActionCommandJournal(oldScope);
        client.TryOriginate(Input(oldScope, 1), OwnerActionTrigger.Attack,
            new SimulationInstant(1), new SimulationInstant(3),
            SimulationInstant.Zero, out var oldIntent);
        authority.Observe(oldIntent);
        var oldResolution = AssertResolution(
            authority, oldIntent, PredictedActionOutcome.Rejected,
            default, PredictedActionRejectionReason.InvalidState);
        client.ApplyResolution(oldResolution, out _);

        Assert.False(client.Reset(oldScope));
        Assert.False(authority.Reset(oldScope));
        Assert.True(client.Reset(newScope));
        Assert.True(authority.Reset(newScope));
        Assert.Equal(0, client.OutstandingCount);
        Assert.Null(client.AppliedResolutionCursor);
        Assert.Equal(0, authority.EntryCount);
        Assert.Null(authority.LastAcknowledgedResolution);
        Assert.Equal(
            ClientActionResolutionDecision.WrongScope,
            client.ApplyResolution(oldResolution, out _));
        Assert.Equal(
            AuthorityActionObserveDecision.WrongScope,
            authority.Observe(oldIntent));
        client.TryOriginate(Input(newScope, 1), OwnerActionTrigger.Attack,
            SimulationInstant.Zero, new SimulationInstant(2),
            SimulationInstant.Zero, out var fresh);
        Assert.Equal(PredictedActionId.Initial, fresh.Identity.Id);
    }

    [Fact]
    public void ImpossibleEvidenceLocksOriginAndResendUntilNewScopeBaseline()
    {
        var oldScope = Scope(control: 1);
        var newScope = Scope(control: 2);
        var client = new OwnerActionCommandJournal(oldScope);
        client.TryOriginate(Input(oldScope, 1), OwnerActionTrigger.Attack,
            SimulationInstant.Zero, new SimulationInstant(2),
            SimulationInstant.Zero, out var outstanding);
        var unknown = Resolution(
            oldScope, 1,
            new PredictedActionIdentity(oldScope, new PredictedActionId(9)),
            PredictedActionOutcome.Rejected,
            default,
            PredictedActionRejectionReason.InvalidState,
            new SimulationInstant(1));

        Assert.Equal(
            ClientActionResolutionDecision.UnknownAction,
            client.ApplyResolution(unknown, out _));
        Assert.True(client.RequiresBaselineRepair);
        Assert.Equal(
            OwnerActionOriginDecision.BaselineRepairRequired,
            client.TryOriginate(Input(oldScope, 2), OwnerActionTrigger.Block,
                new SimulationInstant(1), new SimulationInstant(3),
                SimulationInstant.Zero, out var blocked));
        Assert.Equal(default, blocked);
        Span<PredictedActionIntent> resend = stackalloc PredictedActionIntent[1];
        resend[0] = outstanding;
        Assert.Equal(0, client.CopyOutstanding(resend));
        Assert.False(client.Reset(oldScope));
        Assert.True(client.RequiresBaselineRepair);
        Assert.True(client.Reset(newScope));
        Assert.False(client.RequiresBaselineRepair);
    }

    [Fact]
    public void RetiredSequenceNamingLiveActionRequiresRepairButOldRetiredActionIsDuplicate()
    {
        var scope = Scope(control: 2);
        var cursor = new ActionResolutionIdentity(
            scope, new ActionResolutionSequence(99));
        var restored = OwnerActionCommandJournal.RestoreEmptyBaseline(
            scope, OwnerActionJournalPolicy.Default,
            new PredictedActionId(5), cursor);
        restored.TryOriginate(Input(scope, 5), OwnerActionTrigger.Attack,
            SimulationInstant.Zero, new SimulationInstant(1),
            SimulationInstant.Zero, out var live);
        Assert.Equal(
            ClientActionResolutionDecision.ConflictingResolution,
            restored.ApplyResolution(Resolution(
                scope, 99, live.Identity, PredictedActionOutcome.Accepted,
                SimulationInstant.Zero), out _));
        Assert.True(restored.RequiresBaselineRepair);
        Assert.Equal(1, restored.OutstandingCount);

        var aged = new OwnerActionCommandJournal(scope);
        PredictedActionResolution oldest = default;
        for (ulong sequence = 1;
             sequence <= (ulong)OwnerActionJournalLimits.ResolutionReorderWindow + 1;
             sequence++)
        {
            aged.TryOriginate(Input(scope, sequence), OwnerActionTrigger.Attack,
                SimulationInstant.Zero, new SimulationInstant(1),
                SimulationInstant.Zero, out var retired);
            var terminal = Resolution(
                scope, sequence, retired.Identity, PredictedActionOutcome.Accepted,
                SimulationInstant.Zero, executionId: sequence);
            oldest = sequence == 1 ? terminal : oldest;
            Assert.Equal(
                ClientActionResolutionDecision.Applied,
                aged.ApplyResolution(terminal, out _));
        }
        aged.TryOriginate(Input(scope, 66), OwnerActionTrigger.Attack,
            SimulationInstant.Zero, new SimulationInstant(1),
            SimulationInstant.Zero, out var agedLive);
        Assert.Equal(
            ClientActionResolutionDecision.Duplicate,
            aged.ApplyResolution(oldest, out _));
        Assert.False(aged.RequiresBaselineRepair);
        Assert.Equal(
            ClientActionResolutionDecision.ConflictingResolution,
            aged.ApplyResolution(Resolution(
                scope, 1, agedLive.Identity, PredictedActionOutcome.Accepted,
                SimulationInstant.Zero, executionId: 200), out _));
        Assert.True(aged.RequiresBaselineRepair);
        Assert.Equal(1, aged.OutstandingCount);
    }

    [Fact]
    public void RetentionTimeoutAndSharedValidityPolicyFailClosed()
    {
        var scope = Scope();
        var policy = new OwnerActionJournalPolicy(
            4,
            new SimulationDuration(5),
            new SimulationDuration(5));
        var client = new OwnerActionCommandJournal(scope, policy);
        Assert.Throws<ArgumentOutOfRangeException>(() => client.TryOriginate(
            Input(scope, 1), OwnerActionTrigger.Attack,
            SimulationInstant.Zero, new SimulationInstant(6),
            SimulationInstant.Zero, out _));
        Assert.Equal(0, client.OutstandingCount);

        var authority = new AuthorityOwnerActionCommandJournal(scope, policy);
        Assert.Equal(
            AuthorityActionObserveDecision.InvalidIntent,
            authority.Observe(Intent(
                scope, 1, OwnerActionTrigger.Attack, 0, 6, 0)));
        var valid = Intent(scope, 1, OwnerActionTrigger.Attack, 10, 15, 8);
        authority.Observe(valid);
        AssertResolution(
            authority, valid, PredictedActionOutcome.Accepted,
            valid.PredictedStartFrame, PredictedActionRejectionReason.None,
            decisionAt: 10);
        Assert.False(authority.CheckTombstoneRetention(new SimulationInstant(15)));
        Assert.True(authority.CheckTombstoneRetention(new SimulationInstant(16)));
        Assert.True(authority.RequiresBaselineRepair);
    }

    [Fact]
    public void EmptyBaselinesRequireContiguousCursorsAndCanContinueTogether()
    {
        var scope = Scope(control: 2);
        var other = Scope(control: 3);
        var policy = OwnerActionJournalPolicy.Default;
        var initial = AuthorityOwnerActionCommandJournal.RestoreEmptyBaseline(
            scope, policy, ActionResolutionSequence.Initial, null);
        Assert.Null(initial.LastAcknowledgedResolution);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AuthorityOwnerActionCommandJournal.RestoreEmptyBaseline(
                scope, policy, new ActionResolutionSequence(100), null));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AuthorityOwnerActionCommandJournal.RestoreEmptyBaseline(
                scope, policy, new ActionResolutionSequence(100),
                new ActionResolutionIdentity(scope, ActionResolutionSequence.Initial)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AuthorityOwnerActionCommandJournal.RestoreEmptyBaseline(
                scope, policy, new ActionResolutionSequence(100),
                new ActionResolutionIdentity(other, new ActionResolutionSequence(99))));

        var acknowledged = new ActionResolutionIdentity(
            scope, new ActionResolutionSequence(99));
        var client = OwnerActionCommandJournal.RestoreEmptyBaseline(
            scope, policy, new PredictedActionId(5), acknowledged);
        var authority = AuthorityOwnerActionCommandJournal.RestoreEmptyBaseline(
            scope, policy, new ActionResolutionSequence(100), acknowledged,
            new PredictedActionId(4));
        client.TryOriginate(Input(scope, 5), OwnerActionTrigger.Attack,
            new SimulationInstant(10), new SimulationInstant(15),
            new SimulationInstant(8), out var intent);
        Assert.Equal(AuthorityActionObserveDecision.FirstSeen, authority.Observe(intent));
        var result = AssertResolution(
            authority, intent, PredictedActionOutcome.Accepted,
            intent.PredictedStartFrame, PredictedActionRejectionReason.None);
        Assert.Equal(100UL, result.ResolutionIdentity.Sequence.Value);
        Assert.Equal(
            ClientActionResolutionDecision.Applied,
            client.ApplyResolution(result, out _));
        Assert.Equal(result.ResolutionIdentity, client.AppliedResolutionCursor);
    }

    [Fact]
    public void ActionAndResolutionCounterExhaustionNeverWrapsAndPartialAckRepairsFinalLoss()
    {
        var scope = Scope(control: 2);
        var policy = OwnerActionJournalPolicy.Default;
        var client = OwnerActionCommandJournal.RestoreEmptyBaseline(
            scope, policy, new PredictedActionId(ulong.MaxValue));
        Assert.Equal(
            OwnerActionOriginDecision.Added,
            client.TryOriginate(Input(scope, 1), OwnerActionTrigger.Attack,
                SimulationInstant.Zero, new SimulationInstant(1),
                SimulationInstant.Zero, out var maximum));
        Assert.Equal(ulong.MaxValue, maximum.Identity.Id.Value);
        Assert.Equal(
            OwnerActionOriginDecision.IdentityExhausted,
            client.TryOriginate(Input(scope, 2), OwnerActionTrigger.Block,
                new SimulationInstant(1), new SimulationInstant(2),
                SimulationInstant.Zero, out _));

        var capacityPolicy = policy.WithCapacity(OwnerActionJournalLimits.MaximumCapacity);
        const ulong firstSequence =
            ulong.MaxValue - OwnerActionJournalLimits.MaximumCapacity + 1;
        var beforeWindow = new ActionResolutionIdentity(
            scope, new ActionResolutionSequence(firstSequence - 1));
        var authority = AuthorityOwnerActionCommandJournal.RestoreEmptyBaseline(
            scope, capacityPolicy, new ActionResolutionSequence(firstSequence),
            beforeWindow);
        PredictedActionResolution final = default;
        for (var index = 1; index <= OwnerActionJournalLimits.MaximumCapacity; index++)
        {
            var intent = Intent(
                scope, (ulong)index, OwnerActionTrigger.Attack, 0, 1, 0);
            Assert.Equal(AuthorityActionObserveDecision.FirstSeen, authority.Observe(intent));
            final = AssertResolution(
                authority, intent, PredictedActionOutcome.Accepted,
                SimulationInstant.Zero, PredictedActionRejectionReason.None,
                decisionAt: 0);
        }
        Assert.Equal(ulong.MaxValue, final.ResolutionIdentity.Sequence.Value);
        var partial = new ActionResolutionIdentity(
            scope, new ActionResolutionSequence(ulong.MaxValue - 1));
        Assert.True(authority.AcknowledgeResolutions(partial));
        Assert.Equal(1, authority.TombstoneCount);
        Span<PredictedActionResolution> repair = stackalloc PredictedActionResolution[1];
        Assert.Equal(1, authority.CopyUnacknowledgedResolutions(repair));
        Assert.Equal(final, repair[0]);
        Assert.True(authority.AcknowledgeResolutions(final.ResolutionIdentity));
        Assert.Equal(0, authority.EntryCount);

        var pending = Intent(
            scope, 257, OwnerActionTrigger.Attack, 0, 1, 0);
        Assert.Equal(AuthorityActionObserveDecision.FirstSeen, authority.Observe(pending));
        Assert.Equal(
            AuthorityActionResolveDecision.BaselineRepairRequired,
            authority.Resolve(
                pending.Identity,
                PredictedActionOutcome.Accepted,
                Execution(scope, 500),
                SimulationInstant.Zero,
                PredictedActionRejectionReason.None,
                SimulationInstant.Zero,
                out _));
        Assert.True(authority.RequiresBaselineRepair);
    }

    [Fact]
    public void IntentAndResolutionShapesRejectInvalidScopeFramesEnumsAndExecution()
    {
        var scope = Scope();
        var other = Scope(control: 2);
        var otherLife = new OwnerIntentScope(
            scope.SessionId,
            new LifeEpoch(scope.Life.CombatantId, new LifeGenerationId(4)),
            scope.OwnerControl);
        Assert.Throws<ArgumentOutOfRangeException>(() => new PredictedActionIntent(
            new PredictedActionIdentity(scope, PredictedActionId.Initial),
            Input(other, 1),
            OwnerActionTrigger.Attack,
            new SimulationInstant(2),
            new SimulationInstant(3),
            new SimulationInstant(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PredictedActionIntent(
            new PredictedActionIdentity(scope, PredictedActionId.Initial),
            Input(scope, 1),
            (OwnerActionTrigger)0,
            new SimulationInstant(2),
            new SimulationInstant(3),
            new SimulationInstant(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PredictedActionIntent(
            new PredictedActionIdentity(scope, PredictedActionId.Initial),
            Input(scope, 1),
            OwnerActionTrigger.Attack,
            new SimulationInstant(3),
            new SimulationInstant(2),
            new SimulationInstant(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PredictedActionIntent(
            new PredictedActionIdentity(scope, PredictedActionId.Initial),
            Input(scope, 1),
            OwnerActionTrigger.Attack,
            new SimulationInstant(2),
            new SimulationInstant(3),
            new SimulationInstant(4)));

        var action = new PredictedActionIdentity(scope, PredictedActionId.Initial);
        var resolution = new ActionResolutionIdentity(
            scope, ActionResolutionSequence.Initial);
        Assert.Throws<ArgumentException>(() => new PredictedActionResolution(
            resolution,
            action,
            PredictedActionOutcome.Accepted,
            false,
            default,
            new SimulationInstant(2),
            new SimulationInstant(2),
            PredictedActionRejectionReason.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PredictedActionResolution(
            resolution,
            action,
            PredictedActionOutcome.Accepted,
            true,
            Execution(otherLife, 1),
            new SimulationInstant(2),
            new SimulationInstant(2),
            PredictedActionRejectionReason.None));
        Assert.Throws<ArgumentException>(() => new PredictedActionResolution(
            resolution,
            action,
            PredictedActionOutcome.Rejected,
            false,
            default,
            new SimulationInstant(2),
            new SimulationInstant(2),
            PredictedActionRejectionReason.InvalidState));
    }

    [Fact]
    public void ResolutionShapeRejectsEveryUnknownOrIncoherentEnumPairing()
    {
        var scope = Scope();
        var action = new PredictedActionIdentity(scope, PredictedActionId.Initial);
        var resolution = new ActionResolutionIdentity(
            scope, ActionResolutionSequence.Initial);
        var outcomes = Enum.GetValues<PredictedActionOutcome>()
            .Select(value => (int)value).Append(0).Append(255).Distinct();
        var reasons = Enum.GetValues<PredictedActionRejectionReason>()
            .Select(value => (int)value).Append(255).Distinct();

        foreach (var outcomeValue in outcomes)
        foreach (var reasonValue in reasons)
        foreach (var hasExecution in new[] { false, true })
        {
            var outcome = (PredictedActionOutcome)outcomeValue;
            var reason = (PredictedActionRejectionReason)reasonValue;
            var shouldSucceed = (outcome, reason, hasExecution) switch
            {
                (PredictedActionOutcome.Accepted or PredictedActionOutcome.Remapped,
                    PredictedActionRejectionReason.None, true) => true,
                (PredictedActionOutcome.Rejected,
                    PredictedActionRejectionReason.AuthorityPolicyRejected or
                    PredictedActionRejectionReason.InvalidState or
                    PredictedActionRejectionReason.CooldownActive or
                    PredictedActionRejectionReason.CapabilityUnavailable, false) => true,
                (PredictedActionOutcome.Expired,
                    PredictedActionRejectionReason.DeadlineExpired, false) => true,
                (PredictedActionOutcome.Superseded,
                    PredictedActionRejectionReason.Superseded, false) => true,
                _ => false,
            };

            void Construct() => _ = new PredictedActionResolution(
                resolution,
                action,
                outcome,
                hasExecution,
                hasExecution ? Execution(scope, 1) : default,
                hasExecution ? new SimulationInstant(2) : default,
                new SimulationInstant(2),
                reason);
            if (shouldSucceed)
            {
                Construct();
            }
            else
            {
                Assert.ThrowsAny<ArgumentException>(Construct);
            }
        }
    }

    private static PredictedActionResolution AssertResolution(
        AuthorityOwnerActionCommandJournal authority,
        PredictedActionIntent intent,
        PredictedActionOutcome outcome,
        SimulationInstant startFrame,
        PredictedActionRejectionReason reason,
        long? decisionAt = null)
    {
        var applied = outcome is PredictedActionOutcome.Accepted or
            PredictedActionOutcome.Remapped;
        var decisionFrame = decisionAt is not null
            ? new SimulationInstant(decisionAt.Value)
            : outcome switch
            {
                PredictedActionOutcome.Accepted or PredictedActionOutcome.Remapped =>
                    startFrame,
                PredictedActionOutcome.Expired =>
                    new SimulationInstant(intent.LastValidStartFrame.Tick + 1),
                _ => intent.PredictedStartFrame,
            };
        Assert.Equal(
            AuthorityActionResolveDecision.Resolved,
            authority.Resolve(
                intent.Identity,
                outcome,
                applied ? Execution(scope: intent.Identity.Scope,
                    id: intent.Identity.Id.Value) : default,
                startFrame,
                reason,
                decisionFrame,
                out var resolution));
        Assert.Equal(outcome, resolution.Outcome);
        Assert.Equal(intent.Identity, resolution.ActionIdentity);
        return resolution;
    }

    private static PredictedActionResolution Resolution(
        OwnerIntentScope scope,
        ulong sequence,
        PredictedActionIdentity action,
        PredictedActionOutcome outcome,
        SimulationInstant startFrame,
        PredictedActionRejectionReason reason = PredictedActionRejectionReason.None,
        SimulationInstant? decisionFrame = null,
        ulong? executionId = null)
    {
        var applied = outcome is PredictedActionOutcome.Accepted or
            PredictedActionOutcome.Remapped;
        return new PredictedActionResolution(
            new ActionResolutionIdentity(scope, new ActionResolutionSequence(sequence)),
            action,
            outcome,
            applied,
            applied ? Execution(scope, executionId ?? sequence) : default,
            startFrame,
            decisionFrame ?? (applied ? startFrame : SimulationInstant.Zero),
            reason);
    }

    private static PredictedActionIntent Intent(
        OwnerIntentScope scope,
        ulong id,
        OwnerActionTrigger trigger,
        long firstFrame,
        long lastFrame,
        long renderedFrame) => new(
            new PredictedActionIdentity(scope, new PredictedActionId(id)),
            Input(scope, id),
            trigger,
            new SimulationInstant(firstFrame),
            new SimulationInstant(lastFrame),
            new SimulationInstant(renderedFrame));

    private static AuthorityActionExecutionIdentity Execution(
        OwnerIntentScope scope,
        ulong id) => new(
            new AuthorityActionExecutionScope(scope.SessionId, scope.Life),
            new AuthorityActionExecutionId(id));

    private static OwnerInputIdentity Input(OwnerIntentScope scope, ulong sequence) =>
        new(scope, new InputSequence(sequence));

    private static OwnerIntentScope Scope(ulong control = 1) => new(
        100,
        new LifeEpoch(new CombatantId(7), new LifeGenerationId(3)),
        new OwnerControlEpoch(control));
}
