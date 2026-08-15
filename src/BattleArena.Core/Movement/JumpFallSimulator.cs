using BattleArena.Core.Common;

namespace BattleArena.Core.Movement;

public sealed class JumpFallSimulator
{
    public MovementRuntimeState Simulate(
        MovementRuntimeState state,
        MovementCommand command,
        bool isGrounded,
        JumpMovementAttributes attributes,
        SimulationRate simulationRate)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(attributes);

        var now = command.ClientTick;
        var bufferedUntil = command.WasPressed(MovementButtons.Jump)
            ? now + attributes.InputBufferDuration
            : state.BufferedJumpUntil;
        if (bufferedUntil is { } expiry && now > expiry)
        {
            bufferedUntil = null;
        }

        if (isGrounded)
        {
            var grounded = state with
            {
                LocomotionMode = LocomotionMode.Grounded,
                VerticalVelocity = 0d,
                JumpPhase = JumpPhase.None,
                LastGroundedAt = now,
                BufferedJumpUntil = bufferedUntil,
                JumpCutApplied = false,
            };

            return bufferedUntil is not null
                ? Launch(grounded, now, attributes)
                : grounded;
        }

        var airborne = state.LocomotionMode == LocomotionMode.Grounded
            ? state with
            {
                LocomotionMode = LocomotionMode.Airborne,
                JumpPhase = JumpPhase.Falling,
                ModeStartedAt = now,
            }
            : state;
        airborne = airborne with { BufferedJumpUntil = bufferedUntil };

        var insideCoyoteWindow = now.Tick - airborne.LastGroundedAt.Tick <= attributes.CoyoteDuration.Ticks;
        if (bufferedUntil is not null && insideCoyoteWindow)
        {
            return Launch(airborne, now, attributes);
        }

        var verticalVelocity = airborne.VerticalVelocity;
        var jumpCutApplied = airborne.JumpCutApplied;
        if (command.WasReleased(MovementButtons.Jump) &&
            !jumpCutApplied &&
            verticalVelocity > 0d)
        {
            jumpCutApplied = true;
        }

        var gravity = airborne.JumpPhase == JumpPhase.Falling && verticalVelocity <= 0d
            ? attributes.FallingGravity
            : SelectGravity(verticalVelocity, jumpCutApplied, attributes);
        verticalVelocity = Math.Max(
            verticalVelocity - (gravity / simulationRate.TicksPerSecond),
            -attributes.MaximumFallSpeed);
        var nextPhase = airborne.JumpPhase == JumpPhase.Falling && verticalVelocity <= 0d
            ? JumpPhase.Falling
            : SelectPhase(verticalVelocity, attributes.ApexVelocityThreshold);

        return airborne with
        {
            VerticalVelocity = verticalVelocity,
            JumpPhase = nextPhase,
            JumpCutApplied = jumpCutApplied,
        };
    }

    private static MovementRuntimeState Launch(
        MovementRuntimeState state,
        SimulationInstant now,
        JumpMovementAttributes attributes) =>
        state with
        {
            LocomotionMode = LocomotionMode.Airborne,
            VerticalVelocity = attributes.JumpVelocity +
                (state.HorizontalVelocity.Length * attributes.JumpVelocityPerHorizontalSpeed),
            JumpPhase = JumpPhase.Rising,
            ModeStartedAt = now,
            BufferedJumpUntil = null,
            JumpCutApplied = false,
        };

    private static double SelectGravity(
        double velocity,
        bool jumpReleased,
        JumpMovementAttributes attributes)
    {
        var risingGravity = jumpReleased
            ? attributes.JumpReleaseGravity
            : attributes.RisingGravity;
        if (velocity > attributes.ApexVelocityThreshold)
        {
            return risingGravity;
        }

        if (velocity >= 0d)
        {
            var progress = 1d - (velocity / attributes.ApexVelocityThreshold);
            return SmoothLerp(risingGravity, attributes.ApexGravity, progress);
        }

        if (velocity >= -attributes.ApexVelocityThreshold)
        {
            var progress = -velocity / attributes.ApexVelocityThreshold;
            return SmoothLerp(attributes.ApexGravity, attributes.FallingGravity, progress);
        }

        return attributes.FallingGravity;
    }

    private static double SmoothLerp(double from, double to, double progress)
    {
        var clamped = Math.Clamp(progress, 0d, 1d);
        var smooth = clamped * clamped * (3d - (2d * clamped));
        return from + ((to - from) * smooth);
    }

    private static JumpPhase SelectPhase(double velocity, double apexThreshold)
    {
        if (velocity > apexThreshold)
        {
            return JumpPhase.Rising;
        }

        return velocity >= -apexThreshold ? JumpPhase.Apex : JumpPhase.Falling;
    }
}
