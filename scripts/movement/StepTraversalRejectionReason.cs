namespace BattleArena.Movement;

public enum StepTraversalRejectionReason
{
    None,
    UpwardClearance,
    InsufficientForwardProgress,
    NoLanding,
    UnwalkableLanding,
    InvalidRise,
}
