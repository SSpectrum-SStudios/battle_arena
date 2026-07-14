#nullable enable

using BattleArena.Protocol.V1;

namespace BattleArena.GodotNetworking;

public readonly record struct NetworkMovementInput(
    ulong Sequence,
    ulong ClientTick,
    float MoveX,
    float MoveZ,
    float YawRadians,
    float PitchRadians,
    bool JumpPressed,
    bool SprintHeld)
{
    private const uint JumpBit = 1u << 0;
    private const uint SprintBit = 1u << 1;

    public ClientInputFrame ToProtocol() => new()
    {
        InputSequence = Sequence,
        ClientTick = ClientTick,
        MoveX = MoveX,
        MoveZ = MoveZ,
        ViewYawRadians = YawRadians,
        ViewPitchRadians = PitchRadians,
        ButtonBits = (JumpPressed ? JumpBit : 0) | (SprintHeld ? SprintBit : 0),
    };

    public NetworkMovementInput WithoutOneShotButtons() => this with { JumpPressed = false };

    public static NetworkMovementInput FromProtocol(ClientInputFrame frame) => new(
        frame.InputSequence,
        frame.ClientTick,
        frame.MoveX,
        frame.MoveZ,
        frame.ViewYawRadians,
        frame.ViewPitchRadians,
        (frame.ButtonBits & JumpBit) != 0,
        (frame.ButtonBits & SprintBit) != 0);

    public static NetworkMovementInput Neutral(ulong clientTick, float yaw, float pitch) =>
        new(0, clientTick, 0, 0, yaw, pitch, false, false);
}
