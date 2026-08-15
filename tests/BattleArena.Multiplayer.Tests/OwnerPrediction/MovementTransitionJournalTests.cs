using BattleArena.Core.Common;
using BattleArena.Core.Movement.Simulation;
using BattleArena.Multiplayer.OwnerPrediction;

namespace BattleArena.Multiplayer.Tests.OwnerPrediction;

public sealed class MovementTransitionJournalTests
{
    [Fact]
    public void UnresolvedIntentIsRepeatedExactlyThroughArbitraryLoss()
    {
        var scope = Scope();
        var journal = new MovementTransitionJournal(scope);
        Assert.Equal(
            MovementTransitionOriginDecision.Added,
            journal.TryOriginate(
                Input(scope, 1),
                MovementTransitionKind.JumpPressed,
                new SimulationInstant(10),
                new SimulationInstant(16),
                out var intent));
        Span<MovementTransitionIntent> firstPacket = stackalloc MovementTransitionIntent[1];
        Span<MovementTransitionIntent> retryPacket = stackalloc MovementTransitionIntent[1];

        Assert.Equal(1, journal.CopyOutstanding(firstPacket));
        Assert.Equal(1, journal.CopyOutstanding(retryPacket));

        Assert.Equal(intent, firstPacket[0]);
        Assert.Equal(firstPacket[0], retryPacket[0]);
        Assert.Equal(1, journal.OutstandingCount);
        Assert.Null(journal.AppliedResolutionCursor);
    }

    [Fact]
    public void BoundedPacketsRotateWithoutStarvingOutstandingIntents()
    {
        var scope = Scope();
        var journal = new MovementTransitionJournal(scope);
        journal.TryOriginate(Input(scope, 1), MovementTransitionKind.JumpPressed,
            new SimulationInstant(1), new SimulationInstant(9), out var first);
        journal.TryOriginate(Input(scope, 2), MovementTransitionKind.JumpReleased,
            new SimulationInstant(2), new SimulationInstant(9), out var second);
        journal.TryOriginate(Input(scope, 3), MovementTransitionKind.LedgeGrab,
            new SimulationInstant(3), new SimulationInstant(9), out var third);
        Span<MovementTransitionIntent> packet = stackalloc MovementTransitionIntent[2];

        Assert.Equal(2, journal.CopyOutstanding(packet));
        Assert.Equal(first, packet[0]);
        Assert.Equal(second, packet[1]);
        Assert.Equal(2, journal.CopyOutstanding(packet));
        Assert.Equal(third, packet[0]);
        Assert.Equal(first, packet[1]);
    }

    [Fact]
    public void RemovingAnIntentDuringRotationDoesNotStarveSurvivors()
    {
        var scope = Scope();
        var journal = new MovementTransitionJournal(scope);
        journal.TryOriginate(Input(scope, 1), MovementTransitionKind.JumpPressed,
            new SimulationInstant(1), new SimulationInstant(9), out var first);
        journal.TryOriginate(Input(scope, 2), MovementTransitionKind.JumpReleased,
            new SimulationInstant(2), new SimulationInstant(9), out var second);
        journal.TryOriginate(Input(scope, 3), MovementTransitionKind.LedgeGrab,
            new SimulationInstant(3), new SimulationInstant(9), out var third);
        Span<MovementTransitionIntent> packet = stackalloc MovementTransitionIntent[2];
        journal.CopyOutstanding(packet);
        var terminal = Resolution(
            scope, 1, second.Identity, MovementTransitionOutcome.Rejected,
            default, MovementTransitionRejectionReason.InvalidState,
            second.FirstPredictedFrame);

        Assert.Equal(
            ClientTransitionResolutionDecision.Applied,
            journal.ApplyResolution(terminal, out _));
        Assert.Equal(2, journal.CopyOutstanding(packet));
        Assert.Contains(first, packet.ToArray());
        Assert.Contains(third, packet.ToArray());
    }

    [Fact]
    public void AuthorityDeduplicatesReorderedAndRetiredTransitionIds()
    {
        var scope = Scope();
        var authority = new AuthorityMovementTransitionJournal(scope);
        var first = Intent(scope, 1, MovementTransitionKind.JumpPressed, 10, 15);
        var second = Intent(scope, 2, MovementTransitionKind.JumpReleased, 11, 16);

        Assert.Equal(AuthorityTransitionObserveDecision.FirstSeen, authority.Observe(second));
        Assert.Equal(AuthorityTransitionObserveDecision.Duplicate, authority.Observe(second));
        Assert.Equal(AuthorityTransitionObserveDecision.FirstSeen, authority.Observe(first));
        Assert.Equal(AuthorityTransitionObserveDecision.Duplicate, authority.Observe(first));
        Assert.Equal(2, authority.PendingCount);

        Assert.Equal(
            AuthorityTransitionResolveDecision.Resolved,
            authority.Resolve(
                first.Identity,
                MovementTransitionOutcome.Accepted,
                first.FirstPredictedFrame,
                MovementTransitionRejectionReason.None,
                first.FirstPredictedFrame,
                out var resolution));
        Assert.True(authority.AcknowledgeResolutions(resolution.ResolutionIdentity));
        Assert.Equal(1, authority.EntryCount);
        Assert.Equal(
            AuthorityTransitionObserveDecision.Duplicate,
            authority.Observe(first));
    }

    [Fact]
    public void ConflictingDuplicateNeverExecutesAsANewTransition()
    {
        var scope = Scope();
        var authority = new AuthorityMovementTransitionJournal(scope);
        var original = Intent(scope, 1, MovementTransitionKind.JumpPressed, 10, 15);
        var conflict = Intent(scope, 1, MovementTransitionKind.LedgeDrop, 10, 15);

        Assert.Equal(AuthorityTransitionObserveDecision.FirstSeen, authority.Observe(original));
        Assert.Equal(
            AuthorityTransitionObserveDecision.ConflictingDuplicate,
            authority.Observe(conflict));
        Assert.Equal(1, authority.PendingCount);
    }

    [Fact]
    public void ClientAppliesReorderedResultsAndAdvancesOnlyContiguousCursor()
    {
        var scope = Scope();
        var client = new MovementTransitionJournal(scope);
        client.TryOriginate(Input(scope, 1), MovementTransitionKind.JumpPressed,
            new SimulationInstant(10), new SimulationInstant(15), out var first);
        client.TryOriginate(Input(scope, 2), MovementTransitionKind.JumpReleased,
            new SimulationInstant(11), new SimulationInstant(16), out var second);
        var resolutionOne = Resolution(
            scope, 1, first.Identity, MovementTransitionOutcome.Accepted,
            first.FirstPredictedFrame);
        var resolutionTwo = Resolution(
            scope, 2, second.Identity, MovementTransitionOutcome.Rejected,
            default,
            MovementTransitionRejectionReason.InvalidState,
            second.FirstPredictedFrame);

        Assert.Equal(
            ClientTransitionResolutionDecision.Applied,
            client.ApplyResolution(resolutionTwo, out var resolvedSecond));
        Assert.Equal(second, resolvedSecond);
        Assert.Null(client.AppliedResolutionCursor);
        Assert.Equal(
            ClientTransitionResolutionDecision.Duplicate,
            client.ApplyResolution(resolutionTwo, out _));
        Assert.Equal(
            ClientTransitionResolutionDecision.Applied,
            client.ApplyResolution(resolutionOne, out var resolvedFirst));

        Assert.Equal(first, resolvedFirst);
        Assert.Equal(
            new TransitionResolutionIdentity(scope, new TransitionResolutionSequence(2)),
            client.AppliedResolutionCursor);
        Assert.Equal(0, client.OutstandingCount);
        Assert.Equal(
            ClientTransitionResolutionDecision.Duplicate,
            client.ApplyResolution(resolutionOne, out _));
    }

    [Fact]
    public void ClientRejectsImpossibleTerminalFramesBeforeAnyMutation()
    {
        foreach (var impossible in new[]
                 {
                     (MovementTransitionOutcome.Accepted, new SimulationInstant(9),
                         new SimulationInstant(9)),
                     (MovementTransitionOutcome.Remapped, new SimulationInstant(10),
                         new SimulationInstant(10)),
                     (MovementTransitionOutcome.Remapped, new SimulationInstant(16),
                         new SimulationInstant(16)),
                     (MovementTransitionOutcome.Rejected, SimulationInstant.Zero,
                         new SimulationInstant(16)),
                 })
        {
            var scope = Scope();
            var client = new MovementTransitionJournal(scope);
            client.TryOriginate(Input(scope, 1), MovementTransitionKind.JumpPressed,
                new SimulationInstant(10), new SimulationInstant(15), out var intent);
            var reason = impossible.Item1 == MovementTransitionOutcome.Rejected
                ? MovementTransitionRejectionReason.InvalidState
                : MovementTransitionRejectionReason.None;
            var resolution = Resolution(
                scope,
                1,
                intent.Identity,
                impossible.Item1,
                impossible.Item2,
                reason,
                impossible.Item3);

            Assert.Equal(
                ClientTransitionResolutionDecision.InvalidResolution,
                client.ApplyResolution(resolution, out _));
            Assert.True(client.RequiresBaselineRepair);
            Assert.Equal(1, client.OutstandingCount);
            Assert.Null(client.AppliedResolutionCursor);
        }
    }

    [Fact]
    public void ConflictingPayloadForSameResolutionSequenceRequiresBaselineRepair()
    {
        var scope = Scope();
        var client = new MovementTransitionJournal(scope);
        client.TryOriginate(Input(scope, 1), MovementTransitionKind.JumpPressed,
            new SimulationInstant(10), new SimulationInstant(15), out var intent);
        var original = Resolution(
            scope, 1, intent.Identity, MovementTransitionOutcome.Rejected,
            default, MovementTransitionRejectionReason.InvalidState,
            intent.FirstPredictedFrame);
        var conflict = Resolution(
            scope, 1, intent.Identity, MovementTransitionOutcome.Rejected,
            default, MovementTransitionRejectionReason.CapabilityUnavailable,
            intent.FirstPredictedFrame);

        Assert.Equal(
            ClientTransitionResolutionDecision.Applied,
            client.ApplyResolution(original, out _));
        Assert.Equal(
            ClientTransitionResolutionDecision.ConflictingResolution,
            client.ApplyResolution(conflict, out _));
        Assert.True(client.RequiresBaselineRepair);
    }

    [Fact]
    public void AcceptedRemappedRejectedAndSupersededResultsAreExplicitAndIdempotent()
    {
        var scope = Scope();
        var authority = new AuthorityMovementTransitionJournal(scope);
        var accepted = Intent(scope, 1, MovementTransitionKind.JumpPressed, 10, 15);
        var remapped = Intent(scope, 2, MovementTransitionKind.CrouchOrRollPressed, 11, 18);
        var rejected = Intent(scope, 3, MovementTransitionKind.LedgeGrab, 12, 19);
        var superseded = Intent(scope, 4, MovementTransitionKind.LedgeClimb, 13, 20);
        Assert.Equal(AuthorityTransitionObserveDecision.FirstSeen, authority.Observe(accepted));
        Assert.Equal(AuthorityTransitionObserveDecision.FirstSeen, authority.Observe(remapped));
        Assert.Equal(AuthorityTransitionObserveDecision.FirstSeen, authority.Observe(rejected));
        Assert.Equal(AuthorityTransitionObserveDecision.FirstSeen, authority.Observe(superseded));

        AssertResolution(
            authority, accepted, MovementTransitionOutcome.Accepted,
            accepted.FirstPredictedFrame, MovementTransitionRejectionReason.None);
        var remapResolution = AssertResolution(
            authority, remapped, MovementTransitionOutcome.Remapped,
            new SimulationInstant(14), MovementTransitionRejectionReason.None);
        Assert.Equal(new SimulationInstant(14), remapResolution.ApplicationFrame);
        AssertResolution(
            authority, rejected, MovementTransitionOutcome.Rejected,
            default, MovementTransitionRejectionReason.CapabilityUnavailable);
        AssertResolution(
            authority, superseded, MovementTransitionOutcome.Superseded,
            default, MovementTransitionRejectionReason.Superseded);

        Assert.Equal(
            AuthorityTransitionResolveDecision.AlreadyResolved,
            authority.Resolve(
                remapped.Identity,
                MovementTransitionOutcome.Remapped,
                new SimulationInstant(14),
                MovementTransitionRejectionReason.None,
                new SimulationInstant(14),
                out var duplicate));
        Assert.Equal(remapResolution, duplicate);
        Assert.Equal(
            AuthorityTransitionResolveDecision.ConflictingResolution,
            authority.Resolve(
                remapped.Identity,
                MovementTransitionOutcome.Rejected,
                default,
                MovementTransitionRejectionReason.InvalidState,
                new SimulationInstant(15),
                out _));
    }

    [Fact]
    public void AuthorityExpiresPastDeadlineInTransitionIdOrder()
    {
        var scope = Scope();
        var authority = new AuthorityMovementTransitionJournal(scope);
        var second = Intent(scope, 2, MovementTransitionKind.JumpReleased, 10, 12);
        var first = Intent(scope, 1, MovementTransitionKind.JumpPressed, 10, 12);
        authority.Observe(second);
        authority.Observe(first);
        Span<MovementTransitionResolution> resolutions =
            stackalloc MovementTransitionResolution[2];

        Assert.Equal(0, authority.ExpireThrough(new SimulationInstant(12), resolutions));
        Assert.Equal(2, authority.ExpireThrough(new SimulationInstant(13), resolutions));

        Assert.Equal(first.Identity, resolutions[0].TransitionIdentity);
        Assert.Equal(second.Identity, resolutions[1].TransitionIdentity);
        Assert.All(resolutions.ToArray(), resolution =>
        {
            Assert.Equal(MovementTransitionOutcome.Expired, resolution.Outcome);
            Assert.Equal(
                MovementTransitionRejectionReason.DeadlineExpired,
                resolution.RejectionReason);
            Assert.False(resolution.HasApplicationFrame);
        });
    }

    [Fact]
    public void AuthorityCannotMakeAnyNonExpiredDecisionPastDeadline()
    {
        foreach (var request in new[]
                 {
                     (MovementTransitionOutcome.Accepted, new SimulationInstant(10),
                         MovementTransitionRejectionReason.None),
                     (MovementTransitionOutcome.Remapped, new SimulationInstant(14),
                         MovementTransitionRejectionReason.None),
                     (MovementTransitionOutcome.Rejected, SimulationInstant.Zero,
                         MovementTransitionRejectionReason.InvalidState),
                     (MovementTransitionOutcome.Superseded, SimulationInstant.Zero,
                         MovementTransitionRejectionReason.Superseded),
                 })
        {
            var scope = Scope();
            var authority = new AuthorityMovementTransitionJournal(scope);
            var intent = Intent(scope, 1, MovementTransitionKind.JumpPressed, 10, 15);
            authority.Observe(intent);

            Assert.ThrowsAny<ArgumentException>(() => authority.Resolve(
                intent.Identity,
                request.Item1,
                request.Item2,
                request.Item3,
                new SimulationInstant(100),
                out _));
            Assert.Equal(1, authority.PendingCount);
            Assert.Equal(0, authority.TombstoneCount);
        }
    }

    [Fact]
    public void ResolutionCursorCleansTombstonesButDedupWindowRemains()
    {
        var scope = Scope();
        var authority = new AuthorityMovementTransitionJournal(scope);
        var first = Intent(scope, 1, MovementTransitionKind.JumpPressed, 10, 15);
        var second = Intent(scope, 2, MovementTransitionKind.JumpReleased, 11, 16);
        authority.Observe(first);
        authority.Observe(second);
        var firstResolution = AssertResolution(
            authority, first, MovementTransitionOutcome.Accepted,
            first.FirstPredictedFrame, MovementTransitionRejectionReason.None);
        var secondResolution = AssertResolution(
            authority, second, MovementTransitionOutcome.Rejected,
            default, MovementTransitionRejectionReason.InvalidState);
        Span<MovementTransitionResolution> outbound =
            stackalloc MovementTransitionResolution[2];

        Assert.Equal(2, authority.CopyUnacknowledgedResolutions(outbound));
        Assert.Equal(firstResolution, outbound[0]);
        Assert.Equal(secondResolution, outbound[1]);
        Assert.False(authority.AcknowledgeResolutions(new TransitionResolutionIdentity(
            scope, new TransitionResolutionSequence(3))));
        Assert.True(authority.AcknowledgeResolutions(firstResolution.ResolutionIdentity));
        Assert.Equal(1, authority.TombstoneCount);
        Assert.True(authority.AcknowledgeResolutions(secondResolution.ResolutionIdentity));
        Assert.Equal(0, authority.EntryCount);
        Assert.Equal(AuthorityTransitionObserveDecision.Duplicate, authority.Observe(first));
        Assert.Equal(AuthorityTransitionObserveDecision.Duplicate, authority.Observe(second));
    }

    [Fact]
    public void ResolutionAcknowledgementIsScopeBoundAndInvalidAckDoesNotMutate()
    {
        var scope = Scope(control: 2);
        var oldScope = Scope(control: 1);
        var authority = new AuthorityMovementTransitionJournal(scope);
        var intent = Intent(scope, 1, MovementTransitionKind.JumpPressed, 10, 15);
        authority.Observe(intent);
        var resolution = AssertResolution(
            authority, intent, MovementTransitionOutcome.Accepted,
            intent.FirstPredictedFrame, MovementTransitionRejectionReason.None);

        Assert.False(authority.AcknowledgeResolutions(
            new TransitionResolutionIdentity(
                oldScope,
                resolution.ResolutionIdentity.Sequence)));
        Assert.Equal(1, authority.TombstoneCount);
        Assert.Null(authority.LastAcknowledgedResolution);
        Assert.True(authority.AcknowledgeResolutions(resolution.ResolutionIdentity));
        Assert.Equal(0, authority.TombstoneCount);
        Assert.Equal(resolution.ResolutionIdentity, authority.LastAcknowledgedResolution);
    }

    [Fact]
    public void PartialResolutionRepairAlwaysStartsWithOldestSequence()
    {
        var scope = Scope();
        var authority = new AuthorityMovementTransitionJournal(scope);
        var firstEntry = Intent(scope, 1, MovementTransitionKind.JumpPressed, 10, 15);
        var secondEntry = Intent(scope, 2, MovementTransitionKind.JumpReleased, 11, 16);
        authority.Observe(firstEntry);
        authority.Observe(secondEntry);
        var sequenceOne = AssertResolution(
            authority, secondEntry, MovementTransitionOutcome.Rejected,
            default, MovementTransitionRejectionReason.InvalidState);
        var sequenceTwo = AssertResolution(
            authority, firstEntry, MovementTransitionOutcome.Accepted,
            firstEntry.FirstPredictedFrame, MovementTransitionRejectionReason.None);
        Span<MovementTransitionResolution> one = stackalloc MovementTransitionResolution[1];

        Assert.Equal(1, authority.CopyUnacknowledgedResolutions(one));
        Assert.Equal(sequenceOne, one[0]);
        Assert.True(authority.AcknowledgeResolutions(sequenceOne.ResolutionIdentity));
        Assert.Equal(1, authority.CopyUnacknowledgedResolutions(one));
        Assert.Equal(sequenceTwo, one[0]);
    }

    [Fact]
    public void ClientAndAuthorityCapStorageWithoutConsumingRejectedIds()
    {
        var scope = Scope();
        var client = new MovementTransitionJournal(scope, capacity: 1);
        Assert.Equal(
            MovementTransitionOriginDecision.Added,
            client.TryOriginate(Input(scope, 1), MovementTransitionKind.JumpPressed,
                new SimulationInstant(1), new SimulationInstant(3), out var first));
        Assert.Equal(
            MovementTransitionOriginDecision.CapacityExceeded,
            client.TryOriginate(Input(scope, 2), MovementTransitionKind.JumpReleased,
                new SimulationInstant(2), new SimulationInstant(4), out _));
        Assert.Equal(new MovementTransitionId(1), first.Identity.Id);

        var clientResolution = Resolution(
            scope, 1, first.Identity, MovementTransitionOutcome.Rejected,
            default, MovementTransitionRejectionReason.InvalidState,
            first.FirstPredictedFrame);
        Assert.Equal(
            ClientTransitionResolutionDecision.Applied,
            client.ApplyResolution(clientResolution, out _));
        Assert.Equal(
            MovementTransitionOriginDecision.Added,
            client.TryOriginate(Input(scope, 2), MovementTransitionKind.JumpReleased,
                new SimulationInstant(2), new SimulationInstant(4), out var second));
        Assert.Equal(new MovementTransitionId(2), second.Identity.Id);

        var authority = new AuthorityMovementTransitionJournal(scope, capacity: 1);
        Assert.Equal(AuthorityTransitionObserveDecision.FirstSeen, authority.Observe(first));
        Assert.Equal(
            AuthorityTransitionObserveDecision.CapacityExceeded,
            authority.Observe(second));
        var terminal = AssertResolution(
            authority, first, MovementTransitionOutcome.Rejected,
            default, MovementTransitionRejectionReason.InvalidState);
        Assert.True(authority.AcknowledgeResolutions(terminal.ResolutionIdentity));
        Assert.Equal(AuthorityTransitionObserveDecision.FirstSeen, authority.Observe(second));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MovementTransitionJournal(scope, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AuthorityMovementTransitionJournal(
                scope,
                MovementTransitionJournalLimits.MaximumCapacity + 1));
    }

    [Fact]
    public void ReorderWindowsFailClosedWithoutMutatingState()
    {
        var scope = Scope();
        var authority = new AuthorityMovementTransitionJournal(
            scope,
            new MovementTransitionJournalPolicy(
                MovementTransitionJournalLimits.DefaultCapacity,
                new SimulationDuration(120),
                new SimulationDuration(5)));
        var farIntent = Intent(scope, 65, MovementTransitionKind.JumpPressed, 10, 15);
        Assert.Equal(
            AuthorityTransitionObserveDecision.TooFarAhead,
            authority.Observe(farIntent));
        Assert.Equal(0, authority.EntryCount);

        var client = new MovementTransitionJournal(scope);
        client.TryOriginate(Input(scope, 1), MovementTransitionKind.JumpPressed,
            new SimulationInstant(10), new SimulationInstant(15), out var intent);
        var farResolution = Resolution(
            scope,
            65,
            intent.Identity,
            MovementTransitionOutcome.Rejected,
            default,
            MovementTransitionRejectionReason.InvalidState,
            intent.FirstPredictedFrame);
        Assert.Equal(
            ClientTransitionResolutionDecision.TooFarAhead,
            client.ApplyResolution(farResolution, out _));
        Assert.Equal(1, client.OutstandingCount);
        Assert.Null(client.AppliedResolutionCursor);
    }

    [Fact]
    public void ReorderWindowsAcceptExactEdgeAndRejectFirstValueBeyondIt()
    {
        var scope = Scope();
        var authorityAtEdge = new AuthorityMovementTransitionJournal(scope);
        Assert.Equal(
            AuthorityTransitionObserveDecision.FirstSeen,
            authorityAtEdge.Observe(Intent(
                scope, 64, MovementTransitionKind.JumpPressed, 10, 15)));
        var authorityBeyond = new AuthorityMovementTransitionJournal(scope);
        Assert.Equal(
            AuthorityTransitionObserveDecision.TooFarAhead,
            authorityBeyond.Observe(Intent(
                scope, 65, MovementTransitionKind.JumpPressed, 10, 15)));

        var clientAtEdge = new MovementTransitionJournal(scope);
        clientAtEdge.TryOriginate(Input(scope, 1), MovementTransitionKind.JumpPressed,
            new SimulationInstant(10), new SimulationInstant(15), out var intentAtEdge);
        Assert.Equal(
            ClientTransitionResolutionDecision.Applied,
            clientAtEdge.ApplyResolution(Resolution(
                scope, 64, intentAtEdge.Identity, MovementTransitionOutcome.Rejected,
                default, MovementTransitionRejectionReason.InvalidState,
                intentAtEdge.FirstPredictedFrame), out _));
        Assert.Null(clientAtEdge.AppliedResolutionCursor);

        var clientBeyond = new MovementTransitionJournal(scope);
        clientBeyond.TryOriginate(Input(scope, 1), MovementTransitionKind.JumpPressed,
            new SimulationInstant(10), new SimulationInstant(15), out var intentBeyond);
        Assert.Equal(
            ClientTransitionResolutionDecision.TooFarAhead,
            clientBeyond.ApplyResolution(Resolution(
                scope, 65, intentBeyond.Identity, MovementTransitionOutcome.Rejected,
                default, MovementTransitionRejectionReason.InvalidState,
                intentBeyond.FirstPredictedFrame), out _));
        Assert.True(clientBeyond.RequiresBaselineRepair);
        Assert.Equal(1, clientBeyond.OutstandingCount);
    }

    [Fact]
    public void ScopeResetIsIdempotentAndClearsAllOldEpochState()
    {
        var oldScope = Scope(control: 1);
        var newScope = Scope(control: 2);
        var client = new MovementTransitionJournal(oldScope);
        var authority = new AuthorityMovementTransitionJournal(oldScope);
        client.TryOriginate(Input(oldScope, 1), MovementTransitionKind.JumpPressed,
            new SimulationInstant(1), new SimulationInstant(3), out var oldIntent);
        authority.Observe(oldIntent);
        var oldResolution = AssertResolution(
            authority, oldIntent, MovementTransitionOutcome.Rejected,
            default, MovementTransitionRejectionReason.InvalidState);
        client.ApplyResolution(oldResolution, out _);

        Assert.False(client.Reset(oldScope));
        Assert.False(authority.Reset(oldScope));
        Assert.True(client.Reset(newScope));
        Assert.True(authority.Reset(newScope));

        Assert.Equal(0, client.OutstandingCount);
        Assert.Null(client.AppliedResolutionCursor);
        Assert.Equal(0, authority.EntryCount);
        Assert.Null(authority.LastAcknowledgedResolution);
        Assert.False(authority.RequiresBaselineRepair);
        Assert.Equal(
            ClientTransitionResolutionDecision.WrongScope,
            client.ApplyResolution(oldResolution, out _));
        Assert.Equal(
            AuthorityTransitionObserveDecision.WrongScope,
            authority.Observe(oldIntent));
        Assert.Equal(
            MovementTransitionOriginDecision.Added,
            client.TryOriginate(Input(newScope, 1), MovementTransitionKind.JumpPressed,
                SimulationInstant.Zero, new SimulationInstant(2), out var newIntent));
        Assert.Equal(MovementTransitionId.Initial, newIntent.Identity.Id);
    }

    [Fact]
    public void ImpossibleUnknownResolutionLocksSameScopeUntilNewControlEpoch()
    {
        var oldScope = Scope(control: 1);
        var newScope = Scope(control: 2);
        var client = new MovementTransitionJournal(oldScope);
        Assert.Equal(
            MovementTransitionOriginDecision.Added,
            client.TryOriginate(
                Input(oldScope, 1),
                MovementTransitionKind.JumpPressed,
                SimulationInstant.Zero,
                new SimulationInstant(2),
                out var outstanding));
        var unknown = Resolution(
            oldScope,
            1,
            new MovementTransitionIdentity(oldScope, new MovementTransitionId(9)),
            MovementTransitionOutcome.Rejected,
            default,
            MovementTransitionRejectionReason.InvalidState,
            new SimulationInstant(1));

        Assert.Equal(
            ClientTransitionResolutionDecision.UnknownTransition,
            client.ApplyResolution(unknown, out _));
        Assert.True(client.RequiresBaselineRepair);
        Assert.Equal(1, client.OutstandingCount);
        Assert.Equal(
            MovementTransitionOriginDecision.BaselineRepairRequired,
            client.TryOriginate(
                Input(oldScope, 2),
                MovementTransitionKind.JumpReleased,
                new SimulationInstant(1),
                new SimulationInstant(3),
                out var blocked));
        Assert.Equal(default, blocked);
        Assert.Equal(1, client.OutstandingCount);
        Span<MovementTransitionIntent> resend = stackalloc MovementTransitionIntent[1];
        resend[0] = outstanding;
        Assert.Equal(0, client.CopyOutstanding(resend));
        Assert.Equal(outstanding, resend[0]);
        Assert.False(client.Reset(oldScope));
        Assert.True(client.RequiresBaselineRepair);
        Assert.Equal(
            ClientTransitionResolutionDecision.BaselineRepairRequired,
            client.ApplyResolution(unknown, out _));
        Assert.True(client.Reset(newScope));
        Assert.False(client.RequiresBaselineRepair);
        Assert.Equal(
            MovementTransitionOriginDecision.Added,
            client.TryOriginate(
                Input(newScope, 1),
                MovementTransitionKind.JumpPressed,
                SimulationInstant.Zero,
                new SimulationInstant(2),
                out var recovered));
        Assert.Equal(MovementTransitionId.Initial, recovered.Identity.Id);
    }

    [Fact]
    public void ClientValidatesExpiredAndSupersededDecisionFrameBoundaries()
    {
        static void AssertDecision(
            MovementTransitionOutcome outcome,
            long decisionTick,
            ClientTransitionResolutionDecision expected)
        {
            var scope = Scope();
            var client = new MovementTransitionJournal(scope);
            client.TryOriginate(
                Input(scope, 1),
                MovementTransitionKind.JumpPressed,
                new SimulationInstant(10),
                new SimulationInstant(15),
                out var intent);
            var reason = outcome == MovementTransitionOutcome.Expired
                ? MovementTransitionRejectionReason.DeadlineExpired
                : MovementTransitionRejectionReason.Superseded;
            var resolution = Resolution(
                scope,
                1,
                intent.Identity,
                outcome,
                default,
                reason,
                new SimulationInstant(decisionTick));

            Assert.Equal(expected, client.ApplyResolution(resolution, out _));
            Assert.Equal(
                expected == ClientTransitionResolutionDecision.Applied ? 0 : 1,
                client.OutstandingCount);
            Assert.Equal(
                expected == ClientTransitionResolutionDecision.InvalidResolution,
                client.RequiresBaselineRepair);
        }

        AssertDecision(
            MovementTransitionOutcome.Expired,
            15,
            ClientTransitionResolutionDecision.InvalidResolution);
        AssertDecision(
            MovementTransitionOutcome.Expired,
            16,
            ClientTransitionResolutionDecision.Applied);
        AssertDecision(
            MovementTransitionOutcome.Superseded,
            9,
            ClientTransitionResolutionDecision.InvalidResolution);
        AssertDecision(
            MovementTransitionOutcome.Superseded,
            10,
            ClientTransitionResolutionDecision.Applied);
        AssertDecision(
            MovementTransitionOutcome.Superseded,
            12,
            ClientTransitionResolutionDecision.Applied);
        AssertDecision(
            MovementTransitionOutcome.Superseded,
            16,
            ClientTransitionResolutionDecision.InvalidResolution);
    }

    [Fact]
    public void EmptyAuthorityBaselineRequiresAContiguousAcknowledgedCursor()
    {
        var scope = Scope(control: 2);
        var otherScope = Scope(control: 3);
        var policy = MovementTransitionJournalPolicy.Default;

        var initial = AuthorityMovementTransitionJournal.RestoreEmptyBaseline(
            scope,
            policy,
            TransitionResolutionSequence.Initial,
            acknowledgedThrough: null);
        Assert.Null(initial.LastAcknowledgedResolution);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AuthorityMovementTransitionJournal.RestoreEmptyBaseline(
                scope,
                policy,
                new TransitionResolutionSequence(100),
                acknowledgedThrough: null));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AuthorityMovementTransitionJournal.RestoreEmptyBaseline(
                scope,
                policy,
                new TransitionResolutionSequence(100),
                new TransitionResolutionIdentity(
                    scope,
                    TransitionResolutionSequence.Initial)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AuthorityMovementTransitionJournal.RestoreEmptyBaseline(
                scope,
                policy,
                new TransitionResolutionSequence(100),
                new TransitionResolutionIdentity(
                    otherScope,
                    new TransitionResolutionSequence(99))));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AuthorityMovementTransitionJournal.RestoreEmptyBaseline(
                scope,
                policy,
                TransitionResolutionSequence.Initial,
                new TransitionResolutionIdentity(
                    scope,
                    TransitionResolutionSequence.Initial)));

        var acknowledged = new TransitionResolutionIdentity(
            scope,
            new TransitionResolutionSequence(99));
        var client = MovementTransitionJournal.RestoreEmptyBaseline(
            scope,
            policy,
            new MovementTransitionId(5),
            acknowledged);
        var authority = AuthorityMovementTransitionJournal.RestoreEmptyBaseline(
            scope,
            policy,
            new TransitionResolutionSequence(100),
            acknowledged,
            new MovementTransitionId(4));
        client.TryOriginate(
            Input(scope, 5),
            MovementTransitionKind.JumpPressed,
            new SimulationInstant(10),
            new SimulationInstant(15),
            out var intent);

        Assert.Equal(AuthorityTransitionObserveDecision.FirstSeen, authority.Observe(intent));
        var resolution = AssertResolution(
            authority,
            intent,
            MovementTransitionOutcome.Accepted,
            intent.FirstPredictedFrame,
            MovementTransitionRejectionReason.None);
        Assert.Equal(100UL, resolution.ResolutionIdentity.Sequence.Value);
        Assert.Equal(
            ClientTransitionResolutionDecision.Applied,
            client.ApplyResolution(resolution, out _));
        Assert.Equal(resolution.ResolutionIdentity, client.AppliedResolutionCursor);
    }

    [Fact]
    public void RetiredResolutionSequenceCannotResolveALiveOutstandingTransition()
    {
        var scope = Scope(control: 2);
        var cursor = new TransitionResolutionIdentity(
            scope,
            new TransitionResolutionSequence(99));
        var restored = MovementTransitionJournal.RestoreEmptyBaseline(
            scope,
            MovementTransitionJournalPolicy.Default,
            new MovementTransitionId(5),
            cursor);
        restored.TryOriginate(
            Input(scope, 5),
            MovementTransitionKind.JumpPressed,
            SimulationInstant.Zero,
            new SimulationInstant(1),
            out var restoredLive);
        var staleForRestoredLive = Resolution(
            scope,
            99,
            restoredLive.Identity,
            MovementTransitionOutcome.Accepted,
            SimulationInstant.Zero);

        Assert.Equal(
            ClientTransitionResolutionDecision.ConflictingResolution,
            restored.ApplyResolution(staleForRestoredLive, out _));
        Assert.True(restored.RequiresBaselineRepair);
        Assert.Equal(1, restored.OutstandingCount);
        Assert.Equal(cursor, restored.AppliedResolutionCursor);

        var aged = new MovementTransitionJournal(scope);
        MovementTransitionResolution oldestResolution = default;
        for (ulong sequence = 1;
             sequence <= (ulong)MovementTransitionJournalLimits.ResolutionReorderWindow + 1;
             sequence++)
        {
            aged.TryOriginate(
                Input(scope, sequence),
                MovementTransitionKind.JumpPressed,
                SimulationInstant.Zero,
                new SimulationInstant(1),
                out var retired);
            var terminal = Resolution(
                scope,
                sequence,
                retired.Identity,
                MovementTransitionOutcome.Accepted,
                SimulationInstant.Zero);
            oldestResolution = sequence == 1 ? terminal : oldestResolution;
            Assert.Equal(
                ClientTransitionResolutionDecision.Applied,
                aged.ApplyResolution(terminal, out _));
        }
        aged.TryOriginate(
            Input(scope, 66),
            MovementTransitionKind.JumpPressed,
            SimulationInstant.Zero,
            new SimulationInstant(1),
            out var agedLive);
        var staleForAgedLive = Resolution(
            scope,
            1,
            agedLive.Identity,
            MovementTransitionOutcome.Accepted,
            SimulationInstant.Zero);

        Assert.Equal(
            ClientTransitionResolutionDecision.Duplicate,
            aged.ApplyResolution(oldestResolution, out _));
        Assert.False(aged.RequiresBaselineRepair);
        Assert.Equal(1, aged.OutstandingCount);
        Assert.Equal(
            ClientTransitionResolutionDecision.ConflictingResolution,
            aged.ApplyResolution(staleForAgedLive, out _));
        Assert.True(aged.RequiresBaselineRepair);
        Assert.Equal(1, aged.OutstandingCount);
        Assert.Equal(
            new TransitionResolutionIdentity(
                scope,
                new TransitionResolutionSequence(65)),
            aged.AppliedResolutionCursor);
    }

    [Fact]
    public void UnacknowledgedTombstoneTimeoutRequiresBaselineRepair()
    {
        var scope = Scope();
        var authority = new AuthorityMovementTransitionJournal(
            scope,
            new MovementTransitionJournalPolicy(
                MovementTransitionJournalLimits.DefaultCapacity,
                new SimulationDuration(120),
                new SimulationDuration(5)));
        var intent = Intent(scope, 1, MovementTransitionKind.JumpPressed, 10, 15);
        authority.Observe(intent);
        AssertResolution(
            authority, intent, MovementTransitionOutcome.Accepted,
            intent.FirstPredictedFrame, MovementTransitionRejectionReason.None,
            recordedAt: 10);

        Assert.False(authority.CheckTombstoneRetention(
            new SimulationInstant(15)));
        Assert.True(authority.CheckTombstoneRetention(
            new SimulationInstant(16)));
        Assert.True(authority.RequiresBaselineRepair);
        Assert.Equal(
            AuthorityTransitionObserveDecision.BaselineRepairRequired,
            authority.Observe(Intent(scope, 2, MovementTransitionKind.JumpReleased, 13, 20)));
        Assert.Equal(
            AuthorityTransitionResolveDecision.BaselineRepairRequired,
            authority.Resolve(
                intent.Identity,
                MovementTransitionOutcome.Accepted,
                intent.FirstPredictedFrame,
                MovementTransitionRejectionReason.None,
                new SimulationInstant(16),
                out _));
    }

    [Fact]
    public void ZeroRetentionTriggersOnlyAfterDecisionFrameAndHandlesMaxFrame()
    {
        var scope = Scope();
        var policy = new MovementTransitionJournalPolicy(
            MovementTransitionJournalLimits.DefaultCapacity,
            new SimulationDuration(1),
            SimulationDuration.Zero);
        var authority = new AuthorityMovementTransitionJournal(scope, policy);
        var intent = Intent(scope, 1, MovementTransitionKind.JumpPressed, 0, 1);
        authority.Observe(intent);
        AssertResolution(
            authority, intent, MovementTransitionOutcome.Accepted,
            SimulationInstant.Zero, MovementTransitionRejectionReason.None,
            recordedAt: 0);

        Assert.False(authority.CheckTombstoneRetention(SimulationInstant.Zero));
        Assert.True(authority.CheckTombstoneRetention(new SimulationInstant(1)));

        var maxAuthority = new AuthorityMovementTransitionJournal(scope, policy);
        var maxIntent = Intent(
            scope,
            1,
            MovementTransitionKind.JumpPressed,
            long.MaxValue,
            long.MaxValue);
        maxAuthority.Observe(maxIntent);
        AssertResolution(
            maxAuthority, maxIntent, MovementTransitionOutcome.Accepted,
            new SimulationInstant(long.MaxValue),
            MovementTransitionRejectionReason.None,
            recordedAt: long.MaxValue);
        Assert.False(maxAuthority.CheckTombstoneRetention(
            new SimulationInstant(long.MaxValue)));
    }

    [Fact]
    public void SharedPolicyBoundsIntentLifetimeOnClientAndAuthority()
    {
        var scope = Scope();
        var policy = new MovementTransitionJournalPolicy(
            4,
            new SimulationDuration(5),
            new SimulationDuration(10));
        var client = new MovementTransitionJournal(scope, policy);
        Assert.Throws<ArgumentOutOfRangeException>(() => client.TryOriginate(
            Input(scope, 1), MovementTransitionKind.JumpPressed,
            SimulationInstant.Zero, new SimulationInstant(6), out _));
        Assert.Equal(0, client.OutstandingCount);

        var authority = new AuthorityMovementTransitionJournal(scope, policy);
        Assert.Equal(
            AuthorityTransitionObserveDecision.InvalidIntent,
            authority.Observe(Intent(
                scope, 1, MovementTransitionKind.JumpPressed, 0, 6)));
        Assert.Equal(0, authority.EntryCount);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MovementTransitionJournalPolicy(
                1,
                new SimulationDuration(
                    MovementTransitionJournalLimits.MaximumValidityTicks + 1),
                SimulationDuration.Zero));
    }

    [Fact]
    public void CounterExhaustionFailsClosedAndNeverWrapsIdentity()
    {
        var scope = Scope(control: 2);
        var policy = MovementTransitionJournalPolicy.Default;
        var client = MovementTransitionJournal.RestoreEmptyBaseline(
            scope,
            policy,
            new MovementTransitionId(ulong.MaxValue));
        Assert.Equal(
            MovementTransitionOriginDecision.Added,
            client.TryOriginate(
                Input(scope, 1),
                MovementTransitionKind.JumpPressed,
                SimulationInstant.Zero,
                new SimulationInstant(1),
                out var maximumIntent));
        Assert.Equal(ulong.MaxValue, maximumIntent.Identity.Id.Value);
        Assert.Equal(
            MovementTransitionOriginDecision.IdentityExhausted,
            client.TryOriginate(
                Input(scope, 2),
                MovementTransitionKind.JumpReleased,
                new SimulationInstant(1),
                new SimulationInstant(2),
                out _));

        var authority = AuthorityMovementTransitionJournal.RestoreEmptyBaseline(
            scope,
            policy,
            new TransitionResolutionSequence(ulong.MaxValue),
            new TransitionResolutionIdentity(
                scope,
                new TransitionResolutionSequence(ulong.MaxValue - 1)));
        var first = Intent(scope, 1, MovementTransitionKind.JumpPressed, 0, 1);
        var second = Intent(scope, 2, MovementTransitionKind.JumpReleased, 1, 2);
        Assert.Equal(AuthorityTransitionObserveDecision.FirstSeen, authority.Observe(first));
        Assert.Equal(
            AuthorityTransitionResolveDecision.Resolved,
            authority.Resolve(
                first.Identity,
                MovementTransitionOutcome.Accepted,
                SimulationInstant.Zero,
                MovementTransitionRejectionReason.None,
                SimulationInstant.Zero,
                out var maximumResolution));
        Assert.Equal(ulong.MaxValue, maximumResolution.ResolutionIdentity.Sequence.Value);
        Assert.Equal(AuthorityTransitionObserveDecision.FirstSeen, authority.Observe(second));
        Assert.Equal(
            AuthorityTransitionResolveDecision.BaselineRepairRequired,
            authority.Resolve(
                second.Identity,
                MovementTransitionOutcome.Accepted,
                second.FirstPredictedFrame,
                MovementTransitionRejectionReason.None,
                second.FirstPredictedFrame,
                out _));
        Assert.True(authority.RequiresBaselineRepair);
    }

    [Fact]
    public void ExhaustedResolutionCounterAcceptsPartialAckAndRepairsLostFinalResult()
    {
        var scope = Scope(control: 2);
        var policy = MovementTransitionJournalPolicy.Default.WithCapacity(
            MovementTransitionJournalLimits.MaximumCapacity);
        const ulong firstResolutionSequence =
            ulong.MaxValue - MovementTransitionJournalLimits.MaximumCapacity + 1;
        var acknowledgedBeforeWindow = new TransitionResolutionIdentity(
            scope,
            new TransitionResolutionSequence(firstResolutionSequence - 1));
        var authority = AuthorityMovementTransitionJournal.RestoreEmptyBaseline(
            scope,
            policy,
            new TransitionResolutionSequence(firstResolutionSequence),
            acknowledgedBeforeWindow);

        MovementTransitionResolution final = default;
        for (var index = 1; index <= MovementTransitionJournalLimits.MaximumCapacity; index++)
        {
            var intent = Intent(
                scope,
                (ulong)index,
                MovementTransitionKind.JumpPressed,
                0,
                1);
            Assert.Equal(
                AuthorityTransitionObserveDecision.FirstSeen,
                authority.Observe(intent));
            final = AssertResolution(
                authority,
                intent,
                MovementTransitionOutcome.Accepted,
                SimulationInstant.Zero,
                MovementTransitionRejectionReason.None,
                recordedAt: 0);
        }

        Assert.Equal(ulong.MaxValue, final.ResolutionIdentity.Sequence.Value);
        Assert.Equal(MovementTransitionJournalLimits.MaximumCapacity, authority.TombstoneCount);
        var partialCursor = new TransitionResolutionIdentity(
            scope,
            new TransitionResolutionSequence(ulong.MaxValue - 1));
        Assert.True(authority.AcknowledgeResolutions(partialCursor));
        Assert.Equal(1, authority.TombstoneCount);
        Assert.Equal(partialCursor, authority.LastAcknowledgedResolution);

        Span<MovementTransitionResolution> repair =
            stackalloc MovementTransitionResolution[1];
        Assert.Equal(1, authority.CopyUnacknowledgedResolutions(repair));
        Assert.Equal(final, repair[0]);
        Assert.True(authority.AcknowledgeResolutions(final.ResolutionIdentity));
        Assert.Equal(0, authority.EntryCount);
        Assert.Equal(final.ResolutionIdentity, authority.LastAcknowledgedResolution);
    }

    [Fact]
    public void IntentAndResolutionValidationRejectMalformedState()
    {
        var scope = Scope();
        var other = Scope(control: 2);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MovementTransitionIntent(
                new MovementTransitionIdentity(scope, MovementTransitionId.Initial),
                Input(other, 1),
                MovementTransitionKind.JumpPressed,
                new SimulationInstant(2),
                new SimulationInstant(3)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MovementTransitionIntent(
                new MovementTransitionIdentity(scope, MovementTransitionId.Initial),
                Input(scope, 1),
                (MovementTransitionKind)0,
                new SimulationInstant(2),
                new SimulationInstant(3)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MovementTransitionIntent(
                new MovementTransitionIdentity(scope, MovementTransitionId.Initial),
                Input(scope, 1),
                MovementTransitionKind.JumpPressed,
                new SimulationInstant(3),
                new SimulationInstant(2)));

        var transition = new MovementTransitionIdentity(scope, MovementTransitionId.Initial);
        Assert.Throws<ArgumentException>(() => new MovementTransitionResolution(
            new TransitionResolutionIdentity(scope, TransitionResolutionSequence.Initial),
            transition,
            MovementTransitionOutcome.Accepted,
            false,
            new SimulationInstant(2),
            new SimulationInstant(2),
            MovementTransitionRejectionReason.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovementTransitionResolution(
            new TransitionResolutionIdentity(scope, TransitionResolutionSequence.Initial),
            transition,
            MovementTransitionOutcome.Expired,
            false,
            default,
            new SimulationInstant(4),
            MovementTransitionRejectionReason.None));
        Assert.Throws<ArgumentException>(() => new MovementTransitionResolution(
            new TransitionResolutionIdentity(scope, TransitionResolutionSequence.Initial),
            transition,
            MovementTransitionOutcome.Rejected,
            false,
            new SimulationInstant(2),
            new SimulationInstant(2),
            MovementTransitionRejectionReason.InvalidState));
    }

    [Fact]
    public void ResolutionShapeRejectsEveryUnknownOrIncoherentEnumPairing()
    {
        var scope = Scope();
        var transition = new MovementTransitionIdentity(
            scope,
            MovementTransitionId.Initial);
        var resolutionIdentity = new TransitionResolutionIdentity(
            scope,
            TransitionResolutionSequence.Initial);
        var outcomes = Enum.GetValues<MovementTransitionOutcome>()
            .Select(value => (int)value)
            .Append(0)
            .Append(255)
            .Distinct();
        var reasons = Enum.GetValues<MovementTransitionRejectionReason>()
            .Select(value => (int)value)
            .Append(255)
            .Distinct();

        foreach (var outcomeValue in outcomes)
        foreach (var reasonValue in reasons)
        foreach (var hasApplicationFrame in new[] { false, true })
        {
            var outcome = (MovementTransitionOutcome)outcomeValue;
            var reason = (MovementTransitionRejectionReason)reasonValue;
            var shouldSucceed = (outcome, reason, hasApplicationFrame) switch
            {
                (MovementTransitionOutcome.Accepted or MovementTransitionOutcome.Remapped,
                    MovementTransitionRejectionReason.None, true) => true,
                (MovementTransitionOutcome.Rejected,
                    MovementTransitionRejectionReason.AuthorityPolicyRejected or
                    MovementTransitionRejectionReason.InvalidState or
                    MovementTransitionRejectionReason.CapabilityUnavailable, false) => true,
                (MovementTransitionOutcome.Expired,
                    MovementTransitionRejectionReason.DeadlineExpired, false) => true,
                (MovementTransitionOutcome.Superseded,
                    MovementTransitionRejectionReason.Superseded, false) => true,
                _ => false,
            };

            void Construct() => _ = new MovementTransitionResolution(
                resolutionIdentity,
                transition,
                outcome,
                hasApplicationFrame,
                hasApplicationFrame ? new SimulationInstant(2) : default,
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

    private static MovementTransitionResolution AssertResolution(
        AuthorityMovementTransitionJournal authority,
        MovementTransitionIntent intent,
        MovementTransitionOutcome outcome,
        SimulationInstant applicationFrame,
        MovementTransitionRejectionReason reason,
        long? recordedAt = null)
    {
        var decisionFrame = recordedAt is not null
            ? new SimulationInstant(recordedAt.Value)
            : outcome switch
            {
                MovementTransitionOutcome.Accepted or MovementTransitionOutcome.Remapped =>
                    applicationFrame,
                MovementTransitionOutcome.Expired =>
                    new SimulationInstant(intent.LastValidFrame.Tick + 1),
                _ => intent.FirstPredictedFrame,
            };
        Assert.Equal(
            AuthorityTransitionResolveDecision.Resolved,
            authority.Resolve(
                intent.Identity,
                outcome,
                applicationFrame,
                reason,
                decisionFrame,
                out var resolution));
        Assert.Equal(outcome, resolution.Outcome);
        Assert.Equal(intent.Identity, resolution.TransitionIdentity);
        return resolution;
    }

    private static MovementTransitionResolution Resolution(
        OwnerIntentScope scope,
        ulong sequence,
        MovementTransitionIdentity transition,
        MovementTransitionOutcome outcome,
        SimulationInstant applicationFrame,
        MovementTransitionRejectionReason reason = MovementTransitionRejectionReason.None,
        SimulationInstant? decisionFrame = null) => new(
            new TransitionResolutionIdentity(scope, new TransitionResolutionSequence(sequence)),
            transition,
            outcome,
            outcome is MovementTransitionOutcome.Accepted or MovementTransitionOutcome.Remapped,
            applicationFrame,
            decisionFrame ?? (outcome is MovementTransitionOutcome.Accepted or
                MovementTransitionOutcome.Remapped ? applicationFrame : SimulationInstant.Zero),
            reason);

    private static MovementTransitionIntent Intent(
        OwnerIntentScope scope,
        ulong id,
        MovementTransitionKind kind,
        long firstFrame,
        long lastFrame) => new(
            new MovementTransitionIdentity(scope, new MovementTransitionId(id)),
            Input(scope, id),
            kind,
            new SimulationInstant(firstFrame),
            new SimulationInstant(lastFrame));

    private static OwnerInputIdentity Input(OwnerIntentScope scope, ulong sequence) =>
        new(scope, new InputSequence(sequence));

    private static OwnerIntentScope Scope(ulong control = 1) => new(
        100,
        new LifeEpoch(new CombatantId(7), new LifeGenerationId(3)),
        new OwnerControlEpoch(control));
}
