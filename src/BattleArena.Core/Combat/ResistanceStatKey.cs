namespace BattleArena.Core.Combat;

public enum ResistanceScope
{
    DamageType = 0,
    General = 1,
}

public enum ResistanceMeasure
{
    Percentage = 0,
    Flat = 1,
}

public readonly record struct ResistanceStatKey
{
    private ResistanceStatKey(
        ResistanceScope scope,
        ResistanceMeasure measure,
        DamageType? damageType)
    {
        Scope = scope;
        Measure = measure;
        DamageType = damageType;
    }

    public ResistanceScope Scope { get; }

    public ResistanceMeasure Measure { get; }

    public DamageType? DamageType { get; }

    public static ResistanceStatKey ForDamageType(DamageType damageType, ResistanceMeasure measure)
    {
        if (!Enum.IsDefined(damageType))
        {
            throw new ArgumentOutOfRangeException(nameof(damageType), damageType, "The damage type is not defined.");
        }

        if (!Enum.IsDefined(measure))
        {
            throw new ArgumentOutOfRangeException(nameof(measure), measure, "The resistance measure is not defined.");
        }

        return new ResistanceStatKey(ResistanceScope.DamageType, measure, damageType);
    }

    public static ResistanceStatKey General(ResistanceMeasure measure)
    {
        if (!Enum.IsDefined(measure))
        {
            throw new ArgumentOutOfRangeException(nameof(measure), measure, "The resistance measure is not defined.");
        }

        return new ResistanceStatKey(ResistanceScope.General, measure, null);
    }
}
