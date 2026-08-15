using BattleArena.Core.Movement;
using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Replication;

/// <summary>
/// A movement command paired with the exact immutable configuration revisions
/// under which its owner simulated it.
/// </summary>
public sealed record RevisionedMovementCommand
{
    public RevisionedMovementCommand(
        MovementCommand command,
        ulong movementProfileRevision,
        ulong movementCapabilityRevision)
    {
        if (movementProfileRevision == 0 || movementCapabilityRevision == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(command));
        }

        Command = command;
        MovementProfileRevision = movementProfileRevision;
        MovementCapabilityRevision = movementCapabilityRevision;
    }

    public MovementCommand Command { get; }
    public ulong MovementProfileRevision { get; }
    public ulong MovementCapabilityRevision { get; }

    public static RevisionedMovementCommand FromProtocol(ClientInputFrame frame) => new(
        MovementCommandProtocolMapper.FromProtocol(frame),
        frame.MovementProfileRevision,
        frame.MovementCapabilityRevision);
}
