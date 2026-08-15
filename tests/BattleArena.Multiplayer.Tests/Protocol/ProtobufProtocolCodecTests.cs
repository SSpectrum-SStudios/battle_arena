using BattleArena.Multiplayer.Protocol;
using BattleArena.Multiplayer.OwnerPrediction;
using BattleArena.Protocol.V1;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace BattleArena.Multiplayer.Tests.Protocol;

public sealed class ProtobufProtocolCodecTests
{
    private readonly ProtobufProtocolCodec codec = new();

    [Fact]
    public void InputBatchRoundTripsWithoutLosingPredictionFields()
    {
        var envelope = new PacketEnvelope
        {
            ProtocolVersion = ProtocolConstants.CurrentVersion,
            SessionId = 73,
            Sequence = 19,
            SimulationTick = 280,
            ClientInputBatch = new ClientInputBatch
            {
                LatestAuthoritySequence = 17,
            },
        };
        envelope.ClientInputBatch.Frames.Add(new ClientInputFrame
        {
            InputSequence = 41,
            ClientTick = 280,
            MoveX = 0.25f,
            MoveZ = -0.75f,
            ViewYawRadians = 1.2f,
            ViewPitchRadians = -0.3f,
            ButtonBits = 5,
            PressedButtonBits = 4,
            ReleasedButtonBits = 2,
            EstimatedAuthorityTick = 281,
            MovementProfileRevision = 1,
            MovementCapabilityRevision = 1,
        });

        var result = codec.Decode(codec.Encode(envelope));

        Assert.True(result.IsSuccess);
        var decoded = Assert.IsType<PacketEnvelope>(result.Envelope);
        Assert.Equal(PacketEnvelope.PayloadOneofCase.ClientInputBatch, decoded.PayloadCase);
        Assert.Equal(41UL, decoded.ClientInputBatch.Frames[0].InputSequence);
        Assert.Equal(-0.75f, decoded.ClientInputBatch.Frames[0].MoveZ);
        Assert.Equal(5U, decoded.ClientInputBatch.Frames[0].ButtonBits);
        Assert.Equal(4U, decoded.ClientInputBatch.Frames[0].PressedButtonBits);
        Assert.Equal(2U, decoded.ClientInputBatch.Frames[0].ReleasedButtonBits);
        Assert.Equal(281UL, decoded.ClientInputBatch.Frames[0].EstimatedAuthorityTick);
        Assert.Equal(1UL, decoded.ClientInputBatch.Frames[0].MovementProfileRevision);
        Assert.Equal(17UL, decoded.ClientInputBatch.LatestAuthoritySequence);
    }

    [Fact]
    public void CheckpointRoundTripsCompleteReplicatedState()
    {
        var envelope = new PacketEnvelope
        {
            ProtocolVersion = ProtocolConstants.CurrentVersion,
            SessionId = 73,
            Sequence = 20,
            SimulationTick = 300,
            AuthorityCheckpoint = new AuthorityCheckpoint
            {
                Revision = new ReplicatedStateRevision
                {
                    MatchRevision = 2,
                    EntityRevision = 7,
                    EffectRevision = 9,
                    WorldObjectRevision = 4,
                },
            },
        };
        envelope.AuthorityCheckpoint.Combatants.Add(new CombatantSnapshot
        {
            CombatantId = 11,
            LifeId = 12,
            ControllingPlayerId = 10,
            Position = new Vector3Value { X = 1, Y = 2, Z = 3 },
            Velocity = new Vector3Value { X = 4, Y = 5, Z = 6 },
            ViewYawRadians = 1.2f,
            ViewPitchRadians = -0.3f,
            BodyFacingYawRadians = -2.4f,
            CurrentHealth = 80,
            MaximumHealth = 100,
            RemainingLives = 1,
            LifeState = ReplicatedLifeState.Alive,
            LastProcessedInputSequence = 41,
            IsGrounded = true,
            LocomotionMode = ReplicatedLocomotionMode.Grounded,
            PostureMode = ReplicatedPostureMode.Standing,
            MovementActionMode = ReplicatedMovementActionMode.Attacking,
            JumpPhase = ReplicatedJumpPhase.None,
            AttackExecutionId = 91,
            AttackStepIndex = 2,
            DisplayName = "Fighter",
        });
        envelope.AuthorityCheckpoint.ActiveEffects.Add(new ActiveEffectSnapshot
        {
            EffectInstanceId = 90,
            EffectDefinitionId = "base:greater_poison",
            SourceCombatantId = 11,
            SourceLifeId = 12,
            TargetCombatantId = 21,
            StartedTick = 250,
            ExpiresTick = 850,
            StackCount = 1,
        });

        var result = codec.Decode(codec.Encode(envelope));

        Assert.True(result.IsSuccess);
        var decoded = Assert.IsType<PacketEnvelope>(result.Envelope);
        Assert.Equal(7UL, decoded.AuthorityCheckpoint.Revision.EntityRevision);
        Assert.Equal(80L, decoded.AuthorityCheckpoint.Combatants[0].CurrentHealth);
        Assert.True(decoded.AuthorityCheckpoint.Combatants[0].IsGrounded);
        Assert.Equal(1.2f, decoded.AuthorityCheckpoint.Combatants[0].ViewYawRadians);
        Assert.Equal(-0.3f, decoded.AuthorityCheckpoint.Combatants[0].ViewPitchRadians);
        Assert.Equal(-2.4f, decoded.AuthorityCheckpoint.Combatants[0].BodyFacingYawRadians);
        Assert.Equal(
            ReplicatedMovementActionMode.Attacking,
            decoded.AuthorityCheckpoint.Combatants[0].MovementActionMode);
        Assert.Equal(91UL, decoded.AuthorityCheckpoint.Combatants[0].AttackExecutionId);
        Assert.Equal("base:greater_poison", decoded.AuthorityCheckpoint.ActiveEffects[0].EffectDefinitionId);
        Assert.Equal(850UL, decoded.AuthorityCheckpoint.ActiveEffects[0].ExpiresTick);
    }

    [Fact]
    public void CompactMovementFrameRoundTripsReplayState()
    {
        var envelope = new PacketEnvelope
        {
            ProtocolVersion = ProtocolConstants.CurrentVersion,
            SessionId = 73,
            Sequence = 21,
            SimulationTick = 301,
            AuthorityMovementFrameBatch = new AuthorityMovementFrameBatch
            {
                StreamSequence = 44,
            },
        };
        envelope.AuthorityMovementFrameBatch.Combatants.Add(
            new AuthoritativeMovementState
            {
                CombatantId = 11,
                LifeId = 12,
                Position = new Vector3Value { X = 1, Y = 2, Z = 3 },
                Velocity = new Vector3Value { X = 4, Y = 5, Z = 6 },
                ViewYawRadians = 1.2f,
                ViewPitchRadians = -0.3f,
                BodyFacingYawRadians = -2.4f,
                LastProcessedInputSequence = 41,
                IsGrounded = true,
                LocomotionMode = ReplicatedLocomotionMode.Rolling,
                PostureMode = ReplicatedPostureMode.Standing,
                MovementActionMode = ReplicatedMovementActionMode.Ready,
                JumpPhase = ReplicatedJumpPhase.None,
                RollDirection = new Vector3Value { X = 0, Z = -1 },
                RollEntrySpeed = 12,
                RollDurationTicks = 30,
                MovementProfileRevision = 1,
                MovementCapabilityRevision = 1,
            });

        var result = codec.Decode(codec.Encode(envelope));

        Assert.True(result.IsSuccess);
        var decoded = Assert.IsType<PacketEnvelope>(result.Envelope);
        Assert.Equal(44UL, decoded.AuthorityMovementFrameBatch.StreamSequence);
        Assert.Equal(41UL, decoded.AuthorityMovementFrameBatch.Combatants[0]
            .LastProcessedInputSequence);
        Assert.Equal(
            ReplicatedLocomotionMode.Rolling,
            decoded.AuthorityMovementFrameBatch.Combatants[0].LocomotionMode);
    }

    [Fact]
    public void ClockReplyRoundTripsAllFourTimestampFields()
    {
        var envelope = new PacketEnvelope
        {
            ProtocolVersion = ProtocolConstants.CurrentVersion,
            SessionId = 73,
            Sequence = 22,
            SimulationTick = 302,
            ClockSyncReply = new ClockSyncReply
            {
                ProbeSequence = 5,
                ClientSendTimestampMicroseconds = 1_000,
                AuthorityReceiveTimestampMicroseconds = 1_040,
                AuthoritySendTimestampMicroseconds = 1_041,
                AuthorityTick = 302,
            },
        };

        var result = codec.Decode(codec.Encode(envelope));

        Assert.True(result.IsSuccess);
        Assert.Equal(
            1_041UL,
            result.Envelope!.ClockSyncReply.AuthoritySendTimestampMicroseconds);
        Assert.Equal(302UL, result.Envelope.ClockSyncReply.AuthorityTick);
    }

    [Fact]
    public void AcceptedMovementRelayRoundTripsAuthorityIdentityAndInput()
    {
        var envelope = new PacketEnvelope
        {
            ProtocolVersion = ProtocolConstants.CurrentVersion,
            SessionId = 73,
            Sequence = 23,
            SimulationTick = 303,
            AuthorityAcceptedMovementBatch = new AuthorityAcceptedMovementBatch
            {
                StreamSequence = 9,
            },
        };
        envelope.AuthorityAcceptedMovementBatch.Commands.Add(
            new AuthorityAcceptedMovementCommand
            {
                SourceSessionPeerId = 2,
                PeerSessionGeneration = 1,
                CombatantId = 2,
                LifeId = 4,
                AppliedAuthorityTick = 303,
                AppliedMovementProfileRevision = 1,
                AppliedMovementCapabilityRevision = 1,
                Input = new ClientInputFrame
                {
                    InputSequence = 88,
                    ClientTick = 300,
                    EstimatedAuthorityTick = 303,
                    MovementProfileRevision = 1,
                    MovementCapabilityRevision = 1,
                    MoveZ = -1,
                },
            });

        var result = codec.Decode(codec.Encode(envelope));

        Assert.True(result.IsSuccess);
        var command = Assert.Single(
            result.Envelope!.AuthorityAcceptedMovementBatch.Commands);
        Assert.Equal(2UL, command.SourceSessionPeerId);
        Assert.Equal(1U, command.PeerSessionGeneration);
        Assert.Equal(303UL, command.AppliedAuthorityTick);
        Assert.Equal(88UL, command.Input.InputSequence);
    }

    [Fact]
    public void RouteAuthorizationRoundTripsPeerGenerationsAndOpaqueDescriptor()
    {
        var envelope = new PacketEnvelope
        {
            ProtocolVersion = ProtocolConstants.CurrentVersion,
            SessionId = 73,
            Sequence = 24,
            SimulationTick = 304,
            PredictionRouteAuthorization = new PredictionRouteAuthorization
            {
                LocalSessionPeerId = 2,
                LocalPeerSessionGeneration = 4,
                RemoteSessionPeerId = 3,
                RemotePeerSessionGeneration = 5,
                PredictionRouteGeneration = 6,
                RemoteDescriptor = PredictionProtocolTestData.RouteDescriptor(),
                RouteCredential = Google.Protobuf.ByteString.CopyFrom(
                    new byte[ProtocolConstants.PredictionRouteCredentialBytes]),
                ExpiresAuthorityTick = 10_000,
            },
        };

        var result = codec.Decode(codec.Encode(envelope));

        Assert.True(result.IsSuccess);
        var authorization = result.Envelope!.PredictionRouteAuthorization;
        Assert.Equal(4U, authorization.LocalPeerSessionGeneration);
        Assert.Equal(5U, authorization.RemotePeerSessionGeneration);
        Assert.Equal(6U, authorization.PredictionRouteGeneration);
        Assert.Equal(PredictionTransportKind.Enet, authorization.RemoteDescriptor.TransportKind);
        Assert.Equal(
            ProtocolConstants.PredictionRouteCredentialBytes,
            authorization.RouteCredential.Length);
    }

    [Fact]
    public void OwnerCommandDraftRoundTripsQuantizedCommandsAndUniqueJournals()
    {
        var batch = BuildOwnerCommandDraft(
            commandCount: 6,
            transitionJournalCount: 8,
            actionJournalCount: 8);

        var decoded = OwnerCommandBatchDraft.Parser.ParseFrom(
            batch.ToByteArray());

        Assert.Equal(batch, decoded);
        Assert.Equal(6, decoded.Commands.Count);
        Assert.Equal(8, decoded.OutstandingTransitions.Count);
        Assert.Equal(8, decoded.OutstandingActions.Count);
        Assert.Equal(-32_767, decoded.Commands[0].MoveXQ15);
        Assert.Equal(65_535U, decoded.Commands[0].ViewYawU16);
        Assert.Equal(
            Enumerable.Range(1, 8).Select(value => (ulong)value),
            decoded.Commands
                .SelectMany(command => command.TransitionReferences)
                .OrderBy(reference => reference));
        Assert.Equal(
            Enumerable.Range(1, 8).Select(value => (ulong)value),
            decoded.Commands
                .SelectMany(command => command.ActionReferences)
                .OrderBy(reference => reference));
        Assert.True(decoded.HasTransitionResolutionCursorApplied);
        Assert.True(decoded.HasActionResolutionCursorApplied);
        Assert.Equal(17UL, decoded.TransitionResolutionCursorApplied);
        Assert.Equal(19UL, decoded.ActionResolutionCursorApplied);

        var noResolutionCursors = OwnerCommandBatchDraft.Parser.ParseFrom(
            new OwnerCommandBatchDraft
            {
                Scope = OwnerScopeDraft(),
            }.ToByteArray());
        Assert.False(noResolutionCursors.HasTransitionResolutionCursorApplied);
        Assert.False(noResolutionCursors.HasActionResolutionCursorApplied);

        var frameZeroCursors = OwnerCommandBatchDraft.Parser.ParseFrom(
            new OwnerCommandBatchDraft
            {
                Scope = OwnerScopeDraft(),
                TransitionResolutionCursorApplied = 0,
                ActionResolutionCursorApplied = 0,
            }.ToByteArray());
        Assert.True(frameZeroCursors.HasTransitionResolutionCursorApplied);
        Assert.True(frameZeroCursors.HasActionResolutionCursorApplied);
    }

    [Fact]
    public void OwnerCommandDraftFitsCompleteConservativeTransportDatagramBudgets()
    {
        // Six commands are the required 100 ms recent-input floor. The 12-command
        // shape is the default selection cap, not a promise that worst-case
        // varints plus both journal reservations fit; the exact-size window may
        // select fewer. A fully saturated
        // 64-command/eight-transition/eight-action candidate deliberately does
        // not fit: the exact-size send window must split it instead of fragmenting
        // it. Every supported count combination is enumerated below so the
        // transport-specific body allowance, not a count guess, defines a legal
        // emitted batch.
        var minimumRecentBatch = BuildOwnerCommandDraft(
            OwnerInputSendWindowLimits.DefaultMinimumRecentCommands,
            OwnerInputSendWindowLimits.DefaultMaximumTransitionsPerBatch,
            OwnerInputSendWindowLimits.DefaultMaximumActionsPerBatch,
            worstCaseVarints: true);
        var defaultCommandBurst = BuildOwnerCommandDraft(
            OwnerInputSendWindowLimits.DefaultMaximumCommandsPerBatch,
            OwnerInputSendWindowLimits.DefaultMaximumTransitionsPerBatch,
            OwnerInputSendWindowLimits.DefaultMaximumActionsPerBatch,
            worstCaseVarints: true);
        var saturatedCandidate = BuildOwnerCommandDraft(
            OwnerInputSendWindowLimits.MaximumCommandsPerBatch,
            OwnerInputSendWindowLimits.MaximumJournalEntriesPerBatch,
            OwnerInputSendWindowLimits.MaximumJournalEntriesPerBatch,
            worstCaseVarints: true);
        var maximumReferenceCommand = BuildOwnerCommandDraft(
            commandCount: 1,
            transitionJournalCount: 0,
            actionJournalCount: 0,
            worstCaseVarints: true,
            transitionReferenceCount:
                BattleArena.Core.Movement.Simulation.OwnerSimulationLimits
                    .MaximumTransitionReferences,
            actionReferenceCount:
                BattleArena.Core.Movement.Simulation.OwnerSimulationLimits
                    .MaximumActionReferences);
        Assert.Equal(
            BattleArena.Core.Movement.Simulation.OwnerSimulationLimits
                .MaximumTransitionReferences,
            maximumReferenceCommand.Commands[0].TransitionReferences.Count);
        Assert.Equal(
            BattleArena.Core.Movement.Simulation.OwnerSimulationLimits
                .MaximumActionReferences,
            maximumReferenceCommand.Commands[0].ActionReferences.Count);
        Assert.Empty(maximumReferenceCommand.OutstandingTransitions);
        Assert.Empty(maximumReferenceCommand.OutstandingActions);

        foreach (var transport in OwnerCommandTransportProfiles())
        {
            AssertOwnerCommandDatagramFits(minimumRecentBatch, transport);
            AssertOwnerCommandDatagramIsClassified(defaultCommandBurst, transport);
            Assert.True(
                MeasureAccountedOwnerCommandDatagramBytes(
                    saturatedCandidate.CalculateSize(),
                    transport) > ProtocolConstants.MaxPredictionUnreliablePacketBytes);
            AssertOwnerCommandDatagramFits(
                maximumReferenceCommand,
                transport);

            var bodyAllowance = MeasureOwnerCommandBodyAllowance(transport);
            Assert.InRange(bodyAllowance, 1,
                ProtocolConstants.MaxPredictionUnreliablePacketBytes - 1);

            for (var commands = 0;
                 commands <= OwnerInputSendWindowLimits.MaximumCommandsPerBatch;
                 commands++)
            {
                for (var transitions = 0;
                     transitions <= OwnerInputSendWindowLimits.MaximumJournalEntriesPerBatch;
                     transitions++)
                {
                    for (var actions = 0;
                         actions <= OwnerInputSendWindowLimits.MaximumJournalEntriesPerBatch;
                         actions++)
                    {
                        var body = BuildOwnerCommandDraft(
                            commands,
                            transitions,
                            actions,
                            worstCaseVarints: true).CalculateSize();
                        Assert.Equal(
                            body <= bodyAllowance,
                            MeasureAccountedOwnerCommandDatagramBytes(body, transport) <=
                            ProtocolConstants.MaxPredictionUnreliablePacketBytes);
                    }
                }
            }
        }
    }

    [Fact]
    public void OwnerReceiveAndConsumptionAcknowledgementsPreserveOptionalEvidence()
    {
        var receive = new OwnerInputReceiveAcknowledgementDraft
        {
            Scope = OwnerScopeDraft(),
            ReceivedInputs = new SelectiveSequenceAcknowledgementDraft
            {
                HighestContiguousSequence = 41,
                Following64ReceivedMask = 0x8000_0000_0000_0005UL,
            },
        };
        var decodedReceive = OwnerInputReceiveAcknowledgementDraft.Parser.ParseFrom(
            receive.ToByteArray());
        Assert.Equal(receive, decodedReceive);
        Assert.True(decodedReceive.ReceivedInputs.HasHighestContiguousSequence);

        var noReceiveCursor = OwnerInputReceiveAcknowledgementDraft.Parser.ParseFrom(
            new OwnerInputReceiveAcknowledgementDraft
            {
                Scope = OwnerScopeDraft(),
                ReceivedInputs = new SelectiveSequenceAcknowledgementDraft(),
            }.ToByteArray());
        Assert.False(noReceiveCursor.ReceivedInputs.HasHighestContiguousSequence);

        var consumption = new OwnerInputConsumptionAcknowledgementDraft
        {
            Scope = OwnerScopeDraft(),
            ConsumedThroughSimulationTick = 512,
        };
        consumption.RecentDispositions.Add(new OwnerInputFrameDispositionDraft
        {
            TargetSimulationTick = 510,
            InputSequence = 40,
            Kind = OwnerInputFrameDispositionKindDraft.ReceivedCommand,
        });
        consumption.RecentDispositions.Add(new OwnerInputFrameDispositionDraft
        {
            TargetSimulationTick = 511,
            Kind = OwnerInputFrameDispositionKindDraft.NeutralFallback,
        });
        consumption.RecentDispositions.Add(new OwnerInputFrameDispositionDraft
        {
            TargetSimulationTick = 509,
            InputSequence = 39,
            Kind = OwnerInputFrameDispositionKindDraft.LateCommand,
        });
        consumption.RecentDispositions.Add(new OwnerInputFrameDispositionDraft
        {
            TargetSimulationTick = 512,
            InputSequence = 41,
            Kind = OwnerInputFrameDispositionKindDraft.AuthorityOverride,
        });

        var decodedConsumption =
            OwnerInputConsumptionAcknowledgementDraft.Parser.ParseFrom(
                consumption.ToByteArray());
        Assert.Equal(consumption, decodedConsumption);
        Assert.True(decodedConsumption.HasConsumedThroughSimulationTick);
        Assert.True(decodedConsumption.RecentDispositions[0].HasInputSequence);
        Assert.False(decodedConsumption.RecentDispositions[1].HasInputSequence);
        Assert.Equal(
            OwnerInputFrameDispositionKindDraft.AuthorityOverride,
            decodedConsumption.RecentDispositions[3].Kind);

        var noEvidence = OwnerInputConsumptionAcknowledgementDraft.Parser.ParseFrom(
            new OwnerInputConsumptionAcknowledgementDraft
            {
                Scope = OwnerScopeDraft(),
            }.ToByteArray());
        Assert.False(noEvidence.HasConsumedThroughSimulationTick);

        var consumedFrameZero = OwnerInputConsumptionAcknowledgementDraft.Parser.ParseFrom(
            new OwnerInputConsumptionAcknowledgementDraft
            {
                Scope = OwnerScopeDraft(),
                ConsumedThroughSimulationTick = 0,
            }.ToByteArray());
        Assert.True(consumedFrameZero.HasConsumedThroughSimulationTick);
        Assert.Equal(0UL, consumedFrameZero.ConsumedThroughSimulationTick);
    }

    [Fact]
    public void BootstrapReceiptAndAbsoluteLeadDraftsRoundTripFullControlEpoch()
    {
        var plan = new OwnerPredictionBootstrapPlanDraft
        {
            PlanId = 7,
            Scope = OwnerScopeDraft(),
            Kind = OwnerPredictionBootstrapKindDraft.TimelineRebase,
            Preparation = OwnerPredictionBaselinePreparationDraft.NeutralPreroll,
            PublishedAuthorityTick = 400,
            BaselineTick = 400,
            LocalInputEnableTick = 410,
            FirstCommandTargetTick = 416,
            TargetLeadFrames = 6,
            LeadPolicyRevision = 12,
            SimulationTicksPerSecond = 60,
            AuthorityFrameBoundaryTimestampMicroseconds = 7_000_123,
            MovementProfileRevision = 23,
            MovementCapabilityRevision = 29,
            CollisionContentHash = ByteString.CopyFrom(
                Enumerable.Range(0, 32).Select(value => (byte)value).ToArray()),
        };
        var receipt = new OwnerPredictionBaselineReceiptDraft
        {
            PlanId = 7,
            Scope = OwnerScopeDraft(),
            BaselineTick = 400,
            Source = OwnerPredictionBaselineSourceDraft.AuthorityMovement,
        };
        var lead = new OwnerPredictionLeadUpdateDraft
        {
            Scope = OwnerScopeDraft(),
            TargetLeadFrames = 8,
            LeadPolicyRevision = 13,
            EffectiveTick = 450,
        };

        Assert.Equal(plan,
            OwnerPredictionBootstrapPlanDraft.Parser.ParseFrom(plan.ToByteArray()));
        Assert.Equal(receipt,
            OwnerPredictionBaselineReceiptDraft.Parser.ParseFrom(
                receipt.ToByteArray()));
        Assert.Equal(lead,
            OwnerPredictionLeadUpdateDraft.Parser.ParseFrom(lead.ToByteArray()));
        Assert.Equal(5UL, plan.Scope.MatchFrameEpoch);
        Assert.Equal(73UL, plan.Scope.SessionId);
        Assert.Equal(60U, plan.SimulationTicksPerSecond);
        Assert.Equal(7_000_123UL,
            plan.AuthorityFrameBoundaryTimestampMicroseconds);
        Assert.Equal(23UL, plan.MovementProfileRevision);
        Assert.Equal(29UL, plan.MovementCapabilityRevision);
        Assert.Equal(32, plan.CollisionContentHash.Length);
        Assert.Equal(13UL, lead.LeadPolicyRevision);
    }

    [Fact]
    public void ProtocolNextDraftDoesNotRenumberOrEnterVersionOneEnvelope()
    {
        AssertFieldNumbers(
            ClientInputFrame.Descriptor,
            "input_sequence",
            "client_tick",
            "move_x",
            "move_z",
            "view_yaw_radians",
            "view_pitch_radians",
            "button_bits",
            "pressed_button_bits",
            "released_button_bits",
            "estimated_authority_tick",
            "movement_profile_revision",
            "movement_capability_revision");
        AssertFieldNumbers(
            ClientInputBatch.Descriptor,
            "frames",
            "latest_authority_sequence");
        AssertFieldNumbers(
            ClientActionRequest.Descriptor,
            "action_sequence",
            "client_tick",
            "kind",
            "equipment_slot",
            "source_life_id");
        Assert.DoesNotContain(
            PacketEnvelope.Descriptor.Oneofs[0].Fields,
            field => field.MessageType?.Name.EndsWith(
                "Draft",
                StringComparison.Ordinal) == true);
    }

    [Fact]
    public void EmptyPacketIsRejectedBeforeParsing()
    {
        var result = codec.Decode(ReadOnlySpan<byte>.Empty);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProtocolViolationCode.EmptyPacket, result.Violation?.Code);
    }

    [Fact]
    public void OversizedPacketIsRejectedBeforeParsing()
    {
        var packet = new byte[ProtocolConstants.MaxPacketBytes + 1];

        var result = codec.Decode(packet);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProtocolViolationCode.PacketTooLarge, result.Violation?.Code);
    }

    [Fact]
    public void MalformedPacketReturnsViolationInsteadOfThrowing()
    {
        var result = codec.Decode(new byte[] { 0xff, 0xff, 0xff });

        Assert.False(result.IsSuccess);
        Assert.Equal(ProtocolViolationCode.MalformedPayload, result.Violation?.Code);
    }

    private static OwnerCommandBatchDraft BuildOwnerCommandDraft(
        int commandCount,
        int transitionJournalCount,
        int actionJournalCount,
        bool worstCaseVarints = false,
        int? transitionReferenceCount = null,
        int? actionReferenceCount = null)
    {
        var includedTransitionReferences = commandCount == 0
            ? 0
            : transitionReferenceCount ?? transitionJournalCount;
        var includedActionReferences = commandCount == 0
            ? 0
            : actionReferenceCount ?? actionJournalCount;
        var batch = new OwnerCommandBatchDraft
        {
            Scope = worstCaseVarints
                ? WorstOwnerScopeDraft()
                : OwnerScopeDraft(),
            PacketSequence = ulong.MaxValue,
            TransitionResolutionCursorApplied = worstCaseVarints
                ? ulong.MaxValue
                : 17,
            ActionResolutionCursorApplied = worstCaseVarints
                ? ulong.MaxValue
                : 19,
            LatestAuthorityFrameObserved = ulong.MaxValue - 100,
            LatestAuthorityStreamSequenceObserved = ulong.MaxValue - 200,
        };
        for (var index = 0; index < commandCount; index++)
        {
            var command = new OwnerSimulationCommandDraft
            {
                TargetSimulationTick = ulong.MaxValue - (ulong)(commandCount - index),
                InputSequence = worstCaseVarints
                    ? ulong.MaxValue - (ulong)(commandCount - 1 - index)
                    : (ulong)index + 1,
                MoveXQ15 = -32_767,
                MoveZQ15 = 32_767,
                ViewYawU16 = 65_535,
                ViewPitchI16 = -32_767,
                HeldMovementBits = 0x7,
                HeldCombatBits = 0x1ff,
                MovementProfileRevision = ulong.MaxValue - 2,
                MovementCapabilityRevision = ulong.MaxValue - 1,
            };
            batch.Commands.Add(command);
        }
        for (var index = 1; index <= includedTransitionReferences; index++)
        {
            var reference = worstCaseVarints
                ? ulong.MaxValue - (ulong)(includedTransitionReferences - index)
                : (ulong)index;
            batch.Commands[(index - 1) % commandCount]
                .TransitionReferences.Add(reference);
        }
        for (var index = 1; index <= transitionJournalCount; index++)
        {
            var reference = worstCaseVarints
                ? ulong.MaxValue - (ulong)(transitionJournalCount - index)
                : (ulong)index;
            batch.OutstandingTransitions.Add(new MovementTransitionIntentDraft
            {
                TransitionId = reference,
                OriginatingInputSequence = commandCount == 0
                    ? ulong.MaxValue
                    : batch.Commands[0].InputSequence,
                Kind = index % 2 == 0
                    ? OwnerMovementTransitionKindDraft.JumpReleased
                    : OwnerMovementTransitionKindDraft.JumpPressed,
                FirstPredictedTick = ulong.MaxValue - 20,
                LastValidTick = ulong.MaxValue - 10,
            });
        }
        for (var index = 1; index <= includedActionReferences; index++)
        {
            var reference = worstCaseVarints
                ? ulong.MaxValue - (ulong)(includedActionReferences - index)
                : (ulong)index;
            batch.Commands[(index - 1) % commandCount]
                .ActionReferences.Add(reference);
        }
        for (var index = 1; index <= actionJournalCount; index++)
        {
            var reference = worstCaseVarints
                ? ulong.MaxValue - (ulong)(actionJournalCount - index)
                : (ulong)index;
            batch.OutstandingActions.Add(new PredictedActionIntentDraft
            {
                ActionId = reference,
                OriginatingInputSequence = commandCount == 0
                    ? ulong.MaxValue
                    : batch.Commands[0].InputSequence,
                Trigger = OwnerActionTriggerDraft.ActivateSelectedFlexibleItem,
                PredictedStartTick = ulong.MaxValue - 20,
                LastValidStartTick = ulong.MaxValue - 10,
                RenderedAuthorityTick = ulong.MaxValue - 30,
            });
        }
        return batch;
    }

    private static IEnumerable<PredictionTransportOverhead>
        OwnerCommandTransportProfiles()
    {
        yield return PredictionTransportOverhead.EnetIpv6Unreliable;
        yield return PredictionTransportOverhead.SteamIpv6Unreliable;
    }

    private static int MeasureOwnerCommandBodyAllowance(
        PredictionTransportOverhead transport)
    {
        var allowance = 0;
        for (var bodyBytes = 0;
             bodyBytes <= ProtocolConstants.MaxPredictionUnreliablePacketBytes;
             bodyBytes++)
        {
            if (MeasureAccountedOwnerCommandDatagramBytes(bodyBytes, transport) <=
                ProtocolConstants.MaxPredictionUnreliablePacketBytes)
            {
                allowance = bodyBytes;
            }
        }
        return allowance;
    }

    private static void AssertOwnerCommandDatagramFits(
        OwnerCommandBatchDraft batch,
        PredictionTransportOverhead transport)
    {
        var bodyBytes = batch.CalculateSize();
        var bodyAllowance = MeasureOwnerCommandBodyAllowance(transport);
        var accountedBytes = MeasureAccountedOwnerCommandDatagramBytes(
            bodyBytes,
            transport);
        Assert.True(
            bodyBytes <= bodyAllowance &&
            accountedBytes <= ProtocolConstants.MaxPredictionUnreliablePacketBytes,
            $"{transport.Name}: body {bodyBytes}/{bodyAllowance}, " +
            $"accounted datagram {accountedBytes}/" +
            $"{ProtocolConstants.MaxPredictionUnreliablePacketBytes}; " +
            $"basis={transport.Basis}; {transport.Provenance}");
    }

    private static void AssertOwnerCommandDatagramIsClassified(
        OwnerCommandBatchDraft batch,
        PredictionTransportOverhead transport)
    {
        var bodyBytes = batch.CalculateSize();
        Assert.Equal(
            bodyBytes <= MeasureOwnerCommandBodyAllowance(transport),
            MeasureAccountedOwnerCommandDatagramBytes(bodyBytes, transport) <=
            ProtocolConstants.MaxPredictionUnreliablePacketBytes);
    }

    private static int MeasureAccountedOwnerCommandDatagramBytes(
        int bodyBytes,
        PredictionTransportOverhead transport) =>
        checked(
            bodyBytes +
            MeasureDraftEnvelopeOverheadBytes(bodyBytes) +
            transport.AuthenticationBytes +
            transport.TransportFramingBytes +
            transport.IpUdpBytes);

    private static int MeasureDraftEnvelopeOverheadBytes(int bodyBytes) =>
        CodedOutputStream.ComputeTagSize(1) +
        CodedOutputStream.ComputeUInt32Size(uint.MaxValue) +
        CodedOutputStream.ComputeTagSize(2) + sizeof(ulong) +
        CodedOutputStream.ComputeTagSize(3) +
        CodedOutputStream.ComputeUInt64Size(ulong.MaxValue) +
        CodedOutputStream.ComputeTagSize(4) +
        CodedOutputStream.ComputeUInt64Size(ulong.MaxValue) +
        CodedOutputStream.ComputeTagSize(48) +
        CodedOutputStream.ComputeLengthSize(bodyBytes);

    private static void AssertFieldNumbers(
        MessageDescriptor descriptor,
        params string[] namesInFieldNumberOrder)
    {
        var fields = descriptor.Fields.InFieldNumberOrder();
        Assert.Equal(namesInFieldNumberOrder.Length, fields.Count);
        for (var index = 0; index < namesInFieldNumberOrder.Length; index++)
        {
            Assert.Equal(index + 1, fields[index].FieldNumber);
            Assert.Equal(namesInFieldNumberOrder[index], fields[index].Name);
        }
    }

    private static OwnerPredictionScopeDraft OwnerScopeDraft() => new()
    {
        MatchFrameEpoch = 5,
        CombatantId = 7,
        LifeId = 11,
        AuthorityDiscontinuityId = 13,
        OwnerControlEpoch = 17,
        SessionId = 73,
    };

    private static OwnerPredictionScopeDraft WorstOwnerScopeDraft() => new()
    {
        MatchFrameEpoch = ulong.MaxValue,
        CombatantId = ulong.MaxValue,
        LifeId = ulong.MaxValue,
        AuthorityDiscontinuityId = ulong.MaxValue,
        OwnerControlEpoch = ulong.MaxValue,
        SessionId = ulong.MaxValue,
    };
}
