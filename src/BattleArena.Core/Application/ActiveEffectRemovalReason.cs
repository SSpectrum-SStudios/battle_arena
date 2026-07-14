namespace BattleArena.Core.Application;

public enum ActiveEffectRemovalReason
{
    Expired = 0,
    LifeEnded = 1,
    ExplicitlyRemoved = 2,
}
