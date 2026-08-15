using BattleArena.Core.Common;

namespace BattleArena.Core.Movement;

public readonly record struct MovementCommand
{
    public MovementCommand(
        ulong sequence,
        SimulationInstant clientTick,
        HorizontalVector movement,
        double viewYawRadians,
        double viewPitchRadians,
        MovementButtons heldButtons = MovementButtons.None,
        MovementButtons pressedButtons = MovementButtons.None,
        MovementButtons releasedButtons = MovementButtons.None)
    {
        if (!movement.IsFinite)
        {
            throw new ArgumentOutOfRangeException(nameof(movement), "Movement input must be finite.");
        }

        if (!double.IsFinite(viewYawRadians))
        {
            throw new ArgumentOutOfRangeException(nameof(viewYawRadians), "View yaw must be finite.");
        }

        if (!double.IsFinite(viewPitchRadians))
        {
            throw new ArgumentOutOfRangeException(nameof(viewPitchRadians), "View pitch must be finite.");
        }

        Sequence = sequence;
        ClientTick = clientTick;
        Movement = movement.ClampLength(1d);
        ViewYawRadians = MovementMath.WrapAngle(viewYawRadians);
        ViewPitchRadians = viewPitchRadians;
        HeldButtons = heldButtons;
        PressedButtons = pressedButtons;
        ReleasedButtons = releasedButtons;
    }

    public ulong Sequence { get; }

    public SimulationInstant ClientTick { get; }

    public HorizontalVector Movement { get; }

    public double ViewYawRadians { get; }

    public double ViewPitchRadians { get; }

    public MovementButtons HeldButtons { get; }

    public MovementButtons PressedButtons { get; }

    public MovementButtons ReleasedButtons { get; }

    public bool IsHeld(MovementButtons button) => HeldButtons.HasFlag(button);

    public bool WasPressed(MovementButtons button) => PressedButtons.HasFlag(button);

    public bool WasReleased(MovementButtons button) => ReleasedButtons.HasFlag(button);
}
