using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Core.Movement.Simulation;
using BattleArena.Multiplayer.AuthoritySimulation;
using BattleArena.Multiplayer.OwnerPrediction;
using BattleArena.Multiplayer.Timing;

namespace BattleArena.Multiplayer.Tests.AuthoritySimulation;

public sealed class AuthorityOwnerSchedulingHostTests
{
    private const long HostCombatant = 1;
    private const long RemoteCombatant = 2;

    [Fact]
    public void EveryRegisteredCombatantResolvesEveryDueFrameExactlyOnce()
    {
        var fixture = new Fixture();
        fixture.RegisterHostAndRemote();

        // Three whole frames' worth of wall time in one callback.
        var summary = fixture.RunFrames(frames: 3);

        Assert.Equal(3, summary.FramesRun);
        Assert.Equal(new SimulationInstant(2), summary.LastCompletedFrame);
        Assert.Equal(3, fixture.Simulator.FrameBoundaries.Count);
        foreach (var combatant in new[] { HostCombatant, RemoteCombatant })
        {
            var resolved = fixture.Simulator.IntegratedFrames[combatant];
            Assert.Equal(new long[] { 0, 1, 2 }, resolved);
        }
    }

    [Fact]
    public void ABurstOfCommandsIsConsumedOverSeparateFramesNotCompacted()
    {
        // The whole point of replacing ConsumeFreshest: four commands arriving
        // together must produce four simulated frames, not one.
        var fixture = new Fixture();
        fixture.RegisterHostAndRemote();
        for (var frame = 0L; frame < 4; frame++)
        {
            Assert.True(fixture.AdmitRemote(frame).WasStored);
        }

        fixture.RunFrames(frames: 4);

        var applied = fixture.Simulator.AppliedKinds[RemoteCombatant];
        Assert.Equal(4, applied.Count);
        Assert.All(applied, kind =>
            Assert.Equal(AuthorityInputApplicationKind.ReceivedCommand, kind));
    }

    [Fact]
    public void AMissedFrameIsStillConsumedAndFallsBackRatherThanBeingSkipped()
    {
        var fixture = new Fixture();
        fixture.RegisterHostAndRemote();
        Assert.True(fixture.AdmitRemote(0).WasStored);
        // Frame 1 never arrives.
        Assert.True(fixture.AdmitRemote(2).WasStored);

        fixture.RunFrames(frames: 3);

        var applied = fixture.Simulator.AppliedKinds[RemoteCombatant];
        Assert.Equal(AuthorityInputApplicationKind.ReceivedCommand, applied[0]);
        Assert.Equal(AuthorityInputApplicationKind.RepeatedContinuous, applied[1]);
        Assert.Equal(AuthorityInputApplicationKind.ReceivedCommand, applied[2]);
    }

    [Fact]
    public void AnEliminatedCombatantConsumesItsFrameUnderADeclaredOverride()
    {
        var fixture = new Fixture();
        fixture.RegisterHostAndRemote();
        fixture.Simulator.OverriddenCombatants.Add(RemoteCombatant);

        fixture.RunFrames(frames: 2);

        var applied = fixture.Simulator.AppliedKinds[RemoteCombatant];
        Assert.Equal(2, applied.Count);
        Assert.All(applied, kind =>
            Assert.Equal(AuthorityInputApplicationKind.AuthorityOverride, kind));
        // The timeline stays contiguous, so a later command for a consumed frame
        // is late rather than landing in a hole.
        Assert.Equal(
            OwnerInputArrivalDisposition.LateCommand,
            fixture.AdmitRemote(0).Disposition);
    }

    [Fact]
    public void AdmissionIsRefusedWhileAFrameRunIsInProgress()
    {
        // Draining network evidence is a distinct pipeline step before the frame
        // boundary. Admitting mid-run would make whether a command reaches its
        // target frame depend on the order combatants were simulated in.
        var fixture = new Fixture();
        fixture.RegisterHostAndRemote();
        AuthorityInputAdmission? midRun = null;
        fixture.Simulator.OnIntegrate = () =>
            midRun ??= fixture.AdmitRemote(5);

        fixture.RunFrames(frames: 1);

        Assert.NotNull(midRun);
        Assert.Equal(
            AuthorityInputAdmissionFault.FrameRunInProgress,
            midRun!.Value.Fault);
    }

    [Fact]
    public void AnUnknownCombatantIsClassifiedRatherThanThrowing()
    {
        var fixture = new Fixture();
        fixture.RegisterHostAndRemote();

        var admission = fixture.Host.TryAdmitRemoteCommand(
            new CombatantId(99),
            fixture.RemoteCommand(0));

        Assert.Equal(AuthorityInputAdmissionFault.UnknownCombatant, admission.Fault);
    }

    [Fact]
    public void ReferencedIntentsApplyOnlyAfterTheirIntentHasBeenObserved()
    {
        var fixture = new Fixture();
        fixture.RegisterHostAndRemote();

        // Referenced without ever observing the intent: nothing may apply.
        Assert.True(fixture.AdmitRemote(0, transitions: [7]).WasStored);
        fixture.RunFrames(frames: 1);
        Assert.True(fixture.Host.TryGetScheduling(new CombatantId(RemoteCombatant), out var s));
        Assert.Equal(0, s.IntentResolver.LastSummary.TransitionsApplied);

        // Observe it, then reference it again on a later frame.
        var summary = fixture.ObserveRemoteTransition(id: 7, firstPredicted: 1, lastValid: 20);
        Assert.Equal(1, summary.TransitionsFirstSeen);
        Assert.True(fixture.AdmitRemote(1, transitions: [7]).WasStored);
        fixture.RunFrames(frames: 1);

        Assert.Equal(1, s.IntentResolver.LastSummary.TransitionsApplied);
    }

    [Fact]
    public void ASameScopeRebuildCarriesResolutionNumberingForward()
    {
        // A teleport changes the authority discontinuity but not the owner intent
        // scope, so the client keeps its applied resolution cursor. Restarting
        // resolution sequences at one would make it discard every new result as
        // stale.
        var fixture = new Fixture();
        fixture.RegisterHostAndRemote();
        fixture.ObserveRemoteTransition(id: 1, firstPredicted: 0, lastValid: 1);
        Assert.True(fixture.AdmitRemote(0, transitions: [1]).WasStored);
        fixture.RunFrames(frames: 1);

        Assert.True(fixture.Host.TryGetScheduling(new CombatantId(RemoteCombatant), out var before));
        var issued = before.IntentResolver.LastTransitionResolutions.ToArray();
        Assert.Single(issued);
        var firstSequence = issued[0].ResolutionIdentity.Sequence.Value;

        // Rebuild with only the authority discontinuity advanced.
        var teleported = fixture.EpochFor(RemoteCombatant, discontinuity: 2, control: 1, life: 1);
        fixture.Host.RebuildForEpoch(teleported, Fixture.Capacity, Fixture.Fallback, false);

        Assert.True(fixture.Host.TryGetScheduling(new CombatantId(RemoteCombatant), out var after));
        Assert.Equal(before.Scope, after.Scope);
        fixture.ObserveRemoteTransition(id: 2, firstPredicted: 1, lastValid: 5, scheduling: after);
        Assert.True(fixture.AdmitRemote(1, transitions: [2], discontinuity: 2).WasStored);
        fixture.RunFrames(frames: 1);

        var next = after.IntentResolver.LastTransitionResolutions.ToArray();
        Assert.Single(next);
        Assert.True(
            next[0].ResolutionIdentity.Sequence.Value > firstSequence,
            "A same-scope rebuild must continue resolution numbering, not restart it.");
    }

    [Fact]
    public void ATimelineResetRebuildsEveryCombatantAgainstTheNewMatchEpoch()
    {
        var fixture = new Fixture();
        fixture.RegisterHostAndRemote();
        fixture.RunFrames(frames: 1);

        var reset = fixture.Host.DemandTimelineReset();
        Assert.True(fixture.Host.IsAwaitingTimelineResume);

        var newEpoch = fixture.Host.MatchFrameEpoch.Next();
        fixture.Host.ResumeAfterMatchEpochReset(newEpoch, new SimulationInstant(100));

        Assert.False(fixture.Host.IsAwaitingTimelineResume);
        Assert.Equal(newEpoch, fixture.Host.MatchFrameEpoch);
        Assert.Equal(new SimulationInstant(100), fixture.Host.NextFrame);
        foreach (var id in new[] { HostCombatant, RemoteCombatant })
        {
            Assert.True(fixture.Host.TryGetScheduling(new CombatantId(id), out var scheduling));
            Assert.Equal(newEpoch, scheduling.Epoch.MatchFrameEpoch);
        }

        // And the host is usable again, which it was not before the resume path
        // existed.
        fixture.RunFrames(frames: 1);
        Assert.Equal(new SimulationInstant(100), fixture.Host.LastCompletedFrame);
        _ = reset;
    }

    [Fact]
    public void APersistentRebaseConditionIsClaimedOnceNotEveryEvaluation()
    {
        var fixture = new Fixture();
        fixture.RegisterHostAndRemote();
        fixture.RunFrames(frames: 1);

        // Zero clock confidence sustains a rebase demand.
        var claims = 0;
        for (var i = 0; i < 10; i++)
        {
            var emission = fixture.Host.EvaluateLead(
                new CombatantId(RemoteCombatant),
                Fixture.Path(80d),
                clockConfidence: 0d);
            if (emission.RebaseClaimed)
            {
                claims++;
            }
        }

        Assert.True(claims <= 1, $"A persistent condition claimed {claims} rebases.");
    }

    [Fact]
    public void TheListenHostGoesThroughTheSamePublisherRulesAsARemoteClient()
    {
        var fixture = new Fixture();
        fixture.RegisterHostAndRemote();

        var published = fixture.Host.PublishHostCommand(
            new CombatantId(HostCombatant),
            new SimulationInstant(0),
            Fixture.Input());

        Assert.True(published.Admission.WasStored);
        fixture.RunFrames(frames: 1);
        Assert.Equal(
            AuthorityInputApplicationKind.ReceivedCommand,
            fixture.Simulator.AppliedKinds[HostCombatant][0]);
    }

    [Fact]
    public void TheListenHostMayPublishItsCommandFromInsideBeginFrame()
    {
        // This is how the arena node actually drives it: the host captures input
        // for the frame about to resolve, from BeginFrame. That window is a fixed
        // point in the ordering — before any combatant resolves — so it is safe,
        // and it must not be refused as reentrant admission.
        var fixture = new Fixture();
        fixture.RegisterHostAndRemote();
        fixture.Simulator.OnBeginFrame = frame => fixture.Host.PublishHostCommand(
            new CombatantId(HostCombatant), frame, Fixture.Input());

        fixture.RunFrames(frames: 3);

        var applied = fixture.Simulator.AppliedKinds[HostCombatant];
        Assert.Equal(3, applied.Count);
        Assert.All(applied, kind =>
            Assert.Equal(AuthorityInputApplicationKind.ReceivedCommand, kind));
    }

    [Fact]
    public void PublishingHostInputFromIntegrateFrameIsRefusedAsOrderDependent()
    {
        var fixture = new Fixture();
        fixture.RegisterHostAndRemote();
        Exception? captured = null;
        fixture.Simulator.OnIntegrate = () =>
        {
            try
            {
                fixture.Host.PublishHostCommand(
                    new CombatantId(HostCombatant),
                    new SimulationInstant(5),
                    Fixture.Input());
            }
            catch (InvalidOperationException ex)
            {
                captured ??= ex;
            }
        };

        fixture.RunFrames(frames: 1);

        Assert.NotNull(captured);
    }

    [Fact]
    public void OwnerStateReportsReceivedAndConsumedAsDistinctFacts()
    {
        var fixture = new Fixture();
        fixture.RegisterHostAndRemote();
        // Admitted three frames ahead of the cursor, consumed only one.
        for (var frame = 0L; frame < 3; frame++)
        {
            Assert.True(fixture.AdmitRemote(frame).WasStored);
        }
        fixture.RunFrames(frames: 1);

        var publication = fixture.Host.BuildOwnerState(new CombatantId(RemoteCombatant));

        Assert.NotNull(publication);
        var state = publication!.Value.State;
        Assert.Equal(new SimulationInstant(0), state.ConsumedThroughFrame);
        Assert.Equal(3UL, state.ReceivedInputWindow.HighestContiguousId);
        Assert.Equal(new SimulationInstant(0), state.AppliedInput.Frame);
        Assert.True(state.AppliedInput.UsedOwnerCommand);
    }

    [Fact]
    public void OwnerStateIsNullBeforeTheFirstFrameResolves()
    {
        var fixture = new Fixture();
        fixture.RegisterHostAndRemote();

        Assert.Null(fixture.Host.BuildOwnerState(new CombatantId(RemoteCombatant)));
    }

    private sealed class Fixture
    {
        internal const int Capacity = 64;
        internal static readonly AuthorityInputFallbackPolicy Fallback = new(2);
        private const int TicksPerSecond = 60;
        private long _timestamp;

        public Fixture()
        {
            Rate = new SimulationRate(TicksPerSecond);
            Host = new AuthorityOwnerSchedulingHost(
                Rate,
                AuthoritySimulationClockPolicy.Default,
                MatchFrameEpochId.Initial,
                SimulationInstant.Zero);
            Simulator = new RecordingSimulator();
        }

        public SimulationRate Rate { get; }

        public static NetworkPathEstimate Path(double rttMilliseconds) =>
            new(rttMilliseconds, 5d, 5d, 0d, 0L, 0L, 1UL);
        public AuthorityOwnerSchedulingHost Host { get; }
        public RecordingSimulator Simulator { get; }

        public CombatantAuthorityPredictionEpoch EpochFor(
            long combatantId,
            ulong discontinuity = 1,
            ulong control = 1,
            long life = 1) => new(
                77UL,
                Host.MatchFrameEpoch,
                new CombatantId(combatantId),
                new LifeGenerationId(life),
                new AuthorityDiscontinuityId(discontinuity),
                new OwnerControlEpoch(control));

        public void RegisterHostAndRemote()
        {
            Host.RegisterCombatant(EpochFor(HostCombatant), Capacity, Fallback, true);
            Host.RegisterCombatant(EpochFor(RemoteCombatant), Capacity, Fallback, false);
        }

        public AuthorityFrameRunSummary RunFrames(int frames)
        {
            _timestamp += frames * 1_000L;
            return Host.RunDueFrames(
                frames * (1000d / TicksPerSecond),
                _timestamp,
                Simulator);
        }

        public static CharacterSimulationInput Input(ulong[]? transitions = null) => new(
            MovementAxes.FromUnitVector(new HorizontalVector(0.5d, -0.25d)),
            ViewOrientation.FromRadians(0.75d, -0.1d),
            new MovementHeldState(MovementHeldButtons.Sprint),
            transitions is null
                ? default
                : new TransitionReferenceBuffer(
                    transitions.Select(id => new MovementTransitionId(id)).ToArray()),
            default,
            default,
            new MovementConfigurationRevision(1),
            new MovementCapabilityRevision(1));

        public OwnerSimulationCommand RemoteCommand(
            long frame,
            ulong[]? transitions = null,
            ulong discontinuity = 1)
        {
            var epoch = EpochFor(RemoteCombatant, discontinuity);
            return new OwnerSimulationCommand(
                new OwnerInputIdentity(
                    OwnerIntentScope.From(epoch),
                    new InputSequence((ulong)frame + 1)),
                epoch.AuthorityDiscontinuity,
                epoch.MatchFrameEpoch,
                new SimulationInstant(frame),
                Input(transitions));
        }

        public AuthorityInputAdmission AdmitRemote(
            long frame,
            ulong[]? transitions = null,
            ulong discontinuity = 1) =>
            Host.TryAdmitRemoteCommand(
                new CombatantId(RemoteCombatant),
                RemoteCommand(frame, transitions, discontinuity));

        public AuthorityIntentObservationSummary ObserveRemoteTransition(
            ulong id,
            long firstPredicted,
            long lastValid,
            AuthorityOwnerCombatantScheduling? scheduling = null)
        {
            var scope = scheduling?.Scope
                ?? OwnerIntentScope.From(EpochFor(RemoteCombatant));
            var intent = new MovementTransitionIntent(
                new MovementTransitionIdentity(scope, new MovementTransitionId(id)),
                new OwnerInputIdentity(scope, new InputSequence(id)),
                MovementTransitionKind.JumpPressed,
                new SimulationInstant(firstPredicted),
                new SimulationInstant(lastValid));
            return Host.TryObserveRemoteIntents(
                new CombatantId(RemoteCombatant),
                new[] { intent },
                ReadOnlySpan<PredictedActionIntent>.Empty);
        }
    }

    private sealed class RecordingSimulator : IAuthorityFrameSimulator
    {
        public List<long> FrameBoundaries { get; } = [];
        public Dictionary<long, List<long>> IntegratedFrames { get; } = [];
        public Dictionary<long, List<AuthorityInputApplicationKind>> AppliedKinds { get; } = [];
        public HashSet<long> OverriddenCombatants { get; } = [];
        public Action? OnIntegrate { get; set; }
        public Action<SimulationInstant>? OnBeginFrame { get; set; }

        public void BeginFrame(SimulationInstant frame)
        {
            FrameBoundaries.Add(frame.Tick);
            OnBeginFrame?.Invoke(frame);
        }

        public void EndFrame(SimulationInstant frame)
        {
        }

        public AuthorityFallbackInputBasis GetFallbackBasis(
            CombatantId combatantId,
            SimulationInstant frame) => new(
                ViewOrientation.FromRadians(0.75d, -0.1d),
                new MovementConfigurationRevision(1),
                new MovementCapabilityRevision(1));

        public bool TryGetAuthorityOverride(
            CombatantId combatantId,
            SimulationInstant frame,
            out CharacterSimulationInput overrideInput,
            out AuthorityInputOverrideReason reason)
        {
            if (!OverriddenCombatants.Contains(combatantId.Value))
            {
                overrideInput = default;
                reason = default;
                return false;
            }

            overrideInput = new CharacterSimulationInput(
                default,
                ViewOrientation.FromRadians(0.75d, -0.1d),
                default,
                default,
                default,
                default,
                new MovementConfigurationRevision(1),
                new MovementCapabilityRevision(1));
            reason = AuthorityInputOverrideReason.Eliminated;
            return true;
        }

        public void IntegrateFrame(
            CombatantId combatantId,
            in AuthorityInputFrameDecision decision)
        {
            if (!IntegratedFrames.TryGetValue(combatantId.Value, out var frames))
            {
                frames = [];
                IntegratedFrames[combatantId.Value] = frames;
                AppliedKinds[combatantId.Value] = [];
            }

            frames.Add(decision.Identity.Frame.Tick);
            AppliedKinds[combatantId.Value].Add(decision.ApplicationKind);
            OnIntegrate?.Invoke();
        }
    }
}
