#nullable enable

using Godot;

namespace BattleArena.GodotNetworking;

public sealed class NetworkMovementMotor
{
    public NetworkMovementMotor(
        float moveSpeed = 6f,
        float sprintMultiplier = 1.5f,
        float jumpVelocity = 6f,
        float gravity = 9.8f)
    {
        MoveSpeed = moveSpeed;
        SprintMultiplier = sprintMultiplier;
        JumpVelocity = jumpVelocity;
        Gravity = gravity;
    }

    public float MoveSpeed { get; }

    public float SprintMultiplier { get; }

    public float JumpVelocity { get; }

    public float Gravity { get; }

    public void Simulate(
        NetworkAvatar avatar,
        NetworkMovementInput input,
        float delta,
        bool? groundedOverride = null)
    {
        avatar.Rotation = new Vector3(0, input.YawRadians, 0);
        avatar.SetPitch(input.PitchRadians);

        var desired = (avatar.Transform.Basis.X * input.MoveX) +
                      (avatar.Transform.Basis.Z * input.MoveZ);
        desired.Y = 0;
        if (desired.LengthSquared() > 1f)
        {
            desired = desired.Normalized();
        }

        var speed = MoveSpeed * (input.SprintHeld ? SprintMultiplier : 1f);
        var velocity = avatar.Velocity;
        velocity.X = desired.X * speed;
        velocity.Z = desired.Z * speed;

        var isGrounded = groundedOverride ?? avatar.IsOnFloor();
        if (!isGrounded)
        {
            velocity.Y -= Gravity * delta;
        }
        else if (input.JumpPressed)
        {
            velocity.Y = JumpVelocity;
        }

        avatar.Velocity = velocity;
        avatar.MoveAndSlide();
    }
}
