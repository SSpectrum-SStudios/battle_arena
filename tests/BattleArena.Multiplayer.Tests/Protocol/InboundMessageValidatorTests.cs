using BattleArena.Multiplayer.Protocol;
using BattleArena.Protocol.V1;
using Google.Protobuf;

namespace BattleArena.Multiplayer.Tests.Protocol;

public sealed class InboundMessageValidatorTests
{
    private const ulong SessionId = 73;
    private readonly InboundMessageValidator validator = new();

    [Fact]
    public void ValidClientInputIsAcceptedForEstablishedSession()
    {
        var envelope = CreateInputEnvelope();

        var result = validator.Validate(envelope, EstablishedClientContext());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ClientCannotSendAuthoritySnapshot()
    {
        var envelope = CreateEnvelope();
        envelope.AuthoritySnapshot = new AuthoritySnapshot();

        var result = validator.Validate(envelope, EstablishedClientContext());

        Assert.False(result.IsValid);
        Assert.Equal(ProtocolViolationCode.UnexpectedMessageDirection, result.Violation?.Code);
    }

    [Fact]
    public void EstablishedPacketMustMatchTransportSession()
    {
        var envelope = CreateInputEnvelope();
        envelope.SessionId = 999;

        var result = validator.Validate(envelope, EstablishedClientContext());

        Assert.False(result.IsValid);
        Assert.Equal(ProtocolViolationCode.InvalidSession, result.Violation?.Code);
    }

    [Fact]
    public void NonFiniteInputIsRejected()
    {
        var envelope = CreateInputEnvelope();
        envelope.ClientInputBatch.Frames[0].MoveX = float.NaN;

        var result = validator.Validate(envelope, EstablishedClientContext());

        Assert.False(result.IsValid);
        Assert.Equal(ProtocolViolationCode.InvalidNumericValue, result.Violation?.Code);
    }

    [Fact]
    public void UnknownPressedInputBitIsRejected()
    {
        var envelope = CreateInputEnvelope();
        envelope.ClientInputBatch.Frames[0].PressedButtonBits =
            ProtocolConstants.KnownInputButtonMask + 1;

        var result = validator.Validate(envelope, EstablishedClientContext());

        Assert.False(result.IsValid);
        Assert.Equal(ProtocolViolationCode.InvalidNumericValue, result.Violation?.Code);
    }

    [Fact]
    public void ActionRequestRequiresLifeIdentity()
    {
        var envelope = CreateEnvelope();
        envelope.ClientActionRequest = new ClientActionRequest
        {
            ActionSequence = 1,
            ClientTick = 1,
            Kind = ClientActionKind.Attack,
            EquipmentSlot = 0,
        };

        var result = validator.Validate(envelope, EstablishedClientContext());

        Assert.False(result.IsValid);
        Assert.Equal(ProtocolViolationCode.InvalidNumericValue, result.Violation?.Code);
    }

    [Fact]
    public void InputFramesMustBeStrictlyOrderedWithinBatch()
    {
        var envelope = CreateInputEnvelope();
        envelope.ClientInputBatch.Frames.Add(new ClientInputFrame
        {
            InputSequence = 1,
            ClientTick = 2,
            EstimatedAuthorityTick = 2,
            MovementProfileRevision = 1,
            MovementCapabilityRevision = 1,
        });

        var result = validator.Validate(envelope, EstablishedClientContext());

        Assert.False(result.IsValid);
        Assert.Equal(ProtocolViolationCode.InvalidSequence, result.Violation?.Code);
    }

    [Fact]
    public void InitialJoinRequiresNoClaimedSessionAndValidNonce()
    {
        var envelope = new PacketEnvelope
        {
            ProtocolVersion = ProtocolConstants.CurrentVersion,
            Sequence = 1,
            JoinRequest = new JoinRequest
            {
                ClientProtocolVersion = ProtocolConstants.CurrentVersion,
                DisplayName = "Player",
                ClientNonce = ByteString.CopyFrom(new byte[ProtocolConstants.ClientNonceBytes]),
            },
        };
        var context = new ProtocolValidationContext(RemoteEndpointRole.Client, null, false);

        var result = validator.Validate(envelope, context);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ReconnectTokenMustHaveExactLength()
    {
        var envelope = CreateEnvelope();
        envelope.ReconnectRequest = new ReconnectRequest
        {
            SessionId = SessionId,
            PlayerId = 1,
            ReconnectToken = ByteString.CopyFrom(new byte[4]),
        };

        var context = new ProtocolValidationContext(RemoteEndpointRole.Client, SessionId, false);

        var result = validator.Validate(envelope, context);

        Assert.False(result.IsValid);
        Assert.Equal(ProtocolViolationCode.InvalidCredential, result.Violation?.Code);
    }

    [Fact]
    public void AuthorityCanEstablishSessionWithJoinAcceptance()
    {
        var envelope = CreateEnvelope();
        envelope.JoinAccepted = new JoinAccepted
        {
            SessionId = SessionId,
            SessionPeerId = 10,
            ConnectionGeneration = 1,
            PlayerId = 10,
            CombatantId = 11,
            ReconnectToken = ByteString.CopyFrom(new byte[ProtocolConstants.ReconnectTokenBytes]),
            SimulationTicksPerSecond = 60,
            SnapshotRate = 30,
            CheckpointIntervalTicks = 60,
        };
        var context = new ProtocolValidationContext(RemoteEndpointRole.Authority, null, false);

        var result = validator.Validate(envelope, context);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0UL, 1U)]
    [InlineData(10UL, 0U)]
    public void JoinAcceptanceRequiresStablePeerIdentityAndConnectionGeneration(
        ulong sessionPeerId,
        uint connectionGeneration)
    {
        var envelope = CreateEnvelope();
        envelope.JoinAccepted = new JoinAccepted
        {
            SessionId = SessionId,
            SessionPeerId = sessionPeerId,
            ConnectionGeneration = connectionGeneration,
            PlayerId = 10,
            CombatantId = 11,
            ReconnectToken = ByteString.CopyFrom(new byte[ProtocolConstants.ReconnectTokenBytes]),
            SimulationTicksPerSecond = 60,
            SnapshotRate = 30,
            CheckpointIntervalTicks = 60,
        };
        var context = new ProtocolValidationContext(RemoteEndpointRole.Authority, null, false);

        var result = new InboundMessageValidator().Validate(envelope, context);

        Assert.False(result.IsValid);
        Assert.Equal(ProtocolViolationCode.InvalidSession, result.Violation?.Code);
    }

    [Fact]
    public void AuthorityMovementFrameRequiresValidCompactState()
    {
        var envelope = CreateEnvelope();
        envelope.AuthorityMovementFrameBatch = new AuthorityMovementFrameBatch
        {
            StreamSequence = 1,
        };
        envelope.AuthorityMovementFrameBatch.Combatants.Add(
            new AuthoritativeMovementState
            {
                CombatantId = 10,
                LifeId = 2,
                Position = new Vector3Value(),
                Velocity = new Vector3Value(),
                RollDirection = new Vector3Value(),
                LocomotionMode = ReplicatedLocomotionMode.Grounded,
                PostureMode = ReplicatedPostureMode.Standing,
                MovementActionMode = ReplicatedMovementActionMode.Ready,
                JumpPhase = ReplicatedJumpPhase.None,
                MovementProfileRevision = 1,
                MovementCapabilityRevision = 1,
            });
        var context = new ProtocolValidationContext(
            RemoteEndpointRole.Authority,
            SessionId,
            SessionEstablished: true);

        var result = new InboundMessageValidator().Validate(envelope, context);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ClientCannotSendAuthorityMovementFrame()
    {
        var envelope = CreateEnvelope();
        envelope.AuthorityMovementFrameBatch = new AuthorityMovementFrameBatch
        {
            StreamSequence = 1,
        };
        var context = new ProtocolValidationContext(
            RemoteEndpointRole.Client,
            SessionId,
            SessionEstablished: true);

        var result = new InboundMessageValidator().Validate(envelope, context);

        Assert.False(result.IsValid);
        Assert.Equal(
            ProtocolViolationCode.UnexpectedMessageDirection,
            result.Violation?.Code);
    }

    [Fact]
    public void AuthorityAcceptedMovementRequiresCanonicalIdentityAndValidInput()
    {
        var envelope = CreateEnvelope();
        envelope.AuthorityAcceptedMovementBatch = new AuthorityAcceptedMovementBatch
        {
            StreamSequence = 1,
        };
        envelope.AuthorityAcceptedMovementBatch.Commands.Add(
            new AuthorityAcceptedMovementCommand
            {
                SourceSessionPeerId = 2,
                PeerSessionGeneration = 1,
                CombatantId = 2,
                LifeId = 1,
                AppliedAuthorityTick = 30,
                AppliedMovementProfileRevision = 1,
                AppliedMovementCapabilityRevision = 1,
                Input = new ClientInputFrame
                {
                    InputSequence = 20,
                    ClientTick = 29,
                    EstimatedAuthorityTick = 30,
                    MovementProfileRevision = 1,
                    MovementCapabilityRevision = 1,
                    MoveZ = -1,
                },
            });

        var result = validator.Validate(envelope, EstablishedAuthorityContext());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void DamageEventIsBoundToTargetLife()
    {
        var envelope = CreateEnvelope();
        envelope.AuthorityEventBatch = new AuthorityEventBatch();
        envelope.AuthorityEventBatch.Events.Add(new AuthorityEvent
        {
            EventSequence = 1,
            AuthorityTick = 30,
            Damage = new DamageEvent
            {
                SourceCombatantId = 2,
                SourceLifeId = 3,
                TargetCombatantId = 4,
                TargetLifeId = 5,
                NetDamage = 20,
                CurrentHealth = 80,
                MaximumHealth = 100,
            },
        });

        var valid = validator.Validate(envelope, EstablishedAuthorityContext());
        envelope.AuthorityEventBatch.Events[0].Damage.TargetLifeId = 0;
        var invalid = validator.Validate(envelope, EstablishedAuthorityContext());

        Assert.True(valid.IsValid);
        Assert.Equal(ProtocolViolationCode.InvalidSession, invalid.Violation?.Code);
    }

    [Fact]
    public void ActionEventCarriesExplicitLifecyclePhaseAndPolicyRevision()
    {
        var envelope = CreateEnvelope();
        envelope.AuthorityEventBatch = new AuthorityEventBatch();
        envelope.AuthorityEventBatch.Events.Add(new AuthorityEvent
        {
            EventSequence = 1,
            AuthorityTick = 30,
            ActionState = new ActionStateEvent
            {
                SourceCombatantId = 2,
                SourceLifeId = 3,
                AttackExecutionId = 4,
                AttackStartedTick = 30,
                Lifecycle = ReplicatedActionLifecycleKind.Started,
                Phase = ReplicatedAttackPhase.Startup,
                PhaseStartedTick = 30,
                WeaponDefinitionId = "base:fighter_starter_sword",
                AttackPolicyRevision = 1,
            },
        });

        var valid = validator.Validate(envelope, EstablishedAuthorityContext());
        envelope.AuthorityEventBatch.Events[0].ActionState.Lifecycle =
            ReplicatedActionLifecycleKind.Unspecified;
        var invalid = validator.Validate(envelope, EstablishedAuthorityContext());

        Assert.True(valid.IsValid);
        Assert.Equal(ProtocolViolationCode.InvalidNumericValue, invalid.Violation?.Code);
    }

    [Fact]
    public void ClientCannotSendAuthorityAcceptedMovement()
    {
        var envelope = CreateEnvelope();
        envelope.AuthorityAcceptedMovementBatch = new AuthorityAcceptedMovementBatch
        {
            StreamSequence = 1,
        };

        var result = validator.Validate(envelope, EstablishedClientContext());

        Assert.False(result.IsValid);
        Assert.Equal(
            ProtocolViolationCode.UnexpectedMessageDirection,
            result.Violation?.Code);
    }

    [Fact]
    public void OwnerCommandDraftAcceptsDefaultBurstAndQuantizationBoundaries()
    {
        var batch = CreateOwnerCommandBatch(
            BattleArena.Multiplayer.OwnerPrediction.OwnerInputSendWindowLimits
                .DefaultMaximumCommandsPerBatch);
        var context = OwnerCommandContext(
            latestTargetTick: 200,
            maximumInputSequence: 100);

        var result = validator.ValidateOwnerCommandBatchDraft(batch, context);

        Assert.True(result.IsValid);
        Assert.Equal(
            BattleArena.Multiplayer.OwnerPrediction.OwnerInputSendWindowLimits
                .DefaultMaximumCommandsPerBatch,
            batch.Commands.Count);
        Assert.Equal(-ProtocolConstants.OwnerAxisQ15Magnitude,
            batch.Commands[0].MoveXQ15);
        Assert.Equal(ProtocolConstants.OwnerViewYawU16Maximum,
            batch.Commands[0].ViewYawU16);
    }

    [Fact]
    public void OwnerCommandDraftAcceptsJournalOnlyRetryAndControlOnlyHeartbeat()
    {
        var journalOnly = CreateOwnerCommandBatch(commandCount: 0);
        journalOnly.OutstandingTransitions.Add(new MovementTransitionIntentDraft
        {
            TransitionId = 4,
            OriginatingInputSequence = 9,
            Kind = OwnerMovementTransitionKindDraft.JumpReleased,
            FirstPredictedTick = 95,
            LastValidTick = 110,
        });
        journalOnly.OutstandingActions.Add(new PredictedActionIntentDraft
        {
            ActionId = 4,
            OriginatingInputSequence = 9,
            Trigger = OwnerActionTriggerDraft.Attack,
            PredictedStartTick = 95,
            LastValidStartTick = 110,
            RenderedAuthorityTick = 94,
        });
        var heartbeat = CreateOwnerCommandBatch(commandCount: 0);

        Assert.True(validator.ValidateOwnerCommandBatchDraft(
            journalOnly,
            OwnerCommandContext()).IsValid);
        Assert.True(validator.ValidateOwnerCommandBatchDraft(
            heartbeat,
            OwnerCommandContext()).IsValid);
    }

    [Fact]
    public void OwnerCommandDraftRejectsEverySpoofedScopeComponent()
    {
        var mutations = new Action<OwnerPredictionScopeDraft>[]
        {
            scope => scope.SessionId++,
            scope => scope.MatchFrameEpoch++,
            scope => scope.CombatantId++,
            scope => scope.LifeId++,
            scope => scope.AuthorityDiscontinuityId++,
            scope => scope.OwnerControlEpoch++,
        };

        foreach (var mutate in mutations)
        {
            var batch = CreateOwnerCommandBatch();
            mutate(batch.Scope);

            var result = validator.ValidateOwnerCommandBatchDraft(
                batch,
                OwnerCommandContext());

            Assert.Equal(ProtocolViolationCode.InvalidSession, result.Violation?.Code);
        }
    }

    [Fact]
    public void OwnerCommandDraftRejectsMalformedScalarsFramesAndRevisions()
    {
        var mutations = new (Action<OwnerCommandBatchDraft> Apply,
            ProtocolViolationCode Code)[]
        {
            (batch => batch.PacketSequence = 0, ProtocolViolationCode.InvalidSequence),
            (batch => batch.Commands[0].InputSequence = 0,
                ProtocolViolationCode.InvalidSequence),
            (batch => batch.Commands[0].InputSequence = 81,
                ProtocolViolationCode.InvalidSequence),
            (batch => batch.Commands[0].TargetSimulationTick = 89,
                ProtocolViolationCode.InvalidNumericValue),
            (batch => batch.Commands[0].TargetSimulationTick = 141,
                ProtocolViolationCode.InvalidNumericValue),
            (batch => batch.Commands[0].TargetSimulationTick =
                (ulong)long.MaxValue + 1,
                ProtocolViolationCode.InvalidNumericValue),
            (batch => batch.Commands[0].MoveXQ15 = -32_768,
                ProtocolViolationCode.InvalidNumericValue),
            (batch => batch.Commands[0].MoveZQ15 = 32_768,
                ProtocolViolationCode.InvalidNumericValue),
            (batch => batch.Commands[0].ViewYawU16 = 65_536,
                ProtocolViolationCode.InvalidNumericValue),
            (batch => batch.Commands[0].ViewPitchI16 = -32_768,
                ProtocolViolationCode.InvalidNumericValue),
            (batch => batch.Commands[0].HeldMovementBits = 0x8,
                ProtocolViolationCode.InvalidNumericValue),
            (batch => batch.Commands[0].HeldCombatBits = 0x200,
                ProtocolViolationCode.InvalidNumericValue),
            (batch => batch.Commands[0].MovementProfileRevision = 0,
                ProtocolViolationCode.InvalidNumericValue),
            (batch => batch.Commands[0].MovementProfileRevision = 11,
                ProtocolViolationCode.InvalidNumericValue),
            (batch => batch.Commands[0].MovementCapabilityRevision = 13,
                ProtocolViolationCode.InvalidNumericValue),
            (batch => batch.LatestAuthorityFrameObserved = 121,
                ProtocolViolationCode.InvalidNumericValue),
            (batch => batch.LatestAuthorityStreamSequenceObserved = 21,
                ProtocolViolationCode.InvalidNumericValue),
            (batch => batch.TransitionResolutionCursorApplied = 31,
                ProtocolViolationCode.InvalidSequence),
            (batch => batch.ActionResolutionCursorApplied = 32,
                ProtocolViolationCode.InvalidSequence),
        };

        foreach (var mutation in mutations)
        {
            var batch = CreateOwnerCommandBatch();
            mutation.Apply(batch);

            var result = validator.ValidateOwnerCommandBatchDraft(
                batch,
                OwnerCommandContext());

            Assert.Equal(mutation.Code, result.Violation?.Code);
        }
    }

    [Fact]
    public void OwnerCommandDraftRejectsOversizedCollectionsBeforePayloadUse()
    {
        var cases = new List<OwnerCommandBatchDraft>();

        var commands = CreateOwnerCommandBatch();
        while (commands.Commands.Count <= ProtocolConstants.MaxOwnerCommandsPerBatch)
        {
            commands.Commands.Add(new OwnerSimulationCommandDraft());
        }
        cases.Add(commands);

        var transitions = CreateOwnerCommandBatch();
        while (transitions.OutstandingTransitions.Count <=
               ProtocolConstants.MaxOwnerJournalEntriesPerBatch)
        {
            transitions.OutstandingTransitions.Add(new MovementTransitionIntentDraft());
        }
        cases.Add(transitions);

        var actions = CreateOwnerCommandBatch();
        while (actions.OutstandingActions.Count <=
               ProtocolConstants.MaxOwnerJournalEntriesPerBatch)
        {
            actions.OutstandingActions.Add(new PredictedActionIntentDraft());
        }
        cases.Add(actions);

        foreach (var batch in cases)
        {
            var result = validator.ValidateOwnerCommandBatchDraft(
                batch,
                OwnerCommandContext());
            Assert.Equal(
                ProtocolViolationCode.InvalidCollectionCount,
                result.Violation?.Code);
        }

        var transitionReferences = CreateOwnerCommandBatch();
        transitionReferences.Commands[0].TransitionReferences.Clear();
        for (var id = 1;
             id <= ProtocolConstants.MaxOwnerTransitionReferencesPerCommand + 1;
             id++)
        {
            transitionReferences.Commands[0].TransitionReferences.Add((ulong)id);
        }
        Assert.Equal(
            ProtocolViolationCode.InvalidCollectionCount,
            validator.ValidateOwnerCommandBatchDraft(
                transitionReferences,
                OwnerCommandContext()).Violation?.Code);

        var actionReferences = CreateOwnerCommandBatch();
        actionReferences.Commands[0].ActionReferences.Clear();
        for (var id = 1;
             id <= ProtocolConstants.MaxOwnerActionReferencesPerCommand + 1;
             id++)
        {
            actionReferences.Commands[0].ActionReferences.Add((ulong)id);
        }
        Assert.Equal(
            ProtocolViolationCode.InvalidCollectionCount,
            validator.ValidateOwnerCommandBatchDraft(
                actionReferences,
                OwnerCommandContext()).Violation?.Code);
    }

    [Fact]
    public void OwnerCommandDraftRejectsUnknownEnumsDuplicateAndUnresolvedEvidence()
    {
        var cases = new (Action<OwnerCommandBatchDraft> Apply,
            ProtocolViolationCode Code)[]
        {
            (batch => batch.OutstandingTransitions[0].Kind =
                (OwnerMovementTransitionKindDraft)999,
                ProtocolViolationCode.InvalidEnumValue),
            (batch => batch.OutstandingActions[0].Trigger =
                (OwnerActionTriggerDraft)999,
                ProtocolViolationCode.InvalidEnumValue),
            (batch => batch.Commands.Add(batch.Commands[0].Clone()),
                ProtocolViolationCode.InvalidSequence),
            (batch => batch.OutstandingTransitions.Add(
                batch.OutstandingTransitions[0].Clone()),
                ProtocolViolationCode.InvalidSequence),
            (batch => batch.OutstandingActions.Add(
                batch.OutstandingActions[0].Clone()),
                ProtocolViolationCode.InvalidSequence),
            (batch => batch.Commands[0].TransitionReferences[0] = 6,
                ProtocolViolationCode.InvalidSequence),
            (batch => batch.Commands[0].ActionReferences[0] = 6,
                ProtocolViolationCode.InvalidSequence),
            (batch => batch.OutstandingTransitions[0].LastValidTick = 99,
                ProtocolViolationCode.InvalidNumericValue),
            (batch => batch.OutstandingTransitions[0].LastValidTick = 3_701,
                ProtocolViolationCode.InvalidNumericValue),
            (batch => batch.OutstandingActions[0].RenderedAuthorityTick = 101,
                ProtocolViolationCode.InvalidNumericValue),
        };

        foreach (var mutation in cases)
        {
            var batch = CreateOwnerCommandBatch();
            mutation.Apply(batch);

            var result = validator.ValidateOwnerCommandBatchDraft(
                batch,
                OwnerCommandContext());

            Assert.Equal(mutation.Code, result.Violation?.Code);
        }
    }

    [Fact]
    public void OwnerCommandDraftAcceptsMaximumAtomicReferencesAndJournalPayloadCounts()
    {
        var references = CreateOwnerCommandBatch();
        references.Commands[0].TransitionReferences.Clear();
        references.Commands[0].ActionReferences.Clear();
        references.OutstandingTransitions.Clear();
        references.OutstandingActions.Clear();
        for (var id = 1;
             id <= ProtocolConstants.MaxOwnerTransitionReferencesPerCommand;
             id++)
        {
            references.Commands[0].TransitionReferences.Add((ulong)id);
        }
        for (var id = 1;
             id <= ProtocolConstants.MaxOwnerActionReferencesPerCommand;
             id++)
        {
            references.Commands[0].ActionReferences.Add((ulong)id);
        }

        var journals = CreateOwnerCommandBatch(commandCount: 0);
        for (var id = 5;
             id < 5 + ProtocolConstants.MaxOwnerJournalEntriesPerBatch;
             id++)
        {
            journals.OutstandingTransitions.Add(new MovementTransitionIntentDraft
            {
                TransitionId = (ulong)id,
                OriginatingInputSequence = 9,
                Kind = OwnerMovementTransitionKindDraft.JumpPressed,
                FirstPredictedTick = 95,
                LastValidTick = 110,
            });
            journals.OutstandingActions.Add(new PredictedActionIntentDraft
            {
                ActionId = (ulong)id,
                OriginatingInputSequence = 9,
                Trigger = OwnerActionTriggerDraft.Attack,
                PredictedStartTick = 95,
                LastValidStartTick = 110,
                RenderedAuthorityTick = 94,
            });
        }

        Assert.True(validator.ValidateOwnerCommandBatchDraft(
            references,
            OwnerCommandContext(
                highestContiguousTransitionId: 16,
                highestContiguousActionId: 8)).IsValid);
        Assert.True(validator.ValidateOwnerCommandBatchDraft(
            journals,
            OwnerCommandContext(
                highestContiguousTransitionId: 12,
                highestContiguousActionId: 12)).IsValid);
    }

    [Fact]
    public void OwnerCommandDraftRejectsLegalCountsThatExceedDatagramBodyAllowance()
    {
        var batch = CreateOwnerCommandBatch(
            ProtocolConstants.MaxOwnerCommandsPerBatch);
        batch.OutstandingTransitions.Clear();
        batch.OutstandingActions.Clear();
        foreach (var command in batch.Commands)
        {
            command.TransitionReferences.Clear();
            command.ActionReferences.Clear();
            for (var id = 1;
                 id <= ProtocolConstants.MaxOwnerTransitionReferencesPerCommand;
                 id++)
            {
                command.TransitionReferences.Add((ulong)id);
            }
            for (var id = 1;
                 id <= ProtocolConstants.MaxOwnerActionReferencesPerCommand;
                 id++)
            {
                command.ActionReferences.Add((ulong)id);
            }
        }

        Assert.True(batch.CalculateSize() >
            ProtocolConstants.MaxOwnerCommandDraftBodyBytes);
        Assert.Equal(
            ProtocolViolationCode.PacketTooLarge,
            validator.ValidateOwnerCommandBatchDraft(
                batch,
                OwnerCommandContext(
                    latestTargetTick: 200,
                    maximumInputSequence: 100,
                    highestContiguousTransitionId: 20,
                    highestContiguousActionId: 20)).Violation?.Code);
    }

    [Fact]
    public void OwnerCommandDraftCorrelatesIntentStartToOriginatingCommand()
    {
        var transition = CreateOwnerCommandBatch();
        transition.OutstandingTransitions[0].FirstPredictedTick = 101;
        var action = CreateOwnerCommandBatch();
        action.OutstandingActions[0].PredictedStartTick = 101;
        var stale = CreateOwnerCommandBatch();
        stale.OutstandingTransitions[0].FirstPredictedTick = 80;
        stale.OutstandingTransitions[0].LastValidTick = 89;

        Assert.All(new[] { transition, action, stale }, batch => Assert.Equal(
            ProtocolViolationCode.InvalidNumericValue,
            validator.ValidateOwnerCommandBatchDraft(
                batch,
                OwnerCommandContext()).Violation?.Code));
    }

    [Fact]
    public void FirstSeenIntentRequiresExactlyItsOriginatingCommandReference()
    {
        var missingTransitionReference = CreateOwnerCommandBatch();
        missingTransitionReference.Commands[0].TransitionReferences.Clear();

        var missingActionReference = CreateOwnerCommandBatch();
        missingActionReference.Commands[0].ActionReferences.Clear();

        var wrongCommandReference = CreateOwnerCommandBatch();
        var second = wrongCommandReference.Commands[0].Clone();
        second.InputSequence = 11;
        second.TargetSimulationTick = 101;
        second.TransitionReferences.Clear();
        second.ActionReferences.Clear();
        wrongCommandReference.Commands.Add(second);
        wrongCommandReference.Commands[0].TransitionReferences.Clear();
        wrongCommandReference.Commands[1].TransitionReferences.Add(5);

        var journalOnlyFirstObservation = CreateOwnerCommandBatch(commandCount: 0);
        journalOnlyFirstObservation.OutstandingTransitions.Add(
            new MovementTransitionIntentDraft
            {
                TransitionId = 5,
                OriginatingInputSequence = 9,
                Kind = OwnerMovementTransitionKindDraft.JumpPressed,
                FirstPredictedTick = 95,
                LastValidTick = 110,
            });

        Assert.All(
            new[]
            {
                missingTransitionReference,
                missingActionReference,
                wrongCommandReference,
                journalOnlyFirstObservation,
            },
            batch => Assert.Equal(
                ProtocolViolationCode.InvalidSequence,
                validator.ValidateOwnerCommandBatchDraft(
                    batch,
                    OwnerCommandContext()).Violation?.Code));
    }

    [Fact]
    public void SelectiveKnownJournalWindowDoesNotTreatGapAsKnown()
    {
        var knownOutOfOrderRetry = CreateOwnerCommandBatch(commandCount: 0);
        knownOutOfOrderRetry.OutstandingTransitions.Add(
            new MovementTransitionIntentDraft
            {
                TransitionId = 6,
                OriginatingInputSequence = 9,
                Kind = OwnerMovementTransitionKindDraft.JumpReleased,
                FirstPredictedTick = 95,
                LastValidTick = 110,
            });
        knownOutOfOrderRetry.OutstandingActions.Add(
            new PredictedActionIntentDraft
            {
                ActionId = 6,
                OriginatingInputSequence = 9,
                Trigger = OwnerActionTriggerDraft.Attack,
                PredictedStartTick = 95,
                LastValidStartTick = 110,
                RenderedAuthorityTick = 94,
            });
        var missingGapPayload = CreateOwnerCommandBatch();
        missingGapPayload.OutstandingTransitions.Clear();
        missingGapPayload.Commands[0].TransitionReferences[0] = 5;
        var missingActionGapPayload = CreateOwnerCommandBatch();
        missingActionGapPayload.OutstandingActions.Clear();
        missingActionGapPayload.Commands[0].ActionReferences[0] = 5;
        var journalOnlyGap = CreateOwnerCommandBatch(commandCount: 0);
        journalOnlyGap.OutstandingTransitions.Add(
            new MovementTransitionIntentDraft
            {
                TransitionId = 5,
                OriginatingInputSequence = 9,
                Kind = OwnerMovementTransitionKindDraft.JumpPressed,
                FirstPredictedTick = 95,
                LastValidTick = 110,
            });
        var journalOnlyActionGap = CreateOwnerCommandBatch(commandCount: 0);
        journalOnlyActionGap.OutstandingActions.Add(
            new PredictedActionIntentDraft
            {
                ActionId = 5,
                OriginatingInputSequence = 9,
                Trigger = OwnerActionTriggerDraft.Attack,
                PredictedStartTick = 95,
                LastValidStartTick = 110,
                RenderedAuthorityTick = 94,
            });
        var context = OwnerCommandContext(
            highestContiguousTransitionId: 4,
            followingKnownTransitionMask: 0b10,
            highestContiguousActionId: 4,
            followingKnownActionMask: 0b10);

        Assert.True(validator.ValidateOwnerCommandBatchDraft(
            knownOutOfOrderRetry,
            context).IsValid);
        Assert.Equal(
            ProtocolViolationCode.InvalidSequence,
            validator.ValidateOwnerCommandBatchDraft(
                missingGapPayload,
                context).Violation?.Code);
        Assert.Equal(
            ProtocolViolationCode.InvalidSequence,
            validator.ValidateOwnerCommandBatchDraft(
                missingActionGapPayload,
                context).Violation?.Code);
        Assert.Equal(
            ProtocolViolationCode.InvalidSequence,
            validator.ValidateOwnerCommandBatchDraft(
                journalOnlyGap,
                context).Violation?.Code);
        Assert.Equal(
            ProtocolViolationCode.InvalidSequence,
            validator.ValidateOwnerCommandBatchDraft(
                journalOnlyActionGap,
                context).Violation?.Code);
    }

    [Fact]
    public void OwnerCommandBodyCeilingAcceptsExactBoundaryAndRejectsNextByte()
    {
        var exact = PadOwnerCommandBatchToSize(
            CreateOwnerCommandBatch(),
            ProtocolConstants.MaxOwnerCommandDraftBodyBytes);
        var oversized = PadOwnerCommandBatchToSize(
            CreateOwnerCommandBatch(),
            ProtocolConstants.MaxOwnerCommandDraftBodyBytes + 1);

        Assert.Equal(ProtocolConstants.MaxOwnerCommandDraftBodyBytes,
            exact.CalculateSize());
        Assert.True(validator.ValidateOwnerCommandBatchDraft(
            exact,
            OwnerCommandContext()).IsValid);
        Assert.Equal(
            ProtocolViolationCode.PacketTooLarge,
            validator.ValidateOwnerCommandBatchDraft(
                oversized,
                OwnerCommandContext()).Violation?.Code);
    }

    [Fact]
    public void OwnerCommandAndAcknowledgementOptionalCursorsFailClosed()
    {
        var noCursors = CreateOwnerCommandBatch();
        noCursors.ClearTransitionResolutionCursorApplied();
        noCursors.ClearActionResolutionCursorApplied();
        Assert.True(validator.ValidateOwnerCommandBatchDraft(
            noCursors,
            OwnerCommandContext()).IsValid);

        var zeroCursor = CreateOwnerCommandBatch();
        zeroCursor.TransitionResolutionCursorApplied = 0;
        Assert.Equal(
            ProtocolViolationCode.InvalidSequence,
            validator.ValidateOwnerCommandBatchDraft(
                zeroCursor,
                OwnerCommandContext()).Violation?.Code);

        var noReceiveCursor = new OwnerInputReceiveAcknowledgementDraft
        {
            Scope = OwnerScope(),
            ReceivedInputs = new SelectiveSequenceAcknowledgementDraft
            {
                Following64ReceivedMask = 1,
            },
        };
        Assert.True(validator.ValidateOwnerInputReceiveAcknowledgementDraft(
            noReceiveCursor,
            OwnerCommandContext()).IsValid);

        var frameZeroConsumption = new OwnerInputConsumptionAcknowledgementDraft
        {
            Scope = OwnerScope(),
            ConsumedThroughSimulationTick = 0,
        };
        Assert.True(validator.ValidateOwnerInputConsumptionAcknowledgementDraft(
            frameZeroConsumption,
            OwnerCommandContext()).IsValid);
    }

    [Fact]
    public void OwnerCommandDraftAcceptsSimulationFrameZeroWithoutTreatingItAsAbsent()
    {
        var batch = CreateOwnerCommandBatch();
        batch.LatestAuthorityFrameObserved = 0;
        batch.LatestAuthorityStreamSequenceObserved = 0;
        batch.Commands[0].TargetSimulationTick = 0;
        batch.OutstandingTransitions[0].FirstPredictedTick = 0;
        batch.OutstandingTransitions[0].LastValidTick = 10;
        batch.OutstandingActions[0].PredictedStartTick = 0;
        batch.OutstandingActions[0].LastValidStartTick = 10;
        batch.OutstandingActions[0].RenderedAuthorityTick = 0;

        var result = validator.ValidateOwnerCommandBatchDraft(
            batch,
            OwnerCommandContext(
                earliestTargetTick: 0,
                latestTargetTick: 10,
                highestAuthorityFrame: 0,
                highestAuthorityStreamSequence: 0));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void OwnerDraftUnknownEnumFuzzReturnsStableEnumViolations()
    {
        var unknownValues = new[] { int.MinValue, -1, 999, int.MaxValue };
        foreach (var value in unknownValues)
        {
            var transition = CreateOwnerCommandBatch();
            transition.OutstandingTransitions[0].Kind =
                (OwnerMovementTransitionKindDraft)value;
            Assert.Equal(
                ProtocolViolationCode.InvalidEnumValue,
                validator.ValidateOwnerCommandBatchDraft(
                    transition,
                    OwnerCommandContext()).Violation?.Code);

            var action = CreateOwnerCommandBatch();
            action.OutstandingActions[0].Trigger = (OwnerActionTriggerDraft)value;
            Assert.Equal(
                ProtocolViolationCode.InvalidEnumValue,
                validator.ValidateOwnerCommandBatchDraft(
                    action,
                    OwnerCommandContext()).Violation?.Code);

            var consumption = ValidConsumptionAcknowledgement();
            consumption.RecentDispositions[0].Kind =
                (OwnerInputFrameDispositionKindDraft)value;
            Assert.Equal(
                ProtocolViolationCode.InvalidEnumValue,
                validator.ValidateOwnerInputConsumptionAcknowledgementDraft(
                    consumption,
                    OwnerCommandContext()).Violation?.Code);

            var bootstrap = ValidBootstrapPlan();
            bootstrap.Kind = (OwnerPredictionBootstrapKindDraft)value;
            Assert.Equal(
                ProtocolViolationCode.InvalidEnumValue,
                validator.ValidateOwnerPredictionBootstrapPlanDraft(
                    bootstrap,
                    OwnerControlContext()).Violation?.Code);
        }
    }

    [Fact]
    public void OwnerReceiveAndConsumptionDraftsValidatePresenceAndTerminalSemantics()
    {
        var receive = new OwnerInputReceiveAcknowledgementDraft
        {
            Scope = OwnerScope(),
            ReceivedInputs = new SelectiveSequenceAcknowledgementDraft
            {
                HighestContiguousSequence = 9,
                Following64ReceivedMask = 0b11,
            },
        };
        var consumption = new OwnerInputConsumptionAcknowledgementDraft
        {
            Scope = OwnerScope(),
            ConsumedThroughSimulationTick = 120,
        };
        consumption.RecentDispositions.Add(new OwnerInputFrameDispositionDraft
        {
            TargetSimulationTick = 118,
            InputSequence = 10,
            Kind = OwnerInputFrameDispositionKindDraft.ReceivedCommand,
        });
        consumption.RecentDispositions.Add(new OwnerInputFrameDispositionDraft
        {
            TargetSimulationTick = 119,
            Kind = OwnerInputFrameDispositionKindDraft.RepeatedContinuousFallback,
        });
        consumption.RecentDispositions.Add(new OwnerInputFrameDispositionDraft
        {
            TargetSimulationTick = 120,
            Kind = OwnerInputFrameDispositionKindDraft.AuthorityOverride,
        });

        Assert.True(validator.ValidateOwnerInputReceiveAcknowledgementDraft(
            receive,
            OwnerCommandContext()).IsValid);
        Assert.True(validator.ValidateOwnerInputConsumptionAcknowledgementDraft(
            consumption,
            OwnerCommandContext()).IsValid);

        receive.ReceivedInputs.HighestContiguousSequence = 79;
        receive.ReceivedInputs.Following64ReceivedMask = 0b10;
        Assert.Equal(
            ProtocolViolationCode.InvalidSequence,
            validator.ValidateOwnerInputReceiveAcknowledgementDraft(
                receive,
                OwnerCommandContext()).Violation?.Code);

        var missingCursor = consumption.Clone();
        missingCursor.ClearConsumedThroughSimulationTick();
        Assert.Equal(
            ProtocolViolationCode.InvalidNumericValue,
            validator.ValidateOwnerInputConsumptionAcknowledgementDraft(
                missingCursor,
                OwnerCommandContext()).Violation?.Code);

        var contradictory = consumption.Clone();
        contradictory.RecentDispositions[1].InputSequence = 11;
        Assert.Equal(
            ProtocolViolationCode.InvalidNumericValue,
            validator.ValidateOwnerInputConsumptionAcknowledgementDraft(
                contradictory,
                OwnerCommandContext()).Violation?.Code);
    }

    [Fact]
    public void OwnerConsumptionDraftRejectsUnknownDuplicateFutureAndOversizedDispositions()
    {
        var mutations = new Action<OwnerInputConsumptionAcknowledgementDraft>[]
        {
            ack => ack.RecentDispositions.Add(
                ack.RecentDispositions[0].Clone()),
            ack => ack.RecentDispositions[0].TargetSimulationTick = 121,
            ack => ack.RecentDispositions[0].Kind =
                (OwnerInputFrameDispositionKindDraft)999,
            ack => ack.RecentDispositions[0].ClearInputSequence(),
            ack => ack.RecentDispositions[0].InputSequence = 81,
        };

        foreach (var mutate in mutations)
        {
            var acknowledgement = ValidConsumptionAcknowledgement();
            mutate(acknowledgement);
            Assert.False(validator.ValidateOwnerInputConsumptionAcknowledgementDraft(
                acknowledgement,
                OwnerCommandContext()).IsValid);
        }

        var oversized = ValidConsumptionAcknowledgement();
        oversized.RecentDispositions.Clear();
        for (var index = 0;
             index <= ProtocolConstants.MaxOwnerRecentInputDispositions;
             index++)
        {
            oversized.RecentDispositions.Add(new OwnerInputFrameDispositionDraft());
        }
        Assert.Equal(
            ProtocolViolationCode.InvalidCollectionCount,
            validator.ValidateOwnerInputConsumptionAcknowledgementDraft(
                oversized,
                OwnerCommandContext()).Violation?.Code);

        var duplicateInput = ValidConsumptionAcknowledgement();
        duplicateInput.RecentDispositions.Add(new OwnerInputFrameDispositionDraft
        {
            TargetSimulationTick = 119,
            InputSequence = 10,
            Kind = OwnerInputFrameDispositionKindDraft.LateCommand,
        });
        Assert.Equal(
            ProtocolViolationCode.InvalidSequence,
            validator.ValidateOwnerInputConsumptionAcknowledgementDraft(
                duplicateInput,
                OwnerCommandContext()).Violation?.Code);

        var stale = ValidConsumptionAcknowledgement();
        stale.RecentDispositions[0].TargetSimulationTick = 89;
        Assert.Equal(
            ProtocolViolationCode.InvalidNumericValue,
            validator.ValidateOwnerInputConsumptionAcknowledgementDraft(
                stale,
                OwnerCommandContext()).Violation?.Code);
    }

    [Fact]
    public void OwnerBootstrapDraftValidatesCompleteFrozenAndNeutralSchedules()
    {
        var frozen = ValidBootstrapPlan();
        var neutral = ValidBootstrapPlan();
        neutral.Kind = OwnerPredictionBootstrapKindDraft.TimelineRebase;
        neutral.Preparation = OwnerPredictionBaselinePreparationDraft.NeutralPreroll;
        neutral.BaselineTick = 90;

        Assert.True(validator.ValidateOwnerPredictionBootstrapPlanDraft(
            frozen,
            OwnerControlContext()).IsValid);
        Assert.True(validator.ValidateOwnerPredictionBootstrapPlanDraft(
            neutral,
            OwnerControlContext()).IsValid);
    }

    [Fact]
    public void OwnerBootstrapDraftRejectsMalformedAndSpoofedControlEvidence()
    {
        var mutations = new (Action<OwnerPredictionBootstrapPlanDraft> Apply,
            ProtocolViolationCode Code)[]
        {
            (plan => plan.Scope.LifeId++, ProtocolViolationCode.InvalidSession),
            (plan => plan.PlanId = 0, ProtocolViolationCode.InvalidSequence),
            (plan => plan.Kind = (OwnerPredictionBootstrapKindDraft)999,
                ProtocolViolationCode.InvalidEnumValue),
            (plan => plan.Preparation =
                OwnerPredictionBaselinePreparationDraft.NeutralPreroll,
                ProtocolViolationCode.InvalidNumericValue),
            (plan => plan.SimulationTicksPerSecond = 61,
                ProtocolViolationCode.InvalidNumericValue),
            (plan => plan.AuthorityFrameBoundaryTimestampMicroseconds = 0,
                ProtocolViolationCode.InvalidNumericValue),
            (plan => plan.MovementProfileRevision = 0,
                ProtocolViolationCode.InvalidNumericValue),
            (plan => plan.MovementCapabilityRevision = 13,
                ProtocolViolationCode.InvalidNumericValue),
            (plan => plan.CollisionContentHash = ByteString.CopyFrom(new byte[31]),
                ProtocolViolationCode.InvalidNumericValue),
            (plan => plan.CollisionContentHash = ByteString.CopyFrom(
                    Enumerable.Repeat((byte)255,
                        ProtocolConstants.OwnerCollisionContentHashBytes).ToArray()),
                ProtocolViolationCode.InvalidNumericValue),
            (plan => plan.TargetLeadFrames = 1,
                ProtocolViolationCode.InvalidNumericValue),
            (plan => plan.TargetLeadFrames = 49,
                ProtocolViolationCode.InvalidNumericValue),
            (plan => plan.LocalInputEnableTick = 100,
                ProtocolViolationCode.InvalidNumericValue),
            (plan => plan.FirstCommandTargetTick = 117,
                ProtocolViolationCode.InvalidNumericValue),
            (plan => plan.PublishedAuthorityTick = (ulong)long.MaxValue + 1,
                ProtocolViolationCode.InvalidNumericValue),
        };

        foreach (var mutation in mutations)
        {
            var plan = ValidBootstrapPlan();
            mutation.Apply(plan);
            var result = validator.ValidateOwnerPredictionBootstrapPlanDraft(
                plan,
                OwnerControlContext());
            Assert.Equal(mutation.Code, result.Violation?.Code);
        }
    }

    [Fact]
    public void BaselineReceiptAndLeadUpdateRequireExactScopeAndPendingSchedule()
    {
        var receipt = new OwnerPredictionBaselineReceiptDraft
        {
            PlanId = 7,
            Scope = OwnerScope(),
            BaselineTick = 115,
            Source = OwnerPredictionBaselineSourceDraft.AuthorityMovement,
        };
        var lead = new OwnerPredictionLeadUpdateDraft
        {
            Scope = OwnerScope(),
            TargetLeadFrames = 6,
            LeadPolicyRevision = 8,
            EffectiveTick = 130,
        };

        Assert.True(validator.ValidateOwnerPredictionBaselineReceiptDraft(
            receipt,
            OwnerScopeExpectation(),
            expectedPlanId: 7,
            expectedBaselineTick: 115).IsValid);
        Assert.True(validator.ValidateOwnerPredictionLeadUpdateDraft(
            lead,
            OwnerControlContext()).IsValid);

        receipt.PlanId = 8;
        Assert.Equal(
            ProtocolViolationCode.InvalidSequence,
            validator.ValidateOwnerPredictionBaselineReceiptDraft(
                receipt,
                OwnerScopeExpectation(),
                expectedPlanId: 7,
                expectedBaselineTick: 115).Violation?.Code);
        receipt.PlanId = 7;
        receipt.Source = (OwnerPredictionBaselineSourceDraft)999;
        Assert.Equal(
            ProtocolViolationCode.InvalidEnumValue,
            validator.ValidateOwnerPredictionBaselineReceiptDraft(
                receipt,
                OwnerScopeExpectation(),
                expectedPlanId: 7,
                expectedBaselineTick: 115).Violation?.Code);

        lead.EffectiveTick = 129;
        Assert.Equal(
            ProtocolViolationCode.InvalidNumericValue,
            validator.ValidateOwnerPredictionLeadUpdateDraft(
                lead,
                OwnerControlContext()).Violation?.Code);
        lead.EffectiveTick = 130;
        lead.Scope.OwnerControlEpoch++;
        Assert.Equal(
            ProtocolViolationCode.InvalidSession,
            validator.ValidateOwnerPredictionLeadUpdateDraft(
                lead,
                OwnerControlContext()).Violation?.Code);
    }

    [Fact]
    public void UnauthenticatedPeerCanRequestReconnectToKnownSession()
    {
        var envelope = CreateEnvelope();
        envelope.ReconnectRequest = new ReconnectRequest
        {
            SessionId = SessionId,
            PlayerId = 1,
            ReconnectToken = ByteString.CopyFrom(new byte[ProtocolConstants.ReconnectTokenBytes]),
        };
        var context = new ProtocolValidationContext(RemoteEndpointRole.Client, SessionId, false);

        var result = validator.Validate(envelope, context);

        Assert.True(result.IsValid);
    }

    private static PacketEnvelope CreateInputEnvelope()
    {
        var envelope = CreateEnvelope();
        envelope.ClientInputBatch = new ClientInputBatch();
        envelope.ClientInputBatch.Frames.Add(new ClientInputFrame
        {
            InputSequence = 1,
            ClientTick = 1,
            EstimatedAuthorityTick = 1,
            MovementProfileRevision = 1,
            MovementCapabilityRevision = 1,
            MoveX = 0.5f,
            MoveZ = -0.25f,
            ViewYawRadians = 0.2f,
            ViewPitchRadians = -0.1f,
            ButtonBits = 3,
        });
        return envelope;
    }

    private static OwnerCommandBatchDraft CreateOwnerCommandBatch(int commandCount = 1)
    {
        var batch = new OwnerCommandBatchDraft
        {
            Scope = OwnerScope(),
            PacketSequence = 1,
            TransitionResolutionCursorApplied = 20,
            ActionResolutionCursorApplied = 21,
            LatestAuthorityFrameObserved = 100,
            LatestAuthorityStreamSequenceObserved = 10,
        };
        for (var index = 0; index < commandCount; index++)
        {
            batch.Commands.Add(new OwnerSimulationCommandDraft
            {
                InputSequence = (ulong)(10 + index),
                TargetSimulationTick = (ulong)(100 + index),
                MoveXQ15 = -ProtocolConstants.OwnerAxisQ15Magnitude,
                MoveZQ15 = ProtocolConstants.OwnerAxisQ15Magnitude,
                ViewYawU16 = ProtocolConstants.OwnerViewYawU16Maximum,
                ViewPitchI16 = -ProtocolConstants.OwnerViewPitchI16Magnitude,
                HeldMovementBits = ProtocolConstants.KnownOwnerHeldMovementMask,
                HeldCombatBits = ProtocolConstants.KnownOwnerHeldCombatMask,
                MovementProfileRevision = 10,
                MovementCapabilityRevision = 12,
            });
        }
        if (commandCount == 0)
        {
            return batch;
        }

        batch.Commands[0].TransitionReferences.Add(5);
        batch.Commands[0].ActionReferences.Add(5);
        batch.OutstandingTransitions.Add(new MovementTransitionIntentDraft
        {
            TransitionId = 5,
            OriginatingInputSequence = 10,
            Kind = OwnerMovementTransitionKindDraft.JumpPressed,
            FirstPredictedTick = 100,
            LastValidTick = 110,
        });
        batch.OutstandingActions.Add(new PredictedActionIntentDraft
        {
            ActionId = 5,
            OriginatingInputSequence = 10,
            Trigger = OwnerActionTriggerDraft.Attack,
            PredictedStartTick = 100,
            LastValidStartTick = 110,
            RenderedAuthorityTick = 99,
        });
        return batch;
    }

    private static OwnerInputConsumptionAcknowledgementDraft
        ValidConsumptionAcknowledgement()
    {
        var acknowledgement = new OwnerInputConsumptionAcknowledgementDraft
        {
            Scope = OwnerScope(),
            ConsumedThroughSimulationTick = 120,
        };
        acknowledgement.RecentDispositions.Add(new OwnerInputFrameDispositionDraft
        {
            TargetSimulationTick = 120,
            InputSequence = 10,
            Kind = OwnerInputFrameDispositionKindDraft.ReceivedCommand,
        });
        return acknowledgement;
    }

    private static OwnerCommandBatchDraft PadOwnerCommandBatchToSize(
        OwnerCommandBatchDraft batch,
        int targetBytes)
    {
        var encoded = batch.ToByteArray();
        for (var paddingBytes = 0; paddingBytes <= targetBytes; paddingBytes++)
        {
            using var stream = new MemoryStream();
            stream.Write(encoded);
            WriteVarint(stream, (1_000UL << 3) | 2UL);
            WriteVarint(stream, (ulong)paddingBytes);
            stream.Write(new byte[paddingBytes]);
            var padded = OwnerCommandBatchDraft.Parser.ParseFrom(stream.ToArray());
            if (padded.CalculateSize() == targetBytes)
            {
                return padded;
            }
        }
        throw new InvalidOperationException(
            $"Could not construct an owner command body of exactly {targetBytes} bytes.");
    }

    private static void WriteVarint(Stream destination, ulong value)
    {
        while (value >= 0x80)
        {
            destination.WriteByte((byte)((value & 0x7f) | 0x80));
            value >>= 7;
        }
        destination.WriteByte((byte)value);
    }

    private static OwnerPredictionBootstrapPlanDraft ValidBootstrapPlan() => new()
    {
        PlanId = 7,
        Scope = OwnerScope(),
        Kind = OwnerPredictionBootstrapKindDraft.PreMatch,
        Preparation = OwnerPredictionBaselinePreparationDraft.FrozenCommandPredecessor,
        PublishedAuthorityTick = 100,
        BaselineTick = 115,
        LocalInputEnableTick = 110,
        FirstCommandTargetTick = 116,
        TargetLeadFrames = 6,
        LeadPolicyRevision = 7,
        SimulationTicksPerSecond = 60,
        AuthorityFrameBoundaryTimestampMicroseconds = 1_000_000,
        MovementProfileRevision = 10,
        MovementCapabilityRevision = 12,
        CollisionContentHash = ByteString.CopyFrom(
            Enumerable.Range(0, ProtocolConstants.OwnerCollisionContentHashBytes)
                .Select(value => (byte)value)
                .ToArray()),
    };

    private static OwnerPredictionScopeDraft OwnerScope() => new()
    {
        SessionId = SessionId,
        MatchFrameEpoch = 5,
        CombatantId = 7,
        LifeId = 11,
        AuthorityDiscontinuityId = 13,
        OwnerControlEpoch = 17,
    };

    private static OwnerPredictionDraftScopeExpectation OwnerScopeExpectation() =>
        new(
            SessionId,
            MatchFrameEpoch: 5,
            CombatantId: 7,
            LifeId: 11,
            AuthorityDiscontinuityId: 13,
            OwnerControlEpoch: 17);

    private static OwnerCommandDraftValidationContext OwnerCommandContext(
        ulong earliestTargetTick = 90,
        ulong latestTargetTick = 140,
        ulong maximumInputSequence = 80,
        ulong? highestContiguousTransitionId = 4,
        ulong followingKnownTransitionMask = 0,
        ulong? highestContiguousActionId = 4,
        ulong followingKnownActionMask = 0,
        ulong highestAuthorityFrame = 120,
        ulong highestAuthorityStreamSequence = 20) =>
        new(
            OwnerScopeExpectation(),
            EarliestRetainedTargetTick: earliestTargetTick,
            LatestPermittedTargetTick: latestTargetTick,
            HighestAuthorityFramePublished: highestAuthorityFrame,
            HighestAuthorityStreamSequencePublished: highestAuthorityStreamSequence,
            HighestKnownInputSequence: 9,
            HighestOriginatedInputSequence: maximumInputSequence,
            MaximumPermittedInputSequence: maximumInputSequence,
            KnownTransitionIds: new OwnerKnownJournalIdentityWindow(
                highestContiguousTransitionId,
                followingKnownTransitionMask),
            MaximumPermittedTransitionId: 20,
            KnownActionIds: new OwnerKnownJournalIdentityWindow(
                highestContiguousActionId,
                followingKnownActionMask),
            MaximumPermittedActionId: 20,
            MaximumMovementProfileRevision: 10,
            MaximumMovementCapabilityRevision: 12,
            MaximumIssuedTransitionResolutionSequence: 30,
            MaximumIssuedActionResolutionSequence: 31);

    private static OwnerControlDraftValidationContext OwnerControlContext() =>
        new(
            OwnerScopeExpectation(),
            HighestAuthorityFramePublished: 120,
            EarliestLeadEffectiveTick: 130,
            NegotiatedSimulationTicksPerSecond: 60,
            MinimumLeadFrames: 2,
            MaximumLeadFrames: 48,
            MaximumMovementProfileRevision: 10,
            MaximumMovementCapabilityRevision: 12,
            ExpectedCollisionContentHash: CollisionContentHash());

    private static ByteString CollisionContentHash() => ByteString.CopyFrom(
        Enumerable.Range(0, ProtocolConstants.OwnerCollisionContentHashBytes)
            .Select(value => (byte)value)
            .ToArray());

    private static PacketEnvelope CreateEnvelope() => new()
    {
        ProtocolVersion = ProtocolConstants.CurrentVersion,
        SessionId = SessionId,
        Sequence = 1,
        SimulationTick = 1,
    };

    private static ProtocolValidationContext EstablishedClientContext() =>
        new(RemoteEndpointRole.Client, SessionId, true);

    private static ProtocolValidationContext EstablishedAuthorityContext() =>
        new(RemoteEndpointRole.Authority, SessionId, true);
}
