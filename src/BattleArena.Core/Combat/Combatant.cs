using BattleArena.Core.Common;
using BattleArena.Core.Effects;

namespace BattleArena.Core.Combat;

public sealed class Combatant
{
    public Combatant(CombatantId id, double maximumHealth)
    {
        if (!double.IsFinite(maximumHealth) || maximumHealth <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumHealth),
                "Maximum health must be finite and positive.");
        }

        Id = id;
        BaseMaximumHealth = maximumHealth;
        EquipmentMaximumHealth = maximumHealth;
        MaximumHealth = maximumHealth;
        CurrentHealth = maximumHealth;
        ActiveEffects = new ActiveEffectContainer(id, new LifeGenerationId(1));
    }

    public CombatantId Id { get; }

    public double CurrentHealth { get; private set; }

    public double BaseMaximumHealth { get; }

    public double EquipmentMaximumHealth { get; private set; }

    public double MaximumHealth { get; private set; }

    public bool IsEliminated { get; private set; }

    public long HealthRevision { get; private set; }

    public ActiveEffectContainer ActiveEffects { get; }

    public HealthApplicationResult Apply(CombatResolutionResult resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        var healthBefore = CurrentHealth;
        if (IsEliminated)
        {
            return new HealthApplicationResult(
                HealthApplicationStatus.IgnoredAlreadyEliminated,
                healthBefore,
                healthBefore,
                MaximumHealth,
                resolution.TotalDamage,
                resolution.TotalHealing,
                0d,
                0d,
                false);
        }

        var unclampedHealth = healthBefore + resolution.TotalHealing - resolution.TotalDamage;
        var overkill = Math.Max(0d, -unclampedHealth);
        var overheal = Math.Max(0d, unclampedHealth - MaximumHealth);
        var healthAfter = Math.Clamp(unclampedHealth, 0d, MaximumHealth);

        CurrentHealth = healthAfter;
        HealthRevision++;
        var becameEliminated = healthAfter == 0d;
        if (becameEliminated)
        {
            IsEliminated = true;
        }

        return new HealthApplicationResult(
            HealthApplicationStatus.Applied,
            healthBefore,
            healthAfter,
            MaximumHealth,
            resolution.TotalDamage,
            resolution.TotalHealing,
            overkill,
            overheal,
            becameEliminated);
    }

    public MaximumHealthChangeResult ApplyMaximumHealthSnapshot(MaximumHealthSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.BaseMaximumHealth != BaseMaximumHealth)
        {
            throw new ArgumentException(
                "The compiled snapshot does not belong to this combatant's base maximum health.",
                nameof(snapshot));
        }

        var before = CreateHealthSnapshot();
        var previousMaximum = MaximumHealth;
        var previousCurrent = CurrentHealth;
        var healthPercentage = previousCurrent / previousMaximum;

        EquipmentMaximumHealth = snapshot.EquipmentMaximumHealth;
        MaximumHealth = snapshot.EffectiveMaximumHealth;
        CurrentHealth = IsEliminated
            ? 0d
            : MaximumHealth * healthPercentage;
        HealthRevision++;

        return new MaximumHealthChangeResult(before, CreateHealthSnapshot());
    }

    public HealthSnapshot CreateHealthSnapshot() =>
        new(
            Id,
            BaseMaximumHealth,
            EquipmentMaximumHealth,
            MaximumHealth,
            CurrentHealth,
            HealthRevision,
            IsEliminated);
}
