using BattleArena.Multiplayer.Connection;
using BattleArena.Multiplayer.Protocol;
using BattleArena.Multiplayer.Transport;
using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Tests.Connection;

public sealed class ConnectionHandshakeTests
{
    private static readonly NetworkPeerId AuthorityPeer = new(1);
    private static readonly NetworkPeerId ClientPeer = new(42);

    [Fact]
    public void AuthorityAssignsIdentityAndClientAcceptsSession()
    {
        var authorityTransport = new FakeTransport();
        var clientTransport = new FakeTransport();
        var codec = new ProtobufProtocolCodec();
        var validator = new InboundMessageValidator();
        var credentials = new DeterministicCredentialGenerator();
        using var authority = new AuthorityConnectionService(
            authorityTransport,
            codec,
            validator,
            credentials,
            new AuthoritySessionConfiguration(60, 30, 60));
        using var client = new ClientConnectionService(clientTransport, codec, validator, credentials);

        ConnectedPlayer? joinedPlayer = null;
        ClientSessionIdentity? clientIdentity = null;
        authority.PlayerJoined += player => joinedPlayer = player;
        client.JoinAccepted += identity => clientIdentity = identity;

        client.BeginJoin(AuthorityPeer, "Remote Player");
        var joinPacket = Assert.Single(clientTransport.SentPackets);
        authorityTransport.Receive(ClientPeer, joinPacket);

        var acceptancePacket = Assert.Single(authorityTransport.SentPackets);
        clientTransport.Receive(AuthorityPeer, acceptancePacket);

        Assert.NotNull(joinedPlayer);
        Assert.NotNull(clientIdentity);
        Assert.Equal(2UL, joinedPlayer.PlayerId);
        Assert.Equal(2UL, joinedPlayer.CombatantId);
        Assert.Equal("Remote Player", joinedPlayer.DisplayName);
        Assert.Equal(authority.SessionId, clientIdentity.SessionId);
        Assert.Equal(joinedPlayer.PlayerId, clientIdentity.PlayerId);
        Assert.Equal(joinedPlayer.CombatantId, clientIdentity.CombatantId);
        Assert.Equal(TransportDelivery.ReliableOrdered, acceptancePacket.Delivery);
        Assert.Equal(TransportChannel.Connection, acceptancePacket.Channel);
    }

    [Fact]
    public void ClientCannotJoinBySendingAuthorityMessage()
    {
        var transport = new FakeTransport();
        var codec = new ProtobufProtocolCodec();
        using var authority = new AuthorityConnectionService(
            transport,
            codec,
            new InboundMessageValidator(),
            new DeterministicCredentialGenerator(),
            new AuthoritySessionConfiguration(60, 30, 60));
        ProtocolViolation? violation = null;
        authority.ProtocolViolationDetected += (_, detected) => violation = detected;
        var forged = new PacketEnvelope
        {
            ProtocolVersion = ProtocolConstants.CurrentVersion,
            SessionId = authority.SessionId,
            Sequence = 1,
            AuthoritySnapshot = new AuthoritySnapshot(),
        };

        transport.Receive(
            ClientPeer,
            new OutboundTransportPacket(
                AuthorityPeer,
                TransportChannel.Connection,
                TransportDelivery.ReliableOrdered,
                codec.Encode(forged)));

        Assert.NotNull(violation);
        Assert.Equal(ProtocolViolationCode.UnexpectedMessageDirection, violation.Code);
        Assert.Empty(authority.ConnectedPlayers);
        Assert.Empty(transport.SentPackets);
    }

    private sealed class FakeTransport : INetworkTransport
    {
        public event Action<InboundTransportPacket>? PacketReceived;

        public event Action<NetworkPeerId>? PeerConnected;

        public event Action<NetworkPeerId>? PeerDisconnected;

        public List<OutboundTransportPacket> SentPackets { get; } = [];

        public void Send(OutboundTransportPacket packet) => SentPackets.Add(packet);

        public void Receive(NetworkPeerId sender, OutboundTransportPacket packet) =>
            PacketReceived?.Invoke(new InboundTransportPacket(sender, packet.Channel, packet.Payload));

        public void Connect(NetworkPeerId peerId) => PeerConnected?.Invoke(peerId);

        public void Disconnect(NetworkPeerId peerId) => PeerDisconnected?.Invoke(peerId);
    }

    private sealed class DeterministicCredentialGenerator : ISessionCredentialGenerator
    {
        public ulong CreateSessionId() => 9001;

        public byte[] CreateReconnectToken() =>
            Enumerable.Repeat((byte)0x5a, ProtocolConstants.ReconnectTokenBytes).ToArray();

        public byte[] CreateClientNonce() =>
            Enumerable.Repeat((byte)0x3c, ProtocolConstants.ClientNonceBytes).ToArray();
    }
}
