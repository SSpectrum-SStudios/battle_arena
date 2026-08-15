using BattleArena.Multiplayer.Protocol;
using BattleArena.Protocol.V1;
using Google.Protobuf;

namespace BattleArena.Multiplayer.Tests.Protocol;

internal static class PredictionProtocolTestData
{
    public const ulong SessionId = 73;
    public const ulong LocalPeerId = 3;
    public const ulong RemotePeerId = 2;
    public const uint LocalPeerGeneration = 6;
    public const uint RemotePeerGeneration = 4;
    public const uint RouteGeneration = 5;

    public static PredictionProtocolValidationContext Context(bool authenticated = true) =>
        new(
            SessionId,
            LocalPeerId,
            RemotePeerId,
            LocalPeerGeneration,
            RemotePeerGeneration,
            RouteGeneration,
            10_000,
            200,
            authenticated);

    public static PredictionPacketEnvelope MovementEnvelope()
    {
        var envelope = BaseEnvelope();
        envelope.ClientTick = 102;
        envelope.EstimatedAuthorityTick = 202;
        envelope.MovementPredictionBundle = MovementBundle();
        return envelope;
    }

    public static PredictionPacketEnvelope HelloEnvelope(int nonceBytes = 32)
    {
        var envelope = BaseEnvelope();
        envelope.MeshHello = new PredictionMeshHello
        {
            InitiatorNonce = ByteString.CopyFrom(
                Enumerable.Repeat((byte)0x11, nonceBytes).ToArray()),
        };
        return envelope;
    }

    public static MovementPredictionBundle MovementBundle()
    {
        var bundle = new MovementPredictionBundle
        {
            BundleSequence = 10,
            SourceCombatantId = 2,
            SourceLifeId = 7,
            RollbackState = new PredictedMovementState
            {
                ClientTick = 102,
                EstimatedAuthorityTick = 202,
                LastIncludedInputSequence = 12,
                MovementProfileRevision = 3,
                MovementCapabilityRevision = 4,
                Position = new Vector3Value { X = 1, Y = 2, Z = 3 },
                Velocity = new Vector3Value { X = 4, Y = 5, Z = 6 },
                ViewYawRadians = 1.2f,
                ViewPitchRadians = -0.2f,
                BodyFacingYawRadians = 1.1f,
                IsGrounded = false,
                LocomotionMode = ReplicatedLocomotionMode.Airborne,
                PostureMode = ReplicatedPostureMode.Standing,
                MovementActionMode = ReplicatedMovementActionMode.Ready,
                MovementModeElapsedTicks = 2,
                JumpPhase = ReplicatedJumpPhase.Rising,
                TicksSinceGrounded = 4,
                RollDirection = new Vector3Value { Z = -1 },
            },
        };
        for (ulong sequence = 10; sequence <= 12; sequence++)
        {
            bundle.Commands.Add(new ClientInputFrame
            {
                InputSequence = sequence,
                ClientTick = 90 + sequence,
                EstimatedAuthorityTick = 190 + sequence,
                MovementProfileRevision = 3,
                MovementCapabilityRevision = 4,
                MoveZ = -1,
                ViewYawRadians = 1.2f,
                ViewPitchRadians = -0.2f,
                ButtonBits = 2,
            });
        }

        return bundle;
    }

    public static PredictionRouteDescriptor RouteDescriptor() => new()
    {
        DescriptorVersion = 1,
        TransportKind = PredictionTransportKind.Enet,
        Payload = ByteString.CopyFromUtf8("127.0.0.1:7781"),
    };

    private static PredictionPacketEnvelope BaseEnvelope() => new()
    {
        ProtocolVersion = ProtocolConstants.CurrentVersion,
        SessionId = SessionId,
        SourceSessionPeerId = RemotePeerId,
        DestinationSessionPeerId = LocalPeerId,
        SourcePeerSessionGeneration = RemotePeerGeneration,
        DestinationPeerSessionGeneration = LocalPeerGeneration,
        PredictionRouteGeneration = RouteGeneration,
        PacketSequence = 11,
    };
}
