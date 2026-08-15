using BattleArena.Core.Common;
using BattleArena.Core.Movement.Simulation;
using BattleArena.Multiplayer.OwnerPrediction;

namespace BattleArena.Multiplayer.Tests.OwnerPrediction;

public sealed class OwnerInputSendWindowTests
{
    [Fact]
    public void DefaultsReserveAtLeastOneHundredMillisecondsAtSixtyHertz()
    {
        var policy = OwnerInputSendWindowPolicy.Default;

        Assert.Equal(6, policy.MinimumRecentCommands);
        Assert.True(policy.MaximumCommandsPerBatch >= policy.MinimumRecentCommands);
        Assert.True(policy.Capacity >= policy.MaximumCommandsPerBatch);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OwnerInputSendWindowPolicy(0, 1, 1, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OwnerInputSendWindowPolicy(8, 7, 6, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OwnerInputSendWindowPolicy(8, 1, 9, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OwnerInputSendWindowPolicy(8, 1, 1, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OwnerInputSendWindowPolicy(8, 1, 1, 1, 1));
    }

    [Fact]
    public void CommandsMustBeScopeBoundAndContiguousWithoutConsumingFailedAdds()
    {
        var scope = Scope();
        var other = Scope(control: 2);
        var policy = Policy(capacity: 2, minimum: 1, maximum: 2);
        var window = new OwnerInputSendWindow(scope, policy);

        Assert.Equal(
            OwnerInputWindowAddDecision.WrongScope,
            window.TryAdd(Command(other, 1, 10)));
        Assert.Equal(
            OwnerInputWindowAddDecision.NonContiguousSequence,
            window.TryAdd(Command(scope, 2, 10)));
        Assert.Equal(OwnerInputWindowAddDecision.Added, window.TryAdd(Command(scope, 1, 10)));
        Assert.Equal(
            OwnerInputWindowAddDecision.NonContiguousSequence,
            window.TryAdd(Command(scope, 3, 11)));
        Assert.Equal(
            OwnerInputWindowAddDecision.NonContiguousTargetFrame,
            window.TryAdd(Command(scope, 2, 12)));
        Assert.Equal(OwnerInputWindowAddDecision.Added, window.TryAdd(Command(scope, 2, 11)));
        Assert.Equal(
            OwnerInputWindowAddDecision.CapacityExceeded,
            window.TryAdd(Command(scope, 3, 12)));
        Assert.Equal(new InputSequence(2), window.LastRegisteredSequence);
        Assert.Equal(new SimulationInstant(11), window.LastRegisteredTargetFrame);

        Assert.Equal(
            OwnerInputProgressDecision.Applied,
            window.ApplyAuthorityProgress(Ack(scope, highest: 1), null));
        Assert.Equal(1, window.Count);
        Assert.Equal(OwnerInputWindowAddDecision.Added, window.TryAdd(Command(scope, 3, 12)));
        Assert.Equal(new InputSequence(3), window.LastRegisteredSequence);
    }

    [Fact]
    public void BurstLossSendsRecentContiguousFramesThenEventuallyEveryNewFrame()
    {
        var scope = Scope();
        var window = new OwnerInputSendWindow(
            scope,
            Policy(capacity: 16, minimum: 6, maximum: 8));
        AddRange(window, scope, 1, 10, firstFrame: 100);
        var sizer = new TestSizer(baseBytes: 10, commandBytes: 10);
        var commands = new OwnerSimulationCommand[8];
        var transitions = new MovementTransitionIntent[1];
        var actions = new PredictedActionIntent[1];

        AssertBuilt(window, scope, sizer, 90, commands, transitions, actions, out var first);
        Assert.Equal(Enumerable.Range(1, 8).Select(value => (ulong)value),
            commands.Take(first.CommandCount).Select(command => command.Sequence.Value));
        Assert.True(first.HasDeferredCommands);

        AssertBuilt(window, scope, sizer, 90, commands, transitions, actions, out var second);
        Assert.Contains(9UL, commands.Take(second.CommandCount).Select(c => c.Sequence.Value));
        Assert.Contains(10UL, commands.Take(second.CommandCount).Select(c => c.Sequence.Value));
        Assert.All(commands.Take(second.CommandCount), command =>
            Assert.InRange(command.TargetFrame.Tick, 100, 109));
    }

    [Fact]
    public void RetryTierRotatesInsteadOfStarvingLaterUnacknowledgedCommands()
    {
        var scope = Scope();
        var window = new OwnerInputSendWindow(
            scope,
            Policy(capacity: 8, minimum: 1, maximum: 2, maximumDeferrals: 64));
        AddRange(window, scope, 1, 4, firstFrame: 10);
        var sizer = new TestSizer(0, 1);
        var commands = new OwnerSimulationCommand[2];
        var transitions = new MovementTransitionIntent[1];
        var actions = new PredictedActionIntent[1];

        for (var packet = 0; packet < 3; packet++)
        {
            AssertBuilt(window, scope, sizer, 2, commands, transitions, actions, out _);
        }

        var retries = new List<ulong>();
        for (var packet = 0; packet < 3; packet++)
        {
            AssertBuilt(window, scope, sizer, 2, commands, transitions, actions, out var batch);
            retries.Add(commands.Take(batch.CommandCount)
                .Single(command => command.Sequence.Value != 1).Sequence.Value);
        }
        Assert.Equal(new ulong[] { 2, 3, 4 }, retries);
    }

    [Fact]
    public void RetryCursorSurvivesPruningBeforeAtAndAfterItsSequence()
    {
        var scope = Scope();
        var window = new OwnerInputSendWindow(
            scope,
            Policy(capacity: 8, minimum: 1, maximum: 2, maximumDeferrals: 64));
        AddRange(window, scope, 1, 6, firstFrame: 10);
        var commands = new OwnerSimulationCommand[2];
        var transitions = new MovementTransitionIntent[1];
        var actions = new PredictedActionIntent[1];
        var sizer = new TestSizer(0, 1);

        // Drain the new-command tier, then advance the stable retry cursor to 2.
        for (var packet = 0; packet < 6; packet++)
        {
            AssertBuilt(window, scope, sizer, 2, commands, transitions, actions, out _);
        }

        // Remove sequence 1 before the cursor, 2 at it, and 4 after it.
        Assert.Equal(
            OwnerInputProgressDecision.Applied,
            window.ApplyAuthorityProgress(Ack(scope, 1, (1UL << 0) | (1UL << 2)), null));
        Assert.Equal(3, window.Count);

        AssertBuilt(window, scope, sizer, 2, commands, transitions, actions, out var first);
        Assert.Equal(new ulong[] { 3, 5 },
            commands.Take(first.CommandCount).Select(command => command.Sequence.Value));
        AssertBuilt(window, scope, sizer, 2, commands, transitions, actions, out var second);
        Assert.Equal(new ulong[] { 3, 6 },
            commands.Take(second.CommandCount).Select(command => command.Sequence.Value));
    }

    [Fact]
    public void ReorderedSelectiveAcksMergeMonotonicallyAndPruneInteriorReceipts()
    {
        var scope = Scope();
        var window = new OwnerInputSendWindow(
            scope,
            Policy(capacity: 16, minimum: 1, maximum: 8));
        AddRange(window, scope, 1, 8, firstFrame: 20);

        // Cursor 1, plus sequences 4, 5, and 7.
        var firstMask = (1UL << 2) | (1UL << 3) | (1UL << 5);
        Assert.Equal(
            OwnerInputProgressDecision.Applied,
            window.ApplyAuthorityProgress(Ack(scope, 1, firstMask), null));
        Assert.Equal(4, window.Count);
        Assert.Equal(new InputSequence(1), window.HighestContiguousReceived);
        Assert.Equal(firstMask, window.FollowingReceivedMask);

        // A delayed ACK supplies sequence 2. It bridges only through 2; sequence
        // 3 remains missing while the previously observed later bits survive.
        Assert.Equal(
            OwnerInputProgressDecision.Applied,
            window.ApplyAuthorityProgress(Ack(scope, null, 1UL << 1), null));
        Assert.Equal(new InputSequence(2), window.HighestContiguousReceived);
        Assert.Equal(3, window.Count);

        var sizer = new TestSizer(0, 1);
        var commands = new OwnerSimulationCommand[8];
        AssertBuilt(
            window, scope, sizer, 8, commands,
            new MovementTransitionIntent[1], new PredictedActionIntent[1],
            out var batch);
        Assert.Equal(new ulong[] { 3, 6, 8 },
            commands.Take(batch.CommandCount).Select(command => command.Sequence.Value));
    }

    [Fact]
    public void SelectiveAckGapThenNewThenRemainingGapFollowDeclaredPriority()
    {
        var scope = Scope();
        var window = new OwnerInputSendWindow(
            scope,
            Policy(capacity: 8, minimum: 1, maximum: 3));
        AddRange(window, scope, 1, 5, firstFrame: 10);
        var sizer = new TestSizer(0, 1);
        var commands = new OwnerSimulationCommand[3];
        var transitionOut = new MovementTransitionIntent[1];
        var actionOut = new PredictedActionIntent[1];
        var initialCommands = new OwnerSimulationCommand[3];
        AssertBuilt(window, scope, sizer, 3, initialCommands,
            transitionOut, actionOut, out _);
        AssertBuilt(window, scope, sizer, 3, initialCommands,
            transitionOut, actionOut, out _);

        // Receive 1, 3, and 5. Missing 2 and 4 are explicit interior gaps.
        Assert.Equal(
            OwnerInputProgressDecision.Applied,
            window.ApplyAuthorityProgress(
                Ack(scope, 1, (1UL << 1) | (1UL << 3)), null));
        Assert.Equal(OwnerInputWindowAddDecision.Added,
            window.TryAdd(Command(scope, 6, 15)));

        AssertBuilt(window, scope, sizer, 3, commands,
            transitionOut, actionOut, out var batch);
        Assert.Equal(new ulong[] { 2, 6, 4 },
            commands.Take(batch.CommandCount).Select(command => command.Sequence.Value));
    }

    [Fact]
    public void DiscreteReferenceCommandOutranksOrdinaryRetry()
    {
        var scope = Scope();
        var window = new OwnerInputSendWindow(
            scope,
            Policy(capacity: 8, minimum: 1, maximum: 2, maximumDeferrals: 64));
        window.TryAdd(Command(scope, 1, 10));
        window.TryAdd(Command(scope, 2, 11));
        window.TryAdd(Command(scope, 3, 12));
        window.TryAdd(Command(
            scope, 4, 13,
            transitions: new TransitionReferenceBuffer(
                new[] { MovementTransitionId.Initial })));
        var sizer = new TestSizer(0, 1);
        var commands = new OwnerSimulationCommand[2];
        var transitionOut = new MovementTransitionIntent[1];
        var actionOut = new PredictedActionIntent[1];
        for (var packet = 0; packet < 3; packet++)
        {
            AssertBuilt(window, scope, sizer, 2, commands,
                transitionOut, actionOut, out _);
        }

        AssertBuilt(window, scope, sizer, 2, commands,
            transitionOut, actionOut, out var batch);
        Assert.Equal(new ulong[] { 1, 4 },
            commands.Take(batch.CommandCount).Select(command => command.Sequence.Value));
    }

    [Fact]
    public void ConsumedFramesPruneRegardlessOfReceiptAndNeverMoveBackward()
    {
        var scope = Scope();
        var window = new OwnerInputSendWindow(
            scope,
            Policy(capacity: 16, minimum: 1, maximum: 8));
        AddRange(window, scope, 1, 6, firstFrame: 10);
        var emptyAck = Ack(scope, null);

        Assert.Equal(
            OwnerInputProgressDecision.Applied,
            window.ApplyAuthorityProgress(emptyAck, new SimulationInstant(13)));
        Assert.Equal(2, window.Count);
        Assert.Equal(new SimulationInstant(13), window.ConsumedThroughFrame);
        Assert.Equal(
            OwnerInputProgressDecision.Applied,
            window.ApplyAuthorityProgress(emptyAck, new SimulationInstant(11)));
        Assert.Equal(new SimulationInstant(13), window.ConsumedThroughFrame);
        Assert.Equal(2, window.Count);
        Assert.Equal(
            OwnerInputProgressDecision.Applied,
            window.ApplyAuthorityProgress(emptyAck, new SimulationInstant(20)));
        Assert.Equal(0, window.Count);

        Assert.Equal(
            OwnerInputWindowAddDecision.DeadlineExpired,
            window.TryAdd(Command(scope, 7, 16)));
        Assert.Equal(new InputSequence(7), window.LastRegisteredSequence);
        Assert.Equal(0, window.Count);
        Assert.Equal(
            OwnerInputWindowAddDecision.DeadlineExpired,
            window.TryAdd(Command(scope, 8, 17)));
        Assert.Equal(
            OwnerInputWindowAddDecision.DeadlineExpired,
            window.TryAdd(Command(scope, 9, 18)));
        Assert.Equal(
            OwnerInputWindowAddDecision.DeadlineExpired,
            window.TryAdd(Command(scope, 10, 19)));
        Assert.Equal(
            OwnerInputWindowAddDecision.DeadlineExpired,
            window.TryAdd(Command(scope, 11, 20)));
        Assert.Equal(
            OwnerInputWindowAddDecision.Added,
            window.TryAdd(Command(scope, 12, 21)));
        Assert.Equal(new InputSequence(12), window.LastRegisteredSequence);
    }

    [Fact]
    public void ExpiredIdentityIsRetiredAndNextIdentityContinuesWithoutReuse()
    {
        var scope = Scope();
        var window = new OwnerInputSendWindow(
            scope,
            new OwnerInputSendWindowPolicy(
                capacity: 8,
                minimumRecentCommands: 1,
                maximumCommandsPerBatch: 2,
                maximumTransitionsPerBatch: 2,
                maximumActionsPerBatch: 2));
        Assert.Equal(
            OwnerInputProgressDecision.Applied,
            window.ApplyAuthorityProgress(Ack(scope, null), new SimulationInstant(5)));
        Assert.Equal(
            OwnerInputWindowAddDecision.DeadlineExpired,
            window.TryAdd(Command(scope, 1, 5)));
        Assert.Equal(new InputSequence(1), window.LastRegisteredSequence);
        Assert.Equal(
            OwnerInputWindowAddDecision.NonContiguousSequence,
            window.TryAdd(Command(scope, 1, 6)));
        Assert.Equal(
            OwnerInputWindowAddDecision.Added,
            window.TryAdd(Command(scope, 2, 6)));
    }

    [Fact]
    public void InvalidFutureOrWrongScopeAcknowledgementNeverMutatesWindow()
    {
        var scope = Scope();
        var other = Scope(control: 2);
        var window = new OwnerInputSendWindow(
            scope,
            new OwnerInputSendWindowPolicy(
                capacity: 8,
                minimumRecentCommands: 1,
                maximumCommandsPerBatch: 2,
                maximumTransitionsPerBatch: 2,
                maximumActionsPerBatch: 2));
        AddRange(window, scope, 1, 3, firstFrame: 10);

        Assert.Equal(
            OwnerInputProgressDecision.WrongScope,
            window.ApplyAuthorityProgress(Ack(other, 1), null));
        Assert.Equal(
            OwnerInputProgressDecision.AcknowledgesUnoriginatedInput,
            window.ApplyAuthorityProgress(Ack(scope, 4), null));
        Assert.Equal(
            OwnerInputProgressDecision.AcknowledgesUnoriginatedInput,
            window.ApplyAuthorityProgress(Ack(scope, null, 1UL << 3), null));
        Assert.Equal(3, window.Count);
        Assert.Null(window.HighestContiguousReceived);
        Assert.Null(window.ConsumedThroughFrame);
    }

    [Fact]
    public void JournalCandidatesAreReservedOnceAndMayUseRoundRobinOrder()
    {
        var scope = Scope();
        var window = new OwnerInputSendWindow(
            scope,
            new OwnerInputSendWindowPolicy(
                capacity: 8,
                minimumRecentCommands: 1,
                maximumCommandsPerBatch: 2,
                maximumTransitionsPerBatch: 2,
                maximumActionsPerBatch: 2));
        AddRange(window, scope, 1, 3, firstFrame: 10);
        var transitions = new[]
        {
            Transition(scope, 3),
            Transition(scope, 1),
        };
        var actions = new[]
        {
            Action(scope, 3),
            Action(scope, 1),
        };
        var commandOut = new OwnerSimulationCommand[2];
        var transitionOut = new MovementTransitionIntent[2];
        var actionOut = new PredictedActionIntent[2];

        AssertBuilt(window, scope, new TestSizer(10, 10, 20, 30), 80,
            commandOut, transitionOut, actionOut, out var batch,
            transitions, actions);
        Assert.Equal(2, batch.CommandCount);
        Assert.Equal(2, batch.TransitionCount);
        Assert.Equal(0, batch.ActionCount);
        Assert.Equal(transitions[0], transitionOut[0]);
        Assert.True(batch.HasDeferredCommands);
        Assert.False(batch.HasDeferredTransitions);
        Assert.True(batch.HasDeferredActions);
        Assert.Equal(70, batch.EncodedBytes);
    }

    [Fact]
    public void DuplicateOrWrongScopeJournalCandidatesFailBeforeSelection()
    {
        var scope = Scope();
        var other = Scope(control: 2);
        var window = new OwnerInputSendWindow(
            scope,
            new OwnerInputSendWindowPolicy(
                capacity: 8,
                minimumRecentCommands: 1,
                maximumCommandsPerBatch: 2,
                maximumTransitionsPerBatch: 2,
                maximumActionsPerBatch: 1));
        window.TryAdd(Command(scope, 1, 10));
        var commands = new OwnerSimulationCommand[2];
        var transitions = new MovementTransitionIntent[2];
        var actions = new PredictedActionIntent[1];
        var duplicate = Transition(scope, 1);

        Assert.Throws<ArgumentException>(() => window.TryBuildBatch(
            Header(scope), 100, new TestSizer(0, 1),
            new[] { duplicate, duplicate },
            ReadOnlySpan<PredictedActionIntent>.Empty,
            commands, transitions, actions, out _));
        Assert.Throws<ArgumentException>(() => window.TryBuildBatch(
            Header(scope), 100, new TestSizer(0, 1),
            new[] { Transition(other, 1) },
            ReadOnlySpan<PredictedActionIntent>.Empty,
            commands, transitions, actions, out _));
    }

    [Fact]
    public void JournalOverflowDefersWithoutSuppressingValidCommandTraffic()
    {
        var scope = Scope();
        var window = new OwnerInputSendWindow(
            scope,
            Policy(capacity: 2, minimum: 2, maximum: 2));
        AddRange(window, scope, 1, 2, firstFrame: 10);
        var transition = Transition(scope, 1);
        var action = Action(scope, 1);
        var sizer = new TestSizer(10, 11, 13, 17);
        var commandOut = new OwnerSimulationCommand[2];
        var transitionOut = new MovementTransitionIntent[1];
        var actionOut = new PredictedActionIntent[1];
        const int exact = 62;

        Assert.Equal(
            OwnerInputBatchBuildDecision.Built,
            window.TryBuildBatch(
                Header(scope), exact - 1, sizer,
                new[] { transition }, new[] { action },
                commandOut, transitionOut, actionOut, out var partial));
        Assert.Equal(2, partial.CommandCount);
        Assert.Equal(1, partial.TransitionCount);
        Assert.Equal(0, partial.ActionCount);
        Assert.True(partial.HasDeferredActions);
        Assert.Equal(45, partial.EncodedBytes);
        AssertBuilt(window, scope, sizer, exact,
            commandOut, transitionOut, actionOut, out var batch,
            new[] { transition }, new[] { action });
        Assert.Equal(exact, batch.EncodedBytes);
    }

    [Fact]
    public void EncodedBudgetPropertyHoldsAcrossVariableEntrySizes()
    {
        var random = new Random(7719);
        for (var iteration = 0; iteration < 200; iteration++)
        {
            var scope = Scope((ulong)iteration + 1);
            var commandCount = random.Next(1, 21);
            var maximumCommands = commandCount == 1
                ? 1
                : random.Next(2, Math.Min(8, commandCount) + 1);
            var window = new OwnerInputSendWindow(
                scope,
                Policy(commandCount == 1 ? 1 : 32, minimum: 1,
                    maximum: maximumCommands));
            AddRange(window, scope, 1, commandCount, firstFrame: 100);
            var sizer = new TestSizer(
                baseBytes: random.Next(0, 20),
                commandBytes: random.Next(1, 30),
                transitionBytes: random.Next(1, 30),
                actionBytes: random.Next(1, 30),
                sequenceWeightModulus: random.Next(1, 7));
            var transitionCandidates = iteration % 2 == 0
                ? new[] { Transition(scope, 1) }
                : Array.Empty<MovementTransitionIntent>();
            var actionCandidates = iteration % 3 == 0
                ? new[] { Action(scope, 1) }
                : Array.Empty<PredictedActionIntent>();
            var required = sizer.MeasureBaseBytes(Header(scope)) +
                sizer.MeasureCommandEntryBytes(Command(scope, 1, 100)) +
                transitionCandidates.Sum(candidate =>
                    sizer.MeasureTransitionEntryBytes(candidate)) +
                actionCandidates.Sum(candidate => sizer.MeasureActionEntryBytes(candidate));
            var budget = random.Next(required, required + 150);
            var commandOut = new OwnerSimulationCommand[maximumCommands];
            var transitionOut = new MovementTransitionIntent[1];
            var actionOut = new PredictedActionIntent[1];

            AssertBuilt(window, scope, sizer, budget,
                commandOut, transitionOut, actionOut, out var batch,
                transitionCandidates, actionCandidates);
            var independentlyMeasured = sizer.MeasureBaseBytes(Header(scope));
            for (var index = 0; index < batch.CommandCount; index++)
            {
                independentlyMeasured += sizer.MeasureCommandEntryBytes(commandOut[index]);
            }
            for (var index = 0; index < batch.TransitionCount; index++)
            {
                independentlyMeasured +=
                    sizer.MeasureTransitionEntryBytes(transitionOut[index]);
            }
            for (var index = 0; index < batch.ActionCount; index++)
            {
                independentlyMeasured += sizer.MeasureActionEntryBytes(actionOut[index]);
            }
            Assert.Equal(independentlyMeasured, batch.EncodedBytes);
            Assert.InRange(batch.EncodedBytes, 0, budget);
            Assert.Equal(
                batch.CommandCount,
                commandOut.Take(batch.CommandCount).Select(c => c.Sequence).Distinct().Count());
        }
    }

    [Fact]
    public void RandomizedAddAckConsumeAndLossTracePreservesWindowInvariants()
    {
        var random = new Random(981_337);
        var scope = Scope();
        var policy = Policy(capacity: 32, minimum: 2, maximum: 6);
        var window = new OwnerInputSendWindow(scope, policy);
        var outstanding = new Dictionary<ulong, long>();
        var commands = new OwnerSimulationCommand[6];
        var transitions = new MovementTransitionIntent[1];
        var actions = new PredictedActionIntent[1];
        var sizer = new TestSizer(3, 2, sequenceWeightModulus: 5);
        ulong nextSequence = 1;
        long nextFrame = 10;
        long? consumedThrough = null;

        for (var iteration = 0; iteration < 4_000; iteration++)
        {
            var operation = random.Next(100);
            if (operation < 45)
            {
                var decision = window.TryAdd(Command(scope, nextSequence, nextFrame));
                if (consumedThrough is not null && nextFrame <= consumedThrough.Value)
                {
                    Assert.Equal(OwnerInputWindowAddDecision.DeadlineExpired, decision);
                    nextSequence++;
                    nextFrame++;
                }
                else if (outstanding.Count == policy.Capacity)
                {
                    Assert.Equal(OwnerInputWindowAddDecision.CapacityExceeded, decision);
                }
                else
                {
                    Assert.Equal(OwnerInputWindowAddDecision.Added, decision);
                    outstanding.Add(nextSequence++, nextFrame++);
                }
            }
            else if (operation < 70 && nextSequence > 1)
            {
                var lastOriginated = nextSequence - 1;
                var cursor = (ulong)random.Next(0, checked((int)lastOriginated + 1));
                ulong mask = 0;
                var available = Math.Min(64UL, lastOriginated - cursor);
                for (var distance = 1UL; distance <= available; distance++)
                {
                    if (random.Next(4) == 0)
                    {
                        mask |= 1UL << checked((int)distance - 1);
                    }
                }

                Assert.Equal(
                    OwnerInputProgressDecision.Applied,
                    window.ApplyAuthorityProgress(
                        Ack(scope, cursor == 0 ? null : cursor, mask), null));
                foreach (var sequence in outstanding.Keys.ToArray())
                {
                    var received = sequence <= cursor;
                    if (!received && sequence - cursor is >= 1 and <= 64)
                    {
                        received = (mask &
                            (1UL << checked((int)(sequence - cursor) - 1))) != 0;
                    }
                    if (received)
                    {
                        outstanding.Remove(sequence);
                    }
                }
            }
            else if (operation < 85)
            {
                var candidate = random.NextInt64(0, nextFrame + 6);
                consumedThrough = consumedThrough is null
                    ? candidate
                    : Math.Max(consumedThrough.Value, candidate);
                Assert.Equal(
                    OwnerInputProgressDecision.Applied,
                    window.ApplyAuthorityProgress(
                        Ack(scope, null), new SimulationInstant(candidate)));
                foreach (var pair in outstanding.ToArray())
                {
                    if (pair.Value <= consumedThrough.Value)
                    {
                        outstanding.Remove(pair.Key);
                    }
                }
            }
            else
            {
                // A build with no following ACK models complete packet loss.
                var budget = random.Next(3, 31);
                AssertBuilt(window, scope, sizer, budget,
                    commands, transitions, actions, out var batch);
                Assert.InRange(batch.EncodedBytes, 0, budget);
                var sent = commands.Take(batch.CommandCount)
                    .Select(command => command.Sequence.Value)
                    .ToArray();
                Assert.Equal(sent.Length, sent.Distinct().Count());
                Assert.All(sent, sequence => Assert.Contains(sequence, outstanding.Keys));
            }

            Assert.Equal(outstanding.Count, window.Count);
        }
    }

    [Fact]
    public void EmptyWindowStillBuildsHeaderOnlyHeartbeatAndSizerMustBeSane()
    {
        var scope = Scope();
        var window = new OwnerInputSendWindow(
            scope,
            Policy(capacity: 8, minimum: 1, maximum: 2));
        var commands = new OwnerSimulationCommand[2];
        var transitions = new MovementTransitionIntent[1];
        var actions = new PredictedActionIntent[1];

        AssertBuilt(window, scope, new TestSizer(7, 1), 7,
            commands, transitions, actions, out var batch);
        Assert.Equal(0, batch.CommandCount);
        Assert.Equal(7, batch.EncodedBytes);
        Assert.Throws<InvalidOperationException>(() => window.TryBuildBatch(
            Header(scope), 100, new TestSizer(-1, 1),
            ReadOnlySpan<MovementTransitionIntent>.Empty,
            ReadOnlySpan<PredictedActionIntent>.Empty,
            commands, transitions, actions, out _));

        window.TryAdd(Command(scope, 1, 10));
        Assert.Throws<InvalidOperationException>(() => window.TryBuildBatch(
            Header(scope), 100, new TestSizer(0, 0),
            ReadOnlySpan<MovementTransitionIntent>.Empty,
            ReadOnlySpan<PredictedActionIntent>.Empty,
            commands, transitions, actions, out _));
    }

    [Fact]
    public void ResetIsScopeBoundAndClearsAllDeliveryProgressExactlyOnce()
    {
        var oldScope = Scope(control: 1);
        var newScope = Scope(control: 2);
        var window = new OwnerInputSendWindow(
            oldScope,
            Policy(capacity: 8, minimum: 1, maximum: 2));
        AddRange(window, oldScope, 1, 3, firstFrame: 10);
        window.ApplyAuthorityProgress(Ack(oldScope, 1), new SimulationInstant(10));

        Assert.False(window.Reset(oldScope));
        Assert.True(window.Reset(newScope));
        Assert.Equal(0, window.Count);
        Assert.Null(window.LastRegisteredSequence);
        Assert.Null(window.HighestContiguousReceived);
        Assert.Null(window.ConsumedThroughFrame);
        Assert.Equal(
            OwnerInputWindowAddDecision.WrongScope,
            window.TryAdd(Command(oldScope, 1, 10)));
        Assert.Equal(
            OwnerInputWindowAddDecision.Added,
            window.TryAdd(Command(newScope, 1, 50)));
    }

    [Fact]
    public void SustainedNewTrafficCannotStarveAnOlderSelectiveGap()
    {
        var scope = Scope();
        var window = new OwnerInputSendWindow(
            scope,
            Policy(capacity: 32, minimum: 1, maximum: 2, maximumDeferrals: 2));
        window.TryAdd(Command(scope, 1, 10));
        window.TryAdd(Command(
            scope,
            2,
            11,
            transitions: new TransitionReferenceBuffer(
                new[] { MovementTransitionId.Initial })));
        var sizer = new TestSizer(0, 1);
        var commands = new OwnerSimulationCommand[2];
        var transitions = new MovementTransitionIntent[1];
        var actions = new PredictedActionIntent[1];
        AssertBuilt(window, scope, sizer, 2, commands, transitions, actions, out _);

        var retransmittedGapAt = 0UL;
        ulong followingMask = 0;
        for (ulong sequence = 3; sequence <= 20; sequence++)
        {
            Assert.Equal(
                OwnerInputWindowAddDecision.Added,
                window.TryAdd(Command(scope, sequence, 9 + (long)sequence)));
            AssertBuilt(window, scope, sizer, 2, commands, transitions, actions,
                out var batch);
            if (commands.Take(batch.CommandCount)
                .Any(command => command.Sequence.Value == 2))
            {
                retransmittedGapAt = sequence;
                break;
            }

            followingMask |= 1UL << checked((int)sequence - 1);
            Assert.Equal(
                OwnerInputProgressDecision.Applied,
                window.ApplyAuthorityProgress(
                    Ack(scope, null, followingMask),
                    consumedThroughFrame: null));
        }

        Assert.InRange(retransmittedGapAt, 3UL, 6UL);
    }

    [Fact]
    public void RealJournalIntegrationPreflightsFailureAndEventuallyServicesBothJournals()
    {
        var scope = Scope();
        var window = new OwnerInputSendWindow(
            scope,
            Policy(capacity: 1, minimum: 1, maximum: 1, maximumDeferrals: 2));
        window.TryAdd(Command(scope, 1, 10));
        var transitionJournal = new MovementTransitionJournal(scope);
        var actionJournal = new OwnerActionCommandJournal(scope);
        for (ulong id = 1; id <= 2; id++)
        {
            transitionJournal.TryOriginate(
                new OwnerInputIdentity(scope, new InputSequence(id)),
                MovementTransitionKind.JumpPressed,
                new SimulationInstant(10),
                new SimulationInstant(15),
                out _);
            actionJournal.TryOriginate(
                new OwnerInputIdentity(scope, new InputSequence(id)),
                OwnerActionTrigger.Attack,
                new SimulationInstant(10),
                new SimulationInstant(15),
                new SimulationInstant(8),
                out _);
        }
        var commands = new OwnerSimulationCommand[1];
        var transitionOut = new MovementTransitionIntent[1];
        var actionOut = new PredictedActionIntent[1];

        Assert.Equal(
            OwnerInputBatchBuildDecision.RequiredContentExceedsBudget,
            window.TryBuildBatchFromJournals(
                Header(scope),
                maximumEncodedBytes: 30,
                new TestSizer(baseBytes: 31, commandBytes: 10,
                    transitionBytes: 10, actionBytes: 10),
                transitionJournal,
                actionJournal,
                commands,
                transitionOut,
                actionOut,
                out _));

        var seenTransitions = new HashSet<ulong>();
        var seenActions = new HashSet<ulong>();
        for (var packet = 0; packet < 12; packet++)
        {
            Assert.Equal(
                OwnerInputBatchBuildDecision.Built,
                window.TryBuildBatchFromJournals(
                    Header(scope),
                    maximumEncodedBytes: 30,
                    new TestSizer(baseBytes: 10, commandBytes: 10,
                        transitionBytes: 10, actionBytes: 10),
                    transitionJournal,
                    actionJournal,
                    commands,
                    transitionOut,
                    actionOut,
                    out var batch));
            if (batch.TransitionCount > 0)
            {
                seenTransitions.Add(transitionOut[0].Identity.Id.Value);
            }
            if (batch.ActionCount > 0)
            {
                seenActions.Add(actionOut[0].Identity.Id.Value);
            }
        }

        // The failed preflight did not rotate the first transition away, and
        // bounded cross-category deferrals eventually service every candidate.
        Assert.Contains(1UL, seenTransitions);
        Assert.Equal(new ulong[] { 1, 2 }, seenTransitions.Order());
        Assert.Equal(new ulong[] { 1, 2 }, seenActions.Order());
    }

    [Fact]
    public void MultiCandidateJournalCommitsOnlyEntriesActuallyEncoded()
    {
        var scope = Scope();
        var window = new OwnerInputSendWindow(
            scope,
            new OwnerInputSendWindowPolicy(
                capacity: 1,
                minimumRecentCommands: 1,
                maximumCommandsPerBatch: 1,
                maximumTransitionsPerBatch: 2,
                maximumActionsPerBatch: 2,
                maximumPriorityDeferrals: 2));
        var transitionJournal = new MovementTransitionJournal(scope);
        var actionJournal = new OwnerActionCommandJournal(scope);
        for (ulong id = 1; id <= 4; id++)
        {
            Assert.Equal(
                MovementTransitionOriginDecision.Added,
                transitionJournal.TryOriginate(
                    new OwnerInputIdentity(scope, new InputSequence(id)),
                    MovementTransitionKind.JumpPressed,
                    new SimulationInstant(10),
                    new SimulationInstant(15),
                    out _));
            Assert.Equal(
                OwnerActionOriginDecision.Added,
                actionJournal.TryOriginate(
                    new OwnerInputIdentity(scope, new InputSequence(id)),
                    OwnerActionTrigger.Attack,
                    new SimulationInstant(10),
                    new SimulationInstant(15),
                    new SimulationInstant(8),
                    out _));
        }

        var commands = new OwnerSimulationCommand[1];
        var transitions = new MovementTransitionIntent[2];
        var actions = new PredictedActionIntent[2];
        var seenTransitions = new HashSet<ulong>();
        var seenActions = new HashSet<ulong>();
        for (var packet = 0; packet < 24; packet++)
        {
            Assert.Equal(
                OwnerInputBatchBuildDecision.Built,
                window.TryBuildBatchFromJournals(
                    Header(scope),
                    maximumEncodedBytes: 1,
                    new TestSizer(0, 1, transitionBytes: 1, actionBytes: 1),
                    transitionJournal,
                    actionJournal,
                    commands,
                    transitions,
                    actions,
                    out var batch));
            Assert.InRange(batch.TransitionCount + batch.ActionCount, 0, 1);
            if (batch.TransitionCount == 1)
            {
                seenTransitions.Add(transitions[0].Identity.Id.Value);
            }
            if (batch.ActionCount == 1)
            {
                seenActions.Add(actions[0].Identity.Id.Value);
            }
        }

        Assert.Equal(new ulong[] { 1, 2, 3, 4 }, seenTransitions.Order());
        Assert.Equal(new ulong[] { 1, 2, 3, 4 }, seenActions.Order());
    }

    [Fact]
    public void OverdueJournalPreemptsMinimumCommandWhenOnlyOneEntryFits()
    {
        var scope = Scope();
        var window = new OwnerInputSendWindow(
            scope,
            Policy(capacity: 2, minimum: 1, maximum: 2, maximumDeferrals: 2));
        Assert.Equal(
            OwnerInputWindowAddDecision.Added,
            window.TryAdd(Command(scope, 1, 10)));
        var transitionJournal = new MovementTransitionJournal(scope);
        var actionJournal = new OwnerActionCommandJournal(scope);
        transitionJournal.TryOriginate(
            new OwnerInputIdentity(scope, InputSequence.Initial),
            MovementTransitionKind.JumpPressed,
            new SimulationInstant(10),
            new SimulationInstant(15),
            out _);
        var commands = new OwnerSimulationCommand[2];
        var transitions = new MovementTransitionIntent[1];
        var actions = new PredictedActionIntent[1];
        var sizer = new TestSizer(10, 10, transitionBytes: 10, actionBytes: 10);

        for (var packet = 0; packet < 2; packet++)
        {
            window.TryBuildBatchFromJournals(
                Header(scope), 20, sizer, transitionJournal, actionJournal,
                commands, transitions, actions, out var deferred);
            Assert.Equal(1, deferred.CommandCount);
            Assert.Equal(0, deferred.TransitionCount);
        }

        window.TryBuildBatchFromJournals(
            Header(scope), 20, sizer, transitionJournal, actionJournal,
            commands, transitions, actions, out var serviced);
        Assert.Equal(0, serviced.CommandCount);
        Assert.Equal(1, serviced.TransitionCount);
    }

    [Fact]
    public void ConstrainedBudgetRotatesOverdueCommandTransitionAndAction()
    {
        var scope = Scope();
        var window = new OwnerInputSendWindow(
            scope,
            Policy(capacity: 2, minimum: 1, maximum: 2, maximumDeferrals: 1));
        AddRange(window, scope, 1, 2, firstFrame: 10);
        var transitionJournal = new MovementTransitionJournal(scope);
        var actionJournal = new OwnerActionCommandJournal(scope);
        transitionJournal.TryOriginate(
            new OwnerInputIdentity(scope, InputSequence.Initial),
            MovementTransitionKind.JumpPressed,
            new SimulationInstant(10), new SimulationInstant(15), out _);
        actionJournal.TryOriginate(
            new OwnerInputIdentity(scope, InputSequence.Initial),
            OwnerActionTrigger.Attack,
            new SimulationInstant(10), new SimulationInstant(15),
            new SimulationInstant(8), out _);
        var commands = new OwnerSimulationCommand[2];
        var transitions = new MovementTransitionIntent[1];
        var actions = new PredictedActionIntent[1];
        var sizer = new TestSizer(0, 1, transitionBytes: 1, actionBytes: 1);

        window.TryBuildBatchFromJournals(
            Header(scope), 1, sizer, transitionJournal, actionJournal,
            commands, transitions, actions, out var oldest);
        Assert.Equal(1UL, commands[0].Sequence.Value);
        Assert.Equal(1, oldest.CommandCount);

        window.TryBuildBatchFromJournals(
            Header(scope), 1, sizer, transitionJournal, actionJournal,
            commands, transitions, actions, out var laterCommand);
        Assert.Equal(2UL, commands[0].Sequence.Value);
        Assert.Equal(1, laterCommand.CommandCount);

        window.TryBuildBatchFromJournals(
            Header(scope), 1, sizer, transitionJournal, actionJournal,
            commands, transitions, actions, out var transition);
        Assert.Equal(1, transition.TransitionCount);
        Assert.Equal(0, transition.CommandCount);

        window.TryBuildBatchFromJournals(
            Header(scope), 1, sizer, transitionJournal, actionJournal,
            commands, transitions, actions, out var action);
        Assert.Equal(1, action.ActionCount);
        Assert.Equal(0, action.CommandCount);
    }

    [Fact]
    public void OversizedJournalCandidateDefersWithoutSuppressingHeartbeatOrCommands()
    {
        var scope = Scope();
        var window = new OwnerInputSendWindow(
            scope,
            Policy(capacity: 8, minimum: 1, maximum: 2));
        AddRange(window, scope, 1, 2, firstFrame: 10);
        var commands = new OwnerSimulationCommand[2];
        var transitions = new MovementTransitionIntent[1];
        var actions = new PredictedActionIntent[1];

        AssertBuilt(
            window,
            scope,
            new TestSizer(5, 5, transitionBytes: 1_000),
            budget: 15,
            commands,
            transitions,
            actions,
            out var batch,
            new[] { Transition(scope, 1) });
        Assert.Equal(2, batch.CommandCount);
        Assert.Equal(0, batch.TransitionCount);
        Assert.True(batch.HasDeferredTransitions);
        Assert.Equal(15, batch.EncodedBytes);
    }

    [Fact]
    public void ReceivedBaselineAndTimelineLimitsFailClosedWithoutCounterWrap()
    {
        var scope = Scope();
        var policy = Policy(capacity: 8, minimum: 1, maximum: 2);
        var restored = OwnerInputSendWindow.RestoreEmptyReceivedBaseline(
            scope,
            policy,
            new InputSequence(5),
            new SimulationInstant(20));
        Assert.Equal(new InputSequence(5), restored.HighestContiguousReceived);
        Assert.Equal(
            OwnerInputWindowAddDecision.Added,
            restored.TryAdd(Command(scope, 6, 21)));

        var sequenceExhausted = OwnerInputSendWindow.RestoreEmptyReceivedBaseline(
            scope,
            policy,
            new InputSequence(ulong.MaxValue),
            new SimulationInstant(20));
        Assert.Equal(
            OwnerInputWindowAddDecision.TimelineExhausted,
            sequenceExhausted.TryAdd(Command(scope, ulong.MaxValue, 21)));

        var frameExhausted = OwnerInputSendWindow.RestoreEmptyReceivedBaseline(
            scope,
            policy,
            InputSequence.Initial,
            new SimulationInstant(long.MaxValue));
        Assert.Equal(
            OwnerInputWindowAddDecision.TimelineExhausted,
            frameExhausted.TryAdd(Command(scope, 2, long.MaxValue)));

        var maximum = new OwnerInputSendWindowPolicy(
            OwnerInputSendWindowLimits.MaximumCapacity,
            OwnerInputSendWindowLimits.MaximumCommandsPerBatch - 1,
            OwnerInputSendWindowLimits.MaximumCommandsPerBatch,
            OwnerInputSendWindowLimits.MaximumJournalEntriesPerBatch,
            OwnerInputSendWindowLimits.MaximumJournalEntriesPerBatch,
            OwnerInputSendWindowLimits.MaximumPriorityDeferrals);
        Assert.True(maximum.IsValid);
    }

    [Fact]
    public void WarmedAddAckAndBuildHotPathAllocatesNothing()
    {
        var scope = Scope();
        var window = new OwnerInputSendWindow(
            scope,
            Policy(capacity: 8, minimum: 1, maximum: 2));
        var sizer = new TestSizer(1, 1);
        var header = Header(scope);
        var commands = new OwnerSimulationCommand[2];
        var transitions = new MovementTransitionIntent[1];
        var actions = new PredictedActionIntent[1];
        window.TryAdd(Command(scope, 1, 1));
        window.TryBuildBatch(
            header, 10, sizer,
            ReadOnlySpan<MovementTransitionIntent>.Empty,
            ReadOnlySpan<PredictedActionIntent>.Empty,
            commands, transitions, actions, out _);
        window.ApplyAuthorityProgress(Ack(scope, 1), new SimulationInstant(1));

        // Cross the tiered-JIT promotion threshold for every hot-path method
        // before measuring managed allocations. A single warm-up iteration is
        // order-dependent when this test runs inside the complete suite.
        for (ulong sequence = 2; sequence <= 2_001; sequence++)
        {
            window.TryAdd(Command(scope, sequence, (long)sequence));
            window.TryBuildBatch(
                header, 10, sizer,
                ReadOnlySpan<MovementTransitionIntent>.Empty,
                ReadOnlySpan<PredictedActionIntent>.Empty,
                commands, transitions, actions, out _);
            window.ApplyAuthorityProgress(
                Ack(scope, sequence),
                new SimulationInstant((long)sequence));
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (ulong sequence = 2_002; sequence <= 3_001; sequence++)
        {
            window.TryAdd(Command(scope, sequence, (long)sequence));
            window.TryBuildBatch(
                header, 10, sizer,
                ReadOnlySpan<MovementTransitionIntent>.Empty,
                ReadOnlySpan<PredictedActionIntent>.Empty,
                commands, transitions, actions, out _);
            window.ApplyAuthorityProgress(
                Ack(scope, sequence),
                new SimulationInstant((long)sequence));
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(0, window.Count);
    }

    private static void AssertBuilt(
        OwnerInputSendWindow window,
        OwnerIntentScope scope,
        TestSizer sizer,
        int budget,
        OwnerSimulationCommand[] commands,
        MovementTransitionIntent[] transitionOutput,
        PredictedActionIntent[] actionOutput,
        out OwnerInputSendBatch batch,
        MovementTransitionIntent[]? transitions = null,
        PredictedActionIntent[]? actions = null)
    {
        Assert.Equal(
            OwnerInputBatchBuildDecision.Built,
            window.TryBuildBatch(
                Header(scope),
                budget,
                sizer,
                transitions ?? Array.Empty<MovementTransitionIntent>(),
                actions ?? Array.Empty<PredictedActionIntent>(),
                commands,
                transitionOutput,
                actionOutput,
                out batch));
        Assert.True(batch.EncodedBytes <= budget);
    }

    private static void AddRange(
        OwnerInputSendWindow window,
        OwnerIntentScope scope,
        int firstSequence,
        int count,
        long firstFrame)
    {
        for (var offset = 0; offset < count; offset++)
        {
            Assert.Equal(
                OwnerInputWindowAddDecision.Added,
                window.TryAdd(Command(
                    scope,
                    (ulong)(firstSequence + offset),
                    firstFrame + offset)));
        }
    }

    private static OwnerSimulationCommand Command(
        OwnerIntentScope scope,
        ulong sequence,
        long targetFrame,
        TransitionReferenceBuffer transitions = default,
        ActionReferenceBuffer actions = default)
    {
        var input = new CharacterSimulationInput(
            default,
            default,
            default,
            transitions,
            default,
            actions,
            new MovementConfigurationRevision(1),
            new MovementCapabilityRevision(1));
        return new OwnerSimulationCommand(
            new OwnerInputIdentity(scope, new InputSequence(sequence)),
            new AuthorityDiscontinuityId(1),
            new MatchFrameEpochId(1),
            new SimulationInstant(targetFrame),
            input);
    }

    private static MovementTransitionIntent Transition(
        OwnerIntentScope scope,
        ulong id) => new(
            new MovementTransitionIdentity(scope, new MovementTransitionId(id)),
            new OwnerInputIdentity(scope, new InputSequence(id)),
            MovementTransitionKind.JumpPressed,
            new SimulationInstant(10),
            new SimulationInstant(15));

    private static PredictedActionIntent Action(
        OwnerIntentScope scope,
        ulong id) => new(
            new PredictedActionIdentity(scope, new PredictedActionId(id)),
            new OwnerInputIdentity(scope, new InputSequence(id)),
            OwnerActionTrigger.Attack,
            new SimulationInstant(10),
            new SimulationInstant(15),
            new SimulationInstant(8));

    private static OwnerInputReceiveAcknowledgement Ack(
        OwnerIntentScope scope,
        ulong? highest,
        ulong mask = 0) => new(
            scope,
            highest is null ? null : new InputSequence(highest.Value),
            mask);

    private static OwnerInputSendBatchHeader Header(OwnerIntentScope scope) => new(
        scope,
        PacketSequence.Initial,
        transitionResolutionCursor: null,
        actionResolutionCursor: null);

    private static OwnerInputSendWindowPolicy Policy(
        int capacity,
        int minimum,
        int maximum,
        int maximumDeferrals =
            OwnerInputSendWindowLimits.DefaultMaximumPriorityDeferrals) => new(
            capacity,
            minimum,
            maximum,
            maximumTransitionsPerBatch: 1,
            maximumActionsPerBatch: 1,
            maximumPriorityDeferrals: maximumDeferrals);

    private static OwnerIntentScope Scope(ulong control = 1) => new(
        100,
        new LifeEpoch(new CombatantId(7), new LifeGenerationId(3)),
        new OwnerControlEpoch(control));

    private sealed class TestSizer(
        int baseBytes,
        int commandBytes,
        int transitionBytes = 1,
        int actionBytes = 1,
        int sequenceWeightModulus = 0) : IOwnerInputBatchEncodedSizer
    {
        public int MeasureBaseBytes(in OwnerInputSendBatchHeader header)
        {
            _ = header;
            return baseBytes;
        }

        public int MeasureCommandEntryBytes(in OwnerSimulationCommand command) =>
            commandBytes + (sequenceWeightModulus == 0
                ? 0
                : checked((int)(command.Sequence.Value % (ulong)sequenceWeightModulus)));

        public int MeasureTransitionEntryBytes(in MovementTransitionIntent transition)
        {
            _ = transition;
            return transitionBytes;
        }

        public int MeasureActionEntryBytes(in PredictedActionIntent action)
        {
            _ = action;
            return actionBytes;
        }
    }
}
