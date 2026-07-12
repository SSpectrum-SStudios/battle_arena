using BattleArena.Core.Common;

namespace BattleArena.Core.Effects;

public sealed class EffectScheduler
{
    public IReadOnlyList<ActiveEffectInstance> GetDueEffects(
        IEnumerable<ActiveEffectInstance> effects,
        SimulationInstant currentTime)
    {
        ArgumentNullException.ThrowIfNull(effects);

        return effects
            .Where(effect =>
                effect is not null &&
                !effect.Schedule.IsExpired &&
                effect.Schedule.NextActionAt is { } dueAt &&
                dueAt <= currentTime)
            .OrderBy(static effect => effect.Schedule.NextActionAt)
            .ThenBy(static effect => effect.Id.Value)
            .ToArray();
    }
}
