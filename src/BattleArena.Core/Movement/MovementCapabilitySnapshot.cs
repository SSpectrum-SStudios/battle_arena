namespace BattleArena.Core.Movement;

public sealed record MovementCapabilitySnapshot
{
    public MovementCapabilitySnapshot(
        ulong revision,
        bool canSprint,
        bool canJump,
        bool canCrouch,
        bool canRoll,
        bool canGrabLedge,
        bool canMantle,
        uint maximumJumpCount,
        uint maximumAirRollCount)
    {
        if (revision == 0 || maximumJumpCount > 16 || maximumAirRollCount > 16 ||
            (!canJump && maximumJumpCount != 0) || (!canRoll && maximumAirRollCount != 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(revision),
                "Movement capabilities require a revision and coherent bounded counts.");
        }

        Revision = revision;
        CanSprint = canSprint;
        CanJump = canJump;
        CanCrouch = canCrouch;
        CanRoll = canRoll;
        CanGrabLedge = canGrabLedge;
        CanMantle = canMantle;
        MaximumJumpCount = maximumJumpCount;
        MaximumAirRollCount = maximumAirRollCount;
    }

    public ulong Revision { get; }
    public bool CanSprint { get; }
    public bool CanJump { get; }
    public bool CanCrouch { get; }
    public bool CanRoll { get; }
    public bool CanGrabLedge { get; }
    public bool CanMantle { get; }
    public uint MaximumJumpCount { get; }
    public uint MaximumAirRollCount { get; }

    public static MovementCapabilitySnapshot CreateBaseFighter(ulong revision = 1) =>
        new(revision, true, true, true, true, true, true, 1, 0);
}
