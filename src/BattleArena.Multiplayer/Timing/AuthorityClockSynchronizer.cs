namespace BattleArena.Multiplayer.Timing;

public sealed class AuthorityClockSynchronizer : IAuthorityClockSynchronizer
{
    private const double RttSmoothing = 0.125d;
    private const double JitterSmoothing = 0.25d;
    private const double OffsetSmoothing = 0.2d;
    private const double MaximumOffsetAdjustmentMicroseconds = 2_000d;

    private double _clockOffsetMicroseconds;
    private double _smoothedRttMicroseconds;
    private double _jitterMicroseconds;
    private ulong _anchorAuthorityTimestampMicroseconds;
    private ulong _anchorAuthorityTick;
    private uint _simulationTicksPerSecond;
    private int _sampleCount;

    public bool HasEstimate => _sampleCount > 0;

    public AuthorityTimeEstimate Current { get; private set; }

    public void Observe(AuthorityClockExchange exchange)
    {
        Validate(exchange);

        var localElapsed = checked((double)(exchange.ClientReceiveTimestampMicroseconds -
                                             exchange.ClientSendTimestampMicroseconds));
        var authorityProcessing = checked((double)(exchange.AuthoritySendTimestampMicroseconds -
                                                    exchange.AuthorityReceiveTimestampMicroseconds));
        var rttMicroseconds = Math.Max(0d, localElapsed - authorityProcessing);
        var offsetSample =
            ((double)exchange.AuthorityReceiveTimestampMicroseconds -
             exchange.ClientSendTimestampMicroseconds +
             (double)exchange.AuthoritySendTimestampMicroseconds -
             exchange.ClientReceiveTimestampMicroseconds) * 0.5d;

        if (_sampleCount == 0)
        {
            _clockOffsetMicroseconds = offsetSample;
            _smoothedRttMicroseconds = rttMicroseconds;
        }
        else
        {
            var deviation = Math.Abs(rttMicroseconds - _smoothedRttMicroseconds);
            _jitterMicroseconds +=
                (deviation - _jitterMicroseconds) * JitterSmoothing;
            _smoothedRttMicroseconds +=
                (rttMicroseconds - _smoothedRttMicroseconds) * RttSmoothing;

            var boundedOffsetDelta = Math.Clamp(
                offsetSample - _clockOffsetMicroseconds,
                -MaximumOffsetAdjustmentMicroseconds,
                MaximumOffsetAdjustmentMicroseconds);
            _clockOffsetMicroseconds += boundedOffsetDelta * OffsetSmoothing;
        }

        _sampleCount++;
        _anchorAuthorityTimestampMicroseconds =
            exchange.AuthoritySendTimestampMicroseconds;
        _anchorAuthorityTick = exchange.AuthorityTick;
        _simulationTicksPerSecond = exchange.SimulationTicksPerSecond;
        Current = CreateEstimate(exchange.ClientReceiveTimestampMicroseconds);
    }

    public AuthorityTimeEstimate Estimate(ulong localTimestampMicroseconds)
    {
        if (!HasEstimate)
        {
            throw new InvalidOperationException(
                "Authority time cannot be estimated before observing a clock exchange.");
        }

        Current = CreateEstimate(localTimestampMicroseconds);
        return Current;
    }

    private AuthorityTimeEstimate CreateEstimate(ulong localTimestampMicroseconds)
    {
        var estimatedAuthorityTimestamp =
            localTimestampMicroseconds + _clockOffsetMicroseconds;
        var elapsedAuthorityMicroseconds =
            estimatedAuthorityTimestamp - _anchorAuthorityTimestampMicroseconds;
        var tick = _anchorAuthorityTick +
                   (elapsedAuthorityMicroseconds * _simulationTicksPerSecond / 1_000_000d);

        return new AuthorityTimeEstimate(
            Math.Max(0d, tick),
            _clockOffsetMicroseconds / 1_000d,
            _smoothedRttMicroseconds / 1_000d,
            _jitterMicroseconds / 1_000d,
            Math.Min(1d, _sampleCount / 8d));
    }

    private static void Validate(AuthorityClockExchange exchange)
    {
        if (exchange.ClientSendTimestampMicroseconds == 0 ||
            exchange.AuthorityReceiveTimestampMicroseconds == 0 ||
            exchange.AuthoritySendTimestampMicroseconds <
            exchange.AuthorityReceiveTimestampMicroseconds ||
            exchange.ClientReceiveTimestampMicroseconds <
            exchange.ClientSendTimestampMicroseconds ||
            exchange.SimulationTicksPerSecond == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(exchange),
                "Clock exchange timestamps and simulation rate are invalid.");
        }
    }
}
