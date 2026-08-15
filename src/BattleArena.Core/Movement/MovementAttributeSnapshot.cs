using BattleArena.Core.Common;

namespace BattleArena.Core.Movement;

public sealed record MovementAttributeSnapshot
{
    public MovementAttributeSnapshot(
        ulong revision,
        GroundMovementAttributes ground,
        AirMovementAttributes? air = null,
        JumpMovementAttributes? jump = null,
        CrouchRollAttributes? crouchRoll = null)
    {
        Revision = revision;
        Ground = ground ?? throw new ArgumentNullException(nameof(ground));
        Air = air ?? new AirMovementAttributes(
            forwardAirAcceleration: 23.5d,
            lateralAirAcceleration: 3.5d,
            maximumRunAirSpeed: ground.MaximumRunSpeed,
            maximumAirSpeed: ground.MaximumSprintSpeed,
            highSpeedForwardControlMultiplier: 0.5d,
            highSpeedLateralControlMultiplier: 0.15d,
            turnRateRadians: Math.PI * 180d / 180d);
        Jump = jump ?? new JumpMovementAttributes(
            jumpVelocity: 13.4d,
            jumpVelocityPerHorizontalSpeed: 0.04d,
            risingGravity: 23d,
            apexGravity: 16d,
            fallingGravity: 45d,
            maximumFallSpeed: 36d,
            jumpReleaseGravity: 55d,
            apexVelocityThreshold: 1.5d,
            coyoteDuration: new SimulationDuration(7),
            inputBufferDuration: new SimulationDuration(7));
        CrouchRoll = crouchRoll ?? new CrouchRollAttributes(
            rollEntrySpeed: 1d,
            maximumCrouchSpeed: 3d,
            minimumRollBoostDistance: 3d,
            maximumRollBoostDistance: 5d,
            minimumRollDuration: new SimulationDuration(19),
            maximumRollDuration: new SimulationDuration(51),
            rollCooldown: new SimulationDuration(21),
            lowSpeedSteeringRateRadians: Math.PI * (7d / 3d),
            highSpeedSteeringRateRadians: Math.PI / 2d,
            standingCapsuleHeight: 1.8d,
            crouchingCapsuleHeight: 1.25d,
            rollingCapsuleHeight: 0.95d,
            capsuleRadius: 0.42d);
    }

    public ulong Revision { get; }

    public GroundMovementAttributes Ground { get; }

    public AirMovementAttributes Air { get; }

    public JumpMovementAttributes Jump { get; }

    public CrouchRollAttributes CrouchRoll { get; }
}
