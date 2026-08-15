#nullable enable

using Godot;

namespace BattleArena.Movement;

public partial class MovementCrouchRollHeadlessProbe : Node
{
    private int _tick;
    private MovementTestPlayer _player = null!;
    private CollisionShape3D _collision = null!;
    private float _startZ;

    public override void _Ready()
    {
        var arena = ResourceLoader.Load<PackedScene>(
            "res://scenes/movement/movement_test_arena.tscn").Instantiate<Node3D>();
        AddChild(arena);
        _player = arena.GetNode<MovementTestPlayer>("Player");
        _collision = _player.GetNode<CollisionShape3D>("BodyCollision");
        _startZ = _player.GlobalPosition.Z;
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
            case 36:
                SendKey(Key.Shift, false);
                break;
            case 42:
                RequireHeight(0.95f, "rolling profile");
                break;
            case 80:
                if (_player.Velocity.Length() < 5.5f)
                {
                    Fail($"roll completion discarded entry momentum: velocity={_player.Velocity.Length():0.000}");
                }
                break;
            case 82:
                SendKey(Key.W, false);
                break;
            case 90:
                RequireHeight(1.8f, "standing profile after committed roll");
                if (_player.GlobalPosition.Z >= _startZ - 4f)
                {
                    Fail($"roll travel was too short: start={_startZ:0.000}, end={_player.GlobalPosition.Z:0.000}");
                }
                break;
            case 105:
                SendKey(Key.Shift, true);
                break;
            case 110:
                RequireHeight(1.25f, "held crouch profile");
                break;
            case 115:
                SendKey(Key.Shift, false);
                break;
            case 125:
                RequireHeight(1.8f, "standing profile after crouch release");
                GD.Print("[MovementCrouchRollProbe] PASS: queued input, roll travel, committed completion, and crouch profiles.");
                GetTree().Quit(0);
                break;
        }
    }

    private void RequireHeight(float expected, string context)
    {
        if (_collision.Shape is not CapsuleShape3D capsule ||
            !Mathf.IsEqualApprox(capsule.Height, expected))
        {
            Fail($"{context} expected {expected:0.00} m capsule height.");
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

    private void Fail(string message)
    {
        GD.PushError($"[MovementCrouchRollProbe] {message}");
        GetTree().Quit(1);
    }
}
