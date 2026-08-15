using BattleArena.Multiplayer.Connection;
using BattleArena.Multiplayer.Protocol;
using BattleArena.Multiplayer.Transport;
using BattleArena.Multiplayer.Timing;
using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Prediction;

public sealed class ClientPredictionControlService : IDisposable
{
    private readonly INetworkTransport _authorityTransport;
    private readonly IProtocolCodec _authorityCodec;
    private readonly InboundMessageValidator _authorityValidator;
    private readonly ClientConnectionService _connections;
    private readonly IPredictionMeshTransport _meshTransport;
    private readonly IPredictionHandshakeSessionFactory _handshakeFactory;
    private readonly IPredictionProtocolCodec _predictionCodec;
    private readonly INetworkTimeSource _timeSource;
    private SessionPeerDirectory? _directory;
    private PredictionMeshDriver? _driver;
    private ulong _nextSequence = 2;
    private ulong _currentClientTick = 1;
    private ulong _currentEstimatedAuthorityTick = 1;
    private bool _observedAuthorityTick;
    private bool _disposed;

    public ClientPredictionControlService(
        INetworkTransport authorityTransport,
        IProtocolCodec authorityCodec,
        InboundMessageValidator authorityValidator,
        ClientConnectionService connections,
        IPredictionMeshTransport meshTransport,
        IPredictionHandshakeSessionFactory handshakeFactory,
        IPredictionProtocolCodec predictionCodec,
        INetworkTimeSource timeSource)
    {
        _authorityTransport = authorityTransport ??
            throw new ArgumentNullException(nameof(authorityTransport));
        _authorityCodec = authorityCodec ?? throw new ArgumentNullException(nameof(authorityCodec));
        _authorityValidator = authorityValidator ??
            throw new ArgumentNullException(nameof(authorityValidator));
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
        _meshTransport = meshTransport ?? throw new ArgumentNullException(nameof(meshTransport));
        _handshakeFactory = handshakeFactory ?? throw new ArgumentNullException(nameof(handshakeFactory));
        _predictionCodec = predictionCodec ?? throw new ArgumentNullException(nameof(predictionCodec));
        _timeSource = timeSource ?? throw new ArgumentNullException(nameof(timeSource));
        _authorityTransport.PacketReceived += OnAuthorityPacket;
        _connections.JoinAccepted += OnJoinAccepted;
    }

    public event Action<ProtocolViolation>? ProtocolViolationDetected;
    public event Action<PredictionRouteStatus>? RouteStatusChanged;
    public event Action<AuthenticatedPredictionPacket>? AuthenticatedPacketReceived;

    public IReadOnlyCollection<PredictionRouteStatus> RouteStatuses =>
        _driver?.RouteStatuses ?? [];

    public IReadOnlyCollection<PredictionPeerPathStatus> DirectPathStatuses =>
        _driver?.DirectPathStatuses ?? [];

    public bool TryGetPeer(SessionPeerId peerId, out SessionPeer peer)
    {
        if (_directory is not null)
        {
            return _directory.TryGetPeer(peerId, out peer!);
        }

        peer = null!;
        return false;
    }

    public bool TryGetDirectPathForCombatant(
        ulong combatantId,
        out PredictionPeerPathStatus status)
    {
        if (_directory is null)
        {
            status = null!;
            return false;
        }

        return PredictionPeerPathResolver.TryResolve(
            combatantId,
            _directory.Peers,
            _driver?.DirectPathStatuses ?? [],
            out status);
    }

    public void PublishMovement(MovementPredictionBundle bundle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _driver?.PublishMovement(bundle);
    }

    public void Advance(ulong clientTick, ulong estimatedAuthorityTick)
    {
        if (clientTick < _currentClientTick)
        {
            throw new ArgumentOutOfRangeException(nameof(clientTick));
        }

        var elapsed = clientTick - _currentClientTick;
        if (_observedAuthorityTick)
        {
            _currentEstimatedAuthorityTick = checked(_currentEstimatedAuthorityTick + elapsed);
        }
        else
        {
            _currentEstimatedAuthorityTick = estimatedAuthorityTick;
        }

        _currentClientTick = clientTick;
        _driver?.Advance(clientTick, _currentEstimatedAuthorityTick);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _authorityTransport.PacketReceived -= OnAuthorityPacket;
        _connections.JoinAccepted -= OnJoinAccepted;
        if (_driver is not null)
        {
            _driver.RouteStatusChanged -= OnRouteStatusChanged;
            _driver.AuthenticatedPacketReceived -= OnAuthenticatedPacket;
            _driver.Dispose();
        }
        else
        {
            _meshTransport.Stop();
        }

        _disposed = true;
    }

    private void OnJoinAccepted(ClientSessionIdentity identity)
    {
        var connection = _connections.AuthorityConnection ??
            throw new InvalidOperationException("An accepted session has no authority connection.");
        var envelope = new PacketEnvelope
        {
            ProtocolVersion = ProtocolConstants.CurrentVersion,
            SessionId = identity.SessionId,
            Sequence = _nextSequence++,
            SimulationTick = _currentEstimatedAuthorityTick,
            PredictionRouteAdvertisement = new PredictionRouteAdvertisement
            {
                RouteDescriptor = _meshTransport.LocalRouteDescriptor,
            },
        };
        _authorityTransport.Send(new OutboundTransportPacket(
            connection,
            TransportChannel.Connection,
            TransportDelivery.ReliableOrdered,
            _authorityCodec.Encode(envelope)));
    }

    private void OnAuthorityPacket(InboundTransportPacket packet)
    {
        var identity = _connections.Identity;
        if (identity is null || packet.Channel != TransportChannel.Connection ||
            packet.Sender != _connections.AuthorityConnection)
        {
            return;
        }

        var decoded = _authorityCodec.Decode(packet.Payload.Span);
        if (!decoded.IsSuccess)
        {
            return;
        }

        var payload = decoded.Envelope!.PayloadCase;
        if (payload is not PacketEnvelope.PayloadOneofCase.PeerRosterUpdate and
            not PacketEnvelope.PayloadOneofCase.PredictionRouteAuthorization and
            not PacketEnvelope.PayloadOneofCase.PredictionRouteRevoked)
        {
            return;
        }

        var validation = _authorityValidator.Validate(
            decoded.Envelope,
            new ProtocolValidationContext(
                RemoteEndpointRole.Authority,
                identity.SessionId,
                SessionEstablished: true));
        if (!validation.IsValid)
        {
            ProtocolViolationDetected?.Invoke(validation.Violation!);
            return;
        }

        if (decoded.Envelope.SimulationTick > 0)
        {
            _currentEstimatedAuthorityTick = Math.Max(
                _currentEstimatedAuthorityTick,
                decoded.Envelope.SimulationTick);
            _observedAuthorityTick = true;
        }

        switch (payload)
        {
            case PacketEnvelope.PayloadOneofCase.PeerRosterUpdate:
                ApplyRoster(identity, decoded.Envelope.PeerRosterUpdate);
                break;
            case PacketEnvelope.PayloadOneofCase.PredictionRouteAuthorization:
                _driver?.ApplyAuthorization(
                    decoded.Envelope.PredictionRouteAuthorization,
                    decoded.Envelope.SimulationTick);
                break;
            case PacketEnvelope.PayloadOneofCase.PredictionRouteRevoked:
                _driver?.ApplyRevocation(decoded.Envelope.PredictionRouteRevoked);
                break;
        }
    }

    private void ApplyRoster(ClientSessionIdentity identity, PeerRosterUpdate roster)
    {
        _directory ??= new SessionPeerDirectory(identity.SessionPeerId);
        if (!_directory.TryApply(roster))
        {
            return;
        }

        if (_driver is null)
        {
            _driver = new PredictionMeshDriver(
                new ClientPredictionMeshCoordinator(identity.SessionId, _directory),
                _meshTransport,
                _handshakeFactory,
                _predictionCodec,
                new PredictionInboundMessageValidator(),
                _timeSource,
                identity.SimulationTicksPerSecond);
            _driver.RouteStatusChanged += OnRouteStatusChanged;
            _driver.AuthenticatedPacketReceived += OnAuthenticatedPacket;
        }
        else
        {
            _driver.ApplyRoster(roster);
        }
    }

    private void OnRouteStatusChanged(PredictionRouteStatus status) =>
        RouteStatusChanged?.Invoke(status);

    private void OnAuthenticatedPacket(AuthenticatedPredictionPacket packet)
    {
        if (_directory is null ||
            !_directory.TryGetPeer(packet.Sender, out var peer) ||
            peer.IsAuthority)
        {
            ProtocolViolationDetected?.Invoke(new ProtocolViolation(
                ProtocolViolationCode.InvalidSession,
                "Direct prediction packet ownership does not match the authenticated peer."));
            return;
        }

        if (packet.Envelope.PayloadCase ==
                PredictionPacketEnvelope.PayloadOneofCase.MovementPredictionBundle &&
            packet.Envelope.MovementPredictionBundle.SourceCombatantId != peer.CombatantId)
        {
            ProtocolViolationDetected?.Invoke(new ProtocolViolation(
                ProtocolViolationCode.InvalidSession,
                "Direct movement combatant does not match the authenticated peer."));
            return;
        }

        AuthenticatedPacketReceived?.Invoke(packet);
    }
}
