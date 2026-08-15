using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Multiplayer.Connection;
using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Replication;

public static class MovementCommandProtocolMapper
{
    public static ClientInputFrame ToProtocol(MovementCommand command) => new()
    {
        InputSequence = command.Sequence,
        ClientTick = checked((ulong)command.ClientTick.Tick),
        MoveX = (float)command.Movement.X,
        MoveZ = (float)command.Movement.Z,
        ViewYawRadians = (float)command.ViewYawRadians,
        ViewPitchRadians = (float)command.ViewPitchRadians,
        ButtonBits = (uint)command.HeldButtons,
        PressedButtonBits = (uint)command.PressedButtons,
        ReleasedButtonBits = (uint)command.ReleasedButtons,
        EstimatedAuthorityTick = checked((ulong)command.ClientTick.Tick),
        MovementProfileRevision = 1,
        MovementCapabilityRevision = 1,
    };

    public static MovementCommand FromProtocol(ClientInputFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return new MovementCommand(
            frame.InputSequence,
            new SimulationInstant(checked((long)frame.ClientTick)),
            new HorizontalVector(frame.MoveX, frame.MoveZ),
            frame.ViewYawRadians,
            frame.ViewPitchRadians,
            (MovementButtons)frame.ButtonBits,
            (MovementButtons)frame.PressedButtonBits,
            (MovementButtons)frame.ReleasedButtonBits);
    }

    public static AuthorityAcceptedMovementCommand ToProtocol(
        AcceptedMovementCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var input = ToProtocol(command.Command);
        input.MovementProfileRevision = command.MovementProfileRevision;
        input.MovementCapabilityRevision = command.MovementCapabilityRevision;
        return new AuthorityAcceptedMovementCommand
        {
            SourceSessionPeerId = command.SourcePeerId.Value,
            PeerSessionGeneration = command.ConnectionGeneration.Value,
            CombatantId = command.CombatantId,
            LifeId = command.LifeId,
            AppliedAuthorityTick = command.AppliedAuthorityTick,
            Input = input,
            AppliedMovementProfileRevision = input.MovementProfileRevision,
            AppliedMovementCapabilityRevision = input.MovementCapabilityRevision,
        };
    }

    public static AcceptedMovementCommand FromProtocol(
        AuthorityAcceptedMovementCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return new AcceptedMovementCommand(
            new SessionPeerId(command.SourceSessionPeerId),
            new ConnectionGeneration(command.PeerSessionGeneration),
            command.CombatantId,
            command.LifeId,
            command.AppliedAuthorityTick,
            FromProtocol(command.Input),
            command.AppliedMovementProfileRevision,
            command.AppliedMovementCapabilityRevision);
    }
}
