using BattleArena.Multiplayer.Connection;
using BattleArena.Multiplayer.Prediction;
using BattleArena.Multiplayer.Protocol;
using BattleArena.Multiplayer.Transport;
using BattleArena.Protocol.V1;
using Google.Protobuf;

namespace BattleArena.Multiplayer.Tests.Prediction;

public sealed class AuthorityPredictionControlServiceTests
{
    [Fact]
    public void ClientTickCannotControlExpiryOrOverflowAuthorityRouteCreation()
    {
        var transport = new FakeTransport();
        var codec = new ProtobufProtocolCodec();
        using var connections = new AuthorityConnectionService(
            transport,
            codec,
            new InboundMessageValidator(),
            new Credentials(),
            new AuthoritySessionConfiguration(60, 30, 60));
        var broker = new AuthorityPredictionRouteBroker(
            new PredictionTestCredentialGenerator(),
            AuthorityPredictionRouteBrokerTests.Peer(1, authority: true),
            credentialLifetimeTicks: 3_600);
        using var control = new AuthorityPredictionControlService(
            transport,
            codec,
            new InboundMessageValidator(),
            connections,
            broker,
            new TransportKindPredictionRouteAdvertisementVerifier(
                PredictionTransportKind.Enet));
        control.AdvanceAuthorityTick(100);
        var first = new TransportConnectionId(21);
        var second = new TransportConnectionId(22);
        transport.Receive(first, Join("First"));
        transport.Receive(second, Join("Second"));
        transport.Sent.Clear();

        transport.Receive(first, Advertisement(connections.SessionId, 7782, ulong.MaxValue));
        var exception = Record.Exception(() =>
            transport.Receive(second, Advertisement(connections.SessionId, 7783, ulong.MaxValue)));

        Assert.Null(exception);
        var authorizations = transport.Sent
            .Select(packet => codec.Decode(packet.Payload.Span))
            .Where(result => result.IsSuccess && result.Envelope!.PayloadCase ==
                PacketEnvelope.PayloadOneofCase.PredictionRouteAuthorization)
            .Select(result => result.Envelope!.PredictionRouteAuthorization)
            .ToArray();
        Assert.Equal(2, authorizations.Length);
        Assert.All(authorizations, authorization =>
            Assert.Equal(3_700UL, authorization.ExpiresAuthorityTick));
    }

    private static OutboundTransportPacket Join(string name)
    {
        var envelope = new PacketEnvelope
        {
            ProtocolVersion = ProtocolConstants.CurrentVersion,
            Sequence = 1,
            JoinRequest = new JoinRequest
            {
                ClientProtocolVersion = ProtocolConstants.CurrentVersion,
                DisplayName = name,
                ClientNonce = ByteString.CopyFrom(
                    Enumerable.Repeat((byte)0x2a, ProtocolConstants.ClientNonceBytes).ToArray()),
            },
        };
        return Packet(envelope);
    }

    private static OutboundTransportPacket Advertisement(
        ulong sessionId,
        int port,
        ulong untrustedTick)
    {
        var envelope = new PacketEnvelope
        {
            ProtocolVersion = ProtocolConstants.CurrentVersion,
            SessionId = sessionId,
            Sequence = 2,
            SimulationTick = untrustedTick,
            PredictionRouteAdvertisement = new PredictionRouteAdvertisement
            {
                RouteDescriptor = EnetPredictionRouteDescriptorCodec.Encode(
                    new EnetPredictionEndpoint("127.0.0.1", checked((ushort)port))),
            },
        };
        return Packet(envelope);
    }

    private static OutboundTransportPacket Packet(PacketEnvelope envelope) => new(
        new TransportConnectionId(1),
        TransportChannel.Connection,
        TransportDelivery.ReliableOrdered,
        new ProtobufProtocolCodec().Encode(envelope));

    private sealed class FakeTransport : INetworkTransport
    {
        public event Action<InboundTransportPacket>? PacketReceived;
        public event Action<TransportConnectionId>? ConnectionOpened;
        public event Action<TransportConnectionId>? ConnectionClosed;
        public TransportKind Kind => TransportKind.Test;
        public List<OutboundTransportPacket> Sent { get; } = [];
        public void Send(OutboundTransportPacket packet) => Sent.Add(packet);
        public void Receive(TransportConnectionId sender, OutboundTransportPacket packet) =>
            PacketReceived?.Invoke(new InboundTransportPacket(sender, packet.Channel, packet.Payload));
        public void Connect(TransportConnectionId connection) => ConnectionOpened?.Invoke(connection);
        public void Disconnect(TransportConnectionId connection) => ConnectionClosed?.Invoke(connection);
    }

    private sealed class Credentials : ISessionCredentialGenerator
    {
        public ulong CreateSessionId() => 73;
        public byte[] CreateReconnectToken() =>
            Enumerable.Repeat((byte)0x44, ProtocolConstants.ReconnectTokenBytes).ToArray();
        public byte[] CreateClientNonce() =>
            Enumerable.Repeat((byte)0x55, ProtocolConstants.ClientNonceBytes).ToArray();
    }
}
