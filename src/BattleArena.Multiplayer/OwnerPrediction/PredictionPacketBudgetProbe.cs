using System.Buffers;

namespace BattleArena.Multiplayer.OwnerPrediction;

/// <summary>
/// Serializes concrete Phase-1 candidate owner, collision-world, and checkpoint
/// schemas into valid Protobuf wire bytes. The generated production schemas stay
/// unfrozen until Phase 6; this probe exists to reject an impossible wire design
/// before then.
/// </summary>
public sealed class PredictionPacketBudgetProbe
{
    public const int DefaultAccountedDatagramCeilingBytes = 1_200;
    public const int SimulationFramesPerSecond = 60;
    public const int CheckpointsPerSecond = 2;

    // These are candidate protocol bounds, not incidental test values. Complete
    // repair journals remain on reliable event/checkpoint paths; the owner frame
    // redundantly carries only this recent bounded tail.
    public const int MaximumOwnerMovementSources = 16;
    public const int MaximumWorldInlineMovementSources = 4;
    public const int MaximumContactFacts = 4;
    public const int MaximumInlineAcknowledgementGapRanges = 2;
    public const int MaximumInlineInputDispositions = 4;
    public const int MaximumInlineTransitionResolutions = 1;
    public const int MaximumInlineActionResolutions = 1;
    public const int PackedMovementSourceBytes = 16;
    public const int PackedContactFactBytes = 16;
    public const int MaximumUnresolvedOwnerReservationBytes = 32;
    public const int MaximumUnresolvedCheckpointReservationBytes = 64;

    public const int OwnerEnvelopeFieldNumber = 50;
    public const int CollisionWorldEnvelopeFieldNumber = 51;
    public const int CheckpointEnvelopeFieldNumber = 52;

    private const uint ProtocolVersion = 8;
    private const ulong RepresentativeSessionId = ulong.MaxValue;
    private const ulong RepresentativeSequence = ulong.MaxValue;
    private const ulong RepresentativeMatchFrame = ulong.MaxValue;

    public PredictionPacketBudgetReport Measure(
        int combatantCount,
        PredictionPacketBudgetLoad load,
        PredictionTransportOverhead transport,
        PredictionAuthorityTopology topology = PredictionAuthorityTopology.DedicatedServer,
        int accountedDatagramCeilingBytes = DefaultAccountedDatagramCeilingBytes)
    {
        if (combatantCount is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(
                nameof(combatantCount),
                combatantCount,
                "The prediction budget probe supports the planned one-to-eight combatants.");
        }

        ArgumentNullException.ThrowIfNull(load);
        ArgumentNullException.ThrowIfNull(transport);
        if (!Enum.IsDefined(topology))
        {
            throw new ArgumentOutOfRangeException(nameof(topology));
        }

        if (accountedDatagramCeilingBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountedDatagramCeilingBytes));
        }

        var owner = MeasureIndivisible(
            PredictionPacketProduct.OwnerReconciliation,
            combatantCount: 1,
            SerializeOwnerPayload(load),
            transport,
            accountedDatagramCeilingBytes);
        var collisionWorld = MeasurePartitionedCombatants(
            PredictionPacketProduct.CollisionWorld,
            combatantCount,
            load,
            transport,
            accountedDatagramCeilingBytes,
            SerializeCollisionWorldPayload);
        var checkpoint = MeasurePartitionedCombatants(
            PredictionPacketProduct.Checkpoint,
            combatantCount,
            load,
            transport,
            accountedDatagramCeilingBytes,
            SerializeCheckpointPayload);

        var recipientCount = topology == PredictionAuthorityTopology.ListenServer
            ? combatantCount - 1
            : combatantCount;
        var ownerTraffic = Traffic(owner, SimulationFramesPerSecond, recipientCount);
        var worldTraffic = Traffic(collisionWorld, SimulationFramesPerSecond, recipientCount);
        var checkpointTraffic = Traffic(checkpoint, CheckpointsPerSecond, recipientCount);
        var perRecipientBytes = checked(
            ownerTraffic.BytesPerSecondPerRecipient +
            worldTraffic.BytesPerSecondPerRecipient +
            checkpointTraffic.BytesPerSecondPerRecipient);
        var perRecipientPackets = checked(
            ownerTraffic.PacketsPerSecondPerRecipient +
            worldTraffic.PacketsPerSecondPerRecipient +
            checkpointTraffic.PacketsPerSecondPerRecipient);

        return new PredictionPacketBudgetReport(
            combatantCount,
            recipientCount,
            topology,
            load.Name,
            transport.Name,
            transport.Basis,
            transport.Provenance,
            accountedDatagramCeilingBytes,
            owner,
            collisionWorld,
            checkpoint,
            ownerTraffic,
            worldTraffic,
            checkpointTraffic,
            perRecipientBytes,
            perRecipientPackets,
            checked(perRecipientBytes * recipientCount),
            checked(perRecipientPackets * recipientCount));
    }

    public IReadOnlyList<PredictionPacketBudgetReport> MeasureStandardMatrix(
        PredictionTransportOverhead transport,
        PredictionAuthorityTopology topology = PredictionAuthorityTopology.DedicatedServer,
        int accountedDatagramCeilingBytes = DefaultAccountedDatagramCeilingBytes)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var reports = new List<PredictionPacketBudgetReport>(6);
        foreach (var combatants in new[] { 1, 3, 8 })
        {
            reports.Add(Measure(
                combatants,
                PredictionPacketBudgetLoad.Common,
                transport,
                topology,
                accountedDatagramCeilingBytes));
            reports.Add(Measure(
                combatants,
                PredictionPacketBudgetLoad.WorstBounded,
                transport,
                topology,
                accountedDatagramCeilingBytes));
        }

        return reports;
    }

    private static PredictionPacketProductTraffic Traffic(
        PredictionPacketProductBudget product,
        int productFramesPerSecond,
        int recipients)
    {
        var packets = checked(product.PacketCount * productFramesPerSecond);
        var bytes = checked(product.TotalAccountedBytes * productFramesPerSecond);
        return new PredictionPacketProductTraffic(
            product.Product,
            packets,
            bytes,
            checked(packets * recipients),
            checked(bytes * recipients));
    }

    private static PredictionPacketProductBudget MeasureIndivisible(
        PredictionPacketProduct product,
        int combatantCount,
        byte[] payload,
        PredictionTransportOverhead transport,
        int ceiling)
    {
        var packet = MeasurePacket(
            product,
            partIndex: 0,
            partCount: 1,
            firstCombatantIndex: 0,
            combatantCount,
            payload,
            transport,
            ceiling);
        if (!packet.Fits)
        {
            throw PredictionPacketBudgetExceededException.ForIndivisible(
                product,
                packet.AccountedDatagramBytes,
                ceiling);
        }

        return new PredictionPacketProductBudget(product, combatantCount, [packet]);
    }

    private static PredictionPacketProductBudget MeasurePartitionedCombatants(
        PredictionPacketProduct product,
        int combatantCount,
        PredictionPacketBudgetLoad load,
        PredictionTransportOverhead transport,
        int ceiling,
        Func<int, int, int, int, PredictionPacketBudgetLoad, byte[]> serialize)
    {
        var ranges = new List<(int Start, int Count)>();
        var start = 0;
        while (start < combatantCount)
        {
            var bestCount = 0;
            for (var count = 1; start + count <= combatantCount; count++)
            {
                // At most eight parts exist, so the provisional count has the
                // same Protobuf width as the final count.
                var candidate = MeasurePacket(
                    product,
                    ranges.Count,
                    partCount: 8,
                    start,
                    count,
                    serialize(ranges.Count, start, count, 8, load),
                    transport,
                    ceiling);
                if (!candidate.Fits)
                {
                    break;
                }

                bestCount = count;
            }

            if (bestCount == 0)
            {
                var minimum = MeasurePacket(
                    product,
                    ranges.Count,
                    partCount: 8,
                    start,
                    combatantCount: 1,
                    serialize(ranges.Count, start, 1, 8, load),
                    transport,
                    ceiling);
                throw PredictionPacketBudgetExceededException.ForCombatantRecord(
                    product,
                    start,
                    minimum.AccountedDatagramBytes,
                    ceiling);
            }

            ranges.Add((start, bestCount));
            start += bestCount;
        }

        var packets = new List<PredictionPacketMeasurement>(ranges.Count);
        for (var index = 0; index < ranges.Count; index++)
        {
            var range = ranges[index];
            var payload = serialize(index, range.Start, range.Count, ranges.Count, load);
            var packet = MeasurePacket(
                product,
                index,
                ranges.Count,
                range.Start,
                range.Count,
                payload,
                transport,
                ceiling);
            if (!packet.Fits)
            {
                throw new InvalidOperationException(
                    "A final application part exceeded a budget that its " +
                    "conservative eight-part sizing pass accepted.");
            }

            packets.Add(packet);
        }

        return new PredictionPacketProductBudget(product, combatantCount, packets);
    }

    private static PredictionPacketMeasurement MeasurePacket(
        PredictionPacketProduct product,
        int partIndex,
        int partCount,
        int firstCombatantIndex,
        int combatantCount,
        byte[] payload,
        PredictionTransportOverhead transport,
        int ceiling)
    {
        var envelopeField = ProductEnvelopeField(product);
        var envelope = new CandidateProtoWriter();
        envelope.Varint(1, ProtocolVersion);
        envelope.Fixed64(2, RepresentativeSessionId);
        envelope.Varint(3, RepresentativeSequence);
        envelope.Varint(4, RepresentativeMatchFrame);
        envelope.Bytes(envelopeField, payload);

        var encodedEnvelope = envelope.ToArray();
        var envelopeBytes = encodedEnvelope.Length - payload.Length;
        var accounted = checked(
            encodedEnvelope.Length +
            transport.AuthenticationBytes +
            transport.TransportFramingBytes +
            transport.IpUdpBytes);

        return new PredictionPacketMeasurement(
            product,
            envelopeField,
            partIndex,
            partCount,
            firstCombatantIndex,
            combatantCount,
            payload.Length,
            envelopeBytes,
            transport.AuthenticationBytes,
            transport.TransportFramingBytes,
            transport.IpUdpBytes,
            accounted,
            accounted <= ceiling,
            encodedEnvelope);
    }

    private static int ProductEnvelopeField(PredictionPacketProduct product) => product switch
    {
        PredictionPacketProduct.OwnerReconciliation => OwnerEnvelopeFieldNumber,
        PredictionPacketProduct.CollisionWorld => CollisionWorldEnvelopeFieldNumber,
        PredictionPacketProduct.Checkpoint => CheckpointEnvelopeFieldNumber,
        _ => throw new ArgumentOutOfRangeException(nameof(product)),
    };

    // Mirrors the conceptual owner frame in LOCAL_OWNER_PREDICTION_REDESIGN.md:
    // identity, full state, applied input, two distinct acknowledgements,
    // cursors/repair tails, buffer/lead policy, and versioned canonical hash.
    private static byte[] SerializeOwnerPayload(PredictionPacketBudgetLoad load)
    {
        var writer = new CandidateProtoWriter();
        writer.Varint(1, RepresentativeMatchFrame);
        writer.Fixed64(2, load.CombatantId(0));
        writer.Fixed64(3, load.LifeId(0));
        writer.Varint(4, load.UInt64Value(2));
        writer.Varint(5, load.UInt64Value(3));
        writer.Message(6, state => WriteFullCharacterState(state, 0, load));
        writer.Varint(7, load.UInt64Value(10_000));
        writer.Varint(8, load.UInt32Value(2));
        writer.Message(9, received =>
        {
            received.Varint(1, load.UInt64Value(10_000));
            received.Fixed64(2, ulong.MaxValue);
            for (var index = 0; index < load.AcknowledgementGapRanges; index++)
            {
                received.Message(3, range =>
                {
                    range.Varint(1, load.UInt64Value(9_900 + index * 2));
                    range.Varint(2, load.UInt64Value(9_901 + index * 2));
                });
            }
        });
        writer.Message(10, consumed =>
        {
            consumed.Varint(1, load.UInt64Value(9_996));
            for (var index = 0; index < load.InputDispositions; index++)
            {
                consumed.Message(2, disposition =>
                {
                    disposition.Varint(1, load.UInt64Value(9_997 + index));
                    disposition.Varint(2, load.UInt64Value(10_002 + index));
                    disposition.Varint(3, load.UInt32Value(4));
                });
            }
        });
        writer.Varint(11, load.UInt64Value(20_000));
        for (var index = 0; index < load.TransitionResolutions; index++)
        {
            writer.Message(12, resolution => WriteTransitionResolution(resolution, index, load));
        }

        writer.Varint(13, load.UInt64Value(30_000));
        for (var index = 0; index < load.ActionResolutions; index++)
        {
            writer.Message(14, resolution => WriteActionResolution(resolution, index, load));
        }

        writer.Varint(15, load.UInt32Value(8));
        writer.Varint(16, load.UInt32Value(3));
        writer.Varint(17, load.UInt64Value(17));
        writer.Varint(18, load.UInt64Value(10_010));
        writer.Fixed64(19, ulong.MaxValue);
        writer.Varint(20, load.UInt32Value(1));
        writer.ReservedBytes(31, load.UnresolvedOwnerReservationBytes);
        return writer.ToArray();
    }

    private static byte[] SerializeCollisionWorldPayload(
        int partIndex,
        int firstCombatant,
        int combatantCount,
        int partCount,
        PredictionPacketBudgetLoad load)
    {
        var writer = BasePartitionedPayload(partIndex, partCount);
        for (var index = 0; index < combatantCount; index++)
        {
            var ordinal = firstCombatant + index;
            writer.Message(4, state => WriteCompactWorldState(state, ordinal, load));

            var remaining = load.TotalWorldMovementSources - load.WorldInlineMovementSources;
            var firstSource = load.WorldInlineMovementSources;
            while (remaining > 0)
            {
                var count = Math.Min(MaximumWorldInlineMovementSources, remaining);
                writer.Message(5, overflow =>
                {
                    overflow.Fixed64(1, load.CombatantId(ordinal));
                    overflow.Fixed64(2, load.LifeId(ordinal));
                    overflow.Varint(3, load.UInt64Value(2));
                    overflow.Varint(4, load.UInt64Value(3));
                    overflow.Varint(5, checked((ulong)firstSource));
                    overflow.Varint(6, checked((ulong)count));
                    overflow.Bytes(
                        7,
                        BuildPackedMovementSources(load, ordinal, firstSource, count));
                });
                firstSource += count;
                remaining -= count;
            }
        }

        return writer.ToArray();
    }

    private static byte[] SerializeCheckpointPayload(
        int partIndex,
        int firstCombatant,
        int combatantCount,
        int partCount,
        PredictionPacketBudgetLoad load)
    {
        var writer = BasePartitionedPayload(partIndex, partCount);
        for (var index = 0; index < combatantCount; index++)
        {
            var ordinal = firstCombatant + index;
            writer.Message(4, record =>
            {
                record.Fixed64(1, load.CombatantId(ordinal));
                record.Fixed64(2, load.LifeId(ordinal));
                record.Message(3, state => WriteFullCharacterState(state, ordinal, load));
                record.Message(4, configuration =>
                {
                    configuration.Varint(1, load.UInt64Value(5 + ordinal));
                    configuration.Varint(2, load.UInt64Value(7 + ordinal));
                    for (var field = 3; field <= 18; field++)
                    {
                        configuration.SInt32(field, load.SignedValue(1_000 + field * 137));
                    }

                    configuration.Fixed32(19, uint.MaxValue);
                });
                record.Message(5, repair =>
                {
                    repair.SInt64(1, load.Signed64Value(100));
                    repair.SInt64(2, load.Signed64Value(100));
                    repair.Varint(3, load.UInt32Value(2));
                    repair.Varint(4, load.UInt32Value(1));
                    repair.Varint(5, load.UInt64Value(30_000 + ordinal));
                    repair.Varint(6, load.UInt32Value(3));
                    repair.Varint(7, load.UInt32Value(2));
                    repair.Varint(8, load.UInt64Value(22));
                    repair.Varint(9, load.UInt64Value(23));
                    repair.Varint(10, load.UInt64Value(24));
                    repair.Varint(11, load.UInt64Value(10_050));
                    repair.ReservedBytes(20, load.UnresolvedCheckpointReservationBytes);
                });
            });
        }

        return writer.ToArray();
    }

    private static CandidateProtoWriter BasePartitionedPayload(int partIndex, int partCount)
    {
        var writer = new CandidateProtoWriter();
        writer.Varint(1, RepresentativeMatchFrame);
        writer.Varint(2, checked((ulong)partIndex));
        writer.Varint(3, checked((ulong)partCount));
        return writer;
    }

    private static void WriteCompactWorldState(
        CandidateProtoWriter writer,
        int ordinal,
        PredictionPacketBudgetLoad load)
    {
        WriteStateIdentityAndKinematics(writer, ordinal, load);
        writer.Bytes(12, BuildPackedContactFacts(load, ordinal, load.ContactFacts));
        writer.Bytes(
            13,
            BuildPackedMovementSources(
                load,
                ordinal,
                firstSource: 0,
                load.WorldInlineMovementSources));
        writer.Varint(14, load.UInt64Value(10_000 + ordinal));
    }

    private static void WriteFullCharacterState(
        CandidateProtoWriter writer,
        int ordinal,
        PredictionPacketBudgetLoad load)
    {
        WriteStateIdentityAndKinematics(writer, ordinal, load);
        writer.Bytes(12, BuildPackedContactFacts(load, ordinal, load.ContactFacts));
        writer.Bytes(
            13,
            BuildPackedMovementSources(load, ordinal, firstSource: 0, load.OwnerMovementSources));
        writer.Varint(14, load.UInt64Value(30_000 + ordinal));
        writer.Varint(15, load.UInt64Value(10_000 + ordinal));
        writer.Varint(16, load.UInt32Value(3));
        writer.Varint(17, load.UInt32Value(2));
        writer.SInt32(18, load.SignedValue(18_000 + ordinal));
        writer.Message(19, traversal =>
        {
            traversal.Varint(1, load.UInt32Value(3));
            traversal.Fixed64(2, load.ColliderId(ordinal));
            traversal.Message(3, anchor => WriteVector(anchor, load, 4_000 + ordinal));
        });
        writer.Varint(20, load.UInt64Value(6));
        writer.Varint(21, load.UInt64Value(12));
    }

    private static void WriteStateIdentityAndKinematics(
        CandidateProtoWriter writer,
        int ordinal,
        PredictionPacketBudgetLoad load)
    {
        writer.Fixed64(1, load.CombatantId(ordinal));
        writer.Fixed64(2, load.LifeId(ordinal));
        writer.Varint(3, load.UInt64Value(2));
        writer.Varint(4, load.UInt64Value(3));
        writer.Message(5, vector => WriteVector(vector, load, 12_500 + ordinal));
        writer.Message(6, vector => WriteVector(vector, load, 4_250 + ordinal));
        writer.SInt32(7, load.SignedValue(18_000 + ordinal));
        writer.Varint(8, load.UInt64Value(7));
        writer.Varint(9, load.UInt64Value(11));
        writer.Fixed32(10, 0x01ff_ffff);
        writer.Varint(11, load.UInt64Value(9_999));
    }

    private static void WriteTransitionResolution(
        CandidateProtoWriter writer,
        int index,
        PredictionPacketBudgetLoad load)
    {
        writer.Varint(1, load.UInt64Value(20_000 + index));
        writer.Varint(2, load.UInt64Value(10_000 + index));
        writer.Varint(3, load.UInt64Value(10_001 + index));
        writer.Varint(4, load.UInt32Value(3));
        writer.SInt32(5, load.SignedValue(2_500));
    }

    private static void WriteActionResolution(
        CandidateProtoWriter writer,
        int index,
        PredictionPacketBudgetLoad load)
    {
        writer.Varint(1, load.UInt64Value(30_000 + index));
        writer.Varint(2, load.UInt64Value(10_000 + index));
        writer.Varint(3, load.UInt64Value(10_002 + index));
        writer.Varint(4, load.UInt32Value(2));
        writer.Varint(5, load.UInt32Value(3));
        writer.SInt32(6, load.SignedValue(31_415));
    }

    private static void WriteVector(
        CandidateProtoWriter writer,
        PredictionPacketBudgetLoad load,
        int seed)
    {
        writer.SInt32(1, load.SignedValue(seed));
        writer.SInt32(2, load.SignedValue(seed / 2));
        writer.SInt32(3, load.SignedValue(-seed));
    }

    // Packed source v1 (16 bytes, little endian): source-instance uint24,
    // policy uint16, elapsed frames uint16, octahedral direction uint16, Q15
    // magnitude int16, correlation uint24, priority uint8, and a combined
    // stack/flags uint8. Start frame and curve progress are derived from the
    // parent match frame, elapsed frames, and immutable policy definition.
    // Life/discontinuity/control epochs are carried by the parent state or
    // overflow record. The narrow IDs are content-validation bounds.
    private static byte[] BuildPackedMovementSources(
        PredictionPacketBudgetLoad load,
        int combatantOrdinal,
        int firstSource,
        int count)
    {
        var bytes = new byte[checked(count * PackedMovementSourceBytes)];
        for (var index = 0; index < count; index++)
        {
            var sourceIndex = firstSource + index;
            var offset = index * PackedMovementSourceBytes;
            WriteUInt24(bytes, offset, load.SourceInstanceId(combatantOrdinal, sourceIndex));
            WriteUInt16(bytes, offset + 3, load.UInt16Value(100 + sourceIndex));
            WriteUInt16(bytes, offset + 5, load.UInt16Value(240 + sourceIndex));
            WriteUInt16(bytes, offset + 7, load.UInt16Value(32_000 + sourceIndex));
            WriteInt16(bytes, offset + 9, load.Signed16Value(1_500 + sourceIndex));
            WriteUInt24(bytes, offset + 11, load.SourceCorrelationId(combatantOrdinal, sourceIndex));
            bytes[offset + 14] = load.ByteValue(7 + sourceIndex);
            bytes[offset + 15] = load.ByteValue(0x3f);
        }

        return bytes;
    }

    // Packed contact v1 (16 bytes, little endian): stable collider uint64,
    // authored shape uint16, octahedral normal uint16, millimeter separation
    // int16, contact kind uint8, and flags uint8.
    private static byte[] BuildPackedContactFacts(
        PredictionPacketBudgetLoad load,
        int combatantOrdinal,
        int count)
    {
        var bytes = new byte[checked(count * PackedContactFactBytes)];
        for (var index = 0; index < count; index++)
        {
            var offset = index * PackedContactFactBytes;
            WriteUInt64(bytes, offset, load.ColliderId(combatantOrdinal + index));
            WriteUInt16(bytes, offset + 8, load.UInt16Value(10 + index));
            WriteUInt16(bytes, offset + 10, load.UInt16Value(30_000 + index));
            WriteInt16(bytes, offset + 12, load.Signed16Value(37 + index));
            bytes[offset + 14] = load.ByteValue(3 + index);
            bytes[offset + 15] = load.ByteValue(0x0f);
        }

        return bytes;
    }

    private static void WriteUInt16(byte[] destination, int offset, ushort value)
    {
        destination[offset] = (byte)value;
        destination[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteInt16(byte[] destination, int offset, short value) =>
        WriteUInt16(destination, offset, unchecked((ushort)value));

    private static void WriteUInt24(byte[] destination, int offset, uint value)
    {
        destination[offset] = (byte)value;
        destination[offset + 1] = (byte)(value >> 8);
        destination[offset + 2] = (byte)(value >> 16);
    }

    private static void WriteUInt64(byte[] destination, int offset, ulong value)
    {
        for (var index = 0; index < 8; index++)
        {
            destination[offset + index] = (byte)(value >> (index * 8));
        }
    }

    private sealed class CandidateProtoWriter
    {
        private readonly ArrayBufferWriter<byte> _buffer = new();

        public void Varint(int fieldNumber, ulong value)
        {
            Tag(fieldNumber, wireType: 0);
            RawVarint(value);
        }

        public void SInt32(int fieldNumber, int value) =>
            Varint(fieldNumber, unchecked((uint)((value << 1) ^ (value >> 31))));

        public void SInt64(int fieldNumber, long value) =>
            Varint(fieldNumber, unchecked((ulong)((value << 1) ^ (value >> 63))));

        public void Fixed32(int fieldNumber, uint value)
        {
            Tag(fieldNumber, wireType: 5);
            var span = _buffer.GetSpan(4);
            span[0] = (byte)value;
            span[1] = (byte)(value >> 8);
            span[2] = (byte)(value >> 16);
            span[3] = (byte)(value >> 24);
            _buffer.Advance(4);
        }

        public void Fixed64(int fieldNumber, ulong value)
        {
            Tag(fieldNumber, wireType: 1);
            var span = _buffer.GetSpan(8);
            for (var index = 0; index < 8; index++)
            {
                span[index] = (byte)(value >> (index * 8));
            }

            _buffer.Advance(8);
        }

        public void Message(int fieldNumber, Action<CandidateProtoWriter> write)
        {
            ArgumentNullException.ThrowIfNull(write);
            var nested = new CandidateProtoWriter();
            write(nested);
            Bytes(fieldNumber, nested._buffer.WrittenSpan);
        }

        public void ReservedBytes(int fieldNumber, int count)
        {
            if (count == 0)
            {
                return;
            }

            var bytes = new byte[count];
            Array.Fill(bytes, byte.MaxValue);
            Bytes(fieldNumber, bytes);
        }

        public void Bytes(int fieldNumber, ReadOnlySpan<byte> value)
        {
            Tag(fieldNumber, wireType: 2);
            RawVarint(checked((ulong)value.Length));
            value.CopyTo(_buffer.GetSpan(value.Length));
            _buffer.Advance(value.Length);
        }

        public byte[] ToArray() => _buffer.WrittenSpan.ToArray();

        private void Tag(int fieldNumber, int wireType)
        {
            if (fieldNumber <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(fieldNumber));
            }

            RawVarint(checked((ulong)((fieldNumber << 3) | wireType)));
        }

        private void RawVarint(ulong value)
        {
            do
            {
                var next = (byte)(value & 0x7f);
                value >>= 7;
                if (value != 0)
                {
                    next |= 0x80;
                }

                var span = _buffer.GetSpan(1);
                span[0] = next;
                _buffer.Advance(1);
            }
            while (value != 0);
        }
    }
}

public enum PredictionPacketProduct
{
    OwnerReconciliation,
    CollisionWorld,
    Checkpoint,
}

public enum PredictionAuthorityTopology
{
    ListenServer,
    DedicatedServer,
}

public enum PredictionTransportOverheadBasis
{
    MeasuredCapture,
    ConservativeEngineeringEstimate,
}

public sealed record PredictionPacketBudgetLoad
{
    public static PredictionPacketBudgetLoad Common { get; } = new(
        "common",
        worstCase: false,
        ownerMovementSources: 2,
        totalWorldMovementSources: 1,
        worldInlineMovementSources: 1,
        contactFacts: 1,
        acknowledgementGapRanges: 1,
        inputDispositions: 1,
        transitionResolutions: 1,
        actionResolutions: 1,
        unresolvedOwnerReservationBytes: PredictionPacketBudgetProbe.MaximumUnresolvedOwnerReservationBytes,
        unresolvedCheckpointReservationBytes: 32);

    public static PredictionPacketBudgetLoad WorstBounded { get; } = new(
        "worst-bounded",
        worstCase: true,
        ownerMovementSources: PredictionPacketBudgetProbe.MaximumOwnerMovementSources,
        totalWorldMovementSources: PredictionPacketBudgetProbe.MaximumOwnerMovementSources,
        worldInlineMovementSources: PredictionPacketBudgetProbe.MaximumWorldInlineMovementSources,
        contactFacts: PredictionPacketBudgetProbe.MaximumContactFacts,
        acknowledgementGapRanges: PredictionPacketBudgetProbe.MaximumInlineAcknowledgementGapRanges,
        inputDispositions: PredictionPacketBudgetProbe.MaximumInlineInputDispositions,
        transitionResolutions: PredictionPacketBudgetProbe.MaximumInlineTransitionResolutions,
        actionResolutions: PredictionPacketBudgetProbe.MaximumInlineActionResolutions,
        unresolvedOwnerReservationBytes: PredictionPacketBudgetProbe.MaximumUnresolvedOwnerReservationBytes,
        unresolvedCheckpointReservationBytes: PredictionPacketBudgetProbe.MaximumUnresolvedCheckpointReservationBytes);

    public PredictionPacketBudgetLoad(
        string name,
        bool worstCase,
        int ownerMovementSources,
        int totalWorldMovementSources,
        int worldInlineMovementSources,
        int contactFacts,
        int acknowledgementGapRanges,
        int inputDispositions,
        int transitionResolutions,
        int actionResolutions,
        int unresolvedOwnerReservationBytes,
        int unresolvedCheckpointReservationBytes)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A packet-budget load needs a name.", nameof(name));
        }

        OwnerMovementSources = Count(ownerMovementSources, 16, nameof(ownerMovementSources));
        TotalWorldMovementSources = Count(totalWorldMovementSources, 16, nameof(totalWorldMovementSources));
        WorldInlineMovementSources = Count(worldInlineMovementSources, 4, nameof(worldInlineMovementSources));
        if (worldInlineMovementSources > totalWorldMovementSources)
        {
            throw new ArgumentException(
                "Inline world sources cannot exceed total active world sources.",
                nameof(worldInlineMovementSources));
        }

        ContactFacts = Count(contactFacts, 4, nameof(contactFacts));
        AcknowledgementGapRanges = Count(acknowledgementGapRanges, 2, nameof(acknowledgementGapRanges));
        InputDispositions = Count(inputDispositions, 4, nameof(inputDispositions));
        TransitionResolutions = Count(transitionResolutions, 1, nameof(transitionResolutions));
        ActionResolutions = Count(actionResolutions, 1, nameof(actionResolutions));
        UnresolvedOwnerReservationBytes = Count(
            unresolvedOwnerReservationBytes,
            PredictionPacketBudgetProbe.MaximumUnresolvedOwnerReservationBytes,
            nameof(unresolvedOwnerReservationBytes));
        UnresolvedCheckpointReservationBytes = Count(
            unresolvedCheckpointReservationBytes,
            PredictionPacketBudgetProbe.MaximumUnresolvedCheckpointReservationBytes,
            nameof(unresolvedCheckpointReservationBytes));
        Name = name;
        WorstCase = worstCase;
    }

    public string Name { get; }
    public bool WorstCase { get; }
    public int OwnerMovementSources { get; }
    public int TotalWorldMovementSources { get; }
    public int WorldInlineMovementSources { get; }
    public int ContactFacts { get; }
    public int AcknowledgementGapRanges { get; }
    public int InputDispositions { get; }
    public int TransitionResolutions { get; }
    public int ActionResolutions { get; }
    public int UnresolvedOwnerReservationBytes { get; }
    public int UnresolvedCheckpointReservationBytes { get; }

    internal ulong UInt64Value(int common) => WorstCase ? ulong.MaxValue : checked((ulong)common);
    internal ulong UInt32Value(int common) => WorstCase ? uint.MaxValue : checked((uint)common);
    internal ushort UInt16Value(int common) => WorstCase ? ushort.MaxValue : checked((ushort)common);
    internal byte ByteValue(int common) => WorstCase ? byte.MaxValue : checked((byte)common);
    internal int SignedValue(int common) => WorstCase
        ? common < 0 ? int.MinValue : int.MaxValue
        : common;
    internal long Signed64Value(long common) => WorstCase
        ? common < 0 ? long.MinValue : long.MaxValue
        : common;
    internal short Signed16Value(int common) => WorstCase
        ? common < 0 ? short.MinValue : short.MaxValue
        : checked((short)common);
    internal ulong CombatantId(int ordinal) => WorstCase
        ? ulong.MaxValue - checked((ulong)ordinal)
        : checked((ulong)(101 + ordinal));
    internal ulong LifeId(int ordinal) => WorstCase
        ? ulong.MaxValue - 100UL - checked((ulong)ordinal)
        : checked((ulong)(501 + ordinal));
    internal ulong ColliderId(int ordinal) => WorstCase
        ? ulong.MaxValue - 200UL - checked((ulong)ordinal)
        : checked((ulong)(1_001 + ordinal));
    internal uint SourceInstanceId(int combatantOrdinal, int sourceIndex) => WorstCase
        ? 0x00ff_ffffU - checked((uint)(combatantOrdinal * 16 + sourceIndex))
        : checked((uint)(combatantOrdinal * 16 + sourceIndex + 1));
    internal uint SourceCorrelationId(int combatantOrdinal, int sourceIndex) => WorstCase
        ? 0x00ff_ffffU - 256U - checked((uint)(combatantOrdinal * 16 + sourceIndex))
        : checked((uint)(1_001 + combatantOrdinal * 16 + sourceIndex));

    private static int Count(int value, int maximum, string parameterName)
    {
        if (value < 0 || value > maximum)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"The candidate bound is zero through {maximum}.");
        }

        return value;
    }
}

public sealed record PredictionTransportOverhead
{
    public static PredictionTransportOverhead EnetIpv6Unreliable { get; } = new(
        "ENet/IPv6 estimate",
        authenticationBytes: 0,
        transportFramingBytes: 16,
        ipUdpBytes: 48,
        PredictionTransportOverheadBasis.ConservativeEngineeringEstimate,
        "ENet 1.3.x framing reserve plus exact IPv6-without-extensions/UDP headers; " +
        "replace with project packet captures before protocol freeze.");

    public static PredictionTransportOverhead SteamIpv6Unreliable { get; } = new(
        "Steam Datagram Relay/IPv6 estimate",
        authenticationBytes: 16,
        transportFramingBytes: 48,
        ipUdpBytes: 48,
        PredictionTransportOverheadBasis.ConservativeEngineeringEstimate,
        "Steamworks SDK 1.62 encrypted-datagram engineering reserve plus exact " +
        "IPv6-without-extensions/UDP headers; opaque SDR framing requires project " +
        "packet-capture calibration before protocol freeze.");

    public PredictionTransportOverhead(
        string name,
        int authenticationBytes,
        int transportFramingBytes,
        int ipUdpBytes,
        PredictionTransportOverheadBasis basis,
        string provenance)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A transport-overhead profile needs a name.", nameof(name));
        }

        if (!Enum.IsDefined(basis))
        {
            throw new ArgumentOutOfRangeException(nameof(basis));
        }

        if (string.IsNullOrWhiteSpace(provenance))
        {
            throw new ArgumentException("Overhead provenance is required.", nameof(provenance));
        }

        AuthenticationBytes = NonNegative(authenticationBytes, nameof(authenticationBytes));
        TransportFramingBytes = NonNegative(transportFramingBytes, nameof(transportFramingBytes));
        IpUdpBytes = NonNegative(ipUdpBytes, nameof(ipUdpBytes));
        Name = name;
        Basis = basis;
        Provenance = provenance;
    }

    public string Name { get; }
    public int AuthenticationBytes { get; }
    public int TransportFramingBytes { get; }
    public int IpUdpBytes { get; }
    public PredictionTransportOverheadBasis Basis { get; }
    public string Provenance { get; }

    private static int NonNegative(int value, string parameterName) => value < 0
        ? throw new ArgumentOutOfRangeException(parameterName)
        : value;
}

public sealed class PredictionPacketMeasurement
{
    private readonly byte[] _encodedEnvelope;

    internal PredictionPacketMeasurement(
        PredictionPacketProduct product,
        int envelopePayloadFieldNumber,
        int partIndex,
        int partCount,
        int firstCombatantIndex,
        int combatantCount,
        int protobufPayloadBytes,
        int envelopeBytes,
        int authenticationBytes,
        int transportFramingBytes,
        int ipUdpBytes,
        int accountedDatagramBytes,
        bool fits,
        byte[] encodedEnvelope)
    {
        Product = product;
        EnvelopePayloadFieldNumber = envelopePayloadFieldNumber;
        PartIndex = partIndex;
        PartCount = partCount;
        FirstCombatantIndex = firstCombatantIndex;
        CombatantCount = combatantCount;
        ProtobufPayloadBytes = protobufPayloadBytes;
        EnvelopeBytes = envelopeBytes;
        AuthenticationBytes = authenticationBytes;
        TransportFramingBytes = transportFramingBytes;
        IpUdpBytes = ipUdpBytes;
        AccountedDatagramBytes = accountedDatagramBytes;
        Fits = fits;
        _encodedEnvelope = (byte[])encodedEnvelope.Clone();
    }

    public PredictionPacketProduct Product { get; }
    public int EnvelopePayloadFieldNumber { get; }
    public int PartIndex { get; }
    public int PartCount { get; }
    public int FirstCombatantIndex { get; }
    public int CombatantCount { get; }
    public int ProtobufPayloadBytes { get; }
    public int EnvelopeBytes { get; }
    public int AuthenticationBytes { get; }
    public int TransportFramingBytes { get; }
    public int IpUdpBytes { get; }
    public int AccountedDatagramBytes { get; }
    public bool Fits { get; }
    public ReadOnlyMemory<byte> EncodedEnvelope => _encodedEnvelope;
}

public sealed record PredictionPacketProductBudget
{
    public PredictionPacketProductBudget(
        PredictionPacketProduct product,
        int expectedCombatantCount,
        IReadOnlyList<PredictionPacketMeasurement> packets)
    {
        ArgumentNullException.ThrowIfNull(packets);
        if (expectedCombatantCount <= 0 || packets.Count == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedCombatantCount));
        }

        var expectedFirst = 0;
        for (var index = 0; index < packets.Count; index++)
        {
            var packet = packets[index];
            if (packet.Product != product ||
                packet.PartIndex != index ||
                packet.PartCount != packets.Count ||
                packet.FirstCombatantIndex != expectedFirst ||
                packet.CombatantCount <= 0)
            {
                throw new ArgumentException(
                    "Product parts must be ordered, uniquely numbered, and cover " +
                    "one contiguous combatant range exactly once.",
                    nameof(packets));
            }

            expectedFirst = checked(expectedFirst + packet.CombatantCount);
        }

        if (expectedFirst != expectedCombatantCount)
        {
            throw new ArgumentException(
                "Product parts do not cover the expected combatant count exactly.",
                nameof(packets));
        }

        Product = product;
        ExpectedCombatantCount = expectedCombatantCount;
        Packets = packets.ToArray();
    }

    public PredictionPacketProduct Product { get; }
    public int ExpectedCombatantCount { get; }
    public IReadOnlyList<PredictionPacketMeasurement> Packets { get; }
    public int PacketCount => Packets.Count;
    public int MaximumAccountedBytes => Packets.Max(packet => packet.AccountedDatagramBytes);
    public int TotalAccountedBytes => checked(Packets.Sum(packet => packet.AccountedDatagramBytes));
}

public sealed record PredictionPacketProductTraffic(
    PredictionPacketProduct Product,
    int PacketsPerSecondPerRecipient,
    int BytesPerSecondPerRecipient,
    int AuthorityPacketsPerSecond,
    int AuthorityBytesPerSecond);

public sealed record PredictionPacketBudgetReport(
    int CombatantCount,
    int NetworkRecipientCount,
    PredictionAuthorityTopology Topology,
    string LoadName,
    string TransportName,
    PredictionTransportOverheadBasis TransportOverheadBasis,
    string TransportOverheadProvenance,
    int AccountedDatagramCeilingBytes,
    PredictionPacketProductBudget Owner,
    PredictionPacketProductBudget CollisionWorld,
    PredictionPacketProductBudget Checkpoint,
    PredictionPacketProductTraffic OwnerTraffic,
    PredictionPacketProductTraffic CollisionWorldTraffic,
    PredictionPacketProductTraffic CheckpointTraffic,
    int PerRecipientBytesPerSecond,
    int PerRecipientPacketsPerSecond,
    int AuthorityFanoutBytesPerSecond,
    int AuthorityFanoutPacketsPerSecond);

public sealed class PredictionPacketBudgetExceededException : InvalidOperationException
{
    private PredictionPacketBudgetExceededException(string message) : base(message)
    {
    }

    internal static PredictionPacketBudgetExceededException ForIndivisible(
        PredictionPacketProduct product,
        int bytes,
        int ceiling) => new(
            $"Indivisible {product} candidate requires {bytes} accounted bytes, " +
            $"exceeding the configured {ceiling}-byte datagram ceiling. Revise the " +
            "schema; transport fragmentation is not an accepted fallback.");

    internal static PredictionPacketBudgetExceededException ForCombatantRecord(
        PredictionPacketProduct product,
        int combatantIndex,
        int bytes,
        int ceiling) => new(
            $"One {product} combatant record at index {combatantIndex} requires " +
            $"{bytes} accounted bytes, exceeding the configured {ceiling}-byte " +
            "datagram ceiling. Revise the record; it cannot be split opaquely.");
}
