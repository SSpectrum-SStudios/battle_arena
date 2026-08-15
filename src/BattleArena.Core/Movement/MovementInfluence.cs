namespace BattleArena.Core.Movement;

public sealed record MovementInfluence
{
    public static readonly MovementInfluence Unrestricted = new();

    public MovementInfluence(
        double accelerationMultiplier = 1d,
        double steeringMultiplier = 1d,
        double additionalDeceleration = 0d,
        bool preserveMomentumAboveTargetSpeed = false,
        bool allowSprintAcceleration = true)
    {
        if (!double.IsFinite(accelerationMultiplier) || accelerationMultiplier < 0d ||
            !double.IsFinite(steeringMultiplier) || steeringMultiplier < 0d ||
            !double.IsFinite(additionalDeceleration) || additionalDeceleration < 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(accelerationMultiplier),
                "Movement influence values must be finite and non-negative.");
        }

        AccelerationMultiplier = accelerationMultiplier;
        SteeringMultiplier = steeringMultiplier;
        AdditionalDeceleration = additionalDeceleration;
        PreserveMomentumAboveTargetSpeed = preserveMomentumAboveTargetSpeed;
        AllowSprintAcceleration = allowSprintAcceleration;
    }

    public double AccelerationMultiplier { get; }

    public double SteeringMultiplier { get; }

    public double AdditionalDeceleration { get; }

    public bool PreserveMomentumAboveTargetSpeed { get; }

    public bool AllowSprintAcceleration { get; }
}
