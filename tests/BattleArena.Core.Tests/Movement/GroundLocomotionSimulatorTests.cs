using BattleArena.Core.Common;
using BattleArena.Core.Movement;

namespace BattleArena.Core.Tests.Movement;

public sealed class GroundLocomotionSimulatorTests
{
    private static readonly SimulationRate Rate = new(60);
    private static readonly GroundMovementAttributes Attributes = new(
        maximumRunSpeed: 6d,
        maximumSprintSpeed: 9d,
        runAcceleration: 30d,
        sprintAcceleration: 24d,
        brakingDeceleration: 36d,
        reversalDeceleration: 48d,
        lowSpeedTurnRateRadians: DegreesToRadians(720d),
        highSpeedTurnRateRadians: DegreesToRadians(240d),
        reversalDotThreshold: -0.25d);

    private readonly GroundLocomotionSimulator _simulator = new();

    [Fact]
    public void RunAcceleratesAcrossTicksInsteadOfAssigningMaximumSpeed()
    {
        var state = Grounded();
        var command = Command(new HorizontalVector(0d, -1d));

        var first = _simulator.Simulate(state, command, Attributes, Rate);
        var second = _simulator.Simulate(first, command, Attributes, Rate);

        Assert.Equal(0.5d, first.HorizontalVelocity.Length, 8);
        Assert.Equal(1d, second.HorizontalVelocity.Length, 8);
        Assert.True(second.HorizontalVelocity.Length < Attributes.MaximumRunSpeed);
    }

    [Fact]
    public void RunConvergesOnConfiguredMaximumSpeed()
    {
        var state = Grounded();
        var command = Command(new HorizontalVector(0d, -1d));

        for (var tick = 0; tick < 120; tick++)
        {
            state = _simulator.Simulate(state, command, Attributes, Rate);
        }

        Assert.Equal(Attributes.MaximumRunSpeed, state.HorizontalVelocity.Length, 8);
    }

    [Fact]
    public void SprintUsesItsOwnAccelerationAndMaximumSpeed()
    {
        var state = Grounded();
        var command = Command(
            new HorizontalVector(0d, -1d),
            heldButtons: MovementButtons.Sprint);

        var first = _simulator.Simulate(state, command, Attributes, Rate);
        for (var tick = 1; tick < 120; tick++)
        {
            first = _simulator.Simulate(first, command, Attributes, Rate);
        }

        Assert.Equal(Attributes.MaximumSprintSpeed, first.HorizontalVelocity.Length, 8);
    }

    [Fact]
    public void AnalogInputScalesTargetSpeed()
    {
        var state = Grounded();
        var command = Command(new HorizontalVector(0d, -0.5d));

        for (var tick = 0; tick < 120; tick++)
        {
            state = _simulator.Simulate(state, command, Attributes, Rate);
        }

        Assert.Equal(Attributes.MaximumRunSpeed * 0.5d, state.HorizontalVelocity.Length, 8);
    }

    [Fact]
    public void ReleasingInputBrakesToAStopWithoutChangingFacing()
    {
        var initial = Grounded() with
        {
            HorizontalVelocity = new HorizontalVector(0d, -6d),
            FacingYawRadians = 0d,
        };
        var command = Command(HorizontalVector.Zero);

        var state = initial;
        for (var tick = 0; tick < 20; tick++)
        {
            state = _simulator.Simulate(state, command, Attributes, Rate);
        }

        Assert.Equal(HorizontalVector.Zero, state.HorizontalVelocity);
        Assert.Equal(0d, state.FacingYawRadians);
    }

    [Fact]
    public void OppositeInputBrakesBeforeAcceleratingInReverse()
    {
        var initial = Grounded() with
        {
            HorizontalVelocity = new HorizontalVector(0d, -6d),
        };
        var reverse = Command(new HorizontalVector(0d, 1d));

        var first = _simulator.Simulate(initial, reverse, Attributes, Rate);

        Assert.True(first.HorizontalVelocity.Z < 0d);
        Assert.Equal(5.2d, first.HorizontalVelocity.Length, 8);

        var state = first;
        for (var tick = 0; tick < 30; tick++)
        {
            state = _simulator.Simulate(state, reverse, Attributes, Rate);
        }

        Assert.True(state.HorizontalVelocity.Z > 0d);
    }

    [Fact]
    public void CameraYawRotatesMovementIntoWorldSpace()
    {
        var state = Grounded();
        var command = Command(
            new HorizontalVector(0d, -1d),
            viewYawRadians: Math.PI / 2d);

        state = _simulator.Simulate(state, command, Attributes, Rate);

        Assert.True(state.HorizontalVelocity.X < 0d);
        Assert.Equal(0d, state.HorizontalVelocity.Z, 8);
    }

    [Fact]
    public void HighSpeedDirectionChangesAreMoreCommittedThanLowSpeedChanges()
    {
        var lowSpeed = Grounded() with
        {
            HorizontalVelocity = new HorizontalVector(0d, -1d),
        };
        var highSpeed = Grounded() with
        {
            HorizontalVelocity = new HorizontalVector(0d, -8d),
        };
        var turnRight = Command(new HorizontalVector(1d, 0d));

        var lowResult = _simulator.Simulate(lowSpeed, turnRight, Attributes, Rate);
        var highResult = _simulator.Simulate(highSpeed, turnRight, Attributes, Rate);

        var lowAngle = Math.Abs(Math.Atan2(lowResult.HorizontalVelocity.X, -lowResult.HorizontalVelocity.Z));
        var highAngle = Math.Abs(Math.Atan2(highResult.HorizontalVelocity.X, -highResult.HorizontalVelocity.Z));
        Assert.True(lowAngle > highAngle);
    }

    [Fact]
    public void RejectsAnAirborneStateAtTheStrategyBoundary()
    {
        var state = Grounded() with { LocomotionMode = LocomotionMode.Airborne };

        Assert.Throws<ArgumentException>(() =>
            _simulator.Simulate(state, Command(HorizontalVector.Zero), Attributes, Rate));
    }

    private static MovementRuntimeState Grounded() => MovementRuntimeState.CreateGrounded(SimulationInstant.Zero);

    private static MovementCommand Command(
        HorizontalVector movement,
        double viewYawRadians = 0d,
        MovementButtons heldButtons = MovementButtons.None) =>
        new(
            sequence: 1,
            clientTick: SimulationInstant.Zero,
            movement,
            viewYawRadians,
            viewPitchRadians: 0d,
            heldButtons);

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;
}
