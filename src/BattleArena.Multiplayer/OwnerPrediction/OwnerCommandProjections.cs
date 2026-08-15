using BattleArena.Core.Common;
using BattleArena.Core.Movement.Simulation;

namespace BattleArena.Multiplayer.OwnerPrediction;

/// <summary>Projects a sampled owner command onto the authority-only path.</summary>
public interface IAuthorityOwnerCommandProjector
{
    AuthorityOwnerCommandProjection Project(OwnerSimulationCommand command);
}

/// <summary>Projects a sampled owner command onto the untrusted peer-hint path.</summary>
public interface IRemoteMovementHintProjector
{
    RemoteMovementHintProjection Project(OwnerSimulationCommand command);
}

/// <summary>
/// Authority-only projection. Complete combat held state and action references
/// are retained exactly; the separately transmitted action journal owns each
/// referenced press/release payload.
/// </summary>
public readonly record struct AuthorityOwnerCommandProjection
{
    public AuthorityOwnerCommandProjection(OwnerSimulationCommand command)
    {
        if (!command.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(command));
        }

        Identity = command.Identity;
        AuthorityDiscontinuity = command.AuthorityDiscontinuity;
        TargetFrame = command.TargetFrame;
        Input = command.Input;
    }

    public OwnerInputIdentity Identity { get; }
    public AuthorityDiscontinuityId AuthorityDiscontinuity { get; }
    public SimulationInstant TargetFrame { get; }
    public CharacterSimulationInput Input { get; }
    public bool IsValid =>
        Identity.IsValid &&
        AuthorityDiscontinuity.IsValid &&
        Input.IsValid;
}

/// <summary>
/// Movement-only direct-peer hint. Its public value graph deliberately has no
/// combat held state, action reference, action identity, or complete simulation
/// input from which gameplay authority could be recovered.
/// </summary>
public readonly record struct RemoteMovementHintProjection
{
    public RemoteMovementHintProjection(
        OwnerInputIdentity identity,
        AuthorityDiscontinuityId authorityDiscontinuity,
        SimulationInstant targetFrame,
        MovementAxes movement,
        ViewOrientation view,
        MovementHeldState movementHeld,
        TransitionReferenceBuffer transitionReferences,
        MovementConfigurationRevision movementRevision,
        MovementCapabilityRevision capabilityRevision)
    {
        if (!identity.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(identity));
        }
        if (!authorityDiscontinuity.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(authorityDiscontinuity));
        }
        if (!movementRevision.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(movementRevision));
        }
        if (!capabilityRevision.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(capabilityRevision));
        }

        Identity = identity;
        AuthorityDiscontinuity = authorityDiscontinuity;
        TargetFrame = targetFrame;
        Movement = movement;
        View = view;
        MovementHeld = movementHeld;
        TransitionReferences = transitionReferences;
        MovementRevision = movementRevision;
        CapabilityRevision = capabilityRevision;
    }

    public OwnerInputIdentity Identity { get; }
    public AuthorityDiscontinuityId AuthorityDiscontinuity { get; }
    public SimulationInstant TargetFrame { get; }
    public MovementAxes Movement { get; }
    public ViewOrientation View { get; }
    public MovementHeldState MovementHeld { get; }
    public TransitionReferenceBuffer TransitionReferences { get; }
    public MovementConfigurationRevision MovementRevision { get; }
    public MovementCapabilityRevision CapabilityRevision { get; }
    public bool IsValid =>
        Identity.IsValid &&
        AuthorityDiscontinuity.IsValid &&
        MovementRevision.IsValid &&
        CapabilityRevision.IsValid;
}

public sealed class AuthorityOwnerCommandProjector : IAuthorityOwnerCommandProjector
{
    public AuthorityOwnerCommandProjection Project(OwnerSimulationCommand command) =>
        new(command);
}

public sealed class RemoteMovementHintProjector : IRemoteMovementHintProjector
{
    public RemoteMovementHintProjection Project(OwnerSimulationCommand command)
    {
        if (!command.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(command));
        }

        var input = command.Input;
        return new RemoteMovementHintProjection(
            command.Identity,
            command.AuthorityDiscontinuity,
            command.TargetFrame,
            input.Movement,
            input.View,
            input.MovementHeld,
            input.TransitionReferences,
            input.MovementRevision,
            input.CapabilityRevision);
    }
}
