using BattleArena.Multiplayer.Connection;
using BattleArena.Multiplayer.Protocol;
using BattleArena.Multiplayer.Transport;
using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Tests.Connection;

public sealed class ConnectionHandshakeTests
{
    private static readonly TransportConnectionId AuthorityConnection = new(1);
    private static readonly TransportConnectionId ClientConnection = new(42);

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

        client.BeginJoin(AuthorityConnection, "Remote Player");
        var joinPacket = Assert.Single(clientTransport.SentPackets);
        authorityTransport.Receive(ClientConnection, joinPacket);

        var acceptancePacket = Assert.Single(authorityTransport.SentPackets);
        clientTransport.Receive(AuthorityConnection, acceptancePacket);

        Assert.NotNull(joinedPlayer);
        Assert.NotNull(clientIdentity);
        Assert.Equal(2UL, joinedPlayer.PlayerId);
        Assert.Equal(2UL, joinedPlayer.CombatantId);
        Assert.Equal(new SessionPeerId(2), joinedPlayer.SessionPeerId);
        Assert.Equal(ConnectionGeneration.Initial, joinedPlayer.ConnectionGeneration);
        Assert.Equal(ClientConnection, joinedPlayer.ConnectionId);
        Assert.Equal("Remote Player", joinedPlayer.DisplayName);
        Assert.Equal(authority.SessionId, clientIdentity.SessionId);
        Assert.Equal(joinedPlayer.PlayerId, clientIdentity.PlayerId);
        Assert.Equal(joinedPlayer.CombatantId, clientIdentity.CombatantId);
        Assert.Equal(joinedPlayer.SessionPeerId, clientIdentity.SessionPeerId);
        Assert.Equal(joinedPlayer.ConnectionGeneration, clientIdentity.ConnectionGeneration);
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
            ClientConnection,
            new OutboundTransportPacket(
                AuthorityConnection,
                TransportChannel.Connection,
                TransportDelivery.ReliableOrdered,
                codec.Encode(forged)));

        Assert.NotNull(violation);
        Assert.Equal(ProtocolViolationCode.UnexpectedMessageDirection, violation.Code);
        Assert.Empty(authority.ConnectedPlayers);
        Assert.Empty(transport.SentPackets);
    }

    [Fact]
    public void AuthorityStartsMatchForAuthenticatedClient()
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
        MatchStart? receivedStart = null;
        client.MatchStarted += start => receivedStart = start;

        client.BeginJoin(AuthorityConnection, "Remote Player");
        authorityTransport.Receive(ClientConnection, Assert.Single(clientTransport.SentPackets));
        clientTransport.Receive(AuthorityConnection, Assert.Single(authorityTransport.SentPackets));
        authorityTransport.SentPackets.Clear();

        authority.StartMatch("base:vertical_slice_arena", 1234, 90);
        clientTransport.Receive(AuthorityConnection, Assert.Single(authorityTransport.SentPackets));

        Assert.NotNull(receivedStart);
        Assert.Equal("base:vertical_slice_arena", receivedStart.ArenaDefinitionId);
        Assert.Equal(1234UL, receivedStart.MatchSeed);
        Assert.Equal(90UL, receivedStart.AuthorityStartTick);
    }

    private sealed class FakeTransport : INetworkTransport
    {
        public event Action<InboundTransportPacket>? PacketReceived;

        public event Action<TransportConnectionId>? ConnectionOpened;

        public event Action<TransportConnectionId>? ConnectionClosed;

        public TransportKind Kind => TransportKind.Test;

        public List<OutboundTransportPacket> SentPackets { get; } = [];

        public void Send(OutboundTransportPacket packet) => SentPackets.Add(packet);

        public void Receive(TransportConnectionId sender, OutboundTransportPacket packet) =>
            PacketReceived?.Invoke(new InboundTransportPacket(sender, packet.Channel, packet.Payload));

        public void Connect(TransportConnectionId connectionId) => ConnectionOpened?.Invoke(connectionId);

        public void Disconnect(TransportConnectionId connectionId) => ConnectionClosed?.Invoke(connectionId);
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
