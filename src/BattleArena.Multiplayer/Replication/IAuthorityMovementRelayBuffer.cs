namespace BattleArena.Multiplayer.Replication;

public interface IAuthorityMovementRelayBuffer
{
    void Record(AcceptedMovementCommand command);

    IReadOnlyList<AcceptedMovementCommand> GetRecent(int commandsPerCombatant);

    void RemoveCombatant(ulong combatantId);
}
