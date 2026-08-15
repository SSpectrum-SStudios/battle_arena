namespace BattleArena.Multiplayer.Timing;

public sealed class AdaptivePredictionTimingPolicy : IPredictionTimingPolicy
{
    private const int MaximumPresentationDelayTicks = 6;
    private const int StableEvaluationsBeforeDecrease = 60;
    private int _presentationDelayTicks = 1;
    private int _stableEvaluations;

    public PredictionTimingDecision Evaluate(PredictionTimingContext context)
    {
        if (context.SimulationTicksPerSecond == 0 || context.RecentBufferUnderruns < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(context));
        }

        var millisecondsPerTick = 1_000d / context.SimulationTicksPerSecond;
        var safetyMilliseconds =
            2d + (context.Path.EffectiveJitterMilliseconds * 2d);
        var desiredTicks = Math.Max(
            1,
            checked((int)Math.Ceiling(safetyMilliseconds / millisecondsPerTick)));

        if (context.Path.SmoothedRttMilliseconds > 80d)
        {
            desiredTicks++;
        }

        if (context.Path.SmoothedRttMilliseconds > 140d)
        {
            desiredTicks++;
        }

        if (context.Path.EstimatedLossRate > 0.01d)
        {
            desiredTicks++;
        }

        if (context.Path.EstimatedLossRate > 0.05d)
        {
            desiredTicks++;
        }

        desiredTicks += Math.Min(2, context.RecentBufferUnderruns);
        desiredTicks = Math.Clamp(desiredTicks, 1, MaximumPresentationDelayTicks);

        if (desiredTicks > _presentationDelayTicks)
        {
            _presentationDelayTicks = desiredTicks;
            _stableEvaluations = 0;
        }
        else if (desiredTicks < _presentationDelayTicks)
        {
            _stableEvaluations++;
            if (_stableEvaluations >= StableEvaluationsBeforeDecrease)
            {
                _presentationDelayTicks--;
                _stableEvaluations = 0;
            }
        }
        else
        {
            _stableEvaluations = 0;
        }

        return new PredictionTimingDecision(
            _presentationDelayTicks,
            MillisecondsToTicks(150d, context.SimulationTicksPerSecond),
            MillisecondsToTicks(250d, context.SimulationTicksPerSecond));
    }

    private static int MillisecondsToTicks(double milliseconds, uint ticksPerSecond) =>
        checked((int)Math.Ceiling(milliseconds * ticksPerSecond / 1_000d));
}
