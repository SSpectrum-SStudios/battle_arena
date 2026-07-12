namespace BattleArena.Core.Combat;

public sealed class CombatResolver
{
    private const double MinimumGeneralResistedDamage = 1d;

    public CombatResolutionResult Resolve(
        DamagePacket packet,
        ResistanceProfile resistanceProfile)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(resistanceProfile);

        var resolvedPortions = packet.Portions
            .Select(portion => ResolvePortion(portion, resistanceProfile))
            .ToArray();

        return new CombatResolutionResult(packet.SourceId, resolvedPortions);
    }

    private static ResolvedDamagePortion ResolvePortion(
        DamagePortion portion,
        ResistanceProfile resistanceProfile)
    {
        if (portion.Amount == 0d)
        {
            return new ResolvedDamagePortion(portion.Type, 0d, 0d);
        }

        var typedPercentageKey = ResistanceStatKey.ForDamageType(
            portion.Type,
            ResistanceMeasure.Percentage);
        var typedPercentage = resistanceProfile.Get(typedPercentageKey);

        if (typedPercentage > 1d)
        {
            var healing = portion.Amount * (typedPercentage - 1d);
            return new ResolvedDamagePortion(portion.Type, 0d, healing);
        }

        var remainingDamage = portion.Amount * (1d - typedPercentage);

        var typedFlatKey = ResistanceStatKey.ForDamageType(
            portion.Type,
            ResistanceMeasure.Flat);
        remainingDamage = Math.Max(0d, remainingDamage - resistanceProfile.Get(typedFlatKey));

        if (remainingDamage == 0d)
        {
            return new ResolvedDamagePortion(portion.Type, 0d, 0d);
        }

        var damageBeforeGeneralResistance = remainingDamage;
        var generalPercentageKey = ResistanceStatKey.General(ResistanceMeasure.Percentage);
        var generalPercentage = resistanceProfile.Get(generalPercentageKey);

        // General resistance can amplify damage through weakness, but it cannot convert damage into healing.
        remainingDamage *= Math.Max(0d, 1d - generalPercentage);

        var generalFlatKey = ResistanceStatKey.General(ResistanceMeasure.Flat);
        remainingDamage = Math.Max(0d, remainingDamage - resistanceProfile.Get(generalFlatKey));

        var generalResistanceParticipated =
            resistanceProfile.HasContribution(generalPercentageKey) ||
            resistanceProfile.HasContribution(generalFlatKey);

        if (generalResistanceParticipated && damageBeforeGeneralResistance > 0d)
        {
            remainingDamage = Math.Max(MinimumGeneralResistedDamage, remainingDamage);
        }

        return new ResolvedDamagePortion(portion.Type, remainingDamage, 0d);
    }
}
