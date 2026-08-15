using BattleArena.Core.Common;
using BattleArena.Core.Movement;

namespace BattleArena.Core.Combat.Attacks;

public sealed record AttackStepDefinition
{
    public AttackStepDefinition(
        string id,
        string animationId,
        SimulationDuration startupDuration,
        SimulationDuration committedDuration,
        SimulationDuration recoveryDuration,
        SimulationDuration activeHitStartsAt,
        SimulationDuration activeHitEndsAt,
        SimulationDuration continuationWindowStartsAt,
        long physicalDamage,
        double lungeDistance,
        MovementInfluence movementInfluence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(animationId);
        ArgumentNullException.ThrowIfNull(movementInfluence);
        var total = startupDuration + committedDuration + recoveryDuration;
        if (total == SimulationDuration.Zero ||
            activeHitStartsAt > activeHitEndsAt ||
            activeHitEndsAt > total ||
            continuationWindowStartsAt > total ||
            physicalDamage < 0 ||
            !double.IsFinite(lungeDistance) ||
            lungeDistance < 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startupDuration),
                "Attack timing, damage, or movement values are invalid.");
        }

        Id = id;
        AnimationId = animationId;
        StartupDuration = startupDuration;
        CommittedDuration = committedDuration;
        RecoveryDuration = recoveryDuration;
        ActiveHitStartsAt = activeHitStartsAt;
        ActiveHitEndsAt = activeHitEndsAt;
        ContinuationWindowStartsAt = continuationWindowStartsAt;
        PhysicalDamage = physicalDamage;
        LungeDistance = lungeDistance;
        MovementInfluence = movementInfluence;
    }

    public string Id { get; }
    public string AnimationId { get; }
    public SimulationDuration StartupDuration { get; }
    public SimulationDuration CommittedDuration { get; }
    public SimulationDuration RecoveryDuration { get; }
    public SimulationDuration ActiveHitStartsAt { get; }
    public SimulationDuration ActiveHitEndsAt { get; }
    public SimulationDuration ContinuationWindowStartsAt { get; }
    public long PhysicalDamage { get; }
    public double LungeDistance { get; }
    public MovementInfluence MovementInfluence { get; }
    public SimulationDuration TotalDuration =>
        StartupDuration + CommittedDuration + RecoveryDuration;

    public AttackPhase PhaseAt(SimulationDuration elapsed) =>
        elapsed < StartupDuration
            ? AttackPhase.Startup
            : elapsed < StartupDuration + CommittedDuration
                ? AttackPhase.Committed
                : AttackPhase.Recovery;

    public bool IsHitActive(SimulationDuration elapsed) =>
        elapsed >= ActiveHitStartsAt && elapsed <= ActiveHitEndsAt;
}
