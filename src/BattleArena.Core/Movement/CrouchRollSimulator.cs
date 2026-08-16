using BattleArena.Core.Common;

namespace BattleArena.Core.Movement;

public sealed class CrouchRollSimulator
{
    private const double RollCurveAverage = 0.625d;

    public MovementRuntimeState Simulate(
        MovementRuntimeState state,
        MovementCommand command,
        bool isGrounded,
        bool canStand,
        CrouchRollAttributes attributes,
        JumpMovementAttributes jumpAttributes,
        GroundMovementAttributes groundAttributes,
        SimulationRate simulationRate)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(jumpAttributes);
        ArgumentNullException.ThrowIfNull(groundAttributes);

        return state.LocomotionMode == LocomotionMode.Rolling
            ? ContinueRoll(state, command, isGrounded, canStand, attributes, jumpAttributes, simulationRate)
            : ResolveContext(state, command, isGrounded, canStand, attributes, groundAttributes, simulationRate);
    }

    private static MovementRuntimeState ResolveContext(
        MovementRuntimeState state,
        MovementCommand command,
        bool isGrounded,
        bool canStand,
        CrouchRollAttributes attributes,
        GroundMovementAttributes groundAttributes,
        SimulationRate simulationRate)
    {
        if (!isGrounded || state.LocomotionMode != LocomotionMode.Grounded)
        {
            var landingRollQueued =
                state.LocomotionMode == LocomotionMode.Airborne &&
                command.IsHeld(MovementButtons.CrouchOrRoll) &&
                state.HorizontalVelocity.Length >= attributes.RollEntrySpeed;
            return state with { LandingRollQueued = landingRollQueued };
        }

        var now = command.ClientTick;
        var bufferedLandingRoll =
            state.LandingRollQueued &&
            command.IsHeld(MovementButtons.CrouchOrRoll);
        if (command.WasPressed(MovementButtons.CrouchOrRoll) || bufferedLandingRoll)
        {
            var canRoll = state.HorizontalVelocity.Length >= attributes.RollEntrySpeed &&
                now >= state.RollAvailableAt;
            if (canRoll)
            {
                return StartRoll(state, command, attributes, groundAttributes, simulationRate);
            }

            return state with
            {
                PostureMode = PostureMode.Crouched,
                LandingRollQueued = false,
            };
        }

        if (state.PostureMode == PostureMode.Crouched)
        {
            if (command.WasPressed(MovementButtons.Jump) && canStand)
            {
                return state with { PostureMode = PostureMode.Standing };
            }

            if (command.IsHeld(MovementButtons.CrouchOrRoll) || !canStand)
            {
                return state;
            }

            return state with { PostureMode = PostureMode.Standing };
        }

        return state;
    }

    private static MovementRuntimeState StartRoll(
        MovementRuntimeState state,
        MovementCommand command,
        CrouchRollAttributes attributes,
        GroundMovementAttributes groundAttributes,
        SimulationRate simulationRate)
    {
        var normalizedEntrySpeed = Math.Clamp(
            (state.HorizontalVelocity.Length - attributes.RollEntrySpeed) /
            Math.Max(MovementMath.Epsilon, groundAttributes.MaximumSprintSpeed - attributes.RollEntrySpeed),
            0d,
            1d);
        var duration = attributes.MaximumRollDuration;
        var boostDistance = Lerp(
            attributes.MinimumRollBoostDistance,
            attributes.MaximumRollBoostDistance,
            SmoothStep(normalizedEntrySpeed));
        var requestedDirection = command.Movement.Length > MovementMath.Epsilon
            ? MovementMath.RotateInputByViewYaw(command.Movement.Normalized, command.ViewYawRadians).Normalized
            : state.HorizontalVelocity.Normalized;
        var direction = requestedDirection == HorizontalVector.Zero
            ? DirectionFromFacing(state.FacingYawRadians)
            : requestedDirection;
        var initialBoostSpeed = boostDistance / (simulationRate.SecondsFromDuration(duration) is var seconds && seconds > 0m
            ? (double)seconds * RollCurveAverage
            : 1d);

        return state with
        {
            HorizontalVelocity = direction * (state.HorizontalVelocity.Length + initialBoostSpeed),
            LocomotionMode = LocomotionMode.Rolling,
            PostureMode = PostureMode.Crouched,
            ModeStartedAt = command.ClientTick,
            RollDirection = direction,
            RollEntrySpeed = state.HorizontalVelocity.Length,
            RollBoostDistance = boostDistance,
            RollDuration = duration,
            FacingYawRadians = MovementMath.FacingYawFromDirection(direction),
            LandingRollQueued = false,
        };
    }

    private static MovementRuntimeState ContinueRoll(
        MovementRuntimeState state,
        MovementCommand command,
        bool isGrounded,
        bool canStand,
        CrouchRollAttributes attributes,
        JumpMovementAttributes jumpAttributes,
        SimulationRate simulationRate)
    {
        var elapsedTicks = Math.Max(0L, command.ClientTick.Tick - state.ModeStartedAt.Tick);
        var releasedAfterMinimum =
            elapsedTicks >= attributes.MinimumRollDuration.Ticks &&
            !command.IsHeld(MovementButtons.CrouchOrRoll);
        if (elapsedTicks >= state.RollDuration.Ticks || releasedAfterMinimum)
        {
            var crouched = command.IsHeld(MovementButtons.CrouchOrRoll) || !canStand;
            return state with
            {
                HorizontalVelocity = state.RollDirection * state.RollEntrySpeed,
                LocomotionMode = isGrounded ? LocomotionMode.Grounded : LocomotionMode.Airborne,
                PostureMode = crouched ? PostureMode.Crouched : PostureMode.Standing,
                JumpPhase = isGrounded ? JumpPhase.None : JumpPhase.Falling,
                ModeStartedAt = command.ClientTick,
                RollAvailableAt = command.ClientTick + attributes.RollCooldown,
                LandingRollQueued = false,
            };
        }

        var progress = state.RollDuration.Ticks == 0
            ? 1d
            : Math.Clamp((double)elapsedTicks / state.RollDuration.Ticks, 0d, 1d);
        var curveScale = 1d - (0.75d * SmoothStep(progress));
        var durationSeconds = (double)simulationRate.SecondsFromDuration(state.RollDuration);
        var initialBoostSpeed = state.RollBoostDistance /
            Math.Max(MovementMath.Epsilon, durationSeconds * RollCurveAverage);
        var speed = state.RollEntrySpeed + (initialBoostSpeed * curveScale);
        var direction = state.RollDirection;
        if (command.Movement.Length > MovementMath.Epsilon)
        {
            var target = MovementMath.RotateInputByViewYaw(
                command.Movement.Normalized,
                command.ViewYawRadians).Normalized;
            var speedRatio = Math.Clamp(
                speed / Math.Max(MovementMath.Epsilon, state.RollEntrySpeed + initialBoostSpeed),
                0d,
                1d);
            var turnRate = Lerp(
                attributes.LowSpeedSteeringRateRadians,
                attributes.HighSpeedSteeringRateRadians,
                speedRatio);
            direction = MovementMath.RotateTowards(
                direction,
                target,
                turnRate / simulationRate.TicksPerSecond);
        }

        var verticalVelocity = isGrounded
            ? 0d
            : Math.Max(
                state.VerticalVelocity - (jumpAttributes.FallingGravity / simulationRate.TicksPerSecond),
                -jumpAttributes.MaximumFallSpeed);
        return state with
        {
            HorizontalVelocity = direction * speed,
            VerticalVelocity = verticalVelocity,
            RollDirection = direction,
            FacingYawRadians = MovementMath.FacingYawFromDirection(direction),
            JumpPhase = isGrounded ? JumpPhase.None : JumpPhase.Falling,
        };
    }

    private static HorizontalVector DirectionFromFacing(double yaw) =>
        new HorizontalVector(-Math.Sin(yaw), -Math.Cos(yaw)).Normalized;

    private static double SmoothStep(double value)
    {
        var clamped = Math.Clamp(value, 0d, 1d);
        return clamped * clamped * (3d - (2d * clamped));
    }

    private static double Lerp(double from, double to, double weight) =>
        from + ((to - from) * weight);
}
