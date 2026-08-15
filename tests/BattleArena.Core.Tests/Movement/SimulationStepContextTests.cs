using BattleArena.Core.Common;
using BattleArena.Core.Movement.Simulation;

namespace BattleArena.Core.Tests.Movement;

public sealed class SimulationStepContextTests
{
    [Fact]
    public void CurrentStepRetainsOneSharedFrameAndRate()
    {
        var context = SimulationStepContext.Current(
            new SimulationInstant(42),
            new SimulationRate(60));

        Assert.True(context.IsValid);
        Assert.Equal(new SimulationInstant(42), context.Frame);
        Assert.Equal(new SimulationRate(60), context.Rate);
        Assert.Equal(SimulationPassKind.Current, context.Pass);
        Assert.Equal(new SimulationDuration(1), context.StepDuration);
        Assert.Equal(1m / 60m, context.StepSeconds);
    }

    [Fact]
    public void ReplayChangesOnlyPassIdentity()
    {
        var frame = new SimulationInstant(900);
        var rate = new SimulationRate(120);

        var current = SimulationStepContext.Current(frame, rate);
        var replay = SimulationStepContext.Replay(frame, rate);

        Assert.Equal(current.Frame, replay.Frame);
        Assert.Equal(current.Rate, replay.Rate);
        Assert.Equal(SimulationPassKind.HistoricalReplay, replay.Pass);
        Assert.Equal(1m / 120m, replay.StepSeconds);
    }

    [Fact]
    public void ElapsedFramesAndSecondsUseTheSameExactRate()
    {
        var context = SimulationStepContext.Current(
            new SimulationInstant(125),
            new SimulationRate(60));

        var elapsed = context.ElapsedSince(new SimulationInstant(5));

        Assert.Equal(new SimulationDuration(120), elapsed);
        Assert.Equal(2m, context.SecondsFor(elapsed));
    }

    [Fact]
    public void NextFrameIsCheckedAndPreservesRateAndPass()
    {
        var context = SimulationStepContext.Replay(
            new SimulationInstant(8),
            new SimulationRate(144));

        var next = context.NextFrame();

        Assert.Equal(new SimulationInstant(9), next.Frame);
        Assert.Equal(context.Rate, next.Rate);
        Assert.Equal(context.Pass, next.Pass);
        Assert.Throws<OverflowException>(() =>
            SimulationStepContext.Current(
                    new SimulationInstant(long.MaxValue),
                    SimulationRate.Default)
                .NextFrame());
    }

    [Fact]
    public void ConstructionRejectsInvalidRateAndPass()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SimulationStepContext(
                SimulationInstant.Zero,
                default,
                SimulationPassKind.Current));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SimulationStepContext(
                SimulationInstant.Zero,
                SimulationRate.Default,
                SimulationPassKind.Unspecified));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SimulationStepContext(
                SimulationInstant.Zero,
                SimulationRate.Default,
                (SimulationPassKind)99));
    }

    [Fact]
    public void DefaultContextFailsClosed()
    {
        var context = default(SimulationStepContext);

        Assert.False(context.IsValid);
        Assert.Throws<InvalidOperationException>(() => _ = context.StepDuration);
        Assert.Throws<InvalidOperationException>(() => _ = context.StepSeconds);
        Assert.Throws<InvalidOperationException>(() =>
            context.ElapsedSince(SimulationInstant.Zero));
        Assert.Throws<InvalidOperationException>(() =>
            context.SecondsFor(SimulationDuration.Zero));
        Assert.Throws<InvalidOperationException>(() => context.NextFrame());
    }

    [Fact]
    public void ElapsedTimeCannotRunBackward()
    {
        var context = SimulationStepContext.Current(
            new SimulationInstant(10),
            SimulationRate.Default);

        Assert.Throws<InvalidOperationException>(() =>
            context.ElapsedSince(new SimulationInstant(11)));
    }
}
