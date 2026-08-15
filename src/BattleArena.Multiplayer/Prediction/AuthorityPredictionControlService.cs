using BattleArena.Multiplayer.Connection;
using BattleArena.Multiplayer.Protocol;
using BattleArena.Multiplayer.Transport;
using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Prediction;

/// <summary>
/// Carries prediction route management over the mandatory authority connection.
/// Direct route failure never affects this service or the match connection.
/// </summary>
public sealed class AuthorityPredictionControlService : IDisposable
{
    private readonly INetworkTransport _transport;
    private readonly IProtocolCodec _codec;
    private readonly InboundMessageValidator _validator;
    private readonly AuthorityConnectionService _connections;
    private readonly IAuthorityPredictionRouteBroker _broker;
    private readonly IPredictionRouteAdvertisementVerifier _advertisementVerifier;
    private ulong _nextSequence = 1;
    private ulong _currentAuthorityTick;
    private bool _disposed;

    public AuthorityPredictionControlService(
        INetworkTransport transport,
        IProtocolCodec codec,
        InboundMessageValidator validator,
        AuthorityConnectionService connections,
        IAuthorityPredictionRouteBroker broker,
        IPredictionRouteAdvertisementVerifier advertisementVerifier)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
        _advertisementVerifier = advertisementVerifier ??
            throw new ArgumentNullException(nameof(advertisementVerifier));
        _transport.PacketReceived += OnPacketReceived;
        _connections.PlayerJoined += OnPlayerJoined;
        _connections.PlayerTransportDisconnected += OnPlayerDisconnected;
    }

    public event Action<TransportConnectionId, ProtocolViolation>? ProtocolViolationDetected;

    public void AdvanceAuthorityTick(ulong authorityTick)
    {
        if (authorityTick < _currentAuthorityTick)
        {
            throw new ArgumentOutOfRangeException(nameof(authorityTick));
        }

        _currentAuthorityTick = authorityTick;
        Publish(_broker.AdvanceAuthorityTick(authorityTick), authorityTick);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _transport.PacketReceived -= OnPacketReceived;
        _connections.PlayerJoined -= OnPlayerJoined;
        _connections.PlayerTransportDisconnected -= OnPlayerDisconnected;
        _disposed = true;
    }

    private void OnPlayerJoined(ConnectedPlayer player) => Publish(
        _broker.AddOrUpdatePeer(ToSessionPeer(player)),
        _currentAuthorityTick);

    private void OnPlayerDisconnected(ConnectedPlayer player) => Publish(
        _broker.RemovePeer(
            player.SessionPeerId,
            PredictionRouteRevocationReason.PeerDeparted),
        _currentAuthorityTick);

    private void OnPacketReceived(InboundTransportPacket packet)
    {
        if (packet.Channel != TransportChannel.Connection ||
            !_connections.ConnectedPlayers.Any(player => player.ConnectionId == packet.Sender))
        {
            return;
        }

        var decoded = _codec.Decode(packet.Payload.Span);
        if (!decoded.IsSuccess ||
            decoded.Envelope!.PayloadCase !=
            PacketEnvelope.PayloadOneofCase.PredictionRouteAdvertisement)
        {
            return;
        }

        var validation = _validator.Validate(
            decoded.Envelope,
            new ProtocolValidationContext(
                RemoteEndpointRole.Client,
                _connections.SessionId,
                SessionEstablished: true));
        if (!validation.IsValid)
        {
            ProtocolViolationDetected?.Invoke(packet.Sender, validation.Violation!);
            return;
        }

        var player = _connections.ConnectedPlayers.Single(value => value.ConnectionId == packet.Sender);
        if (!_advertisementVerifier.IsAuthorized(
                player,
                decoded.Envelope.PredictionRouteAdvertisement.RouteDescriptor))
        {
            ProtocolViolationDetected?.Invoke(
                packet.Sender,
                new ProtocolViolation(
                    ProtocolViolationCode.InvalidCredential,
                    "Prediction route identity is not authorized for this transport connection."));
            return;
        }

        Publish(
            _broker.ApplyAdvertisement(
                player.SessionPeerId,
                player.ConnectionGeneration,
                decoded.Envelope.PredictionRouteAdvertisement.RouteDescriptor,
                _currentAuthorityTick),
            _currentAuthorityTick);
    }

    private void Publish(PredictionRouteBrokerDelta delta, ulong authorityTick)
    {
        if (delta.Roster is not null)
        {
            foreach (var player in _connections.ConnectedPlayers)
            {
                Send(player.ConnectionId, authorityTick, envelope =>
                    envelope.PeerRosterUpdate = delta.Roster.Clone());
            }
        }

        foreach (var authorization in delta.Authorizations)
        {
            var target = _connections.ConnectedPlayers.SingleOrDefault(
                player => player.SessionPeerId.Value == authorization.LocalSessionPeerId);
            if (target is not null)
            {
                Send(target.ConnectionId, authorityTick, envelope =>
                    envelope.PredictionRouteAuthorization = authorization.Clone());
            }
        }

        foreach (var revocation in delta.Revocations)
        {
            var target = _connections.ConnectedPlayers.SingleOrDefault(
                player => player.SessionPeerId.Value == revocation.LocalSessionPeerId);
            if (target is not null)
            {
                Send(target.ConnectionId, authorityTick, envelope =>
                    envelope.PredictionRouteRevoked = revocation.Clone());
            }
        }
    }

    private void Send(
        TransportConnectionId recipient,
        ulong authorityTick,
        Action<PacketEnvelope> setPayload)
    {
        var envelope = new PacketEnvelope
        {
            ProtocolVersion = ProtocolConstants.CurrentVersion,
            SessionId = _connections.SessionId,
            Sequence = _nextSequence++,
            SimulationTick = authorityTick,
        };
        setPayload(envelope);
        _transport.Send(new OutboundTransportPacket(
            recipient,
            TransportChannel.Connection,
            TransportDelivery.ReliableOrdered,
            _codec.Encode(envelope)));
    }

    private static SessionPeer ToSessionPeer(ConnectedPlayer player) => new(
        player.SessionPeerId,
        player.ConnectionGeneration,
        player.PlayerId,
        player.CombatantId,
        player.DisplayName,
        isAuthority: false);
}
