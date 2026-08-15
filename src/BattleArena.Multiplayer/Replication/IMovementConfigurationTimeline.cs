namespace BattleArena.Multiplayer.Replication;

public interface IMovementConfigurationTimeline
{
    bool Apply(NetworkMovementConfiguration configuration);
    bool TryResolve(
        ulong combatantId,
        ulong lifeId,
        ulong profileRevision,
        ulong capabilityRevision,
        ulong authorityTick,
        out NetworkMovementConfiguration configuration);
    void RemoveCombatant(ulong combatantId);
}
