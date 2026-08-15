using BattleArena.Core.Common;

namespace BattleArena.Core.Combat.Attacks;

public sealed record WeaponAttackDefinition
{
    public WeaponAttackDefinition(
        string id,
        IReadOnlyList<AttackStepDefinition> groundedCombo,
        AttackStepDefinition crouchedOrAirborne,
        SimulationDuration finisherInputWindow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(groundedCombo);
        ArgumentNullException.ThrowIfNull(crouchedOrAirborne);
        if (groundedCombo.Count == 0 ||
            groundedCombo.Any(step => step is null) ||
            finisherInputWindow == SimulationDuration.Zero)
        {
            throw new ArgumentException("A weapon attack definition is incomplete.", nameof(groundedCombo));
        }

        Id = id;
        GroundedCombo = groundedCombo.ToArray();
        CrouchedOrAirborne = crouchedOrAirborne;
        FinisherInputWindow = finisherInputWindow;
    }

    public string Id { get; }
    public IReadOnlyList<AttackStepDefinition> GroundedCombo { get; }
    public AttackStepDefinition CrouchedOrAirborne { get; }
    public SimulationDuration FinisherInputWindow { get; }
}
