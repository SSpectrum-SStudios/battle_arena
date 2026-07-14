using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Protocol;

public sealed class InboundMessageValidator
{
    private const float HalfPi = MathF.PI / 2f;

    public ProtocolValidationResult Validate(
        PacketEnvelope envelope,
        ProtocolValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (envelope.ProtocolVersion != ProtocolConstants.CurrentVersion)
        {
            return Invalid(
                ProtocolViolationCode.UnsupportedProtocolVersion,
                $"Protocol version {envelope.ProtocolVersion} is not supported.");
        }

        if (envelope.Sequence == 0)
        {
            return Invalid(ProtocolViolationCode.InvalidSequence, "Envelope sequence must be positive.");
        }

        if (envelope.PayloadCase == PacketEnvelope.PayloadOneofCase.None)
        {
            return Invalid(ProtocolViolationCode.MissingPayload, "Envelope does not contain a payload.");
        }

        var directionResult = ValidateDirection(envelope.PayloadCase, context.RemoteRole);
        if (!directionResult.IsValid)
        {
            return directionResult;
        }

        var sessionResult = ValidateSession(envelope, context);
        if (!sessionResult.IsValid)
        {
            return sessionResult;
        }

        return envelope.PayloadCase switch
        {
            PacketEnvelope.PayloadOneofCase.ClientInputBatch => ValidateInputBatch(envelope.ClientInputBatch),
            PacketEnvelope.PayloadOneofCase.ClientActionRequest => ValidateAction(envelope.ClientActionRequest),
            PacketEnvelope.PayloadOneofCase.AuthoritySnapshot => ValidateSnapshot(envelope.AuthoritySnapshot),
            PacketEnvelope.PayloadOneofCase.AuthorityEventBatch => ValidateEventBatch(envelope.AuthorityEventBatch),
            PacketEnvelope.PayloadOneofCase.AuthorityCheckpoint => ValidateCheckpoint(envelope.AuthorityCheckpoint),
            PacketEnvelope.PayloadOneofCase.JoinRequest => ValidateJoinRequest(envelope.JoinRequest),
            PacketEnvelope.PayloadOneofCase.JoinAccepted => ValidateJoinAccepted(envelope.JoinAccepted),
            PacketEnvelope.PayloadOneofCase.ReconnectRequest => ValidateReconnectRequest(envelope.ReconnectRequest),
            PacketEnvelope.PayloadOneofCase.StateBaseline => ValidateBaseline(envelope.StateBaseline),
            _ => Invalid(ProtocolViolationCode.MissingPayload, "Envelope payload type is unsupported."),
        };
    }

    private static ProtocolValidationResult ValidateDirection(
        PacketEnvelope.PayloadOneofCase payload,
        RemoteEndpointRole remoteRole)
    {
        var allowed = remoteRole switch
        {
            RemoteEndpointRole.Client => payload is
                PacketEnvelope.PayloadOneofCase.ClientInputBatch or
                PacketEnvelope.PayloadOneofCase.ClientActionRequest or
                PacketEnvelope.PayloadOneofCase.JoinRequest or
                PacketEnvelope.PayloadOneofCase.ReconnectRequest,
            RemoteEndpointRole.Authority => payload is
                PacketEnvelope.PayloadOneofCase.AuthoritySnapshot or
                PacketEnvelope.PayloadOneofCase.AuthorityEventBatch or
                PacketEnvelope.PayloadOneofCase.AuthorityCheckpoint or
                PacketEnvelope.PayloadOneofCase.JoinAccepted or
                PacketEnvelope.PayloadOneofCase.StateBaseline,
            _ => false,
        };

        return allowed
            ? ProtocolValidationResult.Valid
            : Invalid(
                ProtocolViolationCode.UnexpectedMessageDirection,
                $"A remote {remoteRole} cannot send payload type {payload}.");
    }

    private static ProtocolValidationResult ValidateSession(
        PacketEnvelope envelope,
        ProtocolValidationContext context)
    {
        if (envelope.PayloadCase == PacketEnvelope.PayloadOneofCase.JoinRequest)
        {
            return !context.SessionEstablished && envelope.SessionId == 0
                ? ProtocolValidationResult.Valid
                : Invalid(ProtocolViolationCode.InvalidSession, "Initial join packets must not claim a session.");
        }

        if (envelope.PayloadCase == PacketEnvelope.PayloadOneofCase.JoinAccepted)
        {
            var acceptedSession = envelope.JoinAccepted.SessionId;
            return !context.SessionEstablished &&
                   envelope.SessionId != 0 &&
                   envelope.SessionId == acceptedSession
                ? ProtocolValidationResult.Valid
                : Invalid(
                    ProtocolViolationCode.InvalidSession,
                    "Join acceptance must establish one consistent nonzero session.");
        }

        if (envelope.PayloadCase == PacketEnvelope.PayloadOneofCase.ReconnectRequest)
        {
            var sessionMatchesEnvelope =
                envelope.SessionId != 0 && envelope.SessionId == envelope.ReconnectRequest.SessionId;
            var sessionMatchesKnownIdentity =
                context.ExpectedSessionId is null || envelope.SessionId == context.ExpectedSessionId.Value;

            return !context.SessionEstablished && sessionMatchesEnvelope && sessionMatchesKnownIdentity
                ? ProtocolValidationResult.Valid
                : Invalid(
                    ProtocolViolationCode.InvalidSession,
                    "Reconnect request does not identify a consistent known session.");
        }

        if (!context.SessionEstablished || context.ExpectedSessionId is null)
        {
            return Invalid(
                ProtocolViolationCode.InvalidSession,
                "Only an initial join request is accepted before a session is established.");
        }

        return envelope.SessionId == context.ExpectedSessionId.Value
            ? ProtocolValidationResult.Valid
            : Invalid(ProtocolViolationCode.InvalidSession, "Packet session does not match the connection session.");
    }

    private static ProtocolValidationResult ValidateInputBatch(ClientInputBatch batch)
    {
        if (batch.Frames.Count is < 1 or > ProtocolConstants.MaxInputFramesPerBatch)
        {
            return Invalid(
                ProtocolViolationCode.InvalidCollectionCount,
                $"An input batch must contain 1-{ProtocolConstants.MaxInputFramesPerBatch} frames.");
        }

        ulong previousSequence = 0;
        foreach (var frame in batch.Frames)
        {
            if (frame.InputSequence == 0 || frame.InputSequence <= previousSequence)
            {
                return Invalid(
                    ProtocolViolationCode.InvalidSequence,
                    "Input frame sequences must be positive and strictly increasing within a batch.");
            }

            if (!IsFiniteInRange(frame.MoveX, -1f, 1f) ||
                !IsFiniteInRange(frame.MoveZ, -1f, 1f) ||
                !IsFiniteInRange(frame.ViewYawRadians, -MathF.PI, MathF.PI) ||
                !IsFiniteInRange(frame.ViewPitchRadians, -HalfPi, HalfPi))
            {
                return Invalid(
                    ProtocolViolationCode.InvalidNumericValue,
                    "Input axes or view angles are non-finite or outside their allowed range.");
            }

            if ((frame.ButtonBits & ~ProtocolConstants.KnownInputButtonMask) != 0)
            {
                return Invalid(
                    ProtocolViolationCode.InvalidNumericValue,
                    "Input frame contains unknown button bits.");
            }

            previousSequence = frame.InputSequence;
        }

        return ProtocolValidationResult.Valid;
    }

    private static ProtocolValidationResult ValidateAction(ClientActionRequest action)
    {
        if (action.ActionSequence == 0)
        {
            return Invalid(ProtocolViolationCode.InvalidSequence, "Action sequence must be positive.");
        }

        if (!Enum.IsDefined(action.Kind) || action.Kind == ClientActionKind.Unspecified)
        {
            return Invalid(ProtocolViolationCode.InvalidEnumValue, "Action kind is not recognized.");
        }

        if (action.EquipmentSlot >= ProtocolConstants.EquipmentSlotCount)
        {
            return Invalid(ProtocolViolationCode.InvalidNumericValue, "Equipment slot is outside the allowed range.");
        }

        return ProtocolValidationResult.Valid;
    }

    private static ProtocolValidationResult ValidateSnapshot(AuthoritySnapshot snapshot) =>
        snapshot.Combatants.Count <= ProtocolConstants.MaxCombatants
            ? ProtocolValidationResult.Valid
            : Invalid(ProtocolViolationCode.InvalidCollectionCount, "Snapshot contains too many combatants.");

    private static ProtocolValidationResult ValidateEventBatch(AuthorityEventBatch batch) =>
        batch.Events.Count <= ProtocolConstants.MaxEventsPerBatch
            ? ProtocolValidationResult.Valid
            : Invalid(ProtocolViolationCode.InvalidCollectionCount, "Event batch contains too many events.");

    private static ProtocolValidationResult ValidateCheckpoint(AuthorityCheckpoint checkpoint)
    {
        if (checkpoint.Combatants.Count > ProtocolConstants.MaxCombatants ||
            checkpoint.ActiveEffects.Count > ProtocolConstants.MaxActiveEffects ||
            checkpoint.WorldObjects.Count > ProtocolConstants.MaxWorldObjects)
        {
            return Invalid(
                ProtocolViolationCode.InvalidCollectionCount,
                "Checkpoint contains more replicated objects than the protocol permits.");
        }

        return ProtocolValidationResult.Valid;
    }

    private static ProtocolValidationResult ValidateJoinRequest(JoinRequest request)
    {
        if (request.ClientProtocolVersion != ProtocolConstants.CurrentVersion)
        {
            return Invalid(
                ProtocolViolationCode.UnsupportedProtocolVersion,
                "Join request protocol version is not supported.");
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName) ||
            request.DisplayName.Length > ProtocolConstants.MaxDisplayNameCharacters)
        {
            return Invalid(ProtocolViolationCode.InvalidTextValue, "Display name is empty or too long.");
        }

        return request.ClientNonce.Length == ProtocolConstants.ClientNonceBytes
            ? ProtocolValidationResult.Valid
            : Invalid(ProtocolViolationCode.InvalidCredential, "Client nonce has an invalid length.");
    }

    private static ProtocolValidationResult ValidateJoinAccepted(JoinAccepted accepted)
    {
        if (accepted.SessionId == 0 || accepted.PlayerId == 0 || accepted.CombatantId == 0)
        {
            return Invalid(ProtocolViolationCode.InvalidSession, "Join acceptance contains an invalid identity.");
        }

        if (accepted.ReconnectToken.Length != ProtocolConstants.ReconnectTokenBytes)
        {
            return Invalid(ProtocolViolationCode.InvalidCredential, "Reconnect token has an invalid length.");
        }

        if (accepted.SimulationTicksPerSecond == 0 ||
            accepted.SnapshotRate == 0 ||
            accepted.CheckpointIntervalTicks == 0)
        {
            return Invalid(ProtocolViolationCode.InvalidNumericValue, "Join timing values must be positive.");
        }

        return ProtocolValidationResult.Valid;
    }

    private static ProtocolValidationResult ValidateReconnectRequest(ReconnectRequest request)
    {
        if (request.SessionId == 0 || request.PlayerId == 0)
        {
            return Invalid(ProtocolViolationCode.InvalidSession, "Reconnect request contains an invalid identity.");
        }

        return request.ReconnectToken.Length == ProtocolConstants.ReconnectTokenBytes
            ? ProtocolValidationResult.Valid
            : Invalid(ProtocolViolationCode.InvalidCredential, "Reconnect token has an invalid length.");
    }

    private static ProtocolValidationResult ValidateBaseline(StateBaseline baseline)
    {
        if (baseline.PlayerId == 0 || baseline.ControlledCombatantId == 0 || baseline.Checkpoint is null)
        {
            return Invalid(ProtocolViolationCode.InvalidSession, "State baseline is missing required identity or state.");
        }

        return ValidateCheckpoint(baseline.Checkpoint);
    }

    private static bool IsFiniteInRange(float value, float minimum, float maximum) =>
        float.IsFinite(value) && value >= minimum && value <= maximum;

    private static ProtocolValidationResult Invalid(ProtocolViolationCode code, string message) =>
        ProtocolValidationResult.Invalid(code, message);
}
