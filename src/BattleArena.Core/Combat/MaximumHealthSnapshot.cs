namespace BattleArena.Core.Combat;

public sealed record MaximumHealthSnapshot
{
    public MaximumHealthSnapshot(
        double baseMaximumHealth,
        double equipmentMaximumHealth,
        double effectiveMaximumHealth)
    {
        Validate(baseMaximumHealth, nameof(baseMaximumHealth));
        Validate(equipmentMaximumHealth, nameof(equipmentMaximumHealth));
        Validate(effectiveMaximumHealth, nameof(effectiveMaximumHealth));

        BaseMaximumHealth = baseMaximumHealth;
        EquipmentMaximumHealth = equipmentMaximumHealth;
        EffectiveMaximumHealth = effectiveMaximumHealth;
    }

    public double BaseMaximumHealth { get; }

    public double EquipmentMaximumHealth { get; }

    public double EffectiveMaximumHealth { get; }

    private static void Validate(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < MaximumHealthCompiler.MinimumMaximumHealth)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"Maximum health must be finite and at least {MaximumHealthCompiler.MinimumMaximumHealth}.");
        }
    }
}
