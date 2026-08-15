using BattleArena.Core.Common;
using BattleArena.Core.Movement;

namespace BattleArena.Core.Combat.Attacks;

/// <summary>
/// Interprets the starter sword's authored press/hold/release grammar. The
/// player controller only supplies semantic input and context.
/// </summary>
public sealed class StarterSwordAttackPolicy(WeaponAttackDefinition definition)
{
    private ulong _nextExecutionId = 1;

    public WeaponAttackDefinition Definition { get; } =
        definition ?? throw new ArgumentNullException(nameof(definition));

    public AttackTickResult Advance(
        AttackRuntimeState? current,
        MovementCommand command,
        AttackContext context)
    {
        if (current is null)
        {
            return command.WasPressed(MovementButtons.Attack)
                ? Start(context, 0, command.ClientTick)
                : new AttackTickResult(null, null, null, false, false, false, false);
        }

        var step = ResolveStep(current);
        var elapsed = command.ClientTick - current.StartedAt;
        var phase = step.PhaseAt(elapsed);
        var cancelRequested =
            command.WasPressed(MovementButtons.Jump) ||
            command.WasPressed(MovementButtons.CrouchOrRoll);
        if (cancelRequested && phase is AttackPhase.Startup or AttackPhase.Recovery)
        {
            return new AttackTickResult(null, step, phase, false, false, true, false);
        }

        var next = current;
        if (current.Context == AttackContext.GroundedCombo)
        {
            next = ReadContinuationInput(current, step, elapsed, command);
        }

        if (elapsed >= step.TotalDuration)
        {
            if (next.ContinuationQueued &&
                next.StepIndex + 1 < Definition.GroundedCombo.Count)
            {
                return Start(
                    AttackContext.GroundedCombo,
                    next.StepIndex + 1,
                    command.ClientTick);
            }

            return new AttackTickResult(null, step, AttackPhase.Recovery, false, true, false, false);
        }

        return new AttackTickResult(
            next,
            step,
            phase,
            false,
            false,
            false,
            step.IsHitActive(elapsed));
    }

    public AttackRuntimeState RecordHit(AttackRuntimeState state, ulong targetCombatantId)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (targetCombatantId == 0 || state.HitCombatants.Contains(targetCombatantId))
        {
            return state;
        }

        return state with
        {
            HitCombatants = state.HitCombatants.Append(targetCombatantId).ToHashSet(),
        };
    }

    private AttackRuntimeState ReadContinuationInput(
        AttackRuntimeState state,
        AttackStepDefinition step,
        SimulationDuration elapsed,
        MovementCommand command)
    {
        if (state.StepIndex == 0)
        {
            var queue = elapsed >= step.ContinuationWindowStartsAt &&
                (command.IsHeld(MovementButtons.Attack) ||
                 command.WasPressed(MovementButtons.Attack));
            return queue ? state with { ContinuationQueued = true } : state;
        }

        if (state.StepIndex != 1 || Definition.GroundedCombo.Count < 3)
        {
            return state;
        }

        var released = state.AttackReleasedDuringStep ||
            command.WasReleased(MovementButtons.Attack);
        var finisherWindowStartTicks = Math.Max(
            0,
            step.TotalDuration.Ticks - Definition.FinisherInputWindow.Ticks);
        var inWindow = elapsed.Ticks >= finisherWindowStartTicks &&
            elapsed <= step.TotalDuration;
        var queueFinisher = released &&
            inWindow &&
            command.WasPressed(MovementButtons.Attack);
        return state with
        {
            AttackReleasedDuringStep = released,
            ContinuationQueued = state.ContinuationQueued || queueFinisher,
        };
    }

    private AttackTickResult Start(
        AttackContext context,
        int stepIndex,
        SimulationInstant now)
    {
        var step = context == AttackContext.GroundedCombo
            ? Definition.GroundedCombo[stepIndex]
            : Definition.CrouchedOrAirborne;
        var state = new AttackRuntimeState(
            _nextExecutionId++,
            context,
            stepIndex,
            now,
            ContinuationQueued: false,
            AttackReleasedDuringStep: false,
            HitCombatants: new HashSet<ulong>());
        return new AttackTickResult(
            state,
            step,
            AttackPhase.Startup,
            StepStarted: true,
            Completed: false,
            Cancelled: false,
            HitWindowActive: step.IsHitActive(SimulationDuration.Zero));
    }

    private AttackStepDefinition ResolveStep(AttackRuntimeState state) =>
        state.Context == AttackContext.GroundedCombo
            ? Definition.GroundedCombo[state.StepIndex]
            : Definition.CrouchedOrAirborne;
}
