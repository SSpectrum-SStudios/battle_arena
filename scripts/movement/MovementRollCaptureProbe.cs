#nullable enable

using Godot;

namespace BattleArena.Movement;

public partial class MovementRollCaptureProbe : Node
{
    private const string CaptureRoot = "C:/tmp/battle_arena_roll_capture";
    private int _tick;

    public override void _Ready()
    {
        DirAccess.MakeDirRecursiveAbsolute(CaptureRoot);
        var arena = ResourceLoader.Load<PackedScene>(
            "res://scenes/movement/movement_test_arena.tscn").Instantiate<Node3D>();
        AddChild(arena);
    }

    public override void _PhysicsProcess(double delta)
    {
        _tick++;
        switch (_tick)
        {
            case 5:
                SendKey(Key.W, true);
                break;
            case 35:
                SendKey(Key.Shift, true);
                break;
            case 38:
            case 50:
            case 62:
            case 74:
            case 84:
                CallDeferred(MethodName.CaptureFrame, _tick);
                break;
            case 86:
                SendKey(Key.Shift, false);
                break;
            case 100:
                SendKey(Key.W, false);
                GetTree().Quit(0);
                break;
        }
    }

    private void CaptureFrame(int tick)
    {
        var image = GetViewport().GetTexture().GetImage();
        var path = $"{CaptureRoot}/hold_{tick:000}.png";
        var error = image.SavePng(path);
        if (error != Error.Ok)
        {
            GD.PushError($"[MovementRollCapture] Failed to save '{path}': {error}.");
            GetTree().Quit(1);
        }
        else
        {
            GD.Print($"[MovementRollCapture] Saved {path}");
        }
    }

    private static void SendKey(Key key, bool pressed)
    {
        Input.ParseInputEvent(new InputEventKey
        {
            PhysicalKeycode = key,
            Pressed = pressed,
        });
    }
}
