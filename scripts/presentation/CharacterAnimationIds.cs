namespace BattleArena.Presentation;

/// <summary>
/// Stable semantic animation identifiers emitted by gameplay presentation.
/// Asset-specific clip names belong in a character presentation definition.
/// </summary>
public static class CharacterAnimationIds
{
    public const string Idle = "locomotion.idle";
    public const string Walk = "locomotion.walk";
    public const string WalkBackward = "locomotion.walk_backward";
    public const string Run = "locomotion.run";
    public const string Sprint = "locomotion.sprint";
    public const string StrafeLeft = "locomotion.strafe_left";
    public const string StrafeRight = "locomotion.strafe_right";
    public const string JumpStart = "jump.start";
    public const string Airborne = "jump.airborne";
    public const string JumpLand = "jump.land";
    public const string RollForward = "roll.forward";
    public const string RollBackward = "roll.backward";
    public const string RollLeft = "roll.left";
    public const string RollRight = "roll.right";
    public const string SimpleAttack = "attack.simple";
    public const string Block = "defense.block";
    public const string MantleHang = "mantle.hang";
    public const string MantleClimb = "mantle.climb";
    public const string CrouchIdle = "posture.crouch_idle";
    public const string CrouchMove = "posture.crouch_move";

    public static string LightAttack(int comboIndex)
    {
        if (comboIndex < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(comboIndex), "Combo indexes begin at one.");
        }

        return $"attack.light.{comboIndex}";
    }
}
