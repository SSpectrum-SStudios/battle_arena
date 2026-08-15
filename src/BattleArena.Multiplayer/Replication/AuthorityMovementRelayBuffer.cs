namespace BattleArena.Multiplayer.Replication;

/// <summary>
/// Retains a short, per-combatant history so an unreliable relay packet can
/// carry redundant commands without coupling that policy to a transport.
/// </summary>
public sealed class AuthorityMovementRelayBuffer : IAuthorityMovementRelayBuffer
{
    private const int MaximumRetainedCommandsPerCombatant = 16;
    private readonly Dictionary<ulong, LinkedList<AcceptedMovementCommand>> _commands = [];

    public void Record(AcceptedMovementCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!_commands.TryGetValue(command.CombatantId, out var history))
        {
            history = [];
            _commands.Add(command.CombatantId, history);
        }

        if (history.Last?.Value is { } last)
        {
            if (command.LifeId != last.LifeId)
            {
                if (command.LifeId < last.LifeId)
                {
                    return;
                }

                history.Clear();
            }
            else if (command.AppliedAuthorityTick <= last.AppliedAuthorityTick)
            {
                return;
            }
        }

        history.AddLast(command);
        while (history.Count > MaximumRetainedCommandsPerCombatant)
        {
            history.RemoveFirst();
        }
    }

    public IReadOnlyList<AcceptedMovementCommand> GetRecent(int commandsPerCombatant)
    {
        if (commandsPerCombatant <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(commandsPerCombatant));
        }

        return _commands
            .OrderBy(pair => pair.Key)
            .SelectMany(pair => pair.Value.TakeLast(commandsPerCombatant))
            .OrderBy(command => command.AppliedAuthorityTick)
            .ThenBy(command => command.CombatantId)
            .ToArray();
    }

    public void RemoveCombatant(ulong combatantId) => _commands.Remove(combatantId);
}
