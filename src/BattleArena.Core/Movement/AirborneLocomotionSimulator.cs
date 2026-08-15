using BattleArena.Core.Common;

namespace BattleArena.Core.Movement;

public sealed class AirborneLocomotionSimulator
{
    public MovementRuntimeState Simulate(
        MovementRuntimeState state,
        MovementCommand command,
        AirMovementAttributes attributes,
        SimulationRate simulationRate)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(attributes);
        if (state.LocomotionMode != LocomotionMode.Airborne)
        {
            throw new ArgumentException("The airborne simulator requires an airborne runtime state.", nameof(state));
        }

        var secondsPerTick = 1d / simulationRate.TicksPerSecond;
        var horizontalVelocity = state.HorizontalVelocity;
        if (command.Movement.Length > MovementMath.Epsilon)
        {
            var desiredDirection = MovementMath
                .RotateInputByViewYaw(command.Movement.Normalized, command.ViewYawRadians)
                .Normalized;
            var lateralWeight = Math.Abs(command.Movement.X);
            var forwardWeight = Math.Abs(command.Movement.Z);
            var totalWeight = lateralWeight + forwardWeight;
            var authoredAcceleration = forwardWeight > MovementMath.Epsilon
                ? attributes.ForwardAirAcceleration
                : attributes.LateralAirAcceleration;
            var selectedMaximumSpeed = command.IsHeld(MovementButtons.Sprint)
                ? attributes.MaximumAirSpeed
                : attributes.MaximumRunAirSpeed;
            var effectiveMaximumSpeed = Math.Max(horizontalVelocity.Length, selectedMaximumSpeed);
            var desiredVelocity = desiredDirection *
                (effectiveMaximumSpeed * command.Movement.Length);
            var highSpeedMultiplier = totalWeight <= MovementMath.Epsilon
                ? 1d
                : ((attributes.HighSpeedLateralControlMultiplier * lateralWeight) +
                    (attributes.HighSpeedForwardControlMultiplier * forwardWeight)) / totalWeight;
            var speedRatio = Math.Clamp(
                horizontalVelocity.Length / selectedMaximumSpeed,
                0d,
                1d);
            var controlMultiplier = Lerp(1d, highSpeedMultiplier, speedRatio * speedRatio);
            horizontalVelocity = MoveTowards(
                horizontalVelocity,
                desiredVelocity,
                authoredAcceleration * controlMultiplier * secondsPerTick);
        }

        var targetFacing = horizontalVelocity.Length > MovementMath.Epsilon
            ? MovementMath.FacingYawFromDirection(horizontalVelocity)
            : state.FacingYawRadians;

        return state with
        {
            HorizontalVelocity = horizontalVelocity,
            FacingYawRadians = MovementMath.MoveAngleTowards(
                state.FacingYawRadians,
                targetFacing,
                attributes.TurnRateRadians * secondsPerTick),
        };
    }

    private static HorizontalVector MoveTowards(
        HorizontalVector current,
        HorizontalVector target,
        double maximumDelta)
    {
        var difference = target - current;
        return difference.Length <= maximumDelta
            ? target
            : current + (difference.Normalized * maximumDelta);
    }

    private static double Lerp(double from, double to, double weight) =>
        from + ((to - from) * weight);
}
