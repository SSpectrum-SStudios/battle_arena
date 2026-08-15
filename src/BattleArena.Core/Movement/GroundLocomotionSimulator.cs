using BattleArena.Core.Common;

namespace BattleArena.Core.Movement;

public sealed class GroundLocomotionSimulator
{
    public MovementRuntimeState Simulate(
        MovementRuntimeState state,
        MovementCommand command,
        GroundMovementAttributes attributes,
        SimulationRate simulationRate,
        MovementInfluence? influence = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(attributes);

        if (state.LocomotionMode != LocomotionMode.Grounded)
        {
            throw new ArgumentException("The grounded simulator requires a grounded runtime state.", nameof(state));
        }

        influence ??= MovementInfluence.Unrestricted;
        var secondsPerTick = 1d / simulationRate.TicksPerSecond;
        state = ApplyAdditionalDeceleration(state, influence, secondsPerTick);
        var inputMagnitude = command.Movement.Length;
        if (inputMagnitude <= MovementMath.Epsilon)
        {
            if (influence.PreserveMomentumAboveTargetSpeed)
            {
                return state;
            }

            return ApplyBraking(state, attributes, secondsPerTick);
        }

        var desiredDirection = MovementMath
            .RotateInputByViewYaw(command.Movement.Normalized, command.ViewYawRadians)
            .Normalized;
        var sprinting = command.IsHeld(MovementButtons.Sprint) &&
            influence.AllowSprintAcceleration;
        var maximumSpeed = sprinting ? attributes.MaximumSprintSpeed : attributes.MaximumRunSpeed;
        var targetSpeed = maximumSpeed * inputMagnitude;
        var acceleration = (sprinting ? attributes.SprintAcceleration : attributes.RunAcceleration) *
            influence.AccelerationMultiplier;
        var currentVelocity = state.HorizontalVelocity;
        var currentSpeed = currentVelocity.Length;
        if (influence.PreserveMomentumAboveTargetSpeed && currentSpeed > targetSpeed)
        {
            targetSpeed = currentSpeed;
        }

        if (currentSpeed <= MovementMath.Epsilon)
        {
            var startingSpeed = Math.Min(targetSpeed, acceleration * secondsPerTick);
            return WithGroundMotion(state, desiredDirection * startingSpeed, desiredDirection, attributes, secondsPerTick);
        }

        var currentDirection = currentVelocity.Normalized;
        var directionAlignment = currentDirection.Dot(desiredDirection);
        if (directionAlignment <= attributes.ReversalDotThreshold)
        {
            var slowedSpeed = MovementMath.MoveTowards(
                currentSpeed,
                0d,
                attributes.ReversalDeceleration *
                influence.AccelerationMultiplier *
                secondsPerTick);
            var slowedVelocity = slowedSpeed <= MovementMath.Epsilon
                ? HorizontalVector.Zero
                : currentDirection * slowedSpeed;
            return WithGroundMotion(state, slowedVelocity, currentDirection, attributes, secondsPerTick);
        }

        var normalizedSpeed = Math.Clamp(currentSpeed / attributes.MaximumSprintSpeed, 0d, 1d);
        var turnRate = Lerp(
            attributes.LowSpeedTurnRateRadians,
            attributes.HighSpeedTurnRateRadians,
            normalizedSpeed);
        var steeredDirection = MovementMath.RotateTowards(
            currentDirection,
            desiredDirection,
            turnRate * influence.SteeringMultiplier * secondsPerTick);
        var speedChangeRate = currentSpeed > targetSpeed
            ? attributes.BrakingDeceleration
            : acceleration;
        var nextSpeed = MovementMath.MoveTowards(currentSpeed, targetSpeed, speedChangeRate * secondsPerTick);
        return WithGroundMotion(
            state,
            steeredDirection * nextSpeed,
            steeredDirection,
            attributes,
            secondsPerTick,
            influence.SteeringMultiplier);
    }

    private static MovementRuntimeState ApplyBraking(
        MovementRuntimeState state,
        GroundMovementAttributes attributes,
        double secondsPerTick)
    {
        var currentSpeed = state.HorizontalVelocity.Length;
        if (currentSpeed <= MovementMath.Epsilon)
        {
            return state with { HorizontalVelocity = HorizontalVector.Zero };
        }

        var nextSpeed = MovementMath.MoveTowards(
            currentSpeed,
            0d,
            attributes.BrakingDeceleration * secondsPerTick);
        var nextVelocity = nextSpeed <= MovementMath.Epsilon
            ? HorizontalVector.Zero
            : state.HorizontalVelocity.Normalized * nextSpeed;
        return state with { HorizontalVelocity = nextVelocity };
    }

    private static MovementRuntimeState WithGroundMotion(
        MovementRuntimeState state,
        HorizontalVector velocity,
        HorizontalVector facingDirection,
        GroundMovementAttributes attributes,
        double secondsPerTick,
        double steeringMultiplier = 1d)
    {
        if (velocity == HorizontalVector.Zero)
        {
            return state with { HorizontalVelocity = HorizontalVector.Zero };
        }

        var targetFacing = MovementMath.FacingYawFromDirection(facingDirection);
        var normalizedSpeed = Math.Clamp(velocity.Length / attributes.MaximumSprintSpeed, 0d, 1d);
        var facingTurnRate = Lerp(
            attributes.LowSpeedTurnRateRadians,
            attributes.HighSpeedTurnRateRadians,
            normalizedSpeed);
        return state with
        {
            HorizontalVelocity = velocity,
            FacingYawRadians = MovementMath.MoveAngleTowards(
                state.FacingYawRadians,
                targetFacing,
                facingTurnRate * steeringMultiplier * secondsPerTick),
        };
    }

    private static MovementRuntimeState ApplyAdditionalDeceleration(
        MovementRuntimeState state,
        MovementInfluence influence,
        double secondsPerTick)
    {
        var speed = state.HorizontalVelocity.Length;
        if (speed <= MovementMath.Epsilon || influence.AdditionalDeceleration <= 0d)
        {
            return state;
        }

        var nextSpeed = MovementMath.MoveTowards(
            speed,
            0d,
            influence.AdditionalDeceleration * secondsPerTick);
        return state with
        {
            HorizontalVelocity = nextSpeed <= MovementMath.Epsilon
                ? HorizontalVector.Zero
                : state.HorizontalVelocity.Normalized * nextSpeed,
        };
    }

    private static double Lerp(double from, double to, double weight) => from + ((to - from) * weight);
}
