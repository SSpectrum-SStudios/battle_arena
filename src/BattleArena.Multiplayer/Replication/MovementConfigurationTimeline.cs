namespace BattleArena.Multiplayer.Replication;

public sealed class MovementConfigurationTimeline : IMovementConfigurationTimeline
{
    private const int MaximumConfigurationsPerCombatant = 32;
    private readonly Dictionary<ulong, List<NetworkMovementConfiguration>> _configurations = [];

    public bool Apply(NetworkMovementConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.CombatantId == 0 || configuration.LifeId == 0 ||
            configuration.EffectiveAuthorityTick.Tick <= 0)
        {
            return false;
        }

        if (!_configurations.TryGetValue(configuration.CombatantId, out var values))
        {
            values = [];
            _configurations.Add(configuration.CombatantId, values);
        }

        var currentLife = values.Count == 0 ? configuration.LifeId : values.Max(value => value.LifeId);
        if (configuration.LifeId < currentLife)
        {
            return false;
        }

        if (configuration.LifeId > currentLife)
        {
            values.Clear();
        }

        if (values.Any(value =>
                value.LifeId == configuration.LifeId &&
                value.Attributes.Revision == configuration.Attributes.Revision &&
                value.Capabilities.Revision == configuration.Capabilities.Revision))
        {
            return false;
        }

        values.Add(configuration);
        values.Sort((left, right) =>
            left.EffectiveAuthorityTick.Tick.CompareTo(right.EffectiveAuthorityTick.Tick));
        while (values.Count > MaximumConfigurationsPerCombatant)
        {
            values.RemoveAt(0);
        }

        return true;
    }

    public bool TryResolve(
        ulong combatantId,
        ulong lifeId,
        ulong profileRevision,
        ulong capabilityRevision,
        ulong authorityTick,
        out NetworkMovementConfiguration configuration)
    {
        configuration = null!;
        if (!_configurations.TryGetValue(combatantId, out var values))
        {
            return false;
        }

        var match = values.LastOrDefault(value =>
            value.LifeId == lifeId &&
            value.Attributes.Revision == profileRevision &&
            value.Capabilities.Revision == capabilityRevision &&
            checked((ulong)value.EffectiveAuthorityTick.Tick) <= authorityTick);
        if (match is null)
        {
            return false;
        }

        configuration = match;
        return true;
    }

    public void RemoveCombatant(ulong combatantId) => _configurations.Remove(combatantId);
}
