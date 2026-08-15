using BattleArena.Core.Combat.Attacks;
using BattleArena.Core.Common;
using BattleArena.Core.Movement;

namespace BattleArena.Core.Tests.Combat;

public sealed class StarterSwordAttackPolicyTests
{
    [Fact]
    public void HoldingQueuesSecondStepButNeverQueuesFinisher()
    {
        var policy = new StarterSwordAttackPolicy(Definition());
        var state = policy.Advance(null, Command(0, pressed: MovementButtons.Attack), AttackContext.GroundedCombo).State;
        state = policy.Advance(state, Command(3, held: MovementButtons.Attack), AttackContext.GroundedCombo).State;
        var second = policy.Advance(state, Command(6, held: MovementButtons.Attack), AttackContext.GroundedCombo);

        Assert.True(second.StepStarted);
        Assert.Equal(1, second.State!.StepIndex);

        state = second.State;
        for (var tick = 7; tick <= 12; tick++)
        {
            var result = policy.Advance(
                state,
                Command(tick, held: MovementButtons.Attack),
                AttackContext.GroundedCombo);
            state = result.State;
        }

        Assert.Null(state);
    }

    [Fact]
    public void ReleasedThenFreshPressInFinisherWindowQueuesThirdStep()
    {
        var policy = new StarterSwordAttackPolicy(Definition());
        var state = policy.Advance(null, Command(0, pressed: MovementButtons.Attack), AttackContext.GroundedCombo).State;
        state = policy.Advance(state, Command(3, held: MovementButtons.Attack), AttackContext.GroundedCombo).State;
        state = policy.Advance(state, Command(6), AttackContext.GroundedCombo).State;
        state = policy.Advance(
            state,
            Command(7, released: MovementButtons.Attack),
            AttackContext.GroundedCombo).State;
        state = policy.Advance(
            state,
            Command(10, pressed: MovementButtons.Attack),
            AttackContext.GroundedCombo).State;
        var third = policy.Advance(state, Command(12), AttackContext.GroundedCombo);

        Assert.True(third.StepStarted);
        Assert.Equal(2, third.State!.StepIndex);
    }

    [Fact]
    public void JumpCancelsStartupButNotCommittedPhase()
    {
        var policy = new StarterSwordAttackPolicy(Definition());
        var startup = policy.Advance(null, Command(0, pressed: MovementButtons.Attack), AttackContext.GroundedCombo).State;
        var cancelled = policy.Advance(
            startup,
            Command(1, pressed: MovementButtons.Jump),
            AttackContext.GroundedCombo);
        Assert.True(cancelled.Cancelled);
        Assert.Null(cancelled.State);

        var committed = policy.Advance(null, Command(10, pressed: MovementButtons.Attack), AttackContext.GroundedCombo).State;
        var retained = policy.Advance(
            committed,
            Command(12, pressed: MovementButtons.Jump),
            AttackContext.GroundedCombo);
        Assert.False(retained.Cancelled);
        Assert.NotNull(retained.State);
        Assert.Equal(AttackPhase.Committed, retained.Phase);
    }

    private static WeaponAttackDefinition Definition()
    {
        var movement = MovementInfluence.Unrestricted;
        AttackStepDefinition Step(string id) => new(
            id,
            $"attack.{id}",
            new SimulationDuration(2),
            new SimulationDuration(2),
            new SimulationDuration(2),
            new SimulationDuration(2),
            new SimulationDuration(3),
            new SimulationDuration(3),
            20,
            0,
            movement);
        return new WeaponAttackDefinition(
            "test:sword",
            [Step("one"), Step("two"), Step("three")],
            Step("simple"),
            new SimulationDuration(2));
    }

    private static MovementCommand Command(
        long tick,
        MovementButtons held = MovementButtons.None,
        MovementButtons pressed = MovementButtons.None,
        MovementButtons released = MovementButtons.None) => new(
        checked((ulong)(tick + 1)),
        new SimulationInstant(tick),
        HorizontalVector.Zero,
        0,
        0,
        held,
        pressed,
        released);
}
