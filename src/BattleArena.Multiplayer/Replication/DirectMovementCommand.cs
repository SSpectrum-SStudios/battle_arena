using BattleArena.Core.Movement;
using BattleArena.Multiplayer.Connection;

namespace BattleArena.Multiplayer.Replication;

/// <summary>
/// Untrusted movement evidence received directly from an authenticated peer.
/// It may drive visuals until authority-accepted commands or state supersede it.
/// </summary>
public sealed record DirectMovementCommand
{
    public DirectMovementCommand(
        SessionPeerId sourcePeerId,
        ConnectionGeneration connectionGeneration,
        ulong combatantId,
        ulong lifeId,
        ulong bundleSequence,
        ulong estimatedAuthorityTick,
        MovementCommand command,
        ulong movementProfileRevision = 1,
        ulong movementCapabilityRevision = 1)
    {
        if (combatantId == 0 || lifeId == 0 || bundleSequence == 0 ||
            estimatedAuthorityTick == 0 || command.Sequence == 0 ||
            movementProfileRevision == 0 || movementCapabilityRevision == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(command));
        }

        SourcePeerId = sourcePeerId;
        ConnectionGeneration = connectionGeneration;
        CombatantId = combatantId;
        LifeId = lifeId;
        BundleSequence = bundleSequence;
        EstimatedAuthorityTick = estimatedAuthorityTick;
        Command = command;
        MovementProfileRevision = movementProfileRevision;
        MovementCapabilityRevision = movementCapabilityRevision;
    }

    public SessionPeerId SourcePeerId { get; }
    public ConnectionGeneration ConnectionGeneration { get; }
    public ulong CombatantId { get; }
    public ulong LifeId { get; }
    public ulong BundleSequence { get; }
    public ulong EstimatedAuthorityTick { get; }
    public MovementCommand Command { get; }
    public ulong MovementProfileRevision { get; }
    public ulong MovementCapabilityRevision { get; }
}
