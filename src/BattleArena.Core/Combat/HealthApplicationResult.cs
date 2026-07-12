namespace BattleArena.Core.Combat;

public enum HealthApplicationStatus
{
    Applied = 0,
    IgnoredAlreadyEliminated = 1,
}

public sealed record HealthApplicationResult
{
    public HealthApplicationResult(
        HealthApplicationStatus status,
        double healthBefore,
        double healthAfter,
        double maximumHealth,
        double damageResolved,
        double healingResolved,
        double overkill,
        double overheal,
        bool becameEliminated)
    {
        Status = status;
        HealthBefore = healthBefore;
        HealthAfter = healthAfter;
        MaximumHealth = maximumHealth;
        DamageResolved = damageResolved;
        HealingResolved = healingResolved;
        NetHealthChange = healingResolved - damageResolved;
        ActualHealthGained = Math.Max(0d, healthAfter - healthBefore);
        ActualHealthLost = Math.Max(0d, healthBefore - healthAfter);
        Overkill = overkill;
        Overheal = overheal;
        BecameEliminated = becameEliminated;
    }

    public HealthApplicationStatus Status { get; }

    public double HealthBefore { get; }

    public double HealthAfter { get; }

    public double MaximumHealth { get; }

    public double DamageResolved { get; }

    public double HealingResolved { get; }

    public double NetHealthChange { get; }

    public double ActualHealthGained { get; }

    public double ActualHealthLost { get; }

    public double Overkill { get; }

    public double Overheal { get; }

    public bool BecameEliminated { get; }
}
