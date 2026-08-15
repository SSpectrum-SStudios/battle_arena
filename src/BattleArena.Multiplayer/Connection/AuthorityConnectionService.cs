using BattleArena.Multiplayer.Protocol;
using BattleArena.Multiplayer.Transport;
using BattleArena.Protocol.V1;
using Google.Protobuf;

namespace BattleArena.Multiplayer.Connection;

public sealed class AuthorityConnectionService : IDisposable
{
    private readonly INetworkTransport _transport;
    private readonly IProtocolCodec _codec;
    private readonly InboundMessageValidator _validator;
    private readonly ISessionCredentialGenerator _credentialGenerator;
    private readonly AuthoritySessionConfiguration _configuration;
    private readonly Dictionary<TransportConnectionId, ConnectedPlayer> _players = [];
    private ulong _nextEnvelopeSequence = 1;
    private ulong _nextPlayerId = 2;
    private ulong _nextCombatantId = 2;
    private bool _disposed;

    public AuthorityConnectionService(
        INetworkTransport transport,
        IProtocolCodec codec,
        InboundMessageValidator validator,
        ISessionCredentialGenerator credentialGenerator,
        AuthoritySessionConfiguration configuration)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _credentialGenerator = credentialGenerator ?? throw new ArgumentNullException(nameof(credentialGenerator));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        SessionId = credentialGenerator.CreateSessionId();

        _transport.PacketReceived += OnPacketReceived;
        _transport.ConnectionClosed += OnConnectionClosed;
    }

    public event Action<ConnectedPlayer>? PlayerJoined;

    public event Action<ConnectedPlayer>? PlayerTransportDisconnected;

    public event Action<TransportConnectionId, ProtocolViolation>? ProtocolViolationDetected;

    public ulong SessionId { get; }

    public IReadOnlyCollection<ConnectedPlayer> ConnectedPlayers => _players.Values;

    public void StartMatch(string arenaDefinitionId, ulong matchSeed, ulong authorityStartTick)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(arenaDefinitionId);
        if (matchSeed == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(matchSeed));
        }

        foreach (var player in _players.Values)
        {
            var start = new PacketEnvelope
            {
                ProtocolVersion = ProtocolConstants.CurrentVersion,
                SessionId = SessionId,
                Sequence = _nextEnvelopeSequence++,
                SimulationTick = authorityStartTick,
                MatchStart = new MatchStart
                {
                    ArenaDefinitionId = arenaDefinitionId,
                    MatchSeed = matchSeed,
                    AuthorityStartTick = authorityStartTick,
                },
            };

            _transport.Send(new OutboundTransportPacket(
                player.ConnectionId,
                TransportChannel.Connection,
                TransportDelivery.ReliableOrdered,
                _codec.Encode(start)));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _transport.PacketReceived -= OnPacketReceived;
        _transport.ConnectionClosed -= OnConnectionClosed;
        _disposed = true;
    }

    private void OnPacketReceived(InboundTransportPacket packet)
    {
        if (packet.Channel != TransportChannel.Connection)
        {
            return;
        }

        var decoded = _codec.Decode(packet.Payload.Span);
        if (!decoded.IsSuccess)
        {
            ProtocolViolationDetected?.Invoke(packet.Sender, decoded.Violation!);
            return;
        }

        var knownPlayer = _players.GetValueOrDefault(packet.Sender);
        var context = new ProtocolValidationContext(
            RemoteEndpointRole.Client,
            knownPlayer is null ? null : SessionId,
            knownPlayer is not null);
        var validation = _validator.Validate(decoded.Envelope!, context);
        if (!validation.IsValid)
        {
            ProtocolViolationDetected?.Invoke(packet.Sender, validation.Violation!);
            return;
        }

        if (decoded.Envelope!.PayloadCase != PacketEnvelope.PayloadOneofCase.JoinRequest)
        {
            return;
        }

        AcceptJoin(packet.Sender, decoded.Envelope.JoinRequest);
    }

    private void AcceptJoin(TransportConnectionId connectionId, JoinRequest request)
    {
        if (_players.ContainsKey(connectionId))
        {
            return;
        }

        if (_players.Count >= _configuration.MaximumRemotePlayers)
        {
            ProtocolViolationDetected?.Invoke(
                connectionId,
                new ProtocolViolation(ProtocolViolationCode.InvalidSession, "The hosted session is full."));
            return;
        }

        var playerId = _nextPlayerId++;
        var player = new ConnectedPlayer(
            connectionId,
            new SessionPeerId(playerId),
            ConnectionGeneration.Initial,
            playerId,
            _nextCombatantId++,
            request.DisplayName.Trim(),
            _credentialGenerator.CreateReconnectToken());
        _players.Add(connectionId, player);

        var accepted = new PacketEnvelope
        {
            ProtocolVersion = ProtocolConstants.CurrentVersion,
            SessionId = SessionId,
            Sequence = _nextEnvelopeSequence++,
            JoinAccepted = new JoinAccepted
            {
                SessionId = SessionId,
                SessionPeerId = player.SessionPeerId.Value,
                ConnectionGeneration = player.ConnectionGeneration.Value,
                PlayerId = player.PlayerId,
                CombatantId = player.CombatantId,
                ReconnectToken = ByteString.CopyFrom(player.ReconnectToken),
                SimulationTicksPerSecond = _configuration.SimulationTicksPerSecond,
                SnapshotRate = _configuration.SnapshotRate,
                CheckpointIntervalTicks = _configuration.CheckpointIntervalTicks,
            },
        };

        _transport.Send(new OutboundTransportPacket(
            connectionId,
            TransportChannel.Connection,
            TransportDelivery.ReliableOrdered,
            _codec.Encode(accepted)));
        PlayerJoined?.Invoke(player);
    }

    private void OnConnectionClosed(TransportConnectionId connectionId)
    {
        if (_players.Remove(connectionId, out var player))
        {
            PlayerTransportDisconnected?.Invoke(player);
        }
    }
}
