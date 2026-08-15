using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Core.Movement.Simulation;
using BattleArena.Multiplayer.AuthoritySimulation;
using BattleArena.Multiplayer.OwnerPrediction;
using BattleArena.Multiplayer.Protocol;
using BattleArena.Protocol.V1;
using Google.Protobuf;

namespace BattleArena.Multiplayer.Tests.AuthoritySimulation;

public sealed class InMemoryAuthorityOwnerCommandPublisherTests
{
    [Fact]
    public void HostCommandsReachTheSchedulerThroughTheNormalAdmissionPath()
    {
        var host = NewOwner();

        for (var frame = 100L; frame < 106L; frame++)
        {
            var result = host.Publisher.Publish(Command(host.Epoch, frame));
            Assert.Equal(OwnerCommandPublishOutcome.Admitted, result.Outcome);
            Assert.True(result.Admission.WasStored);
        }

        Assert.Equal(6, host.Scheduler.StoredCommandCount);
        Assert.Equal(6, host.Publisher.PublishedCommandCount);
        Assert.Equal(6, host.Publisher.AdmittedCommandCount);
    }

    [Fact]
    public void HostAndRemoteProduceIdenticalSchedulingFromIdenticalEvidence()
    {
        // The remote side goes through real Protobuf serialisation and the real
        // inbound validator before reaching its scheduler. If the host path
        // preserved anything the wire drops, or skipped a check the wire
        // applies, the two schedulers would diverge.
        var host = NewOwner();
        var remote = NewOwner();
        var validator = new InboundMessageValidator();
        var random = new Random(4242);

        var hostOutcomes = new List<OwnerCommandPublishOutcome>();
        var remoteOutcomes = new List<OwnerCommandPublishOutcome>();

        for (var step = 0; step < 2_000; step++)
        {
            var frame = host.Scheduler.NextFrameToConsume.Tick + random.Next(-2, 6);
            if (frame >= 0)
            {
                var command = Command(
                    host.Epoch,
                    frame,
                    withEdges: random.Next(0, 3) == 0);

                hostOutcomes.Add(host.Publisher.Publish(command).Outcome);

                var wireCommand = RoundTripThroughTheWire(command, remote.Epoch, validator);
                Assert.NotNull(wireCommand);
                remoteOutcomes.Add(remote.Publisher.Publish(wireCommand!.Value).Outcome);
            }

            if (random.Next(0, 2) == 0)
            {
                var hostDecision = host.Scheduler.ResolveNextFrame(Basis());
                var remoteDecision = remote.Scheduler.ResolveNextFrame(Basis());

                Assert.Equal(hostDecision.ApplicationKind, remoteDecision.ApplicationKind);
                Assert.Equal(hostDecision.Identity.Frame, remoteDecision.Identity.Frame);
                Assert.Equal(hostDecision.AppliedInput, remoteDecision.AppliedInput);
                Assert.Equal(
                    hostDecision.ConsecutiveMissingFrames,
                    remoteDecision.ConsecutiveMissingFrames);
            }
        }

        Assert.Equal(hostOutcomes, remoteOutcomes);
        Assert.Equal(host.Scheduler.ConsumedThroughFrame, remote.Scheduler.ConsumedThroughFrame);
        Assert.Equal(host.Scheduler.StoredCommandCount, remote.Scheduler.StoredCommandCount);
        Assert.Equal(host.Scheduler.LateCommandCount, remote.Scheduler.LateCommandCount);
        Assert.Equal(host.Scheduler.RejectedCommandCount, remote.Scheduler.RejectedCommandCount);
        Assert.Equal(host.Scheduler.DuplicateCommandCount, remote.Scheduler.DuplicateCommandCount);
    }

    [Fact]
    public void TheHostCannotGainInputAFieldTheAuthorityProjectionWouldDrop()
    {
        // Whatever the authority projection preserves is what the host delivers,
        // and nothing else. Publishing then reading back the stored command must
        // equal the projection of the original, field for field.
        var host = NewOwner();
        var command = Command(host.Epoch, 100, withEdges: true);

        Assert.Equal(OwnerCommandPublishOutcome.Admitted, host.Publisher.Publish(command).Outcome);
        Assert.True(host.Scheduler.TryPeek(new SimulationInstant(100), out var stored));

        var projection = new AuthorityOwnerCommandProjection(command);
        Assert.Equal(projection.Identity, stored.Identity);
        Assert.Equal(projection.AuthorityDiscontinuity, stored.AuthorityDiscontinuity);
        Assert.Equal(projection.TargetFrame, stored.TargetFrame);
        Assert.Equal(projection.Input, stored.Input);

        // Combat grammar and durable references survive, because authority needs
        // them; the host gets exactly that and no more.
        Assert.Equal(CombatHeldButtons.Attack, stored.Input.CombatInput.HeldButtons);
        Assert.Equal(1, stored.Input.TransitionReferences.Count);
        Assert.Equal(1, stored.Input.ActionReferences.Count);
    }

    [Fact]
    public void EveryScopeComponentIsRefusedTheSameWayIncludingEpochAndDiscontinuity()
    {
        // The wire validator rejects a whole batch on any scope mismatch.
        // Coercing one component here while rejecting another would hand the
        // host a rule no remote client gets.
        var host = NewOwner();
        var scope = OwnerIntentScope.From(host.Epoch);

        var forgedEpoch = new OwnerSimulationCommand(
            new OwnerInputIdentity(scope, new InputSequence(101)),
            host.Epoch.AuthorityDiscontinuity,
            new MatchFrameEpochId(999),
            new SimulationInstant(100),
            Input());
        var forgedDiscontinuity = new OwnerSimulationCommand(
            new OwnerInputIdentity(scope, new InputSequence(101)),
            new AuthorityDiscontinuityId(999),
            host.Epoch.MatchFrameEpoch,
            new SimulationInstant(100),
            Input());

        Assert.Equal(
            OwnerCommandPublishOutcome.ScopeMismatch,
            host.Publisher.Publish(forgedEpoch).Outcome);
        Assert.Equal(
            OwnerCommandPublishOutcome.ScopeMismatch,
            host.Publisher.Publish(forgedDiscontinuity).Outcome);

        Assert.Equal(0, host.Scheduler.StoredCommandCount);
        Assert.Equal(0, host.Scheduler.RejectedCommandCount);
        Assert.False(host.Scheduler.TryPeek(new SimulationInstant(100), out _));
    }

    [Fact]
    public void ACommandForAnotherOwnerIsRefusedBeforeTheSchedulerSeesIt()
    {
        var host = NewOwner();

        foreach (var foreign in new[]
                 {
                     Command(Epoch(life: 2), 100),
                     Command(Epoch(control: 9), 100),
                     Command(Epoch(session: 77), 100),
                     Command(Epoch(combatant: 8), 100),
                 })
        {
            var result = host.Publisher.Publish(foreign);
            Assert.Equal(OwnerCommandPublishOutcome.ScopeMismatch, result.Outcome);
        }

        Assert.Equal(0, host.Scheduler.StoredCommandCount);
        Assert.Equal(0, host.Scheduler.RejectedCommandCount);
        Assert.Equal(0, host.Publisher.AdmittedCommandCount);
    }

    [Fact]
    public void TheHostGetsNoRelaxationOfSchedulingRules()
    {
        var host = NewOwner();

        // Late is late, even with no network in the way.
        host.Scheduler.ResolveNextFrame(Basis());
        Assert.Equal(
            OwnerCommandPublishOutcome.Late,
            host.Publisher.Publish(Command(host.Epoch, 100)).Outcome);

        // The acceptance horizon still bounds the host.
        Assert.Equal(
            OwnerCommandPublishOutcome.Rejected,
            host.Publisher.Publish(Command(host.Epoch, 100 + 9_999)).Outcome);

        // First admission still wins for the host too.
        Assert.Equal(
            OwnerCommandPublishOutcome.Admitted,
            host.Publisher.Publish(Command(host.Epoch, 105)).Outcome);
        Assert.Equal(
            OwnerCommandPublishOutcome.Duplicate,
            host.Publisher.Publish(Command(host.Epoch, 105)).Outcome);
        Assert.Equal(
            OwnerCommandPublishOutcome.Rejected,
            host.Publisher.Publish(Command(host.Epoch, 105, yaw: 0.9d)).Outcome);
    }

    [Fact]
    public void ConstructionFailsClosed()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new InMemoryAuthorityOwnerCommandPublisher(null!));
        Assert.Throws<ArgumentNullException>(() =>
            new InMemoryAuthorityOwnerCommandPublisher(NewOwner().Scheduler, null!));
    }

    /// <summary>
    /// A real remote path: encode the authority projection into the protocol
    /// draft, serialise and deserialise it with Protobuf, run the inbound
    /// validator over the decoded batch, then rebuild the domain command from
    /// the decoded bytes. Comparing the host against a projection re-applied in
    /// memory would compare the code path to itself and prove nothing.
    /// </summary>
    private static OwnerSimulationCommand? RoundTripThroughTheWire(
        OwnerSimulationCommand command,
        CombatantAuthorityPredictionEpoch receivingEpoch,
        InboundMessageValidator validator)
    {
        var projection = new AuthorityOwnerCommandProjection(command);
        var draft = new OwnerSimulationCommandDraft
        {
            TargetSimulationTick = (ulong)projection.TargetFrame.Tick,
            InputSequence = projection.Identity.Sequence.Value,
            MoveXQ15 = projection.Input.Movement.XQ15,
            MoveZQ15 = projection.Input.Movement.ZQ15,
            ViewYawU16 = projection.Input.View.YawU16,
            ViewPitchI16 = projection.Input.View.PitchI16,
            HeldMovementBits = (uint)projection.Input.MovementHeld.Buttons,
            HeldCombatBits = (uint)projection.Input.CombatInput.HeldButtons,
            MovementProfileRevision = projection.Input.MovementRevision.Value,
            MovementCapabilityRevision = projection.Input.CapabilityRevision.Value,
        };
        for (var i = 0; i < projection.Input.TransitionReferences.Count; i++)
        {
            draft.TransitionReferences.Add(projection.Input.TransitionReferences[i].Value);
        }
        for (var i = 0; i < projection.Input.ActionReferences.Count; i++)
        {
            draft.ActionReferences.Add(projection.Input.ActionReferences[i].Value);
        }

        var batch = new OwnerCommandBatchDraft
        {
            Scope = new OwnerPredictionScopeDraft
            {
                MatchFrameEpoch = command.MatchFrameEpoch.Value,
                CombatantId = (ulong)receivingEpoch.CombatantId.Value,
                LifeId = (ulong)receivingEpoch.Life.Value,
                AuthorityDiscontinuityId = command.AuthorityDiscontinuity.Value,
                OwnerControlEpoch = receivingEpoch.OwnerControl.Value,
                SessionId = receivingEpoch.SessionId,
            },
            PacketSequence = 1,
        };
        batch.Commands.Add(draft);

        // Genuine serialisation, so any field the wire cannot carry is lost here.
        var bytes = batch.ToByteArray();
        var decoded = OwnerCommandBatchDraft.Parser.ParseFrom(bytes);

        var validation = validator.ValidateOwnerCommandBatchDraft(
            decoded,
            ValidationContext(receivingEpoch));
        if (!validation.IsValid)
        {
            return null;
        }

        var decodedCommand = decoded.Commands[0];
        var transitions = decodedCommand.TransitionReferences
            .Select(id => new MovementTransitionId(id))
            .ToArray();
        var actions = decodedCommand.ActionReferences
            .Select(id => new PredictedActionId(id))
            .ToArray();

        return new OwnerSimulationCommand(
            new OwnerInputIdentity(
                OwnerIntentScope.From(receivingEpoch),
                new InputSequence(decodedCommand.InputSequence)),
            new AuthorityDiscontinuityId(decoded.Scope.AuthorityDiscontinuityId),
            new MatchFrameEpochId(decoded.Scope.MatchFrameEpoch),
            new SimulationInstant((long)decodedCommand.TargetSimulationTick),
            new CharacterSimulationInput(
                new MovementAxes(
                    (short)decodedCommand.MoveXQ15,
                    (short)decodedCommand.MoveZQ15),
                new ViewOrientation(
                    (ushort)decodedCommand.ViewYawU16,
                    (short)decodedCommand.ViewPitchI16),
                new MovementHeldState((MovementHeldButtons)decodedCommand.HeldMovementBits),
                transitions.Length == 0
                    ? default
                    : new TransitionReferenceBuffer(transitions),
                new CombatInputState((CombatHeldButtons)decodedCommand.HeldCombatBits),
                actions.Length == 0
                    ? default
                    : new ActionReferenceBuffer(actions),
                new MovementConfigurationRevision(decodedCommand.MovementProfileRevision),
                new MovementCapabilityRevision(decodedCommand.MovementCapabilityRevision)));
    }

    private static OwnerCommandDraftValidationContext ValidationContext(
        CombatantAuthorityPredictionEpoch epoch) => new(
        new OwnerPredictionDraftScopeExpectation(
            epoch.SessionId,
            epoch.MatchFrameEpoch.Value,
            (ulong)epoch.CombatantId.Value,
            (ulong)epoch.Life.Value,
            epoch.AuthorityDiscontinuity.Value,
            epoch.OwnerControl.Value),
        EarliestRetainedTargetTick: 0,
        LatestPermittedTargetTick: 1_000_000,
        HighestAuthorityFramePublished: 1_000_000,
        HighestAuthorityStreamSequencePublished: 1_000_000,
        HighestKnownInputSequence: 1_000_000,
        HighestOriginatedInputSequence: 1_000_000,
        MaximumPermittedInputSequence: 1_000_000,
        KnownTransitionIds: new OwnerKnownJournalIdentityWindow(64, 0),
        MaximumPermittedTransitionId: 1_000,
        KnownActionIds: new OwnerKnownJournalIdentityWindow(64, 0),
        MaximumPermittedActionId: 1_000,
        MaximumMovementProfileRevision: 1_000,
        MaximumMovementCapabilityRevision: 1_000,
        MaximumIssuedTransitionResolutionSequence: null,
        MaximumIssuedActionResolutionSequence: null);

    private sealed record Owner(
        CombatantAuthorityPredictionEpoch Epoch,
        AuthorityOwnerInputScheduler Scheduler,
        InMemoryAuthorityOwnerCommandPublisher Publisher);

    private static Owner NewOwner()
    {
        var epoch = Epoch();
        var scheduler = new AuthorityOwnerInputScheduler(
            epoch,
            new SimulationInstant(100),
            capacityFrames: 32,
            new AuthorityInputFallbackPolicy(2),
            AuthorityOwnerInputScheduler.DefaultRetainedDispositionFrames);
        return new Owner(epoch, scheduler, new InMemoryAuthorityOwnerCommandPublisher(scheduler));
    }

    private static AuthorityFallbackInputBasis Basis() => new(
        ViewOrientation.FromRadians(1.25d, -0.15d),
        new MovementConfigurationRevision(7),
        new MovementCapabilityRevision(8));

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
        bool withEdges = false,
        double yaw = 1.25d) => new(
            new OwnerInputIdentity(
                OwnerIntentScope.From(epoch),
                new InputSequence((ulong)frame + 1)),
            epoch.AuthorityDiscontinuity,
            epoch.MatchFrameEpoch,
            new SimulationInstant(frame),
            Input(withEdges, yaw));

    private static CharacterSimulationInput Input(bool withEdges = false, double yaw = 1.25d) => new(
        MovementAxes.FromUnitVector(new HorizontalVector(0.6d, -0.4d)),
        ViewOrientation.FromRadians(yaw, -0.15d),
        new MovementHeldState(MovementHeldButtons.Sprint),
        withEdges
            ? new TransitionReferenceBuffer([new MovementTransitionId(3)])
            : default,
        new CombatInputState(CombatHeldButtons.Attack),
        withEdges
            ? new ActionReferenceBuffer([new PredictedActionId(4)])
            : default,
        new MovementConfigurationRevision(7),
        new MovementCapabilityRevision(8));
}
