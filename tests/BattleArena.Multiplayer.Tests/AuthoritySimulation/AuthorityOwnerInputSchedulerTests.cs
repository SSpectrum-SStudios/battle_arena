using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Core.Movement.Simulation;
using BattleArena.Multiplayer.AuthoritySimulation;
using BattleArena.Multiplayer.OwnerPrediction;

namespace BattleArena.Multiplayer.Tests.AuthoritySimulation;

public sealed class AuthorityOwnerInputSchedulerTests
{
    [Fact]
    public void InOrderArrivalConsumesEveryFrameExactlyOnceInAscendingOrder()
    {
        var epoch = Epoch();
        var scheduler = Scheduler(epoch, firstFrame: 100, capacity: 8);

        for (var frame = 100L; frame < 106L; frame++)
        {
            Assert.True(scheduler.TryAdmit(Command(epoch, frame)).WasStored);
        }

        for (var frame = 100L; frame < 106L; frame++)
        {
            var consumption = scheduler.ConsumeNextFrame();
            Assert.Equal(new SimulationInstant(frame), consumption.Frame);
            Assert.Equal(
                AuthorityOwnerFrameAvailability.CommandAvailable,
                consumption.Availability);
            Assert.Equal(new SimulationInstant(frame), consumption.Command!.Value.TargetFrame);
        }

        Assert.Equal(6, scheduler.ConsumedFrameCount);
        Assert.Equal(0, scheduler.StoredCommandCount);
        Assert.Equal(new SimulationInstant(106), scheduler.NextFrameToConsume);
    }

    [Fact]
    public void BurstArrivalIsNotCompactedIntoOneIntegrationStep()
    {
        // The legacy failure: four commands arriving together were compacted to
        // the newest, so logical time advanced four frames while displacement
        // advanced one. Here a burst fills four cells and takes four frames.
        var epoch = Epoch();
        var scheduler = Scheduler(epoch, firstFrame: 40, capacity: 16);

        foreach (var frame in new[] { 40L, 41L, 42L, 43L })
        {
            Assert.True(scheduler.TryAdmit(Command(epoch, frame)).WasStored);
        }

        Assert.Equal(4, scheduler.BufferedFrameCount);

        var consumedFrames = new List<long>();
        for (var i = 0; i < 4; i++)
        {
            var consumption = scheduler.ConsumeNextFrame();
            Assert.Equal(
                AuthorityOwnerFrameAvailability.CommandAvailable,
                consumption.Availability);
            consumedFrames.Add(consumption.Frame.Tick);
        }

        Assert.Equal(new[] { 40L, 41L, 42L, 43L }, consumedFrames);
        Assert.Equal(0, scheduler.BufferedFrameCount);
    }

    [Fact]
    public void AGapIsConsumedAsAMissingFrameAndNeverSkipped()
    {
        var epoch = Epoch();
        var scheduler = Scheduler(epoch, firstFrame: 10, capacity: 8);

        Assert.True(scheduler.TryAdmit(Command(epoch, 10)).WasStored);
        Assert.True(scheduler.TryAdmit(Command(epoch, 12)).WasStored);

        Assert.Equal(
            AuthorityOwnerFrameAvailability.CommandAvailable,
            scheduler.ConsumeNextFrame().Availability);

        var missing = scheduler.ConsumeNextFrame();
        Assert.Equal(new SimulationInstant(11), missing.Frame);
        Assert.Equal(AuthorityOwnerFrameAvailability.CommandMissing, missing.Availability);
        Assert.Null(missing.Command);

        var recovered = scheduler.ConsumeNextFrame();
        Assert.Equal(new SimulationInstant(12), recovered.Frame);
        Assert.Equal(
            AuthorityOwnerFrameAvailability.CommandAvailable,
            recovered.Availability);
    }

    [Fact]
    public void ANewerCommandNeverSubstitutesItselfIntoAnOlderMissingFrame()
    {
        var epoch = Epoch();
        var scheduler = Scheduler(epoch, firstFrame: 10, capacity: 8);

        Assert.True(scheduler.TryAdmit(Command(epoch, 13)).WasStored);

        for (var frame = 10L; frame < 13L; frame++)
        {
            var consumption = scheduler.ConsumeNextFrame();
            Assert.Equal(new SimulationInstant(frame), consumption.Frame);
            Assert.Equal(
                AuthorityOwnerFrameAvailability.CommandMissing,
                consumption.Availability);
        }

        Assert.Equal(
            AuthorityOwnerFrameAvailability.CommandAvailable,
            scheduler.ConsumeNextFrame().Availability);
    }

    [Fact]
    public void OutOfOrderArrivalIsStoredByTargetFrameNotArrivalOrder()
    {
        var epoch = Epoch();
        var scheduler = Scheduler(epoch, firstFrame: 200, capacity: 16);

        foreach (var frame in new[] { 204L, 201L, 203L, 200L, 202L })
        {
            Assert.True(scheduler.TryAdmit(Command(epoch, frame)).WasStored);
        }

        for (var frame = 200L; frame <= 204L; frame++)
        {
            var consumption = scheduler.ConsumeNextFrame();
            Assert.Equal(new SimulationInstant(frame), consumption.Frame);
            Assert.Equal(
                new SimulationInstant(frame),
                consumption.Command!.Value.TargetFrame);
        }
    }

    [Fact]
    public void RedundantResendIsIdempotentAndConflictingResendIsRefused()
    {
        var epoch = Epoch();
        var scheduler = Scheduler(epoch, firstFrame: 0, capacity: 8);
        var original = Command(epoch, 3);

        Assert.True(scheduler.TryAdmit(original).WasStored);

        var duplicate = scheduler.TryAdmit(original);
        Assert.Equal(OwnerInputArrivalDisposition.DuplicateCommand, duplicate.Disposition);
        Assert.Equal(AuthorityInputAdmissionFault.None, duplicate.Fault);
        Assert.Equal(1, scheduler.StoredCommandCount);

        // Same identity, different intent: the first admission must win.
        var conflicting = Command(
            epoch,
            3,
            input: Input(yaw: 0.5d));
        var refused = scheduler.TryAdmit(conflicting);
        Assert.Equal(OwnerInputArrivalDisposition.RejectedCommand, refused.Disposition);
        Assert.Equal(
            AuthorityInputAdmissionFault.ConflictingCommandForFrame,
            refused.Fault);

        Assert.True(scheduler.TryPeek(new SimulationInstant(3), out var stored));
        Assert.Equal(original, stored);
    }

    [Fact]
    public void ACommandForAConsumedFrameIsLateAndIsNeverSimulatedLater()
    {
        var epoch = Epoch();
        var scheduler = Scheduler(epoch, firstFrame: 50, capacity: 8);

        var consumed = scheduler.ConsumeNextFrame();
        Assert.Equal(AuthorityOwnerFrameAvailability.CommandMissing, consumed.Availability);

        var late = scheduler.TryAdmit(Command(epoch, 50));
        Assert.Equal(OwnerInputArrivalDisposition.LateCommand, late.Disposition);
        Assert.Equal(
            OwnerInputArrivalDisposition.LateCommand,
            late.Arrival!.Value.Disposition);
        Assert.True(late.Arrival!.Value.IsTerminalWithoutApplication);
        Assert.Equal(0, scheduler.StoredCommandCount);
        Assert.False(scheduler.TryPeek(new SimulationInstant(50), out _));
        Assert.Equal(new SimulationInstant(51), scheduler.NextFrameToConsume);
    }

    [Fact]
    public void FutureFloodBeyondTheHorizonIsRefusedWithoutGrowingTheStore()
    {
        var epoch = Epoch();
        const int capacity = 8;
        var scheduler = Scheduler(epoch, firstFrame: 0, capacity: capacity);

        Assert.Equal(new SimulationInstant(capacity - 1), scheduler.AcceptanceHorizon);
        Assert.True(scheduler.TryAdmit(Command(epoch, capacity - 1)).WasStored);

        for (var frame = (long)capacity; frame < capacity + 500; frame++)
        {
            var refused = scheduler.TryAdmit(Command(epoch, frame));
            Assert.Equal(OwnerInputArrivalDisposition.RejectedCommand, refused.Disposition);
            Assert.Equal(
                AuthorityInputAdmissionFault.BeyondAcceptanceHorizon,
                refused.Fault);
        }

        Assert.Equal(1, scheduler.StoredCommandCount);
        Assert.True(scheduler.StoredCommandCount <= scheduler.CapacityFrames);
    }

    [Fact]
    public void TheHorizonSlidesForwardOnlyAsFramesAreConsumed()
    {
        var epoch = Epoch();
        var scheduler = Scheduler(epoch, firstFrame: 0, capacity: 4);

        Assert.Equal(
            OwnerInputArrivalDisposition.RejectedCommand,
            scheduler.TryAdmit(Command(epoch, 4)).Disposition);

        scheduler.ConsumeNextFrame();

        Assert.Equal(new SimulationInstant(4), scheduler.AcceptanceHorizon);
        Assert.True(scheduler.TryAdmit(Command(epoch, 4)).WasStored);
    }

    [Fact]
    public void RingReuseCannotResurrectAStaleCellFromAPriorLap()
    {
        var epoch = Epoch();
        const int capacity = 4;
        var scheduler = Scheduler(epoch, firstFrame: 0, capacity: capacity);

        Assert.True(scheduler.TryAdmit(Command(epoch, 0)).WasStored);
        Assert.Equal(
            AuthorityOwnerFrameAvailability.CommandAvailable,
            scheduler.ConsumeNextFrame().Availability);

        // Frame 4 maps to the same ring cell frame 0 used.
        for (var frame = 1L; frame < 4L; frame++)
        {
            Assert.Equal(
                AuthorityOwnerFrameAvailability.CommandMissing,
                scheduler.ConsumeNextFrame().Availability);
        }

        var reused = scheduler.ConsumeNextFrame();
        Assert.Equal(new SimulationInstant(4), reused.Frame);
        Assert.Equal(AuthorityOwnerFrameAvailability.CommandMissing, reused.Availability);
        Assert.Null(reused.Command);
    }

    [Fact]
    public void ForeignScopeEvidenceIsRefusedAndHasNoFrameIdentity()
    {
        var epoch = Epoch();
        var scheduler = Scheduler(epoch, firstFrame: 0, capacity: 8);

        foreach (var foreign in new[]
                 {
                     Command(Epoch(matchFrameEpoch: 2), 1),
                     Command(Epoch(life: 2), 1),
                     Command(Epoch(discontinuity: 2), 1),
                     Command(Epoch(control: 9), 1),
                     Command(Epoch(session: 99), 1),
                     Command(Epoch(combatant: 8), 1),
                 })
        {
            var refused = scheduler.TryAdmit(foreign);
            Assert.Equal(OwnerInputArrivalDisposition.RejectedCommand, refused.Disposition);
            Assert.Equal(AuthorityInputAdmissionFault.ForeignScope, refused.Fault);
            Assert.Null(refused.Arrival);
        }

        Assert.Equal(0, scheduler.StoredCommandCount);
    }

    [Fact]
    public void ASequenceReusedAcrossFramesIsRefusedSoPerFrameIdentityHolds()
    {
        var epoch = Epoch();
        var scheduler = Scheduler(epoch, firstFrame: 100, capacity: 16);

        Assert.True(scheduler.TryAdmit(Command(epoch, 100, sequence: 500)).WasStored);
        Assert.True(scheduler.TryAdmit(Command(epoch, 101, sequence: 501)).WasStored);

        var skewed = scheduler.TryAdmit(Command(epoch, 102, sequence: 500));
        Assert.Equal(OwnerInputArrivalDisposition.RejectedCommand, skewed.Disposition);
        Assert.Equal(AuthorityInputAdmissionFault.SequenceFrameSkew, skewed.Fault);

        var alsoSkewed = scheduler.TryAdmit(Command(epoch, 103, sequence: 900));
        Assert.Equal(AuthorityInputAdmissionFault.SequenceFrameSkew, alsoSkewed.Fault);

        Assert.True(scheduler.TryAdmit(Command(epoch, 104, sequence: 504)).WasStored);
    }

    [Fact]
    public void SequenceAnchorToleratesExtremeButConsistentOffsets()
    {
        // frame 0 with sequence ulong.MaxValue - 1 is consistent, and the offset
        // arithmetic must not wrap or overflow while checking it.
        var epoch = Epoch();
        var scheduler = Scheduler(epoch, firstFrame: 0, capacity: 8);

        Assert.True(scheduler.TryAdmit(Command(epoch, 0, sequence: ulong.MaxValue - 4)).WasStored);
        Assert.True(scheduler.TryAdmit(Command(epoch, 1, sequence: ulong.MaxValue - 3)).WasStored);
        Assert.Equal(
            AuthorityInputAdmissionFault.SequenceFrameSkew,
            scheduler.TryAdmit(Command(epoch, 2, sequence: 1)).Fault);
    }

    [Fact]
    public void EveryFrameIsAccountedForExactlyOnceUnderRandomizedTraffic()
    {
        var epoch = Epoch();
        const int capacity = 32;
        var scheduler = Scheduler(epoch, firstFrame: 0, capacity: capacity);
        var random = new Random(20260815);

        var admitted = new HashSet<long>();
        var consumed = new List<long>();
        var duplicatesSeen = 0;
        var lateSeen = 0;

        long produced = 0;
        for (var step = 0; step < 4000; step++)
        {
            // Produce a jittered, sometimes-duplicated, sometimes-reordered burst.
            var burst = random.Next(0, 4);
            for (var i = 0; i < burst; i++)
            {
                var target = produced + random.Next(0, 6) - 2;
                if (target < 0)
                {
                    continue;
                }

                var admission = scheduler.TryAdmit(
                    Command(epoch, target, sequence: (ulong)target + 1));
                switch (admission.Disposition)
                {
                    case OwnerInputArrivalDisposition.NewCommandAccepted:
                        Assert.True(admitted.Add(target));
                        break;
                    case OwnerInputArrivalDisposition.DuplicateCommand:
                        duplicatesSeen++;
                        Assert.Contains(target, admitted);
                        break;
                    case OwnerInputArrivalDisposition.LateCommand:
                        lateSeen++;
                        Assert.True(target < scheduler.NextFrameToConsume.Tick);
                        break;
                }
            }

            produced += random.Next(0, 3);

            if (random.Next(0, 2) == 0)
            {
                var consumption = scheduler.ConsumeNextFrame();
                consumed.Add(consumption.Frame.Tick);
                if (consumption.Availability == AuthorityOwnerFrameAvailability.CommandAvailable)
                {
                    Assert.Equal(consumption.Frame.Tick, consumption.Command!.Value.TargetFrame.Tick);
                }
            }

            Assert.True(scheduler.StoredCommandCount >= 0);
            Assert.True(scheduler.StoredCommandCount <= capacity);
        }

        // Strictly ascending, contiguous, no repeats: every frame exactly once.
        Assert.Equal(consumed.Count, consumed.Distinct().Count());
        Assert.Equal(Enumerable.Range(0, consumed.Count).Select(i => (long)i), consumed);
        Assert.Equal(consumed.Count, (int)scheduler.ConsumedFrameCount);
        Assert.True(duplicatesSeen > 0);
        Assert.True(lateSeen > 0);
    }

    [Fact]
    public void ConstructionAndTimelineBoundsFailClosed()
    {
        var epoch = Epoch();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AuthorityOwnerInputScheduler(default, new SimulationInstant(0), 8));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AuthorityOwnerInputScheduler(epoch, new SimulationInstant(-1), 8));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AuthorityOwnerInputScheduler(
                epoch,
                new SimulationInstant(0),
                AuthorityOwnerInputScheduler.MinimumCapacityFrames - 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AuthorityOwnerInputScheduler(
                epoch,
                new SimulationInstant(0),
                AuthorityOwnerInputScheduler.MaximumCapacityFrames + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AuthorityOwnerInputScheduler(epoch, new SimulationInstant(long.MaxValue - 2), 8));

        // At the far end of the timeline the cursor may advance only while the
        // acceptance horizon still fits in a tick. Consumption stops there rather
        // than wrapping, and the caller must issue an explicit timeline reset.
        const int capacity = 4;
        var boundary = new AuthorityOwnerInputScheduler(
            epoch,
            new SimulationInstant(long.MaxValue - 8),
            capacity);

        var consumable = 0;
        while (true)
        {
            Assert.True(boundary.AcceptanceHorizon.Tick > 0);
            try
            {
                boundary.ConsumeNextFrame();
            }
            catch (InvalidOperationException)
            {
                break;
            }

            consumable++;
            Assert.True(consumable < 32);
        }

        Assert.Equal(5, consumable);
        Assert.Throws<InvalidOperationException>(() => boundary.ConsumeNextFrame());
    }

    [Fact]
    public void ReceivedSequenceWindowIsEmptyBeforeAnyCommandArrives()
    {
        var scheduler = Scheduler(Epoch(), firstFrame: 0, capacity: 8);

        var window = scheduler.ReceivedInputSequenceWindow;

        Assert.True(window.IsValid);
        Assert.Null(window.HighestContiguousId);
        Assert.Equal(0UL, window.Following64KnownMask);
    }

    [Fact]
    public void ReceivedSequenceWindowAdvancesContiguouslyWithInOrderArrival()
    {
        var epoch = Epoch();
        var scheduler = Scheduler(epoch, firstFrame: 0, capacity: 8);

        for (var frame = 0L; frame < 5L; frame++)
        {
            Assert.True(scheduler.TryAdmit(Command(epoch, frame)).WasStored);
        }

        var window = scheduler.ReceivedInputSequenceWindow;
        Assert.Equal(5UL, window.HighestContiguousId);
        Assert.Equal(0UL, window.Following64KnownMask);
        for (var sequence = 1UL; sequence <= 5UL; sequence++)
        {
            Assert.True(window.Contains(sequence));
        }
        Assert.False(window.Contains(6));
    }

    [Fact]
    public void AnInteriorGapIsReportedAsAMaskBitRatherThanAdvancingTheCursor()
    {
        var epoch = Epoch();
        var scheduler = Scheduler(epoch, firstFrame: 0, capacity: 8);

        // Frame 2 (sequence 3) is lost; 4 (sequence 5) arrives out of order.
        foreach (var frame in new[] { 0L, 1L, 3L })
        {
            Assert.True(scheduler.TryAdmit(Command(epoch, frame)).WasStored);
        }

        var window = scheduler.ReceivedInputSequenceWindow;
        Assert.Equal(2UL, window.HighestContiguousId);
        Assert.True(window.Contains(4));
        Assert.False(window.Contains(3));
    }

    [Fact]
    public void OutOfOrderArrivalClosingAGapAdvancesTheCursorPastBoth()
    {
        var epoch = Epoch();
        var scheduler = Scheduler(epoch, firstFrame: 0, capacity: 8);

        Assert.True(scheduler.TryAdmit(Command(epoch, 0)).WasStored);
        Assert.True(scheduler.TryAdmit(Command(epoch, 2)).WasStored);
        Assert.Equal(1UL, scheduler.ReceivedInputSequenceWindow.HighestContiguousId);

        Assert.True(scheduler.TryAdmit(Command(epoch, 1)).WasStored);

        var window = scheduler.ReceivedInputSequenceWindow;
        Assert.Equal(3UL, window.HighestContiguousId);
        Assert.Equal(0UL, window.Following64KnownMask);
    }

    [Fact]
    public void AConsumedFrameWhoseCommandNeverArrivedStopsPinningTheCursor()
    {
        // The regression this guards: without retiring the dead sequence, one
        // lost packet pins the contiguous cursor forever and the mask saturates
        // 64 frames later, making the acknowledgement permanently inert.
        var epoch = Epoch();
        var scheduler = Scheduler(epoch, firstFrame: 0, capacity: 16);

        for (var frame = 0L; frame < 12L; frame++)
        {
            if (frame != 5L)
            {
                Assert.True(scheduler.TryAdmit(Command(epoch, frame)).WasStored);
            }
        }

        Assert.Equal(5UL, scheduler.ReceivedInputSequenceWindow.HighestContiguousId);

        for (var frame = 0L; frame < 12L; frame++)
        {
            scheduler.ResolveNextFrame(Basis());
        }

        var window = scheduler.ReceivedInputSequenceWindow;
        Assert.Equal(12UL, window.HighestContiguousId);
        Assert.Equal(0UL, window.Following64KnownMask);
    }

    // Ending on a delivered frame resolves every sequence; ending on a lost one
    // settles exactly one short, because the authority has seen no evidence that
    // the owner ever sent that last sequence and must not claim it.
    [Theory]
    [InlineData(300L, 300UL)] // frame 299 delivered (299 % 7 == 5)
    [InlineData(295L, 294UL)] // frame 294 lost      (294 % 7 == 0)
    public void TheWindowKeepsAdvancingAcrossHundredsOfFramesWithSustainedLoss(
        long frames,
        ulong expectedHighestContiguous)
    {
        var epoch = Epoch();
        var scheduler = Scheduler(epoch, firstFrame: 0, capacity: 16);

        for (var frame = 0L; frame < frames; frame++)
        {
            if (frame % 7 != 0)
            {
                Assert.True(scheduler.TryAdmit(Command(epoch, frame)).WasStored);
            }
            scheduler.ResolveNextFrame(Basis());
        }

        Assert.Equal(
            expectedHighestContiguous,
            scheduler.ReceivedInputSequenceWindow.HighestContiguousId);
    }

    [Fact]
    public void AReorderedFirstArrivalDoesNotClaimTheSequencesItArrivedAheadOf()
    {
        // The first packet to arrive is not necessarily the first live one.
        // Seeding the window from it would advertise frames 0-4 as resolved when
        // they have been neither received nor consumed, and the client's send
        // window prunes on exactly that signal — dropping commands the authority
        // has not yet run and could still use.
        var epoch = Epoch();
        var scheduler = Scheduler(epoch, firstFrame: 0, capacity: 16);

        Assert.True(scheduler.TryAdmit(Command(epoch, 5)).WasStored);

        var window = scheduler.ReceivedInputSequenceWindow;
        Assert.Null(scheduler.ConsumedThroughFrame);
        Assert.Null(window.HighestContiguousId);
        for (var sequence = 1UL; sequence <= 5UL; sequence++)
        {
            Assert.False(window.Contains(sequence));
        }
        Assert.True(window.Contains(6));
    }

    [Fact]
    public void ANewControlEpochAcknowledgesItsFirstCommandWithoutWaitingOutTheLead()
    {
        // Owner sequence numbering is scoped to the owner-control epoch, so it
        // restarts near 1 at spawn, respawn, reconnect, and control renewal while
        // match frames are already high. The frame at the cursor therefore maps to
        // a sequence below 1, and refusing to seed there would leave the receive
        // window empty for a whole prediction lead — one wasted round of resends
        // every epoch.
        const int lead = 24;
        var epoch = Epoch();
        var scheduler = Scheduler(epoch, firstFrame: 10_000, capacity: 64);

        // First command of the new epoch: sequence 1, targeting a full lead ahead.
        Assert.True(scheduler
            .TryAdmit(Command(epoch, 10_000 + lead, sequence: 1))
            .WasStored);

        var window = scheduler.ReceivedInputSequenceWindow;
        Assert.Equal(1UL, window.HighestContiguousId);
        Assert.True(window.Contains(1));
        Assert.False(window.Contains(2));
    }

    [Fact]
    public void TheWindowNeverAcknowledgesPastWhatTheOwnerActuallySent()
    {
        // The authority keeps consuming on its own clock during a client stall.
        // Extrapolating the frame/sequence offset would claim sequences the owner
        // never originated, which the client's own receive gate rejects outright
        // — taking the consumed cursor riding alongside it down too.
        var epoch = Epoch();
        var scheduler = Scheduler(epoch, firstFrame: 0, capacity: 16);

        for (var frame = 0L; frame < 10L; frame++)
        {
            Assert.True(scheduler.TryAdmit(Command(epoch, frame)).WasStored);
        }
        for (var frame = 0L; frame < 130L; frame++)
        {
            scheduler.ResolveNextFrame(Basis());
        }

        var window = scheduler.ReceivedInputSequenceWindow;
        Assert.Equal(new SimulationInstant(129), scheduler.ConsumedThroughFrame);
        Assert.Equal(10UL, window.HighestContiguousId);
        Assert.False(window.Contains(11));

        // The client gate must accept this, or it discards the consumed cursor
        // with it and stops pruning exactly when recovery matters most.
        var sendWindow = new OwnerInputSendWindow(OwnerIntentScope.From(epoch));
        for (var frame = 0L; frame < 10L; frame++)
        {
            Assert.Equal(
                OwnerInputWindowAddDecision.Added,
                sendWindow.TryAdd(Command(epoch, frame)));
        }

        Assert.Equal(
            OwnerInputProgressDecision.Applied,
            sendWindow.ApplyAuthorityProgress(
                new OwnerInputReceiveAcknowledgement(
                    OwnerIntentScope.From(epoch),
                    new InputSequence(window.HighestContiguousId!.Value),
                    window.Following64KnownMask),
                scheduler.ConsumedThroughFrame));
        Assert.Equal(scheduler.ConsumedThroughFrame, sendWindow.ConsumedThroughFrame);
    }

    [Fact]
    public void AStreamThatDoesNotBeginAtSequenceOneStillReportsAWindow()
    {
        // Owner input sequences live in a scope that excludes the authority
        // discontinuity, so they continue across a teleport while this scheduler
        // is rebuilt. A scheduler constructed mid-stream legitimately starts at a
        // large sequence and must not silently record nothing.
        var epoch = Epoch();
        var scheduler = Scheduler(epoch, firstFrame: 1_000, capacity: 8);

        for (var frame = 1_000L; frame < 1_004L; frame++)
        {
            Assert.True(scheduler
                .TryAdmit(Command(epoch, frame, sequence: (ulong)frame + 1))
                .WasStored);
        }

        var window = scheduler.ReceivedInputSequenceWindow;
        Assert.Equal(1_004UL, window.HighestContiguousId);
        Assert.True(window.Contains(1_001));
        Assert.False(window.Contains(1_005));
    }

    [Fact]
    public void ADuplicateOrLateCommandDoesNotDisturbTheWindow()
    {
        var epoch = Epoch();
        var scheduler = Scheduler(epoch, firstFrame: 0, capacity: 8);

        Assert.True(scheduler.TryAdmit(Command(epoch, 0)).WasStored);
        Assert.True(scheduler.TryAdmit(Command(epoch, 1)).WasStored);
        scheduler.ResolveNextFrame(Basis());
        var afterConsumption = scheduler.ReceivedInputSequenceWindow;

        // A redundant resend of a stored frame, and a late copy of one already
        // consumed. Neither is a new admission.
        Assert.Equal(
            OwnerInputArrivalDisposition.DuplicateCommand,
            scheduler.TryAdmit(Command(epoch, 1)).Disposition);
        Assert.Equal(
            OwnerInputArrivalDisposition.LateCommand,
            scheduler.TryAdmit(Command(epoch, 0)).Disposition);

        Assert.Equal(afterConsumption, scheduler.ReceivedInputSequenceWindow);
    }

    [Fact]
    public void TheWindowNeverClaimsASequenceThatWasNeitherAdmittedNorRetired()
    {
        var epoch = Epoch();
        var scheduler = Scheduler(epoch, firstFrame: 0, capacity: 8);

        Assert.True(scheduler.TryAdmit(Command(epoch, 0)).WasStored);
        Assert.True(scheduler.TryAdmit(Command(epoch, 1)).WasStored);

        var window = scheduler.ReceivedInputSequenceWindow;

        // Sequences 3+ target frames still ahead of the cursor and have not
        // arrived, so the window must not advertise them.
        Assert.Equal(2UL, window.HighestContiguousId);
        for (var sequence = 3UL; sequence < 40UL; sequence++)
        {
            Assert.False(window.Contains(sequence));
        }
    }

    private static AuthorityFallbackInputBasis Basis() => new(
        ViewOrientation.FromRadians(1.25d, -0.15d),
        new MovementConfigurationRevision(7),
        new MovementCapabilityRevision(8));

    private static AuthorityOwnerInputScheduler Scheduler(
        CombatantAuthorityPredictionEpoch epoch,
        long firstFrame,
        int capacity) => new(epoch, new SimulationInstant(firstFrame), capacity);

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

    private static CharacterSimulationInput Input(double yaw = 1.25d) => new(
        MovementAxes.FromUnitVector(new HorizontalVector(0.6d, -0.4d)),
        ViewOrientation.FromRadians(yaw, -0.15d),
        new MovementHeldState(MovementHeldButtons.Sprint),
        default,
        new CombatInputState(CombatHeldButtons.None),
        default,
        new MovementConfigurationRevision(7),
        new MovementCapabilityRevision(8));
}
