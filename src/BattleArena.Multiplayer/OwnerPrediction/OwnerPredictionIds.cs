using BattleArena.Core.Movement.Simulation;
using BattleArena.Multiplayer.Connection;
using BattleArena.Multiplayer.Transport;

namespace BattleArena.Multiplayer.OwnerPrediction;

/// <summary>Owner-command delivery and deduplication sequence.</summary>
public readonly record struct InputSequence : IComparable<InputSequence>
{
    public static InputSequence Initial { get; } = new(1);
    public InputSequence(ulong value) =>
        Value = DeliveryIdGuard.RequirePositive(value, nameof(value), "input sequence");
    public ulong Value { get; }
    public bool IsValid => Value != 0;
    public InputSequence Next() => new(DeliveryIdGuard.CheckedNext(Value, "input sequence"));
    public int CompareTo(InputSequence other) =>
        DeliveryIdGuard.Compare(Value, other.Value, "input sequence");
}

/// <summary>Packet ordering/loss sequence within one authenticated transport stream.</summary>
public readonly record struct PacketSequence : IComparable<PacketSequence>
{
    public static PacketSequence Initial { get; } = new(1);
    public PacketSequence(ulong value) =>
        Value = DeliveryIdGuard.RequirePositive(value, nameof(value), "packet sequence");
    public ulong Value { get; }
    public bool IsValid => Value != 0;
    public PacketSequence Next() => new(DeliveryIdGuard.CheckedNext(Value, "packet sequence"));
    public int CompareTo(PacketSequence other) =>
        DeliveryIdGuard.Compare(Value, other.Value, "packet sequence");
}

/// <summary>
/// One prediction-route connection attempt. Packet numbering restarts when this
/// identity changes, even if the authorized route generation is unchanged.
/// </summary>
public readonly record struct PredictionRouteAttemptId :
    IComparable<PredictionRouteAttemptId>
{
    public static PredictionRouteAttemptId Initial { get; } = new(1);
    public PredictionRouteAttemptId(ulong value) =>
        Value = DeliveryIdGuard.RequirePositive(
            value,
            nameof(value),
            "prediction route attempt");
    public ulong Value { get; }
    public bool IsValid => Value != 0;
    public PredictionRouteAttemptId Next() => new(
        DeliveryIdGuard.CheckedNext(Value, "prediction route attempt"));
    public int CompareTo(PredictionRouteAttemptId other) =>
        DeliveryIdGuard.Compare(Value, other.Value, "prediction route attempt");
}

/// <summary>Terminal transition-result cursor; never interchangeable with action results.</summary>
public readonly record struct TransitionResolutionSequence :
    IComparable<TransitionResolutionSequence>
{
    public static TransitionResolutionSequence Initial { get; } = new(1);
    public TransitionResolutionSequence(ulong value) =>
        Value = DeliveryIdGuard.RequirePositive(
            value,
            nameof(value),
            "transition resolution sequence");
    public ulong Value { get; }
    public bool IsValid => Value != 0;
    public TransitionResolutionSequence Next() => new(
        DeliveryIdGuard.CheckedNext(Value, "transition resolution sequence"));
    public int CompareTo(TransitionResolutionSequence other) =>
        DeliveryIdGuard.Compare(Value, other.Value, "transition resolution sequence");
}

/// <summary>Terminal action-result cursor; never interchangeable with transition results.</summary>
public readonly record struct ActionResolutionSequence :
    IComparable<ActionResolutionSequence>
{
    public static ActionResolutionSequence Initial { get; } = new(1);
    public ActionResolutionSequence(ulong value) =>
        Value = DeliveryIdGuard.RequirePositive(
            value,
            nameof(value),
            "action resolution sequence");
    public ulong Value { get; }
    public bool IsValid => Value != 0;
    public ActionResolutionSequence Next() => new(
        DeliveryIdGuard.CheckedNext(Value, "action resolution sequence"));
    public int CompareTo(ActionResolutionSequence other) =>
        DeliveryIdGuard.Compare(Value, other.Value, "action resolution sequence");
}

/// <summary>
/// Lifetime shared by owner inputs, transition/action intents, accepted action
/// correlation, and their result cursors. It intentionally excludes authority
/// discontinuity: a teleport does not discard unresolved journal tombstones.
/// </summary>
public readonly record struct OwnerIntentScope
{
    public OwnerIntentScope(
        ulong sessionId,
        LifeEpoch life,
        OwnerControlEpoch ownerControl)
    {
        if (sessionId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionId));
        }

        if (!life.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(life));
        }

        if (!ownerControl.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(ownerControl));
        }

        SessionId = sessionId;
        Life = life;
        OwnerControl = ownerControl;
    }

    public ulong SessionId { get; }
    public LifeEpoch Life { get; }
    public OwnerControlEpoch OwnerControl { get; }
    public bool IsValid => SessionId != 0 && Life.IsValid && OwnerControl.IsValid;

    public static OwnerIntentScope From(CombatantAuthorityPredictionEpoch epoch)
    {
        if (!epoch.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(epoch));
        }

        return new OwnerIntentScope(
            epoch.SessionId,
            new LifeEpoch(epoch.CombatantId, epoch.Life),
            epoch.OwnerControl);
    }
}

/// <summary>
/// Authority gameplay executions survive an owner-control renewal while their
/// source combatant remains in the same life.
/// </summary>
public readonly record struct AuthorityActionExecutionScope
{
    public AuthorityActionExecutionScope(ulong sessionId, LifeEpoch life)
    {
        if (sessionId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionId));
        }
        if (!life.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(life));
        }
        SessionId = sessionId;
        Life = life;
    }
    public ulong SessionId { get; }
    public LifeEpoch Life { get; }
    public bool IsValid => SessionId != 0 && Life.IsValid;

    public static AuthorityActionExecutionScope From(
        CombatantAuthorityPredictionEpoch epoch)
    {
        if (!epoch.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(epoch));
        }
        return new AuthorityActionExecutionScope(
            epoch.SessionId,
            new LifeEpoch(epoch.CombatantId, epoch.Life));
    }
}

/// <summary>One directional authenticated main-transport stream.</summary>
public readonly record struct SessionPacketStreamScope
{
    public SessionPacketStreamScope(
        ulong sessionId,
        SessionPeerId sourcePeer,
        ConnectionGeneration sourceGeneration,
        SessionPeerId destinationPeer,
        ConnectionGeneration destinationGeneration,
        TransportChannel channel)
    {
        if (sessionId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionId));
        }
        if (sourcePeer.Value == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourcePeer));
        }
        if (sourceGeneration.Value == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceGeneration));
        }
        if (destinationPeer.Value == 0 || destinationPeer == sourcePeer)
        {
            throw new ArgumentOutOfRangeException(nameof(destinationPeer));
        }
        if (destinationGeneration.Value == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(destinationGeneration));
        }
        if (!TransportChannels.IsDefined(channel))
        {
            throw new ArgumentOutOfRangeException(nameof(channel));
        }
        SessionId = sessionId;
        SourcePeer = sourcePeer;
        SourceGeneration = sourceGeneration;
        DestinationPeer = destinationPeer;
        DestinationGeneration = destinationGeneration;
        Channel = channel;
    }
    public ulong SessionId { get; }
    public SessionPeerId SourcePeer { get; }
    public ConnectionGeneration SourceGeneration { get; }
    public SessionPeerId DestinationPeer { get; }
    public ConnectionGeneration DestinationGeneration { get; }
    public TransportChannel Channel { get; }
    public bool IsValid =>
        SessionId != 0 &&
        SourcePeer.Value != 0 &&
        SourceGeneration.Value != 0 &&
        DestinationPeer.Value != 0 &&
        DestinationGeneration.Value != 0 &&
        TransportChannels.IsDefined(Channel) &&
        SourcePeer != DestinationPeer;
}

/// <summary>
/// One directional authenticated prediction-mesh stream, scoped by endpoint
/// generations, authorized route generation, and connection attempt. The
/// attempt identity is the producer's packet-sequence restart boundary.
/// </summary>
public readonly record struct PredictionMeshPacketStreamScope
{
    public PredictionMeshPacketStreamScope(
        SessionPacketStreamScope endpoints,
        uint predictionRouteGeneration,
        PredictionRouteAttemptId attemptId)
    {
        if (!endpoints.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(endpoints));
        }
        if (predictionRouteGeneration == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(predictionRouteGeneration));
        }
        if (!attemptId.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptId));
        }
        Endpoints = endpoints;
        PredictionRouteGeneration = predictionRouteGeneration;
        AttemptId = attemptId;
    }
    public SessionPacketStreamScope Endpoints { get; }
    public uint PredictionRouteGeneration { get; }
    public PredictionRouteAttemptId AttemptId { get; }
    public bool IsValid =>
        Endpoints.IsValid &&
        PredictionRouteGeneration != 0 &&
        AttemptId.IsValid;
}

public readonly record struct OwnerInputIdentity : IComparable<OwnerInputIdentity>
{
    public OwnerInputIdentity(OwnerIntentScope scope, InputSequence sequence)
    {
        ScopeGuard.Require(scope, sequence.IsValid, nameof(sequence));
        Scope = scope;
        Sequence = sequence;
    }
    public OwnerIntentScope Scope { get; }
    public InputSequence Sequence { get; }
    public bool IsValid => Scope.IsValid && Sequence.IsValid;
    public int CompareTo(OwnerInputIdentity other) =>
        ScopeGuard.Compare(Scope, other.Scope, Sequence.Value, other.Sequence.Value, IsValid, other.IsValid);
}

public readonly record struct MovementTransitionIdentity :
    IComparable<MovementTransitionIdentity>
{
    public MovementTransitionIdentity(OwnerIntentScope scope, MovementTransitionId id)
    {
        ScopeGuard.Require(scope, id.IsValid, nameof(id));
        Scope = scope;
        Id = id;
    }
    public OwnerIntentScope Scope { get; }
    public MovementTransitionId Id { get; }
    public bool IsValid => Scope.IsValid && Id.IsValid;
    public int CompareTo(MovementTransitionIdentity other) =>
        ScopeGuard.Compare(Scope, other.Scope, Id.Value, other.Id.Value, IsValid, other.IsValid);
}

public readonly record struct PredictedActionIdentity : IComparable<PredictedActionIdentity>
{
    public PredictedActionIdentity(OwnerIntentScope scope, PredictedActionId id)
    {
        ScopeGuard.Require(scope, id.IsValid, nameof(id));
        Scope = scope;
        Id = id;
    }
    public OwnerIntentScope Scope { get; }
    public PredictedActionId Id { get; }
    public bool IsValid => Scope.IsValid && Id.IsValid;
    public int CompareTo(PredictedActionIdentity other) =>
        ScopeGuard.Compare(Scope, other.Scope, Id.Value, other.Id.Value, IsValid, other.IsValid);
}

public readonly record struct AuthorityActionExecutionIdentity :
    IComparable<AuthorityActionExecutionIdentity>
{
    public AuthorityActionExecutionIdentity(
        AuthorityActionExecutionScope scope,
        AuthorityActionExecutionId id)
    {
        if (!scope.IsValid || !id.IsValid)
        {
            throw new ArgumentOutOfRangeException(!scope.IsValid ? nameof(scope) : nameof(id));
        }
        Scope = scope;
        Id = id;
    }
    public AuthorityActionExecutionScope Scope { get; }
    public AuthorityActionExecutionId Id { get; }
    public bool IsValid => Scope.IsValid && Id.IsValid;
    public int CompareTo(AuthorityActionExecutionIdentity other)
    {
        if (!IsValid || !other.IsValid)
        {
            throw new InvalidOperationException(
                "A default/invalid authority execution identity cannot be ordered.");
        }
        if (Scope != other.Scope)
        {
            throw new InvalidOperationException(
                "Authority executions from different session/life scopes cannot be ordered.");
        }
        return Id.Value.CompareTo(other.Id.Value);
    }
}

public readonly record struct TransitionResolutionIdentity :
    IComparable<TransitionResolutionIdentity>
{
    public TransitionResolutionIdentity(
        OwnerIntentScope scope,
        TransitionResolutionSequence sequence)
    {
        ScopeGuard.Require(scope, sequence.IsValid, nameof(sequence));
        Scope = scope;
        Sequence = sequence;
    }
    public OwnerIntentScope Scope { get; }
    public TransitionResolutionSequence Sequence { get; }
    public bool IsValid => Scope.IsValid && Sequence.IsValid;
    public int CompareTo(TransitionResolutionIdentity other) =>
        ScopeGuard.Compare(Scope, other.Scope, Sequence.Value, other.Sequence.Value, IsValid, other.IsValid);
}

public readonly record struct ActionResolutionIdentity :
    IComparable<ActionResolutionIdentity>
{
    public ActionResolutionIdentity(
        OwnerIntentScope scope,
        ActionResolutionSequence sequence)
    {
        ScopeGuard.Require(scope, sequence.IsValid, nameof(sequence));
        Scope = scope;
        Sequence = sequence;
    }
    public OwnerIntentScope Scope { get; }
    public ActionResolutionSequence Sequence { get; }
    public bool IsValid => Scope.IsValid && Sequence.IsValid;
    public int CompareTo(ActionResolutionIdentity other) =>
        ScopeGuard.Compare(Scope, other.Scope, Sequence.Value, other.Sequence.Value, IsValid, other.IsValid);
}

public readonly record struct SessionPacketIdentity : IComparable<SessionPacketIdentity>
{
    public SessionPacketIdentity(SessionPacketStreamScope scope, PacketSequence sequence)
    {
        if (!scope.IsValid || !sequence.IsValid)
        {
            throw new ArgumentOutOfRangeException(!scope.IsValid ? nameof(scope) : nameof(sequence));
        }
        Scope = scope;
        Sequence = sequence;
    }
    public SessionPacketStreamScope Scope { get; }
    public PacketSequence Sequence { get; }
    public bool IsValid => Scope.IsValid && Sequence.IsValid;
    public int CompareTo(SessionPacketIdentity other)
    {
        if (!IsValid || !other.IsValid)
        {
            throw new InvalidOperationException("A default/invalid packet identity cannot be ordered.");
        }
        if (Scope != other.Scope)
        {
            throw new InvalidOperationException("Packets from different transport streams cannot be ordered.");
        }
        return Sequence.Value.CompareTo(other.Sequence.Value);
    }
}

public readonly record struct PredictionMeshPacketIdentity :
    IComparable<PredictionMeshPacketIdentity>
{
    public PredictionMeshPacketIdentity(
        PredictionMeshPacketStreamScope scope,
        PacketSequence sequence)
    {
        if (!scope.IsValid || !sequence.IsValid)
        {
            throw new ArgumentOutOfRangeException(!scope.IsValid ? nameof(scope) : nameof(sequence));
        }
        Scope = scope;
        Sequence = sequence;
    }
    public PredictionMeshPacketStreamScope Scope { get; }
    public PacketSequence Sequence { get; }
    public bool IsValid => Scope.IsValid && Sequence.IsValid;
    public int CompareTo(PredictionMeshPacketIdentity other)
    {
        if (!IsValid || !other.IsValid)
        {
            throw new InvalidOperationException(
                "A default/invalid prediction-mesh packet identity cannot be ordered.");
        }
        if (Scope != other.Scope)
        {
            throw new InvalidOperationException(
                "Prediction packets from different authenticated routes cannot be ordered.");
        }
        return Sequence.Value.CompareTo(other.Sequence.Value);
    }
}

internal static class ScopeGuard
{
    public static void Require(OwnerIntentScope scope, bool identityValid, string identityName)
    {
        if (!scope.IsValid || !identityValid)
        {
            throw new ArgumentOutOfRangeException(!scope.IsValid ? nameof(scope) : identityName);
        }
    }

    public static int Compare(
        OwnerIntentScope leftScope,
        OwnerIntentScope rightScope,
        ulong left,
        ulong right,
        bool leftValid,
        bool rightValid)
    {
        if (!leftValid || !rightValid)
        {
            throw new InvalidOperationException("A default/invalid owner identity cannot be ordered.");
        }
        if (leftScope != rightScope)
        {
            throw new InvalidOperationException("Owner identities from different intent scopes cannot be ordered.");
        }
        return left.CompareTo(right);
    }
}

internal static class DeliveryIdGuard
{
    public static ulong RequirePositive(ulong value, string parameterName, string identityName)
    {
        if (value == 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, $"A {identityName} must be positive.");
        }
        return value;
    }

    public static ulong CheckedNext(ulong value, string identityName)
    {
        if (value == 0)
        {
            throw new InvalidOperationException($"A default/invalid {identityName} cannot be advanced.");
        }
        return checked(value + 1);
    }

    public static int Compare(ulong left, ulong right, string identityName)
    {
        if (left == 0 || right == 0)
        {
            throw new InvalidOperationException($"A default/invalid {identityName} cannot be ordered.");
        }
        return left.CompareTo(right);
    }
}
