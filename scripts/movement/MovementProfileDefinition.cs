#nullable enable

namespace BattleArena.Movement;

public sealed class MovementProfileDefinition
{
    public int SchemaVersion { get; init; }

    public string Id { get; init; } = "";

    public GroundMovementDefinition Ground { get; init; } = new();

    public AirMovementDefinition Air { get; init; } = new();

    public JumpMovementDefinition Jump { get; init; } = new();

    public CrouchRollDefinition CrouchRoll { get; init; } = new();
}

public sealed class AirMovementDefinition
{
    public double ForwardAirAcceleration { get; init; }

    public double LateralAirAcceleration { get; init; }

    public double MaximumRunAirSpeed { get; init; }

    public double MaximumAirSpeed { get; init; }

    public double HighSpeedForwardControlMultiplier { get; init; }

    public double HighSpeedLateralControlMultiplier { get; init; }

    public double TurnRateDegrees { get; init; }
}

public sealed class JumpMovementDefinition
{
    public double JumpVelocity { get; init; }
    public double JumpVelocityPerHorizontalSpeed { get; init; }
    public double RisingGravity { get; init; }
    public double ApexGravity { get; init; }
    public double FallingGravity { get; init; }
    public double MaximumFallSpeed { get; init; }
    public double JumpReleaseGravity { get; init; }
    public double ApexVelocityThreshold { get; init; }
    public decimal CoyoteTimeSeconds { get; init; }
    public decimal InputBufferSeconds { get; init; }
}

public sealed class CrouchRollDefinition
{
    public double RollEntrySpeed { get; init; }
    public double MaximumCrouchSpeed { get; init; }
    public double MinimumRollBoostDistance { get; init; }
    public double MaximumRollBoostDistance { get; init; }
    public decimal MinimumRollDurationSeconds { get; init; }
    public decimal MaximumRollDurationSeconds { get; init; }
    public decimal RollCooldownSeconds { get; init; }
    public double LowSpeedSteeringRateDegrees { get; init; }
    public double HighSpeedSteeringRateDegrees { get; init; }
    public double StandingCapsuleHeight { get; init; }
    public double CrouchingCapsuleHeight { get; init; }
    public double RollingCapsuleHeight { get; init; }
    public double CapsuleRadius { get; init; }
}

public sealed class GroundMovementDefinition
{
    public double MaximumRunSpeed { get; init; }

    public double MaximumSprintSpeed { get; init; }

    public double RunAcceleration { get; init; }

    public double SprintAcceleration { get; init; }

    public double BrakingDeceleration { get; init; }

    public double ReversalDeceleration { get; init; }

    public double LowSpeedTurnRateDegrees { get; init; }

    public double HighSpeedTurnRateDegrees { get; init; }

    public double ReversalDotThreshold { get; init; }

    public double MaximumStepHeight { get; init; }

    public double FloorSnapDistance { get; init; }

    public double MaximumFloorAngleDegrees { get; init; }

    public double StepForwardAssistDistance { get; init; }
}
