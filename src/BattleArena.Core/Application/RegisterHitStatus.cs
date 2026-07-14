namespace BattleArena.Core.Application;

public enum RegisterHitStatus
{
    Accepted = 0,
    ExecutionNotFound = 1,
    ExecutionEnded = 2,
    SourceLifeInactive = 3,
    TargetNotFound = 4,
    TargetEliminated = 5,
    RejectedByHitPolicy = 6,
}
