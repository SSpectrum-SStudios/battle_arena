namespace BattleArena.Core.Combat.Attacks;

public sealed record AttackTickResult(
    AttackRuntimeState? State,
    AttackStepDefinition? Step,
    AttackPhase? Phase,
    bool StepStarted,
    bool Completed,
    bool Cancelled,
    bool HitWindowActive);
