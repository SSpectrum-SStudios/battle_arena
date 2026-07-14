using BattleArena.Multiplayer.Protocol;
using BattleArena.Multiplayer.Transport;
using BattleArena.Protocol.V1;
using Google.Protobuf;

namespace BattleArena.Multiplayer.Connection;

public sealed class ClientConnectionService : IDisposable
{
    private readonly INetworkTransport _transport;
    private readonly IProtocolCodec _codec;
    private readonly InboundMessageValidator _validator;
    private readonly ISessionCredentialGenerator _credentialGenerator;
    private NetworkPeerId? _authorityPeer;
    private bool _joinSent;
    private bool _disposed;

    public ClientConnectionService(
        INetworkTransport transport,
        IProtocolCodec codec,
        InboundMessageValidator validator,
        ISessionCredentialGenerator credentialGenerator)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _credentialGenerator = credentialGenerator ?? throw new ArgumentNullException(nameof(credentialGenerator));

        _transport.PacketReceived += OnPacketReceived;
        _transport.PeerDisconnected += OnPeerDisconnected;
    }

    public event Action<ClientSessionIdentity>? JoinAccepted;

    public event Action<ProtocolViolation>? ProtocolViolationDetected;

    public event Action? AuthorityTransportDisconnected;

    public ClientSessionIdentity? Identity { get; private set; }

    public void BeginJoin(NetworkPeerId authorityPeer, string displayName)
    {
        if (_joinSent)
        {
            throw new InvalidOperationException("A join request has already been sent.");
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Display name is required.", nameof(displayName));
        }

        _authorityPeer = authorityPeer;
        _joinSent = true;
        var request = new PacketEnvelope
        {
            ProtocolVersion = ProtocolConstants.CurrentVersion,
            Sequence = 1,
            JoinRequest = new JoinRequest
            {
                ClientProtocolVersion = ProtocolConstants.CurrentVersion,
                DisplayName = displayName.Trim(),
                ClientNonce = ByteString.CopyFrom(_credentialGenerator.CreateClientNonce()),
            },
        };

        _transport.Send(new OutboundTransportPacket(
            authorityPeer,
            TransportChannel.Connection,
            TransportDelivery.ReliableOrdered,
            _codec.Encode(request)));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _transport.PacketReceived -= OnPacketReceived;
        _transport.PeerDisconnected -= OnPeerDisconnected;
        _disposed = true;
    }

    private void OnPacketReceived(InboundTransportPacket packet)
    {
        if (packet.Channel != TransportChannel.Connection || packet.Sender != _authorityPeer)
        {
            return;
        }

        var decoded = _codec.Decode(packet.Payload.Span);
        if (!decoded.IsSuccess)
        {
            ProtocolViolationDetected?.Invoke(decoded.Violation!);
            return;
        }

        var context = new ProtocolValidationContext(
            RemoteEndpointRole.Authority,
            Identity?.SessionId,
            Identity is not null);
        var validation = _validator.Validate(decoded.Envelope!, context);
        if (!validation.IsValid)
        {
            ProtocolViolationDetected?.Invoke(validation.Violation!);
            return;
        }

        if (decoded.Envelope!.PayloadCase != PacketEnvelope.PayloadOneofCase.JoinAccepted)
        {
            return;
        }

        var accepted = decoded.Envelope.JoinAccepted;
        Identity = new ClientSessionIdentity(
            accepted.SessionId,
            accepted.PlayerId,
            accepted.CombatantId,
            accepted.ReconnectToken.ToByteArray(),
            accepted.SimulationTicksPerSecond,
            accepted.SnapshotRate,
            accepted.CheckpointIntervalTicks);
        JoinAccepted?.Invoke(Identity);
    }

    private void OnPeerDisconnected(NetworkPeerId peerId)
    {
        if (peerId == _authorityPeer)
        {
            AuthorityTransportDisconnected?.Invoke();
        }
    }
}
