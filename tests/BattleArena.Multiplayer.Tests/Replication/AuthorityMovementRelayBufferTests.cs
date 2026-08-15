using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Multiplayer.Connection;
using BattleArena.Multiplayer.Replication;

namespace BattleArena.Multiplayer.Tests.Replication;

public sealed class AuthorityMovementRelayBufferTests
{
    [Fact]
    public void PacketWindowKeepsRequestedRedundancyPerCombatant()
    {
        var buffer = new AuthorityMovementRelayBuffer();
        for (ulong tick = 1; tick <= 5; tick++)
        {
            buffer.Record(Command(combatantId: 2, lifeId: 1, tick));
            buffer.Record(Command(combatantId: 3, lifeId: 1, tick));
        }

        var recent = buffer.GetRecent(3);

        Assert.Equal(6, recent.Count);
        Assert.Equal([3UL, 4UL, 5UL], recent
            .Where(command => command.CombatantId == 2)
            .Select(command => command.AppliedAuthorityTick));
        Assert.Equal([3UL, 4UL, 5UL], recent
            .Where(command => command.CombatantId == 3)
            .Select(command => command.AppliedAuthorityTick));
    }

    [Fact]
    public void NewLifeDiscardsCommandsFromPreviousLife()
    {
        var buffer = new AuthorityMovementRelayBuffer();
        buffer.Record(Command(2, 1, 10));
        buffer.Record(Command(2, 2, 11));

        var command = Assert.Single(buffer.GetRecent(3));

        Assert.Equal(2UL, command.LifeId);
        Assert.Equal(11UL, command.AppliedAuthorityTick);
    }

    private static AcceptedMovementCommand Command(
        ulong combatantId,
        ulong lifeId,
        ulong tick) => new(
            new SessionPeerId(combatantId),
            ConnectionGeneration.Initial,
            combatantId,
            lifeId,
            tick,
            new MovementCommand(
                tick,
                new SimulationInstant(checked((long)tick)),
                new HorizontalVector(0, -1),
                0,
                0));
}
