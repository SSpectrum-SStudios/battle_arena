namespace BattleArena.Core.Combat;

public sealed record MaximumHealthChangeResult(
    HealthSnapshot Before,
    HealthSnapshot After)
{
    public double CurrentHealthAdjustment => After.CurrentHealth - Before.CurrentHealth;
}
