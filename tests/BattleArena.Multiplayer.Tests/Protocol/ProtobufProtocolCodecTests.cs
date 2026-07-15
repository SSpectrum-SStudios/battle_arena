using BattleArena.Multiplayer.Protocol;
using BattleArena.Protocol.V1;

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
            ClientInputBatch = new ClientInputBatch(),
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
        });

        var result = codec.Decode(codec.Encode(envelope));

        Assert.True(result.IsSuccess);
        var decoded = Assert.IsType<PacketEnvelope>(result.Envelope);
        Assert.Equal(PacketEnvelope.PayloadOneofCase.ClientInputBatch, decoded.PayloadCase);
        Assert.Equal(41UL, decoded.ClientInputBatch.Frames[0].InputSequence);
        Assert.Equal(-0.75f, decoded.ClientInputBatch.Frames[0].MoveZ);
        Assert.Equal(5U, decoded.ClientInputBatch.Frames[0].ButtonBits);
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
            CurrentHealth = 80,
            MaximumHealth = 100,
            RemainingLives = 1,
            LifeState = ReplicatedLifeState.Alive,
            LastProcessedInputSequence = 41,
            IsGrounded = true,
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
        Assert.Equal("base:greater_poison", decoded.AuthorityCheckpoint.ActiveEffects[0].EffectDefinitionId);
        Assert.Equal(850UL, decoded.AuthorityCheckpoint.ActiveEffects[0].ExpiresTick);
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
}
