namespace BattleArena.Core.Movement;

[Flags]
public enum MovementButtons : uint
{
    None = 0,
    Jump = 1 << 0,
    Sprint = 1 << 1,
    CrouchOrRoll = 1 << 2,
    Attack = 1 << 3,
    Block = 1 << 4,
}
