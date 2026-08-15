using BattleArena.Core.Common;
using BattleArena.Core.Movement;

namespace BattleArena.Core.Tests.Movement;

public sealed class AirborneLocomotionSimulatorTests
{
    private static readonly SimulationRate Rate = new(60);
    private static readonly AirMovementAttributes Attributes = new(
        forwardAirAcceleration: 23.5d,
        lateralAirAcceleration: 3.5d,
        maximumRunAirSpeed: 6d,
        maximumAirSpeed: 12d,
        highSpeedForwardControlMultiplier: 0.5d,
        highSpeedLateralControlMultiplier: 0.15d,
        turnRateRadians: Math.PI);

    private readonly AirborneLocomotionSimulator _simulator = new();

    [Fact]
    public void NoAirInputPreservesHorizontalMomentum()
    {
        var state = Airborne(verticalVelocity: -2d) with
        {
            HorizontalVelocity = new HorizontalVector(0d, -9d),
        };

        var result = _simulator.Simulate(state, NeutralCommand(), Attributes, Rate);

        Assert.Equal(state.HorizontalVelocity, result.HorizontalVelocity);
    }

    [Fact]
    public void AirInputAppliesBoundedAccelerationInsteadOfAssigningVelocity()
    {
        var state = Airborne(verticalVelocity: 0d);
        var command = new MovementCommand(
            1,
            SimulationInstant.Zero,
            new HorizontalVector(1d, 0d),
            0d,
            0d);

        var result = _simulator.Simulate(state, command, Attributes, Rate);

        Assert.Equal(Attributes.LateralAirAcceleration / Rate.TicksPerSecond, result.HorizontalVelocity.Length, 8);
        Assert.True(result.HorizontalVelocity.Length < Attributes.MaximumAirSpeed);
    }

    [Fact]
    public void ForwardInputIsStrongerThanLateralInputFromRest()
    {
        var state = Airborne(verticalVelocity: 0d);
        var lateralCommand = new MovementCommand(
            1,
            SimulationInstant.Zero,
            new HorizontalVector(1d, 0d),
            0d,
            0d);
        var forwardCommand = new MovementCommand(
            2,
            SimulationInstant.Zero,
            new HorizontalVector(0d, -1d),
            0d,
            0d);

        var lateral = _simulator.Simulate(state, lateralCommand, Attributes, Rate);
        var forward = _simulator.Simulate(state, forwardCommand, Attributes, Rate);

        Assert.Equal(Attributes.LateralAirAcceleration / 60d, lateral.HorizontalVelocity.Length, 8);
        Assert.Equal(Attributes.ForwardAirAcceleration / 60d, forward.HorizontalVelocity.Length, 8);
        Assert.True(forward.HorizontalVelocity.Length > lateral.HorizontalVelocity.Length);
    }

    [Fact]
    public void LateralAuthorityDecreasesAtHighSpeed()
    {
        var command = new MovementCommand(
            1,
            SimulationInstant.Zero,
            new HorizontalVector(1d, 0d),
            0d,
            0d);
        var slow = Airborne(0d);
        var fast = Airborne(0d) with
        {
            HorizontalVelocity = new HorizontalVector(0d, -Attributes.MaximumAirSpeed),
        };

        var slowResult = _simulator.Simulate(slow, command, Attributes, Rate);
        var fastResult = _simulator.Simulate(fast, command, Attributes, Rate);
        var slowChange = (slowResult.HorizontalVelocity - slow.HorizontalVelocity).Length;
        var fastChange = (fastResult.HorizontalVelocity - fast.HorizontalVelocity).Length;

        Assert.True(fastChange < slowChange * 0.2d);
    }

    [Fact]
    public void RunJumpDoesNotManufactureSprintSpeed()
    {
        var state = Airborne(0d) with
        {
            HorizontalVelocity = new HorizontalVector(0d, -Attributes.MaximumRunAirSpeed),
        };
        var command = new MovementCommand(
            1,
            SimulationInstant.Zero,
            new HorizontalVector(0d, -1d),
            0d,
            0d);

        var result = _simulator.Simulate(state, command, Attributes, Rate);

        Assert.Equal(state.HorizontalVelocity, result.HorizontalVelocity);
    }

    [Fact]
    public void SprintHeldInAirMayAccelerateFromRunTowardSprintSpeed()
    {
        var state = Airborne(0d) with
        {
            HorizontalVelocity = new HorizontalVector(0d, -Attributes.MaximumRunAirSpeed),
        };
        var command = new MovementCommand(
            1,
            SimulationInstant.Zero,
            new HorizontalVector(0d, -1d),
            0d,
            0d,
            heldButtons: MovementButtons.Sprint);

        var result = _simulator.Simulate(state, command, Attributes, Rate);

        Assert.True(result.HorizontalVelocity.Length > state.HorizontalVelocity.Length);
        Assert.True(result.HorizontalVelocity.Length < Attributes.MaximumAirSpeed);
    }

    [Fact]
    public void ReleasingSprintInAirDoesNotDiscardExistingMomentum()
    {
        var state = Airborne(0d) with
        {
            HorizontalVelocity = new HorizontalVector(0d, -Attributes.MaximumAirSpeed),
        };
        var command = new MovementCommand(
            1,
            SimulationInstant.Zero,
            new HorizontalVector(0d, -1d),
            0d,
            0d);

        var result = _simulator.Simulate(state, command, Attributes, Rate);

        Assert.Equal(state.HorizontalVelocity, result.HorizontalVelocity);
    }

    [Fact]
    public void RejectsGroundedStateAtStrategyBoundary()
    {
        var grounded = MovementRuntimeState.CreateGrounded(SimulationInstant.Zero);

        Assert.Throws<ArgumentException>(() =>
            _simulator.Simulate(grounded, NeutralCommand(), Attributes, Rate));
    }

    private static MovementRuntimeState Airborne(double verticalVelocity) => new(
        HorizontalVector.Zero,
        verticalVelocity,
        0d,
        LocomotionMode.Airborne,
        PostureMode.Standing,
        MovementActionMode.Ready,
        SimulationInstant.Zero);

    private static MovementCommand NeutralCommand() => new(
        1,
        SimulationInstant.Zero,
        HorizontalVector.Zero,
        0d,
        0d);
}
