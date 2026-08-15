using BattleArena.Core.Common;

namespace BattleArena.Core.Movement;

public sealed record CrouchRollAttributes
{
    public CrouchRollAttributes(
        double rollEntrySpeed,
        double maximumCrouchSpeed,
        double minimumRollBoostDistance,
        double maximumRollBoostDistance,
        SimulationDuration minimumRollDuration,
        SimulationDuration maximumRollDuration,
        SimulationDuration rollCooldown,
        double lowSpeedSteeringRateRadians,
        double highSpeedSteeringRateRadians,
        double standingCapsuleHeight,
        double crouchingCapsuleHeight,
        double rollingCapsuleHeight,
        double capsuleRadius)
    {
        ValidatePositive(rollEntrySpeed, nameof(rollEntrySpeed));
        ValidatePositive(maximumCrouchSpeed, nameof(maximumCrouchSpeed));
        ValidatePositive(minimumRollBoostDistance, nameof(minimumRollBoostDistance));
        ValidatePositive(maximumRollBoostDistance, nameof(maximumRollBoostDistance));
        ValidatePositive(lowSpeedSteeringRateRadians, nameof(lowSpeedSteeringRateRadians));
        ValidatePositive(highSpeedSteeringRateRadians, nameof(highSpeedSteeringRateRadians));
        ValidatePositive(standingCapsuleHeight, nameof(standingCapsuleHeight));
        ValidatePositive(crouchingCapsuleHeight, nameof(crouchingCapsuleHeight));
        ValidatePositive(rollingCapsuleHeight, nameof(rollingCapsuleHeight));
        ValidatePositive(capsuleRadius, nameof(capsuleRadius));
        if (maximumRollBoostDistance < minimumRollBoostDistance)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRollBoostDistance));
        }

        if (maximumRollDuration < minimumRollDuration)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRollDuration));
        }

        if (crouchingCapsuleHeight > standingCapsuleHeight ||
            rollingCapsuleHeight > crouchingCapsuleHeight ||
            rollingCapsuleHeight < capsuleRadius * 2d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rollingCapsuleHeight),
                "Collision profiles must descend from standing to crouching to rolling and fit the radius.");
        }

        RollEntrySpeed = rollEntrySpeed;
        MaximumCrouchSpeed = maximumCrouchSpeed;
        MinimumRollBoostDistance = minimumRollBoostDistance;
        MaximumRollBoostDistance = maximumRollBoostDistance;
        MinimumRollDuration = minimumRollDuration;
        MaximumRollDuration = maximumRollDuration;
        RollCooldown = rollCooldown;
        LowSpeedSteeringRateRadians = lowSpeedSteeringRateRadians;
        HighSpeedSteeringRateRadians = highSpeedSteeringRateRadians;
        StandingCapsuleHeight = standingCapsuleHeight;
        CrouchingCapsuleHeight = crouchingCapsuleHeight;
        RollingCapsuleHeight = rollingCapsuleHeight;
        CapsuleRadius = capsuleRadius;
    }

    public double RollEntrySpeed { get; }
    public double MaximumCrouchSpeed { get; }
    public double MinimumRollBoostDistance { get; }
    public double MaximumRollBoostDistance { get; }
    public SimulationDuration MinimumRollDuration { get; }
    public SimulationDuration MaximumRollDuration { get; }
    public SimulationDuration RollCooldown { get; }
    public double LowSpeedSteeringRateRadians { get; }
    public double HighSpeedSteeringRateRadians { get; }
    public double StandingCapsuleHeight { get; }
    public double CrouchingCapsuleHeight { get; }
    public double RollingCapsuleHeight { get; }
    public double CapsuleRadius { get; }

    private static void ValidatePositive(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value <= 0d)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Roll values must be positive and finite.");
        }
    }
}
