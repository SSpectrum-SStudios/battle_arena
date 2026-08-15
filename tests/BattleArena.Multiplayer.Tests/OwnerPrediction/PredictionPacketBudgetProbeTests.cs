using BattleArena.Multiplayer.OwnerPrediction;
using Google.Protobuf;

namespace BattleArena.Multiplayer.Tests.OwnerPrediction;

public sealed class PredictionPacketBudgetProbeTests
{
    [Theory]
    [MemberData(nameof(StandardTransports))]
    public void StandardMatrixIndependentlyDecodesAndAccountsForEveryLayer(
        PredictionTransportOverhead transport)
    {
        var reports = new PredictionPacketBudgetProbe().MeasureStandardMatrix(transport);

        Assert.Equal(6, reports.Count);
        Assert.Equal(new[] { 1, 1, 3, 3, 8, 8 }, reports.Select(report => report.CombatantCount));
        Assert.Equal(
            new[] { "common", "worst-bounded", "common", "worst-bounded", "common", "worst-bounded" },
            reports.Select(report => report.LoadName));

        foreach (var report in reports)
        {
            Assert.Equal(transport.Basis, report.TransportOverheadBasis);
            Assert.Equal(transport.Provenance, report.TransportOverheadProvenance);
            foreach (var product in Products(report))
            {
                Assert.Equal(
                    product.Product == PredictionPacketProduct.OwnerReconciliation
                        ? 1
                        : report.CombatantCount,
                    product.ExpectedCombatantCount);
                foreach (var packet in product.Packets)
                {
                    var decoded = DecodeEnvelope(packet.EncodedEnvelope);
                    Assert.Equal(8u, decoded.ProtocolVersion);
                    Assert.Equal(ulong.MaxValue, decoded.SessionId);
                    Assert.Equal(ulong.MaxValue, decoded.Sequence);
                    Assert.Equal(ulong.MaxValue, decoded.MatchFrame);
                    Assert.Equal(packet.EnvelopePayloadFieldNumber, decoded.PayloadField);
                    Assert.Equal(packet.ProtobufPayloadBytes, decoded.Payload.Length);
                    Assert.Equal(ExpectedEnvelopeField(product.Product), decoded.PayloadField);
                    Assert.Equal(
                        packet.ProtobufPayloadBytes + packet.EnvelopeBytes,
                        packet.EncodedEnvelope.Length);
                    Assert.Equal(
                        packet.EncodedEnvelope.Length +
                        transport.AuthenticationBytes +
                        transport.TransportFramingBytes +
                        transport.IpUdpBytes,
                        packet.AccountedDatagramBytes);
                    Assert.True(packet.Fits);
                    Assert.InRange(packet.AccountedDatagramBytes, 1, 1_200);

                    if (product.Product != PredictionPacketProduct.OwnerReconciliation)
                    {
                        var part = DecodePartition(decoded.Payload, product.Product);
                        Assert.Equal(packet.PartIndex, part.PartIndex);
                        Assert.Equal(packet.PartCount, part.PartCount);
                        Assert.Equal(
                            Enumerable.Range(packet.FirstCombatantIndex, packet.CombatantCount)
                                .Select(index => ExpectedCombatantId(report, index)),
                            part.CombatantIds);
                        if (product.Product == PredictionPacketProduct.Checkpoint)
                        {
                            Assert.All(part.CheckpointRecords, record =>
                            {
                                Assert.True(record.HasState);
                                Assert.True(record.HasConfiguration);
                                Assert.True(record.HasRepair);
                                Assert.Equal(record.CombatantId, record.StateCombatantId);
                                Assert.Equal(record.LifeId, record.StateLifeId);
                                Assert.NotEqual(0UL, record.LifeId);
                            });
                        }
                    }
                    else
                    {
                        Assert.Equal(
                            ExpectedCombatantId(report, 0),
                            DecodeOwnerCombatantId(decoded.Payload));
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(StandardTransports))]
    public void EightPlayerCommonWorldIsOneSelfContainedPacket(
        PredictionTransportOverhead transport)
    {
        var report = new PredictionPacketBudgetProbe().Measure(
            8,
            PredictionPacketBudgetLoad.Common,
            transport);

        Assert.Equal(1, report.CollisionWorld.PacketCount);
        Assert.Equal(8, report.CollisionWorld.Packets[0].CombatantCount);
    }

    [Fact]
    public void WorstWorldCarriesEverySixteenSourceOverflowChunk()
    {
        var report = new PredictionPacketBudgetProbe().Measure(
            8,
            PredictionPacketBudgetLoad.WorstBounded,
            PredictionTransportOverhead.SteamIpv6Unreliable);

        Assert.True(report.CollisionWorld.PacketCount > 1);
        var decodedParts = report.CollisionWorld.Packets
            .Select(packet => DecodePartition(
                DecodeEnvelope(packet.EncodedEnvelope).Payload,
                PredictionPacketProduct.CollisionWorld))
            .ToArray();
        var overflowChunks = decodedParts.SelectMany(part => part.Overflows).ToArray();
        Assert.Equal(8 * 3, overflowChunks.Length);
        for (var combatant = 0; combatant < 8; combatant++)
        {
            var combatantId = ulong.MaxValue - checked((ulong)combatant);
            var lifeId = ulong.MaxValue - 100UL - checked((ulong)combatant);
            var inline = Assert.Single(
                decodedParts.SelectMany(part => part.WorldStates),
                state => state.CombatantId == combatantId);
            Assert.Equal(lifeId, inline.LifeId);
            Assert.Equal(ulong.MaxValue, inline.AuthorityDiscontinuityId);
            Assert.Equal(ulong.MaxValue, inline.OwnerControlEpoch);
            Assert.Equal(4, inline.Sources.Count);
            for (var source = 0; source < inline.Sources.Count; source++)
            {
                AssertWorstSource(inline.Sources[source], combatant, source);
            }

            var chunks = overflowChunks.Where(chunk => chunk.CombatantId == combatantId).ToArray();
            Assert.Equal(new[] { 4, 8, 12 }, chunks.Select(chunk => chunk.FirstSource));
            Assert.All(chunks, chunk =>
            {
                Assert.Equal(lifeId, chunk.LifeId);
                Assert.Equal(ulong.MaxValue, chunk.AuthorityDiscontinuityId);
                Assert.Equal(ulong.MaxValue, chunk.OwnerControlEpoch);
                Assert.Equal(4, chunk.SourceCount);
                Assert.Equal(4, chunk.Sources.Count);
                for (var source = 0; source < chunk.SourceCount; source++)
                {
                    AssertWorstSource(
                        chunk.Sources[source],
                        combatant,
                        chunk.FirstSource + source);
                }
            });

            Assert.Equal(
                Enumerable.Range(0, 16)
                    .Select(source => 0x00ff_ffffU - checked((uint)(combatant * 16 + source)))
                    .Order(),
                inline.Sources.Select(source => source.InstanceId)
                    .Concat(chunks.SelectMany(chunk => chunk.Sources).Select(source => source.InstanceId))
                    .Order());
        }
        Assert.Equal(8, report.Checkpoint.PacketCount);
        Assert.All(report.Checkpoint.Packets, packet => Assert.Equal(1, packet.CombatantCount));
    }

    [Fact]
    public void ListenAndDedicatedTopologiesReportExplicitFanoutAndPacketRates()
    {
        var probe = new PredictionPacketBudgetProbe();
        var soloListen = probe.Measure(
            1,
            PredictionPacketBudgetLoad.Common,
            PredictionTransportOverhead.EnetIpv6Unreliable,
            PredictionAuthorityTopology.ListenServer);
        var listen = probe.Measure(
            8,
            PredictionPacketBudgetLoad.Common,
            PredictionTransportOverhead.EnetIpv6Unreliable,
            PredictionAuthorityTopology.ListenServer);
        var dedicated = probe.Measure(
            8,
            PredictionPacketBudgetLoad.Common,
            PredictionTransportOverhead.EnetIpv6Unreliable,
            PredictionAuthorityTopology.DedicatedServer);

        Assert.Equal(0, soloListen.NetworkRecipientCount);
        Assert.Equal(0, soloListen.AuthorityFanoutBytesPerSecond);
        Assert.Equal(0, soloListen.AuthorityFanoutPacketsPerSecond);
        Assert.Equal(7, listen.NetworkRecipientCount);
        Assert.Equal(8, dedicated.NetworkRecipientCount);
        Assert.Equal(listen.PerRecipientBytesPerSecond * 7, listen.AuthorityFanoutBytesPerSecond);
        Assert.Equal(dedicated.PerRecipientBytesPerSecond * 8, dedicated.AuthorityFanoutBytesPerSecond);
        Assert.Equal(60, listen.OwnerTraffic.PacketsPerSecondPerRecipient);
        Assert.Equal(
            listen.CollisionWorld.PacketCount * 60,
            listen.CollisionWorldTraffic.PacketsPerSecondPerRecipient);
        Assert.Equal(
            listen.Checkpoint.PacketCount * 2,
            listen.CheckpointTraffic.PacketsPerSecondPerRecipient);
    }

    [Fact]
    public void ExactCeilingPassesAndOneByteLowerFailsIndivisibleOwner()
    {
        var probe = new PredictionPacketBudgetProbe();
        var baseline = probe.Measure(
            1,
            PredictionPacketBudgetLoad.WorstBounded,
            PredictionTransportOverhead.SteamIpv6Unreliable);
        var exact = baseline.Owner.MaximumAccountedBytes;

        var atBoundary = probe.Measure(
            1,
            PredictionPacketBudgetLoad.WorstBounded,
            PredictionTransportOverhead.SteamIpv6Unreliable,
            accountedDatagramCeilingBytes: exact);
        Assert.Equal(exact, atBoundary.Owner.MaximumAccountedBytes);

        var exception = Assert.Throws<PredictionPacketBudgetExceededException>(() =>
            probe.Measure(
                1,
                PredictionPacketBudgetLoad.WorstBounded,
                PredictionTransportOverhead.SteamIpv6Unreliable,
                accountedDatagramCeilingBytes: exact - 1));
        Assert.Contains("Indivisible OwnerReconciliation", exception.Message, StringComparison.Ordinal);
        Assert.Contains("transport fragmentation is not an accepted fallback", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConservativeProfilesAreExplicitlyEstimatesPendingCapture()
    {
        foreach (var transport in StandardTransports)
        {
            Assert.Equal(
                PredictionTransportOverheadBasis.ConservativeEngineeringEstimate,
                transport.Basis);
            Assert.Contains("capture", transport.Provenance, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void WorstBoundedUsesEveryDeclaredCollectionAndReservationMaximum()
    {
        var worst = PredictionPacketBudgetLoad.WorstBounded;

        Assert.Equal(PredictionPacketBudgetProbe.MaximumOwnerMovementSources, worst.OwnerMovementSources);
        Assert.Equal(PredictionPacketBudgetProbe.MaximumOwnerMovementSources, worst.TotalWorldMovementSources);
        Assert.Equal(PredictionPacketBudgetProbe.MaximumWorldInlineMovementSources, worst.WorldInlineMovementSources);
        Assert.Equal(PredictionPacketBudgetProbe.MaximumContactFacts, worst.ContactFacts);
        Assert.Equal(PredictionPacketBudgetProbe.MaximumInlineAcknowledgementGapRanges, worst.AcknowledgementGapRanges);
        Assert.Equal(PredictionPacketBudgetProbe.MaximumInlineInputDispositions, worst.InputDispositions);
        Assert.Equal(PredictionPacketBudgetProbe.MaximumInlineTransitionResolutions, worst.TransitionResolutions);
        Assert.Equal(PredictionPacketBudgetProbe.MaximumInlineActionResolutions, worst.ActionResolutions);
        Assert.Equal(PredictionPacketBudgetProbe.MaximumUnresolvedOwnerReservationBytes, worst.UnresolvedOwnerReservationBytes);
        Assert.Equal(PredictionPacketBudgetProbe.MaximumUnresolvedCheckpointReservationBytes, worst.UnresolvedCheckpointReservationBytes);
    }

    [Fact]
    public void WorstPackedSourceAndContactSchemasRoundTripEveryField()
    {
        var report = new PredictionPacketBudgetProbe().Measure(
            1,
            PredictionPacketBudgetLoad.WorstBounded,
            PredictionTransportOverhead.SteamIpv6Unreliable);
        var owner = DecodeEnvelope(report.Owner.Packets[0].EncodedEnvelope).Payload;
        var state = DecodeOwnerState(owner);

        Assert.Equal(ulong.MaxValue, state.AuthorityDiscontinuityId);
        Assert.Equal(ulong.MaxValue, state.OwnerControlEpoch);
        Assert.Equal(16, state.Sources.Count);
        for (var index = 0; index < state.Sources.Count; index++)
        {
            AssertWorstSource(state.Sources[index], combatant: 0, source: index);
        }

        Assert.Equal(4, state.Contacts.Count);
        for (var index = 0; index < state.Contacts.Count; index++)
        {
            var contact = state.Contacts[index];
            Assert.Equal(ulong.MaxValue - 200UL - checked((ulong)index), contact.ColliderId);
            Assert.Equal(ushort.MaxValue, contact.ShapeId);
            Assert.Equal(ushort.MaxValue, contact.OctahedralNormal);
            Assert.Equal(short.MaxValue, contact.SeparationMillimeters);
            Assert.Equal(byte.MaxValue, contact.Kind);
            Assert.Equal(byte.MaxValue, contact.Flags);
        }
    }

    [Fact]
    public void ProductBudgetRejectsMalformedPartSets()
    {
        var report = new PredictionPacketBudgetProbe().Measure(
            3,
            PredictionPacketBudgetLoad.Common,
            PredictionTransportOverhead.EnetIpv6Unreliable);
        var valid = report.CollisionWorld.Packets[0];

        Assert.Throws<ArgumentException>(() => new PredictionPacketProductBudget(
            PredictionPacketProduct.CollisionWorld,
            expectedCombatantCount: 3,
            [valid, valid]));
        Assert.Throws<ArgumentException>(() => new PredictionPacketProductBudget(
            PredictionPacketProduct.CollisionWorld,
            expectedCombatantCount: 4,
            [valid]));
    }

    [Fact]
    public void AccountingOverflowIsRejected()
    {
        var impossible = new PredictionTransportOverhead(
            "overflow",
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            PredictionTransportOverheadBasis.MeasuredCapture,
            "synthetic overflow test");

        Assert.Throws<OverflowException>(() => new PredictionPacketBudgetProbe().Measure(
            1,
            PredictionPacketBudgetLoad.Common,
            impossible,
            accountedDatagramCeilingBytes: int.MaxValue));
    }

    [Fact]
    public void SingleCheckpointRecordTooLargeFailsWithoutOpaqueFragmentation()
    {
        var probe = new PredictionPacketBudgetProbe();
        var checkpointHeavy = new PredictionPacketBudgetLoad(
            "checkpoint-heavy",
            worstCase: false,
            ownerMovementSources: 0,
            totalWorldMovementSources: 0,
            worldInlineMovementSources: 0,
            contactFacts: 0,
            acknowledgementGapRanges: 0,
            inputDispositions: 0,
            transitionResolutions: 0,
            actionResolutions: 0,
            unresolvedOwnerReservationBytes: 0,
            unresolvedCheckpointReservationBytes: PredictionPacketBudgetProbe.MaximumUnresolvedCheckpointReservationBytes);
        var baseline = probe.Measure(
            1,
            checkpointHeavy,
            PredictionTransportOverhead.EnetIpv6Unreliable);
        Assert.True(baseline.Checkpoint.MaximumAccountedBytes > baseline.Owner.MaximumAccountedBytes);

        var exception = Assert.Throws<PredictionPacketBudgetExceededException>(() => probe.Measure(
            1,
            checkpointHeavy,
            PredictionTransportOverhead.EnetIpv6Unreliable,
            accountedDatagramCeilingBytes: baseline.Owner.MaximumAccountedBytes));
        Assert.Contains("Checkpoint combatant record", exception.Message, StringComparison.Ordinal);
        Assert.Contains("cannot be split opaquely", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StandardProbeIsDeterministicAndHasStableGoldenMeasurements()
    {
        var probe = new PredictionPacketBudgetProbe();
        var first = probe.Measure(
            8,
            PredictionPacketBudgetLoad.WorstBounded,
            PredictionTransportOverhead.SteamIpv6Unreliable);
        var second = probe.Measure(
            8,
            PredictionPacketBudgetLoad.WorstBounded,
            PredictionTransportOverhead.SteamIpv6Unreliable);

        Assert.Equal(
            first.Owner.Packets.Select(packet => packet.EncodedEnvelope.ToArray()),
            second.Owner.Packets.Select(packet => packet.EncodedEnvelope.ToArray()),
            ByteArrayComparer.Instance);
        Assert.Equal(first.Owner.MaximumAccountedBytes, second.Owner.MaximumAccountedBytes);
        Assert.Equal(first.CollisionWorld.PacketCount, second.CollisionWorld.PacketCount);
        Assert.Equal(first.Checkpoint.PacketCount, second.Checkpoint.PacketCount);
        Assert.Equal(first.AuthorityFanoutBytesPerSecond, second.AuthorityFanoutBytesPerSecond);
        Assert.Equal(
            (Owner: 1189, WorldParts: 8, WorldMaximum: 770, CheckpointParts: 8,
                CheckpointMaximum: 1042, PerRecipient: 457612, Fanout: 3660896),
            (Owner: first.Owner.MaximumAccountedBytes,
                WorldParts: first.CollisionWorld.PacketCount,
                WorldMaximum: first.CollisionWorld.MaximumAccountedBytes,
                CheckpointParts: first.Checkpoint.PacketCount,
                CheckpointMaximum: first.Checkpoint.MaximumAccountedBytes,
                PerRecipient: first.PerRecipientBytesPerSecond,
                Fanout: first.AuthorityFanoutBytesPerSecond));

        var common = probe.Measure(
            8,
            PredictionPacketBudgetLoad.Common,
            PredictionTransportOverhead.SteamIpv6Unreliable);
        Assert.Equal(
            (Owner: 495, WorldParts: 1, WorldMaximum: 988, CheckpointParts: 3,
                CheckpointMaximum: 1106, PerRecipient: 94988, Fanout: 759904),
            (Owner: common.Owner.MaximumAccountedBytes,
                WorldParts: common.CollisionWorld.PacketCount,
                WorldMaximum: common.CollisionWorld.MaximumAccountedBytes,
                CheckpointParts: common.Checkpoint.PacketCount,
                CheckpointMaximum: common.Checkpoint.MaximumAccountedBytes,
                PerRecipient: common.PerRecipientBytesPerSecond,
                Fanout: common.AuthorityFanoutBytesPerSecond));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void RejectsUnsupportedCombatantCounts(int combatants)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PredictionPacketBudgetProbe().Measure(
                combatants,
                PredictionPacketBudgetLoad.Common,
                PredictionTransportOverhead.EnetIpv6Unreliable));
    }

    public static TheoryData<PredictionTransportOverhead> StandardTransports => new()
    {
        PredictionTransportOverhead.EnetIpv6Unreliable,
        PredictionTransportOverhead.SteamIpv6Unreliable,
    };

    private static IEnumerable<PredictionPacketProductBudget> Products(PredictionPacketBudgetReport report)
    {
        yield return report.Owner;
        yield return report.CollisionWorld;
        yield return report.Checkpoint;
    }

    private static int ExpectedEnvelopeField(PredictionPacketProduct product) => product switch
    {
        PredictionPacketProduct.OwnerReconciliation => PredictionPacketBudgetProbe.OwnerEnvelopeFieldNumber,
        PredictionPacketProduct.CollisionWorld => PredictionPacketBudgetProbe.CollisionWorldEnvelopeFieldNumber,
        PredictionPacketProduct.Checkpoint => PredictionPacketBudgetProbe.CheckpointEnvelopeFieldNumber,
        _ => throw new ArgumentOutOfRangeException(nameof(product)),
    };

    private static ulong ExpectedCombatantId(PredictionPacketBudgetReport report, int index) =>
        report.LoadName == PredictionPacketBudgetLoad.Common.Name
            ? checked((ulong)(101 + index))
            : ulong.MaxValue - checked((ulong)index);

    private static DecodedEnvelope DecodeEnvelope(ReadOnlyMemory<byte> bytes)
    {
        var input = new CodedInputStream(bytes.ToArray());
        uint protocolVersion = 0;
        ulong sessionId = 0;
        ulong sequence = 0;
        ulong matchFrame = 0;
        var payloadField = 0;
        byte[]? payload = null;
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            var field = (int)(tag >> 3);
            if (field == 1)
            {
                protocolVersion = input.ReadUInt32();
            }
            else if (field == 2)
            {
                sessionId = input.ReadFixed64();
            }
            else if (field == 3)
            {
                sequence = input.ReadUInt64();
            }
            else if (field == 4)
            {
                matchFrame = input.ReadUInt64();
            }
            else if (field is PredictionPacketBudgetProbe.OwnerEnvelopeFieldNumber or
                PredictionPacketBudgetProbe.CollisionWorldEnvelopeFieldNumber or
                PredictionPacketBudgetProbe.CheckpointEnvelopeFieldNumber)
            {
                Assert.Null(payload);
                payloadField = field;
                payload = input.ReadBytes().ToByteArray();
            }
            else
            {
                input.SkipLastField();
            }
        }

        return new DecodedEnvelope(
            protocolVersion,
            sessionId,
            sequence,
            matchFrame,
            payloadField,
            Assert.IsType<byte[]>(payload));
    }

    private static DecodedPartition DecodePartition(
        byte[] payload,
        PredictionPacketProduct product)
    {
        var input = new CodedInputStream(payload);
        var partIndex = -1;
        var partCount = -1;
        var combatants = new List<ulong>();
        var overflows = new List<DecodedOverflow>();
        var checkpointRecords = new List<DecodedCheckpointRecord>();
        var worldStates = new List<DecodedCharacterState>();
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (tag >> 3)
            {
                case 1:
                    _ = input.ReadUInt64();
                    break;
                case 2:
                    partIndex = checked((int)input.ReadUInt64());
                    break;
                case 3:
                    partCount = checked((int)input.ReadUInt64());
                    break;
                case 4:
                    var recordBytes = input.ReadBytes().ToByteArray();
                    if (product == PredictionPacketProduct.Checkpoint)
                    {
                        var checkpoint = DecodeCheckpointRecord(recordBytes);
                        checkpointRecords.Add(checkpoint);
                        combatants.Add(checkpoint.CombatantId);
                    }
                    else
                    {
                        var state = DecodeCharacterState(recordBytes);
                        worldStates.Add(state);
                        combatants.Add(state.CombatantId);
                    }
                    break;
                case 5:
                    Assert.Equal(PredictionPacketProduct.CollisionWorld, product);
                    overflows.Add(DecodeOverflow(input.ReadBytes().ToByteArray()));
                    break;
                default:
                    input.SkipLastField();
                    break;
            }
        }

        return new DecodedPartition(
            partIndex,
            partCount,
            combatants,
            overflows,
            checkpointRecords,
            worldStates);
    }

    private static ulong DecodeOwnerCombatantId(byte[] payload)
    {
        var input = new CodedInputStream(payload);
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            if ((tag >> 3) == 2)
            {
                return input.ReadFixed64();
            }

            input.SkipLastField();
        }

        throw new InvalidDataException("Owner payload omitted combatant identity.");
    }

    private static ulong DecodeStateCombatantId(byte[] state)
    {
        var input = new CodedInputStream(state);
        Assert.Equal(1u, input.ReadTag() >> 3);
        return input.ReadFixed64();
    }

    private static DecodedCharacterState DecodeOwnerState(byte[] owner)
    {
        var input = new CodedInputStream(owner);
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            if ((tag >> 3) == 6)
            {
                return DecodeCharacterState(input.ReadBytes().ToByteArray());
            }

            input.SkipLastField();
        }

        throw new InvalidDataException("Owner payload omitted its full state.");
    }

    private static DecodedCharacterState DecodeCharacterState(byte[] state)
    {
        var input = new CodedInputStream(state);
        ulong combatantId = 0;
        ulong lifeId = 0;
        ulong discontinuity = 0;
        ulong controlEpoch = 0;
        byte[] contacts = [];
        byte[] sources = [];
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (tag >> 3)
            {
                case 1:
                    combatantId = input.ReadFixed64();
                    break;
                case 2:
                    lifeId = input.ReadFixed64();
                    break;
                case 3:
                    discontinuity = input.ReadUInt64();
                    break;
                case 4:
                    controlEpoch = input.ReadUInt64();
                    break;
                case 12:
                    contacts = input.ReadBytes().ToByteArray();
                    break;
                case 13:
                    sources = input.ReadBytes().ToByteArray();
                    break;
                default:
                    input.SkipLastField();
                    break;
            }
        }

        return new DecodedCharacterState(
            combatantId,
            lifeId,
            discontinuity,
            controlEpoch,
            DecodeSources(sources),
            DecodeContacts(contacts));
    }

    private static DecodedOverflow DecodeOverflow(byte[] bytes)
    {
        var input = new CodedInputStream(bytes);
        ulong combatantId = 0;
        ulong lifeId = 0;
        ulong discontinuity = 0;
        ulong controlEpoch = 0;
        var firstSource = -1;
        var sourceCount = -1;
        byte[] packed = [];
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (tag >> 3)
            {
                case 1: combatantId = input.ReadFixed64(); break;
                case 2: lifeId = input.ReadFixed64(); break;
                case 3: discontinuity = input.ReadUInt64(); break;
                case 4: controlEpoch = input.ReadUInt64(); break;
                case 5: firstSource = checked((int)input.ReadUInt64()); break;
                case 6: sourceCount = checked((int)input.ReadUInt64()); break;
                case 7: packed = input.ReadBytes().ToByteArray(); break;
                default: input.SkipLastField(); break;
            }
        }

        var sources = DecodeSources(packed);
        Assert.Equal(sourceCount, sources.Count);
        return new DecodedOverflow(
            combatantId,
            lifeId,
            discontinuity,
            controlEpoch,
            firstSource,
            sourceCount,
            sources);
    }

    private static DecodedCheckpointRecord DecodeCheckpointRecord(byte[] bytes)
    {
        var input = new CodedInputStream(bytes);
        ulong combatantId = 0;
        ulong lifeId = 0;
        ulong stateCombatantId = 0;
        ulong stateLifeId = 0;
        var hasState = false;
        var hasConfiguration = false;
        var hasRepair = false;
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (tag >> 3)
            {
                case 1: combatantId = input.ReadFixed64(); break;
                case 2: lifeId = input.ReadFixed64(); break;
                case 3:
                    hasState = true;
                    var state = DecodeCharacterState(input.ReadBytes().ToByteArray());
                    stateCombatantId = state.CombatantId;
                    stateLifeId = state.LifeId;
                    break;
                case 4:
                    hasConfiguration = ValidateNonEmptyMessage(input.ReadBytes().ToByteArray());
                    break;
                case 5:
                    hasRepair = ValidateNonEmptyMessage(input.ReadBytes().ToByteArray());
                    break;
                default: input.SkipLastField(); break;
            }
        }

        return new DecodedCheckpointRecord(
            combatantId,
            lifeId,
            stateCombatantId,
            stateLifeId,
            hasState,
            hasConfiguration,
            hasRepair);
    }

    private static bool ValidateNonEmptyMessage(byte[] bytes)
    {
        var input = new CodedInputStream(bytes);
        var fields = 0;
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            fields++;
            input.SkipLastField();
        }

        return fields > 0 && input.IsAtEnd;
    }

    private static IReadOnlyList<DecodedSource> DecodeSources(byte[] bytes)
    {
        Assert.Equal(0, bytes.Length % PredictionPacketBudgetProbe.PackedMovementSourceBytes);
        var sources = new List<DecodedSource>();
        for (var offset = 0; offset < bytes.Length; offset += PredictionPacketBudgetProbe.PackedMovementSourceBytes)
        {
            sources.Add(new DecodedSource(
                ReadUInt24(bytes, offset),
                ReadUInt16(bytes, offset + 3),
                ReadUInt16(bytes, offset + 5),
                ReadUInt16(bytes, offset + 7),
                ReadInt16(bytes, offset + 9),
                ReadUInt24(bytes, offset + 11),
                bytes[offset + 14],
                bytes[offset + 15]));
        }

        return sources;
    }

    private static IReadOnlyList<DecodedContact> DecodeContacts(byte[] bytes)
    {
        Assert.Equal(0, bytes.Length % PredictionPacketBudgetProbe.PackedContactFactBytes);
        var contacts = new List<DecodedContact>();
        for (var offset = 0; offset < bytes.Length; offset += PredictionPacketBudgetProbe.PackedContactFactBytes)
        {
            contacts.Add(new DecodedContact(
                ReadUInt64(bytes, offset),
                ReadUInt16(bytes, offset + 8),
                ReadUInt16(bytes, offset + 10),
                ReadInt16(bytes, offset + 12),
                bytes[offset + 14],
                bytes[offset + 15]));
        }

        return contacts;
    }

    private static void AssertWorstSource(DecodedSource actual, int combatant, int source)
    {
        Assert.Equal(0x00ff_ffffU - checked((uint)(combatant * 16 + source)), actual.InstanceId);
        Assert.Equal(ushort.MaxValue, actual.PolicyId);
        Assert.Equal(ushort.MaxValue, actual.ProgressFrames);
        Assert.Equal(ushort.MaxValue, actual.OctahedralDirection);
        Assert.Equal(short.MaxValue, actual.MagnitudeQ15);
        Assert.Equal(0x00ff_ffffU - 256U - checked((uint)(combatant * 16 + source)), actual.CorrelationId);
        Assert.Equal(byte.MaxValue, actual.Priority);
        Assert.Equal(byte.MaxValue, actual.StackAndFlags);
        Assert.Equal(15, actual.StackCount);
        Assert.Equal(15, actual.Flags);
    }

    private static ushort ReadUInt16(byte[] bytes, int offset) =>
        (ushort)(bytes[offset] | bytes[offset + 1] << 8);
    private static short ReadInt16(byte[] bytes, int offset) =>
        unchecked((short)ReadUInt16(bytes, offset));
    private static uint ReadUInt24(byte[] bytes, int offset) =>
        (uint)(bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16);
    private static ulong ReadUInt64(byte[] bytes, int offset)
    {
        ulong value = 0;
        for (var index = 0; index < 8; index++)
        {
            value |= (ulong)bytes[offset + index] << (index * 8);
        }

        return value;
    }

    private sealed record DecodedEnvelope(
        uint ProtocolVersion,
        ulong SessionId,
        ulong Sequence,
        ulong MatchFrame,
        int PayloadField,
        byte[] Payload);

    private sealed record DecodedPartition(
        int PartIndex,
        int PartCount,
        IReadOnlyList<ulong> CombatantIds,
        IReadOnlyList<DecodedOverflow> Overflows,
        IReadOnlyList<DecodedCheckpointRecord> CheckpointRecords,
        IReadOnlyList<DecodedCharacterState> WorldStates);

    private sealed record DecodedCharacterState(
        ulong CombatantId,
        ulong LifeId,
        ulong AuthorityDiscontinuityId,
        ulong OwnerControlEpoch,
        IReadOnlyList<DecodedSource> Sources,
        IReadOnlyList<DecodedContact> Contacts);

    private sealed record DecodedSource(
        uint InstanceId,
        ushort PolicyId,
        ushort ProgressFrames,
        ushort OctahedralDirection,
        short MagnitudeQ15,
        uint CorrelationId,
        byte Priority,
        byte StackAndFlags)
    {
        public int StackCount => StackAndFlags >> 4;
        public int Flags => StackAndFlags & 0x0f;
    }

    private sealed record DecodedContact(
        ulong ColliderId,
        ushort ShapeId,
        ushort OctahedralNormal,
        short SeparationMillimeters,
        byte Kind,
        byte Flags);

    private sealed record DecodedOverflow(
        ulong CombatantId,
        ulong LifeId,
        ulong AuthorityDiscontinuityId,
        ulong OwnerControlEpoch,
        int FirstSource,
        int SourceCount,
        IReadOnlyList<DecodedSource> Sources);

    private sealed record DecodedCheckpointRecord(
        ulong CombatantId,
        ulong LifeId,
        ulong StateCombatantId,
        ulong StateLifeId,
        bool HasState,
        bool HasConfiguration,
        bool HasRepair);

    private sealed class ByteArrayComparer : IEqualityComparer<byte[]>
    {
        public static ByteArrayComparer Instance { get; } = new();
        public bool Equals(byte[]? x, byte[]? y) =>
            x is not null && y is not null && x.AsSpan().SequenceEqual(y);
        public int GetHashCode(byte[] obj) => obj.Length;
    }
}
