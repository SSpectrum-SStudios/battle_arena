using BattleArena.Core.Common;
using BattleArena.Core.Movement;

namespace BattleArena.Core.Tests.Movement;

public sealed class CrouchRollSimulatorTests
{
    private static readonly SimulationRate Rate = new(60);
    private static readonly CrouchRollAttributes Attributes = new(
        1d,
        3d,
        3d,
        5d,
        new SimulationDuration(19),
        new SimulationDuration(51),
        new SimulationDuration(21),
        Math.Tau,
        Math.PI / 4d,
        1.8d,
        1.25d,
        0.95d,
        0.42d);
    private static readonly JumpMovementAttributes Jump = new(
        13.4d,
        0.04d,
        23d,
        16d,
        45d,
        36d,
        55d,
        1.5d,
        new SimulationDuration(7),
        new SimulationDuration(7));
    private static readonly GroundMovementAttributes Ground = new(
        6d,
        12.5d,
        16d,
        14d,
        32d,
        42d,
        Math.Tau * 2d,
        Math.PI,
        -0.25d,
        0.4d,
        0.5d,
        Math.PI / 3d,
        0.08d);

    private readonly CrouchRollSimulator _simulator = new();

    [Fact]
    public void StationaryPressEntersHoldCrouch()
    {
        var result = Simulate(Grounded(), Command(0, pressed: MovementButtons.CrouchOrRoll));

        Assert.Equal(LocomotionMode.Grounded, result.LocomotionMode);
        Assert.Equal(PostureMode.Crouched, result.PostureMode);
    }

    [Fact]
    public void MovingPressStartsCommittedRoll()
    {
        var state = Grounded() with { HorizontalVelocity = new HorizontalVector(0d, -6d) };

        var result = Simulate(
            state,
            Command(0, new HorizontalVector(0d, -1d), pressed: MovementButtons.CrouchOrRoll));

        Assert.Equal(LocomotionMode.Rolling, result.LocomotionMode);
        Assert.Equal(PostureMode.Crouched, result.PostureMode);
        Assert.True(result.HorizontalVelocity.Length > state.HorizontalVelocity.Length);
        Assert.Equal(Attributes.MaximumRollDuration, result.RollDuration);
    }

    [Fact]
    public void HeldInputAfterEnteringCrouchDoesNotRetroactivelyRoll()
    {
        var crouched = Simulate(Grounded(), Command(0, pressed: MovementButtons.CrouchOrRoll));
        crouched = crouched with { HorizontalVelocity = new HorizontalVector(0d, -3d) };

        var result = Simulate(
            crouched,
            Command(1, new HorizontalVector(0d, -1d), held: MovementButtons.CrouchOrRoll));

        Assert.Equal(LocomotionMode.Grounded, result.LocomotionMode);
        Assert.Equal(PostureMode.Crouched, result.PostureMode);
    }

    [Fact]
    public void JumpAndAttackCannotCancelActiveRoll()
    {
        var rolling = StartRoll();

        var result = Simulate(
            rolling,
            Command(
                1,
                pressed: MovementButtons.Jump | MovementButtons.Attack));

        Assert.Equal(LocomotionMode.Rolling, result.LocomotionMode);
    }

    [Fact]
    public void ReleasedInputEndsRollAtMinimumDuration()
    {
        var rolling = StartRoll();

        var beforeMinimum = Simulate(
            rolling,
            Command(Attributes.MinimumRollDuration.Ticks - 1));
        var atMinimum = Simulate(
            rolling,
            Command(Attributes.MinimumRollDuration.Ticks));

        Assert.Equal(LocomotionMode.Rolling, beforeMinimum.LocomotionMode);
        Assert.Equal(LocomotionMode.Grounded, atMinimum.LocomotionMode);
    }

    [Fact]
    public void HeldInputExtendsRollToMaximumDuration()
    {
        var rolling = StartRoll();

        var afterMinimum = Simulate(
            rolling,
            Command(
                Attributes.MinimumRollDuration.Ticks,
                held: MovementButtons.CrouchOrRoll));
        var atMaximum = Simulate(
            rolling,
            Command(
                Attributes.MaximumRollDuration.Ticks,
                held: MovementButtons.CrouchOrRoll));

        Assert.Equal(LocomotionMode.Rolling, afterMinimum.LocomotionMode);
        Assert.Equal(LocomotionMode.Grounded, atMaximum.LocomotionMode);
        Assert.Equal(PostureMode.Crouched, atMaximum.PostureMode);
    }

    [Fact]
    public void HeldAirborneInputQueuesRollForLanding()
    {
        var airborne = Grounded() with
        {
            LocomotionMode = LocomotionMode.Airborne,
            JumpPhase = JumpPhase.Falling,
            HorizontalVelocity = new HorizontalVector(0d, -6d),
        };
        var queued = _simulator.Simulate(
            airborne,
            Command(
                10,
                new HorizontalVector(0d, -1d),
                held: MovementButtons.CrouchOrRoll),
            isGrounded: false,
            canStand: true,
            Attributes,
            Jump,
            Ground,
            Rate);
        var landed = queued with
        {
            LocomotionMode = LocomotionMode.Grounded,
            JumpPhase = JumpPhase.None,
        };

        var result = Simulate(
            landed,
            Command(
                11,
                new HorizontalVector(0d, -1d),
                held: MovementButtons.CrouchOrRoll));

        Assert.True(queued.LandingRollQueued);
        Assert.Equal(LocomotionMode.Rolling, result.LocomotionMode);
        Assert.False(result.LandingRollQueued);
    }

    [Fact]
    public void ReleasingAirborneInputClearsLandingRollQueue()
    {
        var airborne = Grounded() with
        {
            LocomotionMode = LocomotionMode.Airborne,
            JumpPhase = JumpPhase.Falling,
            HorizontalVelocity = new HorizontalVector(0d, -6d),
            LandingRollQueued = true,
        };

        var result = _simulator.Simulate(
            airborne,
            Command(10),
            isGrounded: false,
            canStand: true,
            Attributes,
            Jump,
            Ground,
            Rate);

        Assert.False(result.LandingRollQueued);
    }

    [Fact]
    public void RollContinuesFallingAfterLeavingLedge()
    {
        var rolling = StartRoll();

        var result = _simulator.Simulate(
            rolling,
            Command(1),
            isGrounded: false,
            canStand: true,
            Attributes,
            Jump,
            Ground,
            Rate);

        Assert.Equal(LocomotionMode.Rolling, result.LocomotionMode);
        Assert.Equal(JumpPhase.Falling, result.JumpPhase);
        Assert.True(result.VerticalVelocity < 0d);
    }

    [Fact]
    public void CompletionStartsCooldownAndResolvesStandingPosture()
    {
        var rolling = StartRoll();
        var completionTick = rolling.ModeStartedAt.Tick + rolling.RollDuration.Ticks;

        var result = Simulate(rolling, Command(completionTick));

        Assert.Equal(LocomotionMode.Grounded, result.LocomotionMode);
        Assert.Equal(PostureMode.Standing, result.PostureMode);
        Assert.Equal(rolling.RollEntrySpeed, result.HorizontalVelocity.Length, 8);
        Assert.Equal(completionTick + Attributes.RollCooldown.Ticks, result.RollAvailableAt.Tick);
    }

    [Fact]
    public void HeldCrouchOrBlockedClearanceKeepsCrouchedAfterRoll()
    {
        var rolling = StartRoll();
        var completionTick = rolling.ModeStartedAt.Tick + rolling.RollDuration.Ticks;

        var held = Simulate(
            rolling,
            Command(completionTick, held: MovementButtons.CrouchOrRoll));
        var blocked = _simulator.Simulate(
            rolling,
            Command(completionTick),
            true,
            canStand: false,
            Attributes,
            Jump,
            Ground,
            Rate);

        Assert.Equal(PostureMode.Crouched, held.PostureMode);
        Assert.Equal(PostureMode.Crouched, blocked.PostureMode);
    }

    [Fact]
    public void SprintEntryProducesFartherBoostThanThresholdEntry()
    {
        var slow = StartRoll(1.01d);
        var sprint = StartRoll(12.5d);

        Assert.True(sprint.RollBoostDistance > slow.RollBoostDistance);
        Assert.Equal(slow.RollDuration, sprint.RollDuration);
    }

    private MovementRuntimeState StartRoll(double speed = 6d)
    {
        var state = Grounded() with { HorizontalVelocity = new HorizontalVector(0d, -speed) };
        return Simulate(
            state,
            Command(0, new HorizontalVector(0d, -1d), pressed: MovementButtons.CrouchOrRoll));
    }

    private MovementRuntimeState Simulate(
        MovementRuntimeState state,
        MovementCommand command,
        bool canStand = true) =>
        _simulator.Simulate(state, command, true, canStand, Attributes, Jump, Ground, Rate);

    private static MovementRuntimeState Grounded() =>
        MovementRuntimeState.CreateGrounded(SimulationInstant.Zero);

    private static MovementCommand Command(
        long tick,
        HorizontalVector? movement = null,
        MovementButtons held = MovementButtons.None,
        MovementButtons pressed = MovementButtons.None) => new(
        (ulong)tick + 1,
        new SimulationInstant(tick),
        movement ?? HorizontalVector.Zero,
        0d,
        0d,
        held,
        pressed);
}
