#nullable enable

using Godot;

namespace BattleArena.VerticalSlice;

public static class VerticalSliceInput
{
    public static readonly StringName MoveForward = "move_forward";
    public static readonly StringName MoveBackward = "move_backward";
    public static readonly StringName MoveLeft = "move_left";
    public static readonly StringName MoveRight = "move_right";
    public static readonly StringName LookUp = "look_up";
    public static readonly StringName LookDown = "look_down";
    public static readonly StringName LookLeft = "look_left";
    public static readonly StringName LookRight = "look_right";
    public static readonly StringName Jump = "jump";
    public static readonly StringName Sprint = "sprint";
    public static readonly StringName CrouchOrRoll = "crouch";
    public static readonly StringName Attack = "attack";
    public static readonly StringName CameraToggle = "camera_toggle";
    public static readonly StringName ItemActivate1 = "item_activate_1";
    public static readonly StringName ItemActivate2 = "item_activate_2";
    public static readonly StringName ItemActivate3 = "item_activate_3";
    public static readonly StringName ItemActivate4 = "item_activate_4";
    public static readonly StringName ItemActivate5 = "item_activate_5";
    public static readonly StringName ItemActivate6 = "item_activate_6";

    public static void EnsureDefaultBindings()
    {
        EnsureAction(MoveForward, 0.2f, Key.W, JoyAxis.LeftY, -1f);
        EnsureAction(MoveBackward, 0.2f, Key.S, JoyAxis.LeftY, 1f);
        EnsureAction(MoveLeft, 0.2f, Key.A, JoyAxis.LeftX, -1f);
        EnsureAction(MoveRight, 0.2f, Key.D, JoyAxis.LeftX, 1f);
        EnsureAction(LookUp, 0.15f, null, JoyAxis.RightY, -1f);
        EnsureAction(LookDown, 0.15f, null, JoyAxis.RightY, 1f);
        EnsureAction(LookLeft, 0.15f, null, JoyAxis.RightX, -1f);
        EnsureAction(LookRight, 0.15f, null, JoyAxis.RightX, 1f);

        EnsureAction(Jump, 0.2f, Key.Space, JoyButton.A);
        EnsureAction(Sprint, 0.2f, Key.Ctrl, JoyButton.LeftStick);
        EnsureAction(CrouchOrRoll, 0.2f, Key.Shift, JoyButton.B);
        EnsureAction(CameraToggle, 0.2f, Key.V, JoyButton.RightStick);

        EnsureAction(Attack, 0.2f);
        AddIfMissing(Attack, new InputEventMouseButton { ButtonIndex = MouseButton.Left });
        AddIfMissing(
            Attack,
            new InputEventJoypadMotion
            {
                Axis = JoyAxis.TriggerRight,
                AxisValue = 1f,
            });

        EnsureAction(ItemActivate1, 0.2f, Key.Q, JoyButton.LeftShoulder);
        EnsureAction(ItemActivate2, 0.2f, Key.Key2, JoyButton.RightShoulder);
        EnsureAction(ItemActivate3, 0.2f, Key.Key3, JoyButton.X);
        EnsureAction(ItemActivate4, 0.2f, Key.E, JoyButton.Y);
        EnsureAction(ItemActivate5, 0.2f, Key.R, JoyButton.DpadDown);
        EnsureAction(ItemActivate6, 0.2f, Key.F, JoyButton.DpadUp);
    }

    public static void ReplaceBindings(StringName action, IEnumerable<InputEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        EnsureAction(action, 0.2f);
        InputMap.ActionEraseEvents(action);

        foreach (var inputEvent in events)
        {
            ArgumentNullException.ThrowIfNull(inputEvent);
            InputMap.ActionAddEvent(action, inputEvent);
        }
    }

    public static IReadOnlyList<InputEvent> GetBindings(StringName action) =>
        InputMap.HasAction(action)
            ? InputMap.ActionGetEvents(action).ToArray()
            : [];

    private static void EnsureAction(
        StringName action,
        float deadzone,
        Key? keyboardKey = null,
        JoyAxis? joyAxis = null,
        float axisValue = 0f)
    {
        EnsureAction(action, deadzone);
        if (keyboardKey is { } key)
        {
            AddIfMissing(action, new InputEventKey { PhysicalKeycode = key });
        }

        if (joyAxis is { } axis)
        {
            AddIfMissing(
                action,
                new InputEventJoypadMotion
                {
                    Axis = axis,
                    AxisValue = axisValue,
                });
        }
    }

    private static void EnsureAction(
        StringName action,
        float deadzone,
        Key? keyboardKey,
        JoyButton joyButton)
    {
        EnsureAction(action, deadzone);
        if (keyboardKey is { } key)
        {
            AddIfMissing(action, new InputEventKey { PhysicalKeycode = key });
        }

        AddIfMissing(action, new InputEventJoypadButton { ButtonIndex = joyButton });
    }

    private static void EnsureAction(StringName action, float deadzone)
    {
        if (!InputMap.HasAction(action))
        {
            InputMap.AddAction(action, deadzone);
        }
        else
        {
            InputMap.ActionSetDeadzone(action, deadzone);
        }
    }

    private static void AddIfMissing(StringName action, InputEvent inputEvent)
    {
        if (!InputMap.ActionHasEvent(action, inputEvent))
        {
            InputMap.ActionAddEvent(action, inputEvent);
        }
    }
}
