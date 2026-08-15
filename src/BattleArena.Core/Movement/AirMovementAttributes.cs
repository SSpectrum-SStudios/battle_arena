namespace BattleArena.Core.Movement;

public sealed record AirMovementAttributes
{
    public AirMovementAttributes(
        double forwardAirAcceleration,
        double lateralAirAcceleration,
        double maximumRunAirSpeed,
        double maximumAirSpeed,
        double highSpeedForwardControlMultiplier,
        double highSpeedLateralControlMultiplier,
        double turnRateRadians)
    {
        ValidatePositive(forwardAirAcceleration, nameof(forwardAirAcceleration));
        ValidatePositive(lateralAirAcceleration, nameof(lateralAirAcceleration));
        ValidatePositive(maximumRunAirSpeed, nameof(maximumRunAirSpeed));
        ValidatePositive(maximumAirSpeed, nameof(maximumAirSpeed));
        if (maximumRunAirSpeed > maximumAirSpeed)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumRunAirSpeed),
                "Run air speed cannot exceed maximum sprint air speed.");
        }
        ValidateMultiplier(highSpeedForwardControlMultiplier, nameof(highSpeedForwardControlMultiplier));
        ValidateMultiplier(highSpeedLateralControlMultiplier, nameof(highSpeedLateralControlMultiplier));
        ValidatePositive(turnRateRadians, nameof(turnRateRadians));

        ForwardAirAcceleration = forwardAirAcceleration;
        LateralAirAcceleration = lateralAirAcceleration;
        MaximumRunAirSpeed = maximumRunAirSpeed;
        MaximumAirSpeed = maximumAirSpeed;
        HighSpeedForwardControlMultiplier = highSpeedForwardControlMultiplier;
        HighSpeedLateralControlMultiplier = highSpeedLateralControlMultiplier;
        TurnRateRadians = turnRateRadians;
    }

    public double ForwardAirAcceleration { get; }

    public double LateralAirAcceleration { get; }

    public double MaximumRunAirSpeed { get; }

    public double MaximumAirSpeed { get; }

    public double HighSpeedForwardControlMultiplier { get; }

    public double HighSpeedLateralControlMultiplier { get; }

    public double TurnRateRadians { get; }

    private static void ValidatePositive(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value <= 0d)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Air movement values must be positive and finite.");
        }
    }

    private static void ValidateMultiplier(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0d || value > 1d)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Air-control multipliers must be finite and between zero and one.");
        }
    }
}
