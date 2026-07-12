using BattleArena.Core.Combat;
using BattleArena.Core.Common;

namespace BattleArena.Core.Tests.Combat;

public sealed class CombatantTests
{
    [Fact]
    public void AppliesDamageAndHealingAsOneAtomicNetChange()
    {
        var combatant = CombatantWithHealth(currentHealth: 10d, maximumHealth: 100d);
        var resolution = Resolution(damage: 20d, healing: 30d);

        var result = combatant.Apply(resolution);

        Assert.Equal(20d, combatant.CurrentHealth, precision: 10);
        Assert.Equal(10d, result.NetHealthChange, precision: 10);
        Assert.Equal(10d, result.ActualHealthGained, precision: 10);
        Assert.Equal(0d, result.ActualHealthLost);
        Assert.False(combatant.IsEliminated);
    }

    [Fact]
    public void NetDamageClampsAtZeroAndRecordsOverkill()
    {
        var combatant = CombatantWithHealth(20d);
        var resolution = Resolution(damage: 75d, healing: 0d);

        var result = combatant.Apply(resolution);

        Assert.Equal(0d, combatant.CurrentHealth);
        Assert.Equal(55d, result.Overkill, precision: 10);
        Assert.Equal(20d, result.ActualHealthLost, precision: 10);
        Assert.True(result.BecameEliminated);
        Assert.True(combatant.IsEliminated);
    }

    [Fact]
    public void NetHealingClampsAtMaximumAndRecordsOverheal()
    {
        var combatant = CombatantWithHealth(currentHealth: 90d, maximumHealth: 100d);
        var resolution = Resolution(damage: 5d, healing: 40d);

        var result = combatant.Apply(resolution);

        Assert.Equal(100d, combatant.CurrentHealth);
        Assert.Equal(25d, result.Overheal, precision: 10);
        Assert.Equal(10d, result.ActualHealthGained, precision: 10);
        Assert.Equal(0d, result.ActualHealthLost);
    }

    [Fact]
    public void EqualDamageAndHealingProduceNoActualHealthMovement()
    {
        var combatant = CombatantWithHealth(100d);
        var resolution = Resolution(damage: 20d, healing: 20d);

        var result = combatant.Apply(resolution);

        Assert.Equal(100d, combatant.CurrentHealth);
        Assert.Equal(20d, result.DamageResolved);
        Assert.Equal(20d, result.HealingResolved);
        Assert.Equal(0d, result.NetHealthChange);
        Assert.Equal(0d, result.ActualHealthGained);
        Assert.Equal(0d, result.ActualHealthLost);
    }

    [Fact]
    public void OrdinaryHealingCannotReviveAnEliminatedCombatant()
    {
        var combatant = CombatantWithHealth(10d);
        combatant.Apply(Resolution(damage: 10d, healing: 0d));

        var result = combatant.Apply(Resolution(damage: 0d, healing: 100d));

        Assert.Equal(HealthApplicationStatus.IgnoredAlreadyEliminated, result.Status);
        Assert.Equal(0d, combatant.CurrentHealth);
        Assert.True(combatant.IsEliminated);
    }

    [Fact]
    public void IncreasingMaximumHealthPreservesHealthPercentage()
    {
        var combatant = CombatantWithHealth(currentHealth: 50d, maximumHealth: 100d);

        var result = combatant.ApplyMaximumHealthSnapshot(
            new MaximumHealthSnapshot(100d, 150d, 200d));

        Assert.Equal(200d, combatant.MaximumHealth);
        Assert.Equal(100d, combatant.BaseMaximumHealth);
        Assert.Equal(150d, combatant.EquipmentMaximumHealth);
        Assert.Equal(100d, combatant.CurrentHealth, precision: 10);
        Assert.Equal(50d, result.CurrentHealthAdjustment, precision: 10);
        Assert.Equal(result.Before.Revision + 1, result.After.Revision);
    }

    [Fact]
    public void DecreasingMaximumHealthPreservesHealthPercentage()
    {
        var combatant = CombatantWithHealth(currentHealth: 150d, maximumHealth: 200d);

        var result = combatant.ApplyMaximumHealthSnapshot(
            new MaximumHealthSnapshot(200d, 150d, 100d));

        Assert.Equal(100d, combatant.MaximumHealth);
        Assert.Equal(75d, combatant.CurrentHealth, precision: 10);
        Assert.Equal(-75d, result.CurrentHealthAdjustment, precision: 10);
    }

    [Fact]
    public void MaximumHealthChangeCannotReviveAnEliminatedCombatant()
    {
        var combatant = CombatantWithHealth(10d);
        combatant.Apply(Resolution(damage: 10d, healing: 0d));

        combatant.ApplyMaximumHealthSnapshot(
            new MaximumHealthSnapshot(10d, 100d, 1_000d));

        Assert.Equal(0d, combatant.CurrentHealth);
        Assert.Equal(1_000d, combatant.MaximumHealth);
        Assert.True(combatant.IsEliminated);
    }

    [Fact]
    public void SnapshotExposesEveryMaximumHealthLayerAndRevision()
    {
        var combatant = new Combatant(new CombatantId(7), 100d);
        combatant.ApplyMaximumHealthSnapshot(
            new MaximumHealthSnapshot(100d, 150d, 300d));

        var snapshot = combatant.CreateHealthSnapshot();

        Assert.Equal(new CombatantId(7), snapshot.CombatantId);
        Assert.Equal(100d, snapshot.BaseMaximumHealth);
        Assert.Equal(150d, snapshot.EquipmentMaximumHealth);
        Assert.Equal(300d, snapshot.EffectiveMaximumHealth);
        Assert.Equal(300d, snapshot.CurrentHealth);
        Assert.Equal(1, snapshot.Revision);
        Assert.False(snapshot.IsEliminated);
    }

    [Fact]
    public void CombatantRejectsMaximumHealthCompiledForAnotherBaseValue()
    {
        var combatant = new Combatant(new CombatantId(1), 100d);
        var incompatible = new MaximumHealthSnapshot(200d, 200d, 200d);

        Assert.Throws<ArgumentException>(
            () => combatant.ApplyMaximumHealthSnapshot(incompatible));
    }

    private static Combatant CombatantWithHealth(
        double currentHealth,
        double? maximumHealth = null)
    {
        var maximum = maximumHealth ?? currentHealth;
        var combatant = new Combatant(new CombatantId(1), maximum);

        if (currentHealth < maximum)
        {
            combatant.Apply(Resolution(damage: maximum - currentHealth, healing: 0d));
        }

        return combatant;
    }

    private static CombatResolutionResult Resolution(double damage, double healing)
    {
        var portions = new List<ResolvedDamagePortion>();
        if (damage > 0d)
        {
            portions.Add(new ResolvedDamagePortion(DamageType.Physical, damage, 0d));
        }

        if (healing > 0d)
        {
            portions.Add(new ResolvedDamagePortion(DamageType.Fire, 0d, healing));
        }

        if (portions.Count == 0)
        {
            portions.Add(new ResolvedDamagePortion(DamageType.Physical, 0d, 0d));
        }

        return new CombatResolutionResult(new CombatantId(2), portions);
    }
}
