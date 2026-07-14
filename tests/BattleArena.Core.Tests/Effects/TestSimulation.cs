using BattleArena.Core.Common;

namespace BattleArena.Core.Tests.Effects;

internal static class TestSimulation
{
    internal static readonly SimulationRate Rate = new(60);

    internal static SimulationDuration Duration(decimal seconds) =>
        Rate.DurationFromSeconds(seconds);

    internal static SimulationInstant At(decimal seconds) =>
        new(Duration(seconds).Ticks);
}
