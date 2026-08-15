using BattleArena.Core.Common;

namespace BattleArena.Core.Movement;

public sealed record JumpMovementAttributes
{
    public JumpMovementAttributes(
        double jumpVelocity,
        double jumpVelocityPerHorizontalSpeed,
        double risingGravity,
        double apexGravity,
        double fallingGravity,
        double maximumFallSpeed,
        double jumpReleaseGravity,
        double apexVelocityThreshold,
        SimulationDuration coyoteDuration,
        SimulationDuration inputBufferDuration)
    {
        ValidatePositive(jumpVelocity, nameof(jumpVelocity));
        ValidateNonNegative(jumpVelocityPerHorizontalSpeed, nameof(jumpVelocityPerHorizontalSpeed));
        ValidatePositive(risingGravity, nameof(risingGravity));
        ValidatePositive(apexGravity, nameof(apexGravity));
        ValidatePositive(fallingGravity, nameof(fallingGravity));
        ValidatePositive(maximumFallSpeed, nameof(maximumFallSpeed));
        ValidatePositive(jumpReleaseGravity, nameof(jumpReleaseGravity));
        ValidatePositive(apexVelocityThreshold, nameof(apexVelocityThreshold));

        JumpVelocity = jumpVelocity;
        JumpVelocityPerHorizontalSpeed = jumpVelocityPerHorizontalSpeed;
        RisingGravity = risingGravity;
        ApexGravity = apexGravity;
        FallingGravity = fallingGravity;
        MaximumFallSpeed = maximumFallSpeed;
        JumpReleaseGravity = jumpReleaseGravity;
        ApexVelocityThreshold = apexVelocityThreshold;
        CoyoteDuration = coyoteDuration;
        InputBufferDuration = inputBufferDuration;
    }

    public double JumpVelocity { get; }
    public double JumpVelocityPerHorizontalSpeed { get; }
    public double RisingGravity { get; }
    public double ApexGravity { get; }
    public double FallingGravity { get; }
    public double MaximumFallSpeed { get; }
    public double JumpReleaseGravity { get; }
    public double ApexVelocityThreshold { get; }
    public SimulationDuration CoyoteDuration { get; }
    public SimulationDuration InputBufferDuration { get; }

    private static void ValidatePositive(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value <= 0d)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Jump values must be positive and finite.");
        }
    }

    private static void ValidateNonNegative(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0d)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Jump values must be non-negative and finite.");
        }
    }
}
