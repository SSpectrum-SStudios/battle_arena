using BattleArena.Multiplayer.Protocol;
using BattleArena.Protocol.V1;
using Google.Protobuf;

namespace BattleArena.Multiplayer.Tests.Protocol;

public sealed class PredictionProtocolCodecTests
{
    private readonly ProtobufPredictionProtocolCodec codec = new();

    [Fact]
    public void MovementBundleRoundTripsCommandsTimingAndRollbackState()
    {
        var envelope = PredictionProtocolTestData.MovementEnvelope();

        var result = codec.Decode(codec.Encode(envelope));

        Assert.True(result.IsSuccess);
        var decoded = Assert.IsType<PredictionPacketEnvelope>(result.Envelope);
        Assert.Equal(
            PredictionPacketEnvelope.PayloadOneofCase.MovementPredictionBundle,
            decoded.PayloadCase);
        Assert.Equal(202UL, decoded.EstimatedAuthorityTick);
        Assert.Equal(3, decoded.MovementPredictionBundle.Commands.Count);
        Assert.Equal(12UL, decoded.MovementPredictionBundle.Commands[^1].InputSequence);
        Assert.Equal(
            ReplicatedJumpPhase.Rising,
            decoded.MovementPredictionBundle.RollbackState.JumpPhase);
        Assert.Equal(3UL, decoded.MovementPredictionBundle.RollbackState.Position.Z);
    }

    [Fact]
    public void MeshProofRoundTripsButDiagnosticFormattingRedactsIt()
    {
        var envelope = PredictionProtocolTestData.HelloEnvelope();
        envelope.MeshProof = new PredictionMeshProof
        {
            ChallengePacketSequence = 10,
            InitiatorProof = Google.Protobuf.ByteString.CopyFrom(
                Enumerable.Repeat(
                    (byte)0x5a,
                    ProtocolConstants.PredictionRouteCredentialBytes).ToArray()),
        };

        var decoded = codec.Decode(codec.Encode(envelope));
        var formatted = new ProtobufPredictionDiagnosticFormatter().Format(envelope);

        Assert.True(decoded.IsSuccess);
        Assert.Equal(
            ProtocolConstants.PredictionRouteCredentialBytes,
            decoded.Envelope!.MeshProof.InitiatorProof.Length);
        Assert.DoesNotContain(
            Convert.ToBase64String(envelope.MeshProof.InitiatorProof.ToByteArray()),
            formatted,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyOversizedAndMalformedPredictionPacketsAreRejected()
    {
        var empty = codec.Decode(ReadOnlySpan<byte>.Empty);
        var oversized = codec.Decode(new byte[ProtocolConstants.MaxPredictionControlPacketBytes + 1]);
        var malformed = codec.Decode(new byte[] { 0xff, 0xff, 0xff });

        Assert.Equal(ProtocolViolationCode.EmptyPacket, empty.Violation?.Code);
        Assert.Equal(ProtocolViolationCode.PacketTooLarge, oversized.Violation?.Code);
        Assert.Equal(ProtocolViolationCode.MalformedPayload, malformed.Violation?.Code);
    }

    [Fact]
    public void LargestLegitimateMovementBundleFitsUnreliableDatagramBudget()
    {
        var envelope = PredictionProtocolTestData.MovementEnvelope();
        envelope.MovementPredictionBundle.RollbackState.Traversal = new ReplicatedTraversalState
        {
            TraversalInstanceId = ulong.MaxValue,
            Phase = ReplicatedTraversalPhase.Mantling,
            LedgeAnchor = new Vector3Value { X = float.MaxValue, Y = float.MaxValue, Z = float.MaxValue },
            LedgeNormal = new Vector3Value { X = 1, Y = 1, Z = 1 },
            PhaseElapsedTicks = ulong.MaxValue,
            PhaseDurationTicks = ulong.MaxValue,
        };

        var encoded = codec.Encode(envelope);

        Assert.True(encoded.Length <= ProtocolConstants.MaxPredictionUnreliablePacketBytes);
        Assert.True(codec.Decode(encoded).IsSuccess);
    }

    [Fact]
    public void OversizedUnreliablePayloadCannotUseLargerControlBudget()
    {
        var envelope = PredictionProtocolTestData.MovementEnvelope();
        while (envelope.CalculateSize() <= ProtocolConstants.MaxPredictionUnreliablePacketBytes)
        {
            envelope.MovementPredictionBundle.Commands.Add(
                envelope.MovementPredictionBundle.Commands[^1].Clone());
        }

        var result = codec.Decode(envelope.ToByteArray());

        Assert.Equal(ProtocolViolationCode.PacketTooLarge, result.Violation?.Code);
    }
}
