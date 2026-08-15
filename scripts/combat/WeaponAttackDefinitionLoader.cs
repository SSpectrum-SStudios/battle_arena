#nullable enable

using System.IO;
using System.Text.Json;
using BattleArena.Core.Combat.Attacks;
using BattleArena.Core.Common;
using BattleArena.Core.Movement;

namespace BattleArena.Combat;

public static class WeaponAttackDefinitionLoader
{
    private const int SupportedSchemaVersion = 1;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static WeaponAttackDefinition Load(
        string resourcePath,
        SimulationRate simulationRate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcePath);
        if (!resourcePath.StartsWith("res://", StringComparison.Ordinal) ||
            !Godot.FileAccess.FileExists(resourcePath))
        {
            throw new FileNotFoundException(
                "Weapon attack definition was not found.",
                resourcePath);
        }

        var authored = JsonSerializer.Deserialize<AuthoredWeaponAttackDefinition>(
            Godot.FileAccess.GetFileAsString(resourcePath),
            SerializerOptions) ?? throw new InvalidDataException(
                $"Weapon attack definition '{resourcePath}' is empty.");
        if (authored.SchemaVersion != SupportedSchemaVersion ||
            authored.GroundedCombo is null ||
            authored.CrouchedOrAirborne is null)
        {
            throw new InvalidDataException(
                $"Weapon attack definition '{resourcePath}' has an unsupported or incomplete schema.");
        }

        return new WeaponAttackDefinition(
            authored.Id,
            authored.GroundedCombo.Select(step => Compile(step, simulationRate)).ToArray(),
            Compile(authored.CrouchedOrAirborne, simulationRate),
            simulationRate.DurationFromSeconds((decimal)authored.FinisherInputWindowSeconds));
    }

    private static AttackStepDefinition Compile(
        AuthoredAttackStep authored,
        SimulationRate rate) => new(
        authored.Id,
        authored.AnimationId,
        rate.DurationFromSeconds((decimal)authored.StartupSeconds),
        rate.DurationFromSeconds((decimal)authored.CommittedSeconds),
        rate.DurationFromSeconds((decimal)authored.RecoverySeconds),
        rate.DurationFromSeconds((decimal)authored.ActiveHitStartsSeconds),
        rate.DurationFromSeconds((decimal)authored.ActiveHitEndsSeconds),
        rate.DurationFromSeconds((decimal)authored.ContinuationWindowStartsSeconds),
        authored.PhysicalDamage,
        authored.LungeDistance,
        new MovementInfluence(
            authored.MovementAuthority,
            authored.MovementAuthority,
            authored.AdditionalDeceleration,
            preserveMomentumAboveTargetSpeed: true,
            allowSprintAcceleration: false));

    private sealed record AuthoredWeaponAttackDefinition
    {
        public int SchemaVersion { get; init; }
        public string Id { get; init; } = "";
        public double FinisherInputWindowSeconds { get; init; }
        public List<AuthoredAttackStep>? GroundedCombo { get; init; }
        public AuthoredAttackStep? CrouchedOrAirborne { get; init; }
    }

    private sealed record AuthoredAttackStep
    {
        public string Id { get; init; } = "";
        public string AnimationId { get; init; } = "";
        public double StartupSeconds { get; init; }
        public double CommittedSeconds { get; init; }
        public double RecoverySeconds { get; init; }
        public double ActiveHitStartsSeconds { get; init; }
        public double ActiveHitEndsSeconds { get; init; }
        public double ContinuationWindowStartsSeconds { get; init; }
        public long PhysicalDamage { get; init; }
        public double LungeDistance { get; init; }
        public double MovementAuthority { get; init; }
        public double AdditionalDeceleration { get; init; }
    }
}
