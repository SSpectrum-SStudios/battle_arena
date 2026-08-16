namespace BattleArena.Movement;

/// <summary>
/// Which movement motor the offline test arena drives its player with.
/// </summary>
/// <remarks>
/// Both modes read the same authored attributes, so switching between them
/// changes only how the resulting motion is integrated against the world. That
/// is what makes the comparison meaningful: any difference in feel is the
/// motor's doing, not a difference in tuning.
/// </remarks>
public enum MovementTestMotorMode
{
    /// <summary>
    /// The accepted Godot driver, using MoveAndSlide against a live body. The
    /// default, so the arena behaves as before unless the switch is thrown.
    /// </summary>
    Legacy = 0,

    /// <summary>
    /// The explicit-state kinematic motor, resolving motion from replayable
    /// value state through explicit-transform queries.
    /// </summary>
    ExplicitQueryMotor = 1,
}
