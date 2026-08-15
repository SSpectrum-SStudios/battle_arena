using BattleArena.Multiplayer.Protocol;
using BattleArena.Protocol.V1;
using Google.Protobuf;

namespace BattleArena.Multiplayer.Prediction;

public sealed class PredictionHandshakeSession : IPredictionHandshakeSession
{
    private readonly AuthorizedPredictionRoute _route;
    private readonly IPredictionHandshakeAuthenticator _authenticator;
    private readonly IPredictionRouteCredentialGenerator _credentialGenerator;
    private readonly PredictionInboundMessageValidator _validator;
    private readonly PredictionPacketSequenceWindow _inboundSequences = new();
    private byte[]? _initiatorNonce;
    private byte[]? _responderNonce;
    private ulong _helloPacketSequence;
    private ulong _challengePacketSequence;
    private ulong _proofPacketSequence;
    private PredictionPacketEnvelope? _cachedHello;
    private PredictionPacketEnvelope? _cachedChallenge;
    private PredictionPacketEnvelope? _cachedProof;
    private PredictionPacketEnvelope? _cachedAccepted;

    public PredictionHandshakeSession(
        AuthorizedPredictionRoute route,
        IPredictionHandshakeAuthenticator authenticator,
        IPredictionRouteCredentialGenerator credentialGenerator,
        PredictionInboundMessageValidator validator)
    {
        _route = route ?? throw new ArgumentNullException(nameof(route));
        _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
        _credentialGenerator = credentialGenerator ??
            throw new ArgumentNullException(nameof(credentialGenerator));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        State = route.LocalPeerInitiates
            ? PredictionHandshakeState.ReadyToInitiate
            : PredictionHandshakeState.WaitingForHello;
    }

    public bool IsAuthenticated => State == PredictionHandshakeState.Authenticated;
    public PredictionHandshakeState State { get; private set; }

    public PredictionPacketEnvelope CreateHello(
        ulong packetSequence,
        ulong clientTick,
        ulong estimatedAuthorityTick)
    {
        if (State == PredictionHandshakeState.WaitingForChallenge && _cachedHello is not null)
        {
            return _cachedHello.Clone();
        }

        EnsureCanSend(
            State == PredictionHandshakeState.ReadyToInitiate,
            packetSequence,
            clientTick,
            estimatedAuthorityTick);
        _initiatorNonce = CreateNonce();
        _helloPacketSequence = packetSequence;
        _cachedHello = WithPayload(
            BaseEnvelope(packetSequence, clientTick, estimatedAuthorityTick),
            new PredictionMeshHello { InitiatorNonce = ByteString.CopyFrom(_initiatorNonce) });
        State = PredictionHandshakeState.WaitingForChallenge;
        return _cachedHello.Clone();
    }

    public PredictionHandshakeResult ReceiveHello(
        PredictionPacketEnvelope hello,
        ulong responsePacketSequence,
        ulong localClientTick,
        ulong currentEstimatedAuthorityTick)
    {
        if (_cachedHello is not null && hello.Equals(_cachedHello) && _cachedChallenge is not null &&
            State is PredictionHandshakeState.WaitingForProof or PredictionHandshakeState.Authenticated)
        {
            return PredictionHandshakeResult.Success(_cachedChallenge.Clone());
        }

        if (State != PredictionHandshakeState.WaitingForHello ||
            responsePacketSequence == 0 || localClientTick == 0)
        {
            return Failure(ProtocolViolationCode.InvalidSession, "Route is not awaiting an initiator hello.");
        }

        var invalid = ValidateInbound(
            hello,
            PredictionPacketEnvelope.PayloadOneofCase.MeshHello,
            currentEstimatedAuthorityTick);
        if (invalid is not null)
        {
            return invalid;
        }

        _initiatorNonce = hello.MeshHello.InitiatorNonce.ToByteArray();
        _helloPacketSequence = hello.PacketSequence;
        _responderNonce = CreateNonce();
        _challengePacketSequence = responsePacketSequence;
        var proof = _authenticator.CreateResponderProof(
            _route.Credential, Scope(), _initiatorNonce, _responderNonce);
        _cachedHello = hello.Clone();
        _cachedChallenge = WithPayload(
            BaseEnvelope(responsePacketSequence, localClientTick, currentEstimatedAuthorityTick),
            new PredictionMeshChallenge
            {
                HelloPacketSequence = _helloPacketSequence,
                ResponderNonce = ByteString.CopyFrom(_responderNonce),
                ResponderProof = ByteString.CopyFrom(proof),
            });
        State = PredictionHandshakeState.WaitingForProof;
        return PredictionHandshakeResult.Success(_cachedChallenge.Clone());
    }

    public PredictionHandshakeResult ReceiveChallenge(
        PredictionPacketEnvelope challenge,
        ulong responsePacketSequence,
        ulong localClientTick,
        ulong currentEstimatedAuthorityTick)
    {
        if (_cachedChallenge is not null && challenge.Equals(_cachedChallenge) && _cachedProof is not null &&
            State is PredictionHandshakeState.WaitingForAccepted or PredictionHandshakeState.Authenticated)
        {
            return PredictionHandshakeResult.Success(_cachedProof.Clone());
        }

        if (State != PredictionHandshakeState.WaitingForChallenge || _initiatorNonce is null ||
            responsePacketSequence == 0 || localClientTick == 0)
        {
            return Failure(ProtocolViolationCode.InvalidSession, "Route is not awaiting a responder challenge.");
        }

        var invalid = ValidateInbound(
            challenge,
            PredictionPacketEnvelope.PayloadOneofCase.MeshChallenge,
            currentEstimatedAuthorityTick);
        if (invalid is not null)
        {
            return invalid;
        }

        var message = challenge.MeshChallenge;
        if (message.HelloPacketSequence != _helloPacketSequence ||
            !_authenticator.VerifyResponderProof(
                _route.Credential,
                Scope(),
                _initiatorNonce,
                message.ResponderNonce.Span,
                message.ResponderProof.Span))
        {
            return Failure(ProtocolViolationCode.InvalidCredential, "Responder challenge is invalid or stale.");
        }

        _responderNonce = message.ResponderNonce.ToByteArray();
        _challengePacketSequence = challenge.PacketSequence;
        _proofPacketSequence = responsePacketSequence;
        var proof = _authenticator.CreateInitiatorProof(
            _route.Credential, Scope(), _initiatorNonce, _responderNonce);
        _cachedChallenge = challenge.Clone();
        _cachedProof = WithPayload(
            BaseEnvelope(responsePacketSequence, localClientTick, currentEstimatedAuthorityTick),
            new PredictionMeshProof
            {
                ChallengePacketSequence = _challengePacketSequence,
                InitiatorProof = ByteString.CopyFrom(proof),
            });
        State = PredictionHandshakeState.WaitingForAccepted;
        return PredictionHandshakeResult.Success(_cachedProof.Clone());
    }

    public PredictionHandshakeResult ReceiveProof(
        PredictionPacketEnvelope proof,
        ulong responsePacketSequence,
        ulong localClientTick,
        ulong currentEstimatedAuthorityTick)
    {
        if (_cachedProof is not null && proof.Equals(_cachedProof) && _cachedAccepted is not null &&
            State == PredictionHandshakeState.Authenticated)
        {
            return PredictionHandshakeResult.Success(_cachedAccepted.Clone());
        }

        if (State != PredictionHandshakeState.WaitingForProof || _initiatorNonce is null ||
            _responderNonce is null || responsePacketSequence == 0 || localClientTick == 0)
        {
            return Failure(ProtocolViolationCode.InvalidSession, "Route is not awaiting an initiator proof.");
        }

        var invalid = ValidateInbound(
            proof,
            PredictionPacketEnvelope.PayloadOneofCase.MeshProof,
            currentEstimatedAuthorityTick);
        if (invalid is not null)
        {
            return invalid;
        }

        var message = proof.MeshProof;
        if (message.ChallengePacketSequence != _challengePacketSequence ||
            !_authenticator.VerifyInitiatorProof(
                _route.Credential,
                Scope(),
                _initiatorNonce,
                _responderNonce,
                message.InitiatorProof.Span))
        {
            return Failure(ProtocolViolationCode.InvalidCredential, "Initiator proof is invalid or stale.");
        }

        _cachedProof = proof.Clone();
        var confirmation = _authenticator.CreateAcceptanceConfirmation(
            _route.Credential,
            Scope(),
            _initiatorNonce,
            _responderNonce,
            proof.PacketSequence);
        _cachedAccepted = WithPayload(
            BaseEnvelope(responsePacketSequence, localClientTick, currentEstimatedAuthorityTick),
            new PredictionMeshAccepted
            {
                ProofPacketSequence = proof.PacketSequence,
                ResponderConfirmation = ByteString.CopyFrom(confirmation),
            });
        State = PredictionHandshakeState.Authenticated;
        return PredictionHandshakeResult.Success(_cachedAccepted.Clone());
    }

    public PredictionHandshakeResult ReceiveAccepted(
        PredictionPacketEnvelope accepted,
        ulong currentEstimatedAuthorityTick)
    {
        if (State == PredictionHandshakeState.Authenticated &&
            _cachedAccepted is not null && accepted.Equals(_cachedAccepted))
        {
            return PredictionHandshakeResult.Success();
        }

        if (State != PredictionHandshakeState.WaitingForAccepted || _proofPacketSequence == 0)
        {
            return Failure(ProtocolViolationCode.InvalidSession, "Route is not awaiting final acceptance.");
        }

        var invalid = ValidateInbound(
            accepted,
            PredictionPacketEnvelope.PayloadOneofCase.MeshAccepted,
            currentEstimatedAuthorityTick);
        if (invalid is not null)
        {
            return invalid;
        }

        if (_initiatorNonce is null || _responderNonce is null ||
            accepted.MeshAccepted.ProofPacketSequence != _proofPacketSequence ||
            !_authenticator.VerifyAcceptanceConfirmation(
                _route.Credential,
                Scope(),
                _initiatorNonce,
                _responderNonce,
                _proofPacketSequence,
                accepted.MeshAccepted.ResponderConfirmation.Span))
        {
            return Failure(
                ProtocolViolationCode.InvalidCredential,
                "Acceptance identifies a stale proof or has invalid key confirmation.");
        }

        _cachedAccepted = accepted.Clone();
        State = PredictionHandshakeState.Authenticated;
        return PredictionHandshakeResult.Success();
    }

    public void Reset()
    {
        _initiatorNonce = null;
        _responderNonce = null;
        _helloPacketSequence = 0;
        _challengePacketSequence = 0;
        _proofPacketSequence = 0;
        _cachedHello = null;
        _cachedChallenge = null;
        _cachedProof = null;
        _cachedAccepted = null;
        _inboundSequences.Reset();
        State = _route.LocalPeerInitiates
            ? PredictionHandshakeState.ReadyToInitiate
            : PredictionHandshakeState.WaitingForHello;
    }

    private PredictionHandshakeResult? ValidateInbound(
        PredictionPacketEnvelope envelope,
        PredictionPacketEnvelope.PayloadOneofCase expected,
        ulong currentEstimatedAuthorityTick)
    {
        var validation = _validator.Validate(
            envelope,
            ValidationContext(currentEstimatedAuthorityTick, authenticated: false));
        if (!validation.IsValid)
        {
            return PredictionHandshakeResult.Failure(validation.Violation!);
        }

        if (!_inboundSequences.TryAccept(envelope.PacketSequence))
        {
            return Failure(
                ProtocolViolationCode.InvalidSequence,
                "Prediction handshake packet is duplicated or reordered.");
        }

        return envelope.PayloadCase == expected
            ? null
            : Failure(ProtocolViolationCode.MissingPayload, $"Expected {expected}.");
    }

    private void EnsureCanSend(
        bool correctRole,
        ulong packetSequence,
        ulong clientTick,
        ulong estimatedAuthorityTick)
    {
        if (!correctRole || IsAuthenticated || packetSequence == 0 || clientTick == 0 ||
            estimatedAuthorityTick == 0 || estimatedAuthorityTick >= _route.ExpiresAuthorityTick)
        {
            throw new InvalidOperationException("Handshake cannot send in its current state.");
        }
    }

    private byte[] CreateNonce()
    {
        var nonce = _credentialGenerator.CreateHandshakeNonce();
        if (nonce.Length != ProtocolConstants.PredictionHandshakeNonceBytes ||
            nonce.All(value => value == 0))
        {
            throw new InvalidOperationException("Nonce generator returned an invalid nonce.");
        }

        return nonce;
    }

    private PredictionPacketEnvelope BaseEnvelope(
        ulong packetSequence,
        ulong clientTick,
        ulong estimatedAuthorityTick) => new()
    {
        ProtocolVersion = ProtocolConstants.CurrentVersion,
        SessionId = _route.SessionId,
        SourceSessionPeerId = _route.LocalPeerId.Value,
        DestinationSessionPeerId = _route.RemotePeerId.Value,
        SourcePeerSessionGeneration = _route.LocalPeerSessionGeneration.Value,
        DestinationPeerSessionGeneration = _route.RemotePeerSessionGeneration.Value,
        PredictionRouteGeneration = _route.RouteGeneration,
        PacketSequence = packetSequence,
        ClientTick = clientTick,
        EstimatedAuthorityTick = estimatedAuthorityTick,
    };

    private static PredictionPacketEnvelope WithPayload(
        PredictionPacketEnvelope envelope,
        PredictionMeshHello payload)
    {
        envelope.MeshHello = payload;
        return envelope;
    }

    private static PredictionPacketEnvelope WithPayload(
        PredictionPacketEnvelope envelope,
        PredictionMeshChallenge payload)
    {
        envelope.MeshChallenge = payload;
        return envelope;
    }

    private static PredictionPacketEnvelope WithPayload(
        PredictionPacketEnvelope envelope,
        PredictionMeshProof payload)
    {
        envelope.MeshProof = payload;
        return envelope;
    }

    private static PredictionPacketEnvelope WithPayload(
        PredictionPacketEnvelope envelope,
        PredictionMeshAccepted payload)
    {
        envelope.MeshAccepted = payload;
        return envelope;
    }

    private PredictionProtocolValidationContext ValidationContext(
        ulong currentEstimatedAuthorityTick,
        bool authenticated) => new(
        _route.SessionId,
        _route.LocalPeerId.Value,
        _route.RemotePeerId.Value,
        _route.LocalPeerSessionGeneration.Value,
        _route.RemotePeerSessionGeneration.Value,
        _route.RouteGeneration,
        _route.ExpiresAuthorityTick,
        currentEstimatedAuthorityTick,
        authenticated);

    private PredictionHandshakeScope Scope()
    {
        var localInitiates = _route.LocalPeerInitiates;
        return new PredictionHandshakeScope(
            _route.SessionId,
            localInitiates ? _route.LocalPeerId.Value : _route.RemotePeerId.Value,
            localInitiates
                ? _route.LocalPeerSessionGeneration.Value
                : _route.RemotePeerSessionGeneration.Value,
            localInitiates ? _route.RemotePeerId.Value : _route.LocalPeerId.Value,
            localInitiates
                ? _route.RemotePeerSessionGeneration.Value
                : _route.LocalPeerSessionGeneration.Value,
            _route.RouteGeneration);
    }

    private static PredictionHandshakeResult Failure(
        ProtocolViolationCode code,
        string message) => PredictionHandshakeResult.Failure(new ProtocolViolation(code, message));
}
