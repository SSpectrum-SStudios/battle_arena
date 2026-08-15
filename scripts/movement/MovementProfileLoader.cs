#nullable enable

using System.IO;
using System.Text.Json;
using BattleArena.Core.Common;
using BattleArena.Core.Movement;

namespace BattleArena.Movement;

public static class MovementProfileLoader
{
    public const int SupportedSchemaVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static LoadedMovementProfile Load(
        string resourcePath,
        ulong revision = 0,
        SimulationRate? simulationRate = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcePath);
        if (!resourcePath.StartsWith("res://", StringComparison.Ordinal))
        {
            throw new ArgumentException("Movement profiles must use a res:// path.", nameof(resourcePath));
        }

        if (!Godot.FileAccess.FileExists(resourcePath))
        {
            throw new FileNotFoundException("Movement profile was not found.", resourcePath);
        }

        MovementProfileDefinition definition;
        try
        {
            definition = JsonSerializer.Deserialize<MovementProfileDefinition>(
                Godot.FileAccess.GetFileAsString(resourcePath),
                SerializerOptions) ?? throw new InvalidDataException("The movement profile is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Movement profile '{resourcePath}' is invalid JSON.", exception);
        }

        if (definition.SchemaVersion != SupportedSchemaVersion)
        {
            throw new InvalidDataException(
                $"Movement profile '{resourcePath}' uses schema {definition.SchemaVersion}; " +
                $"schema {SupportedSchemaVersion} is required.");
        }

        if (string.IsNullOrWhiteSpace(definition.Id))
        {
            throw new InvalidDataException($"Movement profile '{resourcePath}' requires an id.");
        }

        try
        {
            var ground = definition.Ground;
            var compiledGround = new GroundMovementAttributes(
                ground.MaximumRunSpeed,
                ground.MaximumSprintSpeed,
                ground.RunAcceleration,
                ground.SprintAcceleration,
                ground.BrakingDeceleration,
                ground.ReversalDeceleration,
                DegreesToRadians(ground.LowSpeedTurnRateDegrees),
                DegreesToRadians(ground.HighSpeedTurnRateDegrees),
                ground.ReversalDotThreshold,
                ground.MaximumStepHeight,
                ground.FloorSnapDistance,
                DegreesToRadians(ground.MaximumFloorAngleDegrees),
                ground.StepForwardAssistDistance);
            var air = definition.Air;
            var compiledAir = new AirMovementAttributes(
                air.ForwardAirAcceleration,
                air.LateralAirAcceleration,
                air.MaximumRunAirSpeed,
                air.MaximumAirSpeed,
                air.HighSpeedForwardControlMultiplier,
                air.HighSpeedLateralControlMultiplier,
                DegreesToRadians(air.TurnRateDegrees));
            var jump = definition.Jump;
            var rate = simulationRate ?? SimulationRate.Default;
            var compiledJump = new JumpMovementAttributes(
                jump.JumpVelocity,
                jump.JumpVelocityPerHorizontalSpeed,
                jump.RisingGravity,
                jump.ApexGravity,
                jump.FallingGravity,
                jump.MaximumFallSpeed,
                jump.JumpReleaseGravity,
                jump.ApexVelocityThreshold,
                rate.DurationFromSeconds(jump.CoyoteTimeSeconds),
                rate.DurationFromSeconds(jump.InputBufferSeconds));
            var crouchRoll = definition.CrouchRoll;
            var compiledCrouchRoll = new CrouchRollAttributes(
                crouchRoll.RollEntrySpeed,
                crouchRoll.MaximumCrouchSpeed,
                crouchRoll.MinimumRollBoostDistance,
                crouchRoll.MaximumRollBoostDistance,
                rate.DurationFromSeconds(crouchRoll.MinimumRollDurationSeconds),
                rate.DurationFromSeconds(crouchRoll.MaximumRollDurationSeconds),
                rate.DurationFromSeconds(crouchRoll.RollCooldownSeconds),
                DegreesToRadians(crouchRoll.LowSpeedSteeringRateDegrees),
                DegreesToRadians(crouchRoll.HighSpeedSteeringRateDegrees),
                crouchRoll.StandingCapsuleHeight,
                crouchRoll.CrouchingCapsuleHeight,
                crouchRoll.RollingCapsuleHeight,
                crouchRoll.CapsuleRadius);
            return new LoadedMovementProfile(
                definition.Id,
                new MovementAttributeSnapshot(
                    revision,
                    compiledGround,
                    compiledAir,
                    compiledJump,
                    compiledCrouchRoll));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException(
                $"Movement profile '{definition.Id}' contains an invalid authored value.",
                exception);
        }
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;
}
