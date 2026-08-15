using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Multiplayer.Protocol;
using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Replication;

public static class MovementConfigurationProtocolMapper
{
    public static AuthorityMovementConfigurationUpdate ToProtocol(
        NetworkMovementConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var ground = configuration.Attributes.Ground;
        var air = configuration.Attributes.Air;
        var jump = configuration.Attributes.Jump;
        var roll = configuration.Attributes.CrouchRoll;
        var capabilities = configuration.Capabilities;
        return new AuthorityMovementConfigurationUpdate
        {
            CombatantId = configuration.CombatantId,
            LifeId = configuration.LifeId,
            MovementProfileRevision = configuration.Attributes.Revision,
            MovementCapabilityRevision = capabilities.Revision,
            EffectiveAuthorityTick = checked((ulong)configuration.EffectiveAuthorityTick.Tick),
            Ground = new ResolvedGroundMovementAttributes
            {
                MaximumRunSpeed = ground.MaximumRunSpeed,
                MaximumSprintSpeed = ground.MaximumSprintSpeed,
                RunAcceleration = ground.RunAcceleration,
                SprintAcceleration = ground.SprintAcceleration,
                BrakingDeceleration = ground.BrakingDeceleration,
                ReversalDeceleration = ground.ReversalDeceleration,
                LowSpeedTurnRateRadians = ground.LowSpeedTurnRateRadians,
                HighSpeedTurnRateRadians = ground.HighSpeedTurnRateRadians,
                ReversalDotThreshold = ground.ReversalDotThreshold,
                MaximumStepHeight = ground.MaximumStepHeight,
                FloorSnapDistance = ground.FloorSnapDistance,
                MaximumFloorAngleRadians = ground.MaximumFloorAngleRadians,
                StepForwardAssistDistance = ground.StepForwardAssistDistance,
            },
            Air = new ResolvedAirMovementAttributes
            {
                ForwardAirAcceleration = air.ForwardAirAcceleration,
                LateralAirAcceleration = air.LateralAirAcceleration,
                MaximumRunAirSpeed = air.MaximumRunAirSpeed,
                MaximumAirSpeed = air.MaximumAirSpeed,
                HighSpeedForwardControlMultiplier = air.HighSpeedForwardControlMultiplier,
                HighSpeedLateralControlMultiplier = air.HighSpeedLateralControlMultiplier,
                TurnRateRadians = air.TurnRateRadians,
            },
            Jump = new ResolvedJumpMovementAttributes
            {
                JumpVelocity = jump.JumpVelocity,
                JumpVelocityPerHorizontalSpeed = jump.JumpVelocityPerHorizontalSpeed,
                RisingGravity = jump.RisingGravity,
                ApexGravity = jump.ApexGravity,
                FallingGravity = jump.FallingGravity,
                MaximumFallSpeed = jump.MaximumFallSpeed,
                JumpReleaseGravity = jump.JumpReleaseGravity,
                ApexVelocityThreshold = jump.ApexVelocityThreshold,
                CoyoteDurationTicks = checked((ulong)jump.CoyoteDuration.Ticks),
                InputBufferDurationTicks = checked((ulong)jump.InputBufferDuration.Ticks),
            },
            CrouchRoll = new ResolvedCrouchRollAttributes
            {
                RollEntrySpeed = roll.RollEntrySpeed,
                MaximumCrouchSpeed = roll.MaximumCrouchSpeed,
                MinimumRollBoostDistance = roll.MinimumRollBoostDistance,
                MaximumRollBoostDistance = roll.MaximumRollBoostDistance,
                MinimumRollDurationTicks = checked((ulong)roll.MinimumRollDuration.Ticks),
                MaximumRollDurationTicks = checked((ulong)roll.MaximumRollDuration.Ticks),
                RollCooldownTicks = checked((ulong)roll.RollCooldown.Ticks),
                LowSpeedSteeringRateRadians = roll.LowSpeedSteeringRateRadians,
                HighSpeedSteeringRateRadians = roll.HighSpeedSteeringRateRadians,
                StandingCapsuleHeight = roll.StandingCapsuleHeight,
                CrouchingCapsuleHeight = roll.CrouchingCapsuleHeight,
                RollingCapsuleHeight = roll.RollingCapsuleHeight,
                CapsuleRadius = roll.CapsuleRadius,
            },
            Capabilities = new ResolvedMovementCapabilities
            {
                CanSprint = capabilities.CanSprint,
                CanJump = capabilities.CanJump,
                CanCrouch = capabilities.CanCrouch,
                CanRoll = capabilities.CanRoll,
                CanGrabLedge = capabilities.CanGrabLedge,
                CanMantle = capabilities.CanMantle,
                MaximumJumpCount = capabilities.MaximumJumpCount,
                MaximumAirRollCount = capabilities.MaximumAirRollCount,
            },
        };
    }

    public static NetworkMovementConfiguration FromProtocol(
        AuthorityMovementConfigurationUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var validation = InboundMessageValidator.ValidateMovementConfiguration(update);
        if (!validation.IsValid)
        {
            throw new ArgumentException(validation.Violation!.Message, nameof(update));
        }

        var ground = update.Ground;
        var air = update.Air;
        var jump = update.Jump;
        var roll = update.CrouchRoll;
        var capabilities = update.Capabilities;
        return new NetworkMovementConfiguration(
            update.CombatantId,
            update.LifeId,
            new SimulationInstant(checked((long)update.EffectiveAuthorityTick)),
            new MovementAttributeSnapshot(
                update.MovementProfileRevision,
                new GroundMovementAttributes(
                    ground.MaximumRunSpeed, ground.MaximumSprintSpeed,
                    ground.RunAcceleration, ground.SprintAcceleration,
                    ground.BrakingDeceleration, ground.ReversalDeceleration,
                    ground.LowSpeedTurnRateRadians, ground.HighSpeedTurnRateRadians,
                    ground.ReversalDotThreshold, ground.MaximumStepHeight,
                    ground.FloorSnapDistance, ground.MaximumFloorAngleRadians,
                    ground.StepForwardAssistDistance),
                new AirMovementAttributes(
                    air.ForwardAirAcceleration, air.LateralAirAcceleration,
                    air.MaximumRunAirSpeed, air.MaximumAirSpeed,
                    air.HighSpeedForwardControlMultiplier,
                    air.HighSpeedLateralControlMultiplier, air.TurnRateRadians),
                new JumpMovementAttributes(
                    jump.JumpVelocity, jump.JumpVelocityPerHorizontalSpeed,
                    jump.RisingGravity, jump.ApexGravity, jump.FallingGravity,
                    jump.MaximumFallSpeed, jump.JumpReleaseGravity,
                    jump.ApexVelocityThreshold,
                    Duration(jump.CoyoteDurationTicks),
                    Duration(jump.InputBufferDurationTicks)),
                new CrouchRollAttributes(
                    roll.RollEntrySpeed, roll.MaximumCrouchSpeed,
                    roll.MinimumRollBoostDistance, roll.MaximumRollBoostDistance,
                    Duration(roll.MinimumRollDurationTicks),
                    Duration(roll.MaximumRollDurationTicks),
                    Duration(roll.RollCooldownTicks),
                    roll.LowSpeedSteeringRateRadians, roll.HighSpeedSteeringRateRadians,
                    roll.StandingCapsuleHeight, roll.CrouchingCapsuleHeight,
                    roll.RollingCapsuleHeight, roll.CapsuleRadius)),
            new MovementCapabilitySnapshot(
                update.MovementCapabilityRevision,
                capabilities.CanSprint, capabilities.CanJump, capabilities.CanCrouch,
                capabilities.CanRoll, capabilities.CanGrabLedge, capabilities.CanMantle,
                capabilities.MaximumJumpCount, capabilities.MaximumAirRollCount));
    }

    private static SimulationDuration Duration(ulong ticks) =>
        new(checked((long)ticks));
}
