#nullable enable

using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Protocol.V1;

namespace BattleArena.GodotNetworking;

public readonly record struct NetworkMovementInput(
    ulong Sequence,
    ulong ClientTick,
    float MoveX,
    float MoveZ,
    float YawRadians,
    float PitchRadians,
    MovementButtons HeldButtons,
    MovementButtons PressedButtons,
    MovementButtons ReleasedButtons)
{
    public bool JumpPressed => PressedButtons.HasFlag(MovementButtons.Jump);

    public bool SprintHeld => HeldButtons.HasFlag(MovementButtons.Sprint);

    public ClientInputFrame ToProtocol() => new()
    {
        InputSequence = Sequence,
        ClientTick = ClientTick,
        MoveX = MoveX,
        MoveZ = MoveZ,
        ViewYawRadians = YawRadians,
        ViewPitchRadians = PitchRadians,
        ButtonBits = (uint)HeldButtons,
        PressedButtonBits = (uint)PressedButtons,
        ReleasedButtonBits = (uint)ReleasedButtons,
        EstimatedAuthorityTick = ClientTick,
        MovementProfileRevision = 1,
        MovementCapabilityRevision = 1,
    };

    public MovementCommand ToMovementCommand() => new(
        Sequence,
        new SimulationInstant(checked((long)ClientTick)),
        new HorizontalVector(MoveX, MoveZ),
        YawRadians,
        PitchRadians,
        HeldButtons,
        PressedButtons,
        ReleasedButtons);

    public NetworkMovementInput WithoutOneShotButtons() => this with
    {
        PressedButtons = MovementButtons.None,
        ReleasedButtons = MovementButtons.None,
    };

    public static NetworkMovementInput FromProtocol(ClientInputFrame frame) => new(
        frame.InputSequence,
        frame.ClientTick,
        frame.MoveX,
        frame.MoveZ,
        frame.ViewYawRadians,
        frame.ViewPitchRadians,
        (MovementButtons)frame.ButtonBits,
        (MovementButtons)frame.PressedButtonBits,
        (MovementButtons)frame.ReleasedButtonBits);

    public static NetworkMovementInput FromMovementCommand(MovementCommand command) => new(
        command.Sequence,
        checked((ulong)command.ClientTick.Tick),
        (float)command.Movement.X,
        (float)command.Movement.Z,
        (float)command.ViewYawRadians,
        (float)command.ViewPitchRadians,
        command.HeldButtons,
        command.PressedButtons,
        command.ReleasedButtons);

    public static NetworkMovementInput Neutral(ulong clientTick, float yaw, float pitch) =>
        new(
            0,
            clientTick,
            0,
            0,
            yaw,
            pitch,
            MovementButtons.None,
            MovementButtons.None,
            MovementButtons.None);
}
