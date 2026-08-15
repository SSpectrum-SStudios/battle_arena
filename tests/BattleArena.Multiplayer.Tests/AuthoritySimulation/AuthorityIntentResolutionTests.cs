using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Core.Movement.Simulation;
using BattleArena.Multiplayer.AuthoritySimulation;
using BattleArena.Multiplayer.OwnerPrediction;

namespace BattleArena.Multiplayer.Tests.AuthoritySimulation;

public sealed class AuthorityIntentResolutionTests
{
    [Fact]
    public void ATransitionReferencedOnItsPredictedFrameIsAcceptedThere()
    {
        var fixture = new Fixture(firstFrame: 100);
        fixture.ObserveTransition(id: 7, firstPredicted: 100, lastValid: 110);
        fixture.Admit(frame: 100, transitions: [7]);

        var decision = fixture.ResolveFrame();

        Assert.Equal(AuthorityInputApplicationKind.ReceivedCommand, decision.ApplicationKind);
        Assert.Equal(1, fixture.Resolver.LastSummary.TransitionsApplied);

        var resolution = Assert.Single(fixture.Resolver.LastTransitionResolutions.ToArray());
        Assert.Equal(MovementTransitionOutcome.Accepted, resolution.Outcome);
        Assert.True(resolution.HasApplicationFrame);
        Assert.Equal(new SimulationInstant(100), resolution.ApplicationFrame);
    }

    [Fact]
    public void ALateTransitionIsRemappedToAnExplicitlyNamedLaterFrame()
    {
        // The command carrying the jump press on frame 100 was lost. The client
        // keeps advertising transition 7, and it reaches the authority attached
        // to frame 103. It must not be inserted into frame 100, which is gone.
        var fixture = new Fixture(firstFrame: 100);
        fixture.ObserveTransition(id: 7, firstPredicted: 100, lastValid: 110);
        fixture.Admit(frame: 103, transitions: [7]);

        for (var i = 0; i < 3; i++)
        {
            fixture.ResolveFrame();
            Assert.Equal(0, fixture.Resolver.LastSummary.TransitionsApplied);
            Assert.Empty(fixture.Resolver.LastTransitionResolutions.ToArray());
        }

        fixture.ResolveFrame();

        var resolution = Assert.Single(fixture.Resolver.LastTransitionResolutions.ToArray());
        Assert.Equal(MovementTransitionOutcome.Remapped, resolution.Outcome);
        Assert.Equal(new SimulationInstant(103), resolution.ApplicationFrame);
        Assert.Equal(new SimulationInstant(103), resolution.DecisionFrame);
        Assert.Equal(1, fixture.Resolver.LastSummary.TransitionsApplied);
    }

    [Fact]
    public void OneTransitionYieldsExactlyOneTerminalResultUnderHeavyResending()
    {
        var fixture = new Fixture(firstFrame: 0);
        fixture.ObserveTransition(id: 5, firstPredicted: 2, lastValid: 40);

        var applied = 0;
        var resolutions = new List<MovementTransitionResolution>();
        for (var frame = 0; frame < 20; frame++)
        {
            // The client resends the same reference on every command it builds.
            fixture.Admit(frame, transitions: [5]);
            fixture.ResolveFrame();
            applied += fixture.Resolver.LastSummary.TransitionsApplied;
            resolutions.AddRange(fixture.Resolver.LastTransitionResolutions.ToArray());
        }

        Assert.Equal(1, applied);
        var resolution = Assert.Single(resolutions);
        Assert.Equal(MovementTransitionOutcome.Accepted, resolution.Outcome);
        Assert.Equal(new SimulationInstant(2), resolution.ApplicationFrame);
    }

    [Fact]
    public void ATransitionIsNeverAppliedBeforeItsOwnPredictedFrame()
    {
        var fixture = new Fixture(firstFrame: 0);
        fixture.ObserveTransition(id: 9, firstPredicted: 5, lastValid: 20);
        fixture.Admit(frame: 0, transitions: [9]);
        fixture.Admit(frame: 1, transitions: [9]);

        fixture.ResolveFrame();
        Assert.Equal(0, fixture.Resolver.LastSummary.TransitionsApplied);
        Assert.Equal(1, fixture.Resolver.LastSummary.ReferencesIgnored);

        fixture.ResolveFrame();
        Assert.Equal(0, fixture.Resolver.LastSummary.TransitionsApplied);

        Assert.False(fixture.TransitionJournal.TryGetIntent(
            fixture.TransitionIdentity(9),
            out _,
            out var resolution) && resolution is not null);
    }

    [Fact]
    public void AnIntentWhoseCommandsAreAllLostStillReachesATerminalResult()
    {
        // Nothing ever references transition 12, because every command carrying
        // it was dropped. It must still expire, or the client advertises it
        // forever and the journal never drains.
        var fixture = new Fixture(firstFrame: 0);
        fixture.ObserveTransition(id: 12, firstPredicted: 2, lastValid: 4);

        MovementTransitionResolution? expired = null;
        for (var frame = 0; frame <= 6; frame++)
        {
            fixture.ResolveFrame();
            foreach (var resolution in fixture.Resolver.LastTransitionResolutions.ToArray())
            {
                expired = resolution;
            }
        }

        Assert.NotNull(expired);
        Assert.Equal(MovementTransitionOutcome.Expired, expired!.Value.Outcome);
        Assert.Equal(
            MovementTransitionRejectionReason.DeadlineExpired,
            expired.Value.RejectionReason);
        Assert.False(expired.Value.HasApplicationFrame);
        Assert.Equal(0, fixture.TransitionJournal.PendingCount);
    }

    [Fact]
    public void AReferenceArrivingAfterTheDeadlineExpiresRatherThanApplying()
    {
        var fixture = new Fixture(firstFrame: 0);
        fixture.ObserveTransition(id: 3, firstPredicted: 1, lastValid: 2);
        fixture.Admit(frame: 5, transitions: [3]);

        var applied = 0;
        var outcomes = new List<MovementTransitionOutcome>();
        for (var frame = 0; frame <= 5; frame++)
        {
            fixture.ResolveFrame();
            applied += fixture.Resolver.LastSummary.TransitionsApplied;
            outcomes.AddRange(fixture.Resolver.LastTransitionResolutions
                .ToArray()
                .Select(r => r.Outcome));
        }

        Assert.Equal(0, applied);
        Assert.Equal(MovementTransitionOutcome.Expired, Assert.Single(outcomes));
    }

    [Fact]
    public void FallbackAndOverrideFramesCannotFireAnyDurableIntent()
    {
        // This is the "repeating a held command never invents a new press" rule
        // expressed at the scheduling layer.
        var fixture = new Fixture(firstFrame: 0);
        fixture.ObserveTransition(id: 21, firstPredicted: 0, lastValid: 30);
        fixture.ObserveAction(id: 31, predictedStart: 0, lastValidStart: 10);
        fixture.Admit(frame: 0, transitions: [21], actions: [31]);

        var received = fixture.ResolveFrame();
        Assert.Equal(AuthorityInputApplicationKind.ReceivedCommand, received.ApplicationKind);
        Assert.Equal(1, fixture.Resolver.LastSummary.TransitionsApplied);
        Assert.Equal(1, fixture.Resolver.LastSummary.ActionsApplied);

        for (var i = 0; i < 6; i++)
        {
            var decision = fixture.ResolveFrame();
            Assert.NotEqual(AuthorityInputApplicationKind.ReceivedCommand, decision.ApplicationKind);
            Assert.Equal(0, decision.AppliedInput.TransitionReferences.Count);
            Assert.Equal(0, decision.AppliedInput.ActionReferences.Count);
            Assert.Equal(0, fixture.Resolver.LastSummary.TransitionsApplied);
            Assert.Equal(0, fixture.Resolver.LastSummary.ActionsApplied);
        }

        var overrideDecision = fixture.Scheduler.ResolveNextFrameAsAuthorityOverride(
            Fixture.NeutralInput(),
            AuthorityInputOverrideReason.Eliminated,
            fixture.Resolver);
        Assert.Equal(
            AuthorityInputApplicationKind.AuthorityOverride,
            overrideDecision.ApplicationKind);
        Assert.Equal(0, fixture.Resolver.LastSummary.TransitionsApplied);
        Assert.Equal(0, fixture.Resolver.LastSummary.ActionsApplied);
    }

    [Fact]
    public void AnAcceptedActionGetsADistinctAuthorityExecutionIdentity()
    {
        var fixture = new Fixture(firstFrame: 0);
        fixture.ObserveAction(id: 4, predictedStart: 1, lastValidStart: 10);
        fixture.Admit(frame: 1, actions: [4]);

        fixture.ResolveFrame();
        fixture.ResolveFrame();

        var resolution = Assert.Single(fixture.Resolver.LastActionResolutions.ToArray());
        Assert.Equal(PredictedActionOutcome.Accepted, resolution.Outcome);
        Assert.True(resolution.HasAuthorityExecution);
        Assert.True(resolution.AuthorityExecution.IsValid);
        Assert.Equal(new SimulationInstant(1), resolution.StartFrame);

        // The authority execution identity is authority-owned and must not be
        // the client's correlation id in disguise.
        Assert.NotEqual(
            resolution.ActionIdentity.Id.Value,
            resolution.AuthorityExecution.Id.Value);
    }

    [Fact]
    public void APolicyRefusalProducesARejectedResultAndNoApplication()
    {
        var fixture = new Fixture(
            firstFrame: 0,
            transitionPolicy: new ScriptedTransitionPolicy(
                DurableIntentAdmissionDecision.Reject,
                MovementTransitionRejectionReason.CapabilityUnavailable));
        fixture.ObserveTransition(id: 8, firstPredicted: 1, lastValid: 10);
        fixture.Admit(frame: 1, transitions: [8]);

        fixture.ResolveFrame();
        fixture.ResolveFrame();

        var resolution = Assert.Single(fixture.Resolver.LastTransitionResolutions.ToArray());
        Assert.Equal(MovementTransitionOutcome.Rejected, resolution.Outcome);
        Assert.Equal(
            MovementTransitionRejectionReason.CapabilityUnavailable,
            resolution.RejectionReason);
        Assert.False(resolution.HasApplicationFrame);
        Assert.Equal(0, fixture.Resolver.LastSummary.TransitionsApplied);
    }

    [Fact]
    public void AReservedRefusalReasonIsSubstitutedRatherThanAbortingACommittedFrame()
    {
        // The frame is already consumed by the time policy runs. A misbehaving
        // policy must not be able to abort resolution and leave that frame
        // half-processed, so a reserved reason degrades to the generic refusal.
        foreach (var reason in new[]
                 {
                     MovementTransitionRejectionReason.None,
                     MovementTransitionRejectionReason.DeadlineExpired,
                     MovementTransitionRejectionReason.Superseded,
                 })
        {
            var fixture = new Fixture(
                firstFrame: 0,
                transitionPolicy: new ScriptedTransitionPolicy(
                    DurableIntentAdmissionDecision.Reject,
                    reason));
            fixture.ObserveTransition(id: 8, firstPredicted: 0, lastValid: 10);
            fixture.Admit(frame: 0, transitions: [8]);

            fixture.ResolveFrame();

            var resolution = Assert.Single(fixture.Resolver.LastTransitionResolutions.ToArray());
            Assert.Equal(MovementTransitionOutcome.Rejected, resolution.Outcome);
            Assert.Equal(
                MovementTransitionRejectionReason.AuthorityPolicyRejected,
                resolution.RejectionReason);
            Assert.Equal(0, fixture.Resolver.LastSummary.TransitionsApplied);
        }
    }

    [Fact]
    public void PolicyCanSupersedeAnUnresolvedIntent()
    {
        var fixture = new Fixture(
            firstFrame: 0,
            transitionPolicy: new ScriptedTransitionPolicy(
                DurableIntentAdmissionDecision.Supersede,
                MovementTransitionRejectionReason.None));
        fixture.ObserveTransition(id: 6, firstPredicted: 1, lastValid: 10);
        fixture.Admit(frame: 1, transitions: [6]);

        fixture.ResolveFrame();
        fixture.ResolveFrame();

        var resolution = Assert.Single(fixture.Resolver.LastTransitionResolutions.ToArray());
        Assert.Equal(MovementTransitionOutcome.Superseded, resolution.Outcome);
        Assert.Equal(
            MovementTransitionRejectionReason.Superseded,
            resolution.RejectionReason);
        Assert.False(resolution.HasApplicationFrame);
        Assert.Equal(0, fixture.Resolver.LastSummary.TransitionsApplied);
        Assert.Equal(0, fixture.TransitionJournal.PendingCount);
    }

    [Fact]
    public void AFullReferenceSetAndSimultaneousExpiriesAllReportInTheSameFrame()
    {
        // The worst case for the per-frame resolution buffers: a command carrying
        // the maximum 16 references, on the same frame that a separate batch of
        // outstanding intents hits its deadline. Every terminal result must still
        // be reported on the frame it happened, or the client waits on results
        // the authority already decided.
        var fixture = new Fixture(firstFrame: 0, capacity: 32);

        var referenced = new ulong[OwnerSimulationLimits.MaximumTransitionReferences];
        for (var i = 0; i < referenced.Length; i++)
        {
            referenced[i] = (ulong)(i + 1);
            fixture.ObserveTransition(referenced[i], firstPredicted: 5, lastValid: 20);
        }

        // These are never referenced and all expire exactly on frame 5.
        const int expiringCount = 20;
        for (var i = 0; i < expiringCount; i++)
        {
            fixture.ObserveTransition(
                (ulong)(referenced.Length + i + 1),
                firstPredicted: 1,
                lastValid: 4);
        }

        fixture.Admit(frame: 5, transitions: referenced);

        var accepted = 0;
        var expired = 0;
        for (var frame = 0; frame <= 5; frame++)
        {
            fixture.ResolveFrame();
            if (frame < 5)
            {
                continue;
            }

            foreach (var resolution in fixture.Resolver.LastTransitionResolutions.ToArray())
            {
                if (resolution.Outcome == MovementTransitionOutcome.Accepted)
                {
                    accepted++;
                }
                else if (resolution.Outcome == MovementTransitionOutcome.Expired)
                {
                    expired++;
                }
            }
        }

        Assert.Equal(OwnerSimulationLimits.MaximumTransitionReferences, accepted);
        Assert.Equal(expiringCount, expired);
        Assert.Equal(
            OwnerSimulationLimits.MaximumTransitionReferences,
            fixture.Resolver.LastSummary.TransitionsApplied);
        Assert.Equal(0, fixture.TransitionJournal.PendingCount);
    }

    [Fact]
    public void AReferenceToAnUnobservedIntentIsIgnoredWithoutResolvingAnything()
    {
        var fixture = new Fixture(firstFrame: 0);
        fixture.Admit(frame: 0, transitions: [404], actions: [909]);

        fixture.ResolveFrame();

        Assert.Equal(0, fixture.Resolver.LastSummary.TransitionsApplied);
        Assert.Equal(0, fixture.Resolver.LastSummary.ActionsApplied);
        Assert.Equal(2, fixture.Resolver.LastSummary.ReferencesIgnored);
        Assert.Empty(fixture.Resolver.LastTransitionResolutions.ToArray());
        Assert.Empty(fixture.Resolver.LastActionResolutions.ToArray());
    }

    [Fact]
    public void ResolutionsAreReportedOnlyOnTheFrameThatProducedThem()
    {
        var fixture = new Fixture(firstFrame: 0);
        fixture.ObserveTransition(id: 1, firstPredicted: 0, lastValid: 20);
        fixture.Admit(frame: 0, transitions: [1]);

        fixture.ResolveFrame();
        Assert.Single(fixture.Resolver.LastTransitionResolutions.ToArray());

        fixture.ResolveFrame();
        Assert.Empty(fixture.Resolver.LastTransitionResolutions.ToArray());
        Assert.Equal(new SimulationInstant(1), fixture.Resolver.LastSummary.Frame);
    }

    [Fact]
    public void EveryObservedIntentReachesExactlyOneTerminalResultUnderLossyTraffic()
    {
        var fixture = new Fixture(firstFrame: 0, capacity: 32);
        var random = new Random(4404);
        const int intentCount = 40;

        var terminalById = new Dictionary<ulong, int>();
        for (ulong id = 1; id <= intentCount; id++)
        {
            var predicted = (long)id * 3;
            fixture.ObserveTransition(id, firstPredicted: predicted, lastValid: predicted + 6);
            terminalById[id] = 0;
        }

        for (long frame = 0; frame < intentCount * 3 + 40; frame++)
        {
            // Reference a jittered id, dropping ~40% of commands entirely.
            if (random.Next(0, 10) < 6)
            {
                var candidate = (ulong)Math.Clamp(frame / 3 + random.Next(-1, 2), 1, intentCount);
                fixture.Admit(frame, transitions: [candidate]);
            }

            fixture.ResolveFrame();
            foreach (var resolution in fixture.Resolver.LastTransitionResolutions.ToArray())
            {
                terminalById[resolution.TransitionIdentity.Id.Value]++;
            }
        }

        Assert.All(terminalById, pair => Assert.Equal(1, pair.Value));
        Assert.Equal(0, fixture.TransitionJournal.PendingCount);
    }

    [Fact]
    public void TheSuppliedResolverNeverThrowsSoACommittedFrameIsNeverAbandoned()
    {
        // Every policy and journal failure the resolver can meet is classified,
        // not raised. This matters because the sink runs after the frame is
        // committed: a throw there would consume a frame whose decision never
        // reaches the simulation, which is the "increment a frame without
        // simulating it" failure the design prohibits.
        var scenarios = new (DurableIntentAdmissionDecision Decision,
            MovementTransitionRejectionReason Reason)[]
        {
            (DurableIntentAdmissionDecision.Reject, MovementTransitionRejectionReason.None),
            (DurableIntentAdmissionDecision.Reject, MovementTransitionRejectionReason.Superseded),
            // Undefined byte values, not just the named reserved ones: a mis-cast
            // int or a value from a newer wire version must not reach the
            // journal's validation and throw from inside the sink.
            (DurableIntentAdmissionDecision.Reject, (MovementTransitionRejectionReason)200),
            (DurableIntentAdmissionDecision.Reject, (MovementTransitionRejectionReason)255),
            (DurableIntentAdmissionDecision.Supersede, MovementTransitionRejectionReason.None),
            ((DurableIntentAdmissionDecision)200, (MovementTransitionRejectionReason)77),
        };

        foreach (var (decision, reason) in scenarios)
        {
            var fixture = new Fixture(
                firstFrame: 0,
                transitionPolicy: new ScriptedTransitionPolicy(decision, reason));
            fixture.ObserveTransition(id: 2, firstPredicted: 0, lastValid: 10);
            fixture.Admit(frame: 0, transitions: [2, 404]);

            var committed = fixture.ResolveFrame();

            Assert.True(committed.IsValid);
            Assert.Equal(committed, fixture.Scheduler.PreviousDecision);
            Assert.Equal(new SimulationInstant(0), fixture.Scheduler.ConsumedThroughFrame);
            Assert.Equal(0, fixture.Resolver.LastSummary.TransitionsApplied);

            // The timeline still advances normally afterwards.
            Assert.Equal(
                new SimulationInstant(1),
                fixture.ResolveFrame().Identity.Frame);
        }
    }

    [Fact]
    public void AThrowingSinkStillLeavesTheFrameConsumedOnceAndRecoverable()
    {
        var fixture = new Fixture(firstFrame: 0);
        var basis = new AuthorityFallbackInputBasis(
            ViewOrientation.FromRadians(1.25d, -0.15d),
            new MovementConfigurationRevision(7),
            new MovementCapabilityRevision(8));

        Assert.Throws<InvalidOperationException>(() =>
            fixture.Scheduler.ResolveNextFrame(basis, new ThrowingSink()));

        // Exactly one frame was consumed, its record is final, and the decision
        // the caller lost is still readable.
        Assert.Equal(new SimulationInstant(0), fixture.Scheduler.ConsumedThroughFrame);
        Assert.Equal(new SimulationInstant(1), fixture.Scheduler.NextFrameToConsume);
        Assert.NotNull(fixture.Scheduler.PreviousDecision);
        Assert.Equal(
            new SimulationInstant(0),
            fixture.Scheduler.PreviousDecision!.Value.Identity.Frame);
        Assert.True(fixture.Scheduler.TryGetDisposition(new SimulationInstant(0), out _));

        Assert.Equal(
            new SimulationInstant(1),
            fixture.Scheduler.ResolveNextFrame(basis).Identity.Frame);
    }

    [Fact]
    public void AMisbehavingExecutionAllocatorCannotAbortACommittedFrame()
    {
        // The execution allocator is the third injected seam. An allocator that
        // reports success but hands back an invalid or foreign-scoped identity
        // must be classified, not allowed to throw out of the sink.
        foreach (var allocator in new IAuthorityActionExecutionAllocator[]
                 {
                     new FixedIdentityAllocator(default),
                     new FixedIdentityAllocator(new AuthorityActionExecutionIdentity(
                         new AuthorityActionExecutionScope(
                             999,
                             new LifeEpoch(new CombatantId(77), new LifeGenerationId(5))),
                         new AuthorityActionExecutionId(1))),
                     new FailingAllocator(),
                 })
        {
            var fixture = new Fixture(firstFrame: 0, actionExecutionAllocator: allocator);
            fixture.ObserveAction(id: 2, predictedStart: 0, lastValidStart: 8);
            fixture.Admit(frame: 0, actions: [2]);

            var committed = fixture.ResolveFrame();

            Assert.True(committed.IsValid);
            Assert.Equal(committed, fixture.Scheduler.PreviousDecision);
            Assert.Equal(0, fixture.Resolver.LastSummary.ActionsApplied);
            Assert.Empty(fixture.Resolver.LastActionResolutions.ToArray());

            // The intent is left outstanding rather than wrongly terminal, and
            // the timeline continues.
            Assert.Equal(1, fixture.ActionJournal.PendingCount);
            Assert.Equal(new SimulationInstant(1), fixture.ResolveFrame().Identity.Frame);
        }
    }

    private sealed class FixedIdentityAllocator : IAuthorityActionExecutionAllocator
    {
        private readonly AuthorityActionExecutionIdentity _identity;

        public FixedIdentityAllocator(AuthorityActionExecutionIdentity identity) =>
            _identity = identity;

        public bool TryAllocate(
            PredictedActionIntent intent,
            SimulationInstant startFrame,
            out AuthorityActionExecutionIdentity identity)
        {
            identity = _identity;
            return true;
        }
    }

    private sealed class FailingAllocator : IAuthorityActionExecutionAllocator
    {
        public bool TryAllocate(
            PredictedActionIntent intent,
            SimulationInstant startFrame,
            out AuthorityActionExecutionIdentity identity)
        {
            identity = default;
            return false;
        }
    }

    private sealed class ThrowingSink : IAuthorityFrameIntentSink
    {
        public void OnFrameDecisionCommitted(in AuthorityInputFrameDecision decision) =>
            throw new InvalidOperationException("sink failure");
    }

    private sealed class ScriptedTransitionPolicy : IMovementTransitionAdmissionPolicy
    {
        private readonly DurableIntentAdmissionDecision _decision;
        private readonly MovementTransitionRejectionReason _reason;

        public ScriptedTransitionPolicy(
            DurableIntentAdmissionDecision decision,
            MovementTransitionRejectionReason reason)
        {
            _decision = decision;
            _reason = reason;
        }

        public DurableIntentAdmissionDecision Evaluate(
            MovementTransitionIntent intent,
            SimulationInstant applicationFrame,
            out MovementTransitionRejectionReason rejectionReason)
        {
            rejectionReason = _reason;
            return _decision;
        }
    }

    private sealed class Fixture
    {
        private readonly CombatantAuthorityPredictionEpoch _epoch;
        private readonly OwnerIntentScope _scope;
        private long _firstFrame;

        public Fixture(
            long firstFrame,
            int capacity = 16,
            IMovementTransitionAdmissionPolicy? transitionPolicy = null,
            IAuthorityActionExecutionAllocator? actionExecutionAllocator = null)
        {
            _epoch = new CombatantAuthorityPredictionEpoch(
                10,
                new MatchFrameEpochId(1),
                new CombatantId(4),
                new LifeGenerationId(1),
                new AuthorityDiscontinuityId(1),
                new OwnerControlEpoch(3));
            _scope = OwnerIntentScope.From(_epoch);
            _firstFrame = firstFrame;

            Scheduler = new AuthorityOwnerInputScheduler(
                _epoch,
                new SimulationInstant(firstFrame),
                capacity,
                new AuthorityInputFallbackPolicy(2),
                AuthorityOwnerInputScheduler.DefaultRetainedDispositionFrames);
            TransitionJournal = new AuthorityMovementTransitionJournal(_scope, 128);
            ActionJournal = new AuthorityOwnerActionCommandJournal(_scope, 128);
            Resolver = new AuthorityFrameIntentResolver(
                TransitionJournal,
                ActionJournal,
                new MovementTransitionResolver(
                    transitionPolicy ?? PermissiveMovementTransitionAdmissionPolicy.Instance),
                new PredictedActionResolver(
                    actionExecutionAllocator
                        ?? new MonotonicAuthorityActionExecutionAllocator(9_000)));
        }

        public AuthorityOwnerInputScheduler Scheduler { get; }
        public AuthorityMovementTransitionJournal TransitionJournal { get; }
        public AuthorityOwnerActionCommandJournal ActionJournal { get; }
        public AuthorityFrameIntentResolver Resolver { get; }

        public MovementTransitionIdentity TransitionIdentity(ulong id) =>
            new(_scope, new MovementTransitionId(id));

        public void ObserveTransition(ulong id, long firstPredicted, long lastValid)
        {
            var decision = TransitionJournal.Observe(new MovementTransitionIntent(
                TransitionIdentity(id),
                new OwnerInputIdentity(_scope, new InputSequence(id)),
                MovementTransitionKind.JumpPressed,
                new SimulationInstant(firstPredicted),
                new SimulationInstant(lastValid)));
            Assert.Equal(AuthorityTransitionObserveDecision.FirstSeen, decision);
        }

        public void ObserveAction(ulong id, long predictedStart, long lastValidStart)
        {
            var decision = ActionJournal.Observe(new PredictedActionIntent(
                new PredictedActionIdentity(_scope, new PredictedActionId(id)),
                new OwnerInputIdentity(_scope, new InputSequence(id)),
                OwnerActionTrigger.Attack,
                new SimulationInstant(predictedStart),
                new SimulationInstant(lastValidStart),
                new SimulationInstant(predictedStart)));
            Assert.Equal(AuthorityActionObserveDecision.FirstSeen, decision);
        }

        public void Admit(
            long frame,
            ulong[]? transitions = null,
            ulong[]? actions = null)
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
                    _scope,
                    new InputSequence((ulong)(frame - _firstFrame) + 1)),
                _epoch.AuthorityDiscontinuity,
                _epoch.MatchFrameEpoch,
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

        public static CharacterSimulationInput NeutralInput() => new(
            default,
            ViewOrientation.FromRadians(1.25d, -0.15d),
            default,
            default,
            default,
            default,
            new MovementConfigurationRevision(7),
            new MovementCapabilityRevision(8));
    }
}
