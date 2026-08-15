using BattleArena.Core.Common;
using BattleArena.Core.Movement;

namespace BattleArena.Core.Tests.Movement;

public sealed class JumpFallSimulatorTests
{
    private static readonly SimulationRate Rate = new(60);
    private static readonly JumpMovementAttributes Attributes = new(
        jumpVelocity: 13.4d,
        jumpVelocityPerHorizontalSpeed: 0.04d,
        risingGravity: 23d,
        apexGravity: 16d,
        fallingGravity: 45d,
        maximumFallSpeed: 36d,
        jumpReleaseGravity: 55d,
        apexVelocityThreshold: 1.5d,
        coyoteDuration: new SimulationDuration(7),
        inputBufferDuration: new SimulationDuration(7));

    private readonly JumpFallSimulator _simulator = new();

    [Fact]
    public void GroundedJumpPressLaunchesImmediately()
    {
        var result = _simulator.Simulate(
            MovementRuntimeState.CreateGrounded(SimulationInstant.Zero),
            Command(0, pressed: MovementButtons.Jump),
            isGrounded: true,
            Attributes,
            Rate);

        Assert.Equal(LocomotionMode.Airborne, result.LocomotionMode);
        Assert.Equal(JumpPhase.Rising, result.JumpPhase);
        Assert.Equal(Attributes.JumpVelocity, result.VerticalVelocity);
    }

    [Fact]
    public void WalkingOffEdgeImmediatelyBeginsFallingCurve()
    {
        var result = _simulator.Simulate(
            MovementRuntimeState.CreateGrounded(SimulationInstant.Zero),
            Command(1),
            isGrounded: false,
            Attributes,
            Rate);

        Assert.Equal(LocomotionMode.Airborne, result.LocomotionMode);
        Assert.Equal(JumpPhase.Falling, result.JumpPhase);
        Assert.Equal(-Attributes.FallingGravity / Rate.TicksPerSecond, result.VerticalVelocity, 8);
    }

    [Fact]
    public void CoyoteJumpWorksThroughInclusiveAuthoredWindow()
    {
        var state = MovementRuntimeState.CreateGrounded(new SimulationInstant(10));

        var result = _simulator.Simulate(
            state,
            Command(17, pressed: MovementButtons.Jump),
            isGrounded: false,
            Attributes,
            Rate);

        Assert.Equal(Attributes.JumpVelocity, result.VerticalVelocity);
        Assert.Equal(JumpPhase.Rising, result.JumpPhase);
    }

    [Fact]
    public void CoyoteJumpExpiresAfterAuthoredWindow()
    {
        var state = MovementRuntimeState.CreateGrounded(new SimulationInstant(10));

        var result = _simulator.Simulate(
            state,
            Command(18, pressed: MovementButtons.Jump),
            isGrounded: false,
            Attributes,
            Rate);

        Assert.NotEqual(Attributes.JumpVelocity, result.VerticalVelocity);
        Assert.Equal(JumpPhase.Falling, result.JumpPhase);
        Assert.NotNull(result.BufferedJumpUntil);
    }

    [Fact]
    public void BufferedPressLaunchesOnLanding()
    {
        var airborne = Airborne(tick: 0, verticalVelocity: -5d, JumpPhase.Falling);
        var buffered = _simulator.Simulate(
            airborne,
            Command(10, pressed: MovementButtons.Jump),
            isGrounded: false,
            Attributes,
            Rate);

        var result = _simulator.Simulate(
            buffered,
            Command(12),
            isGrounded: true,
            Attributes,
            Rate);

        Assert.Equal(Attributes.JumpVelocity, result.VerticalVelocity);
        Assert.Null(result.BufferedJumpUntil);
    }

    [Fact]
    public void ExpiredBufferedPressDoesNotLaunchOnLanding()
    {
        var airborne = Airborne(tick: 0, verticalVelocity: -5d, JumpPhase.Falling);
        var buffered = _simulator.Simulate(
            airborne,
            Command(10, pressed: MovementButtons.Jump),
            isGrounded: false,
            Attributes,
            Rate);

        var result = _simulator.Simulate(
            buffered,
            Command(18),
            isGrounded: true,
            Attributes,
            Rate);

        Assert.Equal(LocomotionMode.Grounded, result.LocomotionMode);
        Assert.Equal(0d, result.VerticalVelocity);
    }

    [Fact]
    public void ReleasingJumpChangesGravityWithoutCuttingVelocityInstantly()
    {
        var state = Airborne(tick: 0, verticalVelocity: 10d, JumpPhase.Rising);
        var released = _simulator.Simulate(
            state,
            Command(20, released: MovementButtons.Jump),
            isGrounded: false,
            Attributes,
            Rate);
        var next = _simulator.Simulate(
            released,
            Command(21, released: MovementButtons.Jump),
            isGrounded: false,
            Attributes,
            Rate);

        var expectedAfterCut = 10d - (Attributes.JumpReleaseGravity / Rate.TicksPerSecond);
        Assert.Equal(expectedAfterCut, released.VerticalVelocity, 8);
        Assert.Equal(
            expectedAfterCut - (Attributes.JumpReleaseGravity / Rate.TicksPerSecond),
            next.VerticalVelocity,
            8);
    }

    [Fact]
    public void ApexUsesLighterAuthoredGravity()
    {
        var result = _simulator.Simulate(
            Airborne(tick: 0, verticalVelocity: 1d, JumpPhase.Apex),
            Command(20),
            isGrounded: false,
            Attributes,
            Rate);

        var appliedGravity = (1d - result.VerticalVelocity) * Rate.TicksPerSecond;
        Assert.InRange(appliedGravity, Attributes.ApexGravity, Attributes.RisingGravity);
    }

    [Fact]
    public void FallingVelocityClampsAtTerminalSpeed()
    {
        var result = _simulator.Simulate(
            Airborne(tick: 0, verticalVelocity: -35.9d, JumpPhase.Falling),
            Command(20),
            isGrounded: false,
            Attributes,
            Rate);

        Assert.Equal(-Attributes.MaximumFallSpeed, result.VerticalVelocity, 8);
    }

    [Fact]
    public void HeldFullJumpHasCartoonishPeakHeight()
    {
        Assert.InRange(PeakHeight(0d), 3.8d, 4.05d);
    }

    [Fact]
    public void SprintMomentumAddsAuthoredJumpHeightContinuously()
    {
        var runPeak = PeakHeight(6d);
        var sprintPeak = PeakHeight(12.5d);

        Assert.InRange(runPeak, 4.0d, 4.3d);
        Assert.InRange(sprintPeak, 4.2d, 4.5d);
        Assert.True(sprintPeak > runPeak);
    }

    private double PeakHeight(double horizontalSpeed)
    {
        var initial = MovementRuntimeState.CreateGrounded(SimulationInstant.Zero) with
        {
            HorizontalVelocity = new HorizontalVector(0d, -horizontalSpeed),
        };
        var state = _simulator.Simulate(
            initial,
            Command(0, pressed: MovementButtons.Jump),
            isGrounded: true,
            Attributes,
            Rate);
        var height = state.VerticalVelocity / Rate.TicksPerSecond;
        var peak = height;

        for (var tick = 1; tick < 180 && state.VerticalVelocity > 0d; tick++)
        {
            state = _simulator.Simulate(state, Command(tick), false, Attributes, Rate);
            height += state.VerticalVelocity / Rate.TicksPerSecond;
            peak = Math.Max(peak, height);
        }

        return peak;
    }

    private static MovementRuntimeState Airborne(long tick, double verticalVelocity, JumpPhase phase) => new(
        HorizontalVector.Zero,
        verticalVelocity,
        0d,
        LocomotionMode.Airborne,
        PostureMode.Standing,
        MovementActionMode.Ready,
        new SimulationInstant(tick),
        phase,
        new SimulationInstant(tick));

    private static MovementCommand Command(
        long tick,
        MovementButtons pressed = MovementButtons.None,
        MovementButtons released = MovementButtons.None) => new(
        (ulong)tick + 1,
        new SimulationInstant(tick),
        HorizontalVector.Zero,
        0d,
        0d,
        pressedButtons: pressed,
        releasedButtons: released);
}
