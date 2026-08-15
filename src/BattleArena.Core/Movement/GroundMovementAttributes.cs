namespace BattleArena.Core.Movement;

public sealed record GroundMovementAttributes
{
    public GroundMovementAttributes(
        double maximumRunSpeed,
        double maximumSprintSpeed,
        double runAcceleration,
        double sprintAcceleration,
        double brakingDeceleration,
        double reversalDeceleration,
        double lowSpeedTurnRateRadians,
        double highSpeedTurnRateRadians,
        double reversalDotThreshold,
        double maximumStepHeight = 0.4d,
        double floorSnapDistance = 0.5d,
        double maximumFloorAngleRadians = Math.PI * 50d / 180d,
        double stepForwardAssistDistance = 0.08d)
    {
        ValidatePositive(maximumRunSpeed, nameof(maximumRunSpeed));
        ValidatePositive(maximumSprintSpeed, nameof(maximumSprintSpeed));
        ValidatePositive(runAcceleration, nameof(runAcceleration));
        ValidatePositive(sprintAcceleration, nameof(sprintAcceleration));
        ValidatePositive(brakingDeceleration, nameof(brakingDeceleration));
        ValidatePositive(reversalDeceleration, nameof(reversalDeceleration));
        ValidatePositive(lowSpeedTurnRateRadians, nameof(lowSpeedTurnRateRadians));
        ValidatePositive(highSpeedTurnRateRadians, nameof(highSpeedTurnRateRadians));
        ValidatePositive(maximumStepHeight, nameof(maximumStepHeight));
        ValidatePositive(floorSnapDistance, nameof(floorSnapDistance));
        ValidatePositive(stepForwardAssistDistance, nameof(stepForwardAssistDistance));

        if (maximumSprintSpeed < maximumRunSpeed)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumSprintSpeed),
                "Sprint speed cannot be lower than normal running speed.");
        }

        if (lowSpeedTurnRateRadians < highSpeedTurnRateRadians)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lowSpeedTurnRateRadians),
                "Low-speed turning must be at least as responsive as high-speed turning.");
        }

        if (!double.IsFinite(reversalDotThreshold) || reversalDotThreshold is < -1d or > 1d)
        {
            throw new ArgumentOutOfRangeException(nameof(reversalDotThreshold));
        }

        if (!double.IsFinite(maximumFloorAngleRadians) ||
            maximumFloorAngleRadians <= 0d ||
            maximumFloorAngleRadians >= Math.PI / 2d)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumFloorAngleRadians));
        }

        MaximumRunSpeed = maximumRunSpeed;
        MaximumSprintSpeed = maximumSprintSpeed;
        RunAcceleration = runAcceleration;
        SprintAcceleration = sprintAcceleration;
        BrakingDeceleration = brakingDeceleration;
        ReversalDeceleration = reversalDeceleration;
        LowSpeedTurnRateRadians = lowSpeedTurnRateRadians;
        HighSpeedTurnRateRadians = highSpeedTurnRateRadians;
        ReversalDotThreshold = reversalDotThreshold;
        MaximumStepHeight = maximumStepHeight;
        FloorSnapDistance = floorSnapDistance;
        MaximumFloorAngleRadians = maximumFloorAngleRadians;
        StepForwardAssistDistance = stepForwardAssistDistance;
    }

    public double MaximumRunSpeed { get; }

    public double MaximumSprintSpeed { get; }

    public double RunAcceleration { get; }

    public double SprintAcceleration { get; }

    public double BrakingDeceleration { get; }

    public double ReversalDeceleration { get; }

    public double LowSpeedTurnRateRadians { get; }

    public double HighSpeedTurnRateRadians { get; }

    public double ReversalDotThreshold { get; }

    public double MaximumStepHeight { get; }

    public double FloorSnapDistance { get; }

    public double MaximumFloorAngleRadians { get; }

    public double StepForwardAssistDistance { get; }

    private static void ValidatePositive(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value <= 0d)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Movement values must be positive and finite.");
        }
    }
}
