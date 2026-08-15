namespace BattleArena.Multiplayer.Timing;

public sealed class PeerLatencyEstimate
{
    public const double MaximumRewindMilliseconds = 200d;
    private bool _initialized;

    public double SmoothedRttMilliseconds { get; private set; }

    public double JitterMilliseconds { get; private set; }

    public double RewindAllowanceMilliseconds =>
        Math.Min(
            MaximumRewindMilliseconds,
            (SmoothedRttMilliseconds * 0.5d) +
            Math.Max(5d, JitterMilliseconds * 2d));

    public double LastRequestedAttackAgeMilliseconds { get; private set; }

    public double LastActualRewindMilliseconds { get; private set; }

    public void Observe(double rttMilliseconds)
    {
        if (!double.IsFinite(rttMilliseconds) || rttMilliseconds < 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rttMilliseconds),
                "An RTT sample must be finite and non-negative.");
        }

        if (!_initialized)
        {
            SmoothedRttMilliseconds = rttMilliseconds;
            JitterMilliseconds = 0d;
            _initialized = true;
            return;
        }

        var deviation = Math.Abs(rttMilliseconds - SmoothedRttMilliseconds);
        JitterMilliseconds += (deviation - JitterMilliseconds) * 0.25d;
        SmoothedRttMilliseconds +=
            (rttMilliseconds - SmoothedRttMilliseconds) * 0.125d;
    }

    public void RecordRewind(
        double requestedAttackAgeMilliseconds,
        double actualRewindMilliseconds)
    {
        LastRequestedAttackAgeMilliseconds = Math.Max(
            0d,
            requestedAttackAgeMilliseconds);
        LastActualRewindMilliseconds = Math.Clamp(
            actualRewindMilliseconds,
            0d,
            MaximumRewindMilliseconds);
    }
}
