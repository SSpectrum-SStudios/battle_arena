using BattleArena.Core.Movement;
using BattleArena.Multiplayer.Connection;

namespace BattleArena.Multiplayer.Replication;

/// <summary>
/// The immutable command the authority actually selected for one combatant on
/// one simulation tick. It is safe for remote visual prediction, but does not
/// confer gameplay authority on the receiver.
/// </summary>
public sealed record AcceptedMovementCommand
{
    public AcceptedMovementCommand(
        SessionPeerId sourcePeerId,
        ConnectionGeneration connectionGeneration,
        ulong combatantId,
        ulong lifeId,
        ulong appliedAuthorityTick,
        MovementCommand command,
        ulong movementProfileRevision = 1,
        ulong movementCapabilityRevision = 1)
    {
        if (combatantId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(combatantId));
        }

        if (lifeId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lifeId));
        }

        if (appliedAuthorityTick == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(appliedAuthorityTick));
        }

        if (command.Sequence == 0 || movementProfileRevision == 0 ||
            movementCapabilityRevision == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(command));
        }

        SourcePeerId = sourcePeerId;
        ConnectionGeneration = connectionGeneration;
        CombatantId = combatantId;
        LifeId = lifeId;
        AppliedAuthorityTick = appliedAuthorityTick;
        Command = command;
        MovementProfileRevision = movementProfileRevision;
        MovementCapabilityRevision = movementCapabilityRevision;
    }

    public SessionPeerId SourcePeerId { get; }

    public ConnectionGeneration ConnectionGeneration { get; }

    public ulong CombatantId { get; }

    public ulong LifeId { get; }

    public ulong AppliedAuthorityTick { get; }

    public MovementCommand Command { get; }

    public ulong MovementProfileRevision { get; }

    public ulong MovementCapabilityRevision { get; }
}
