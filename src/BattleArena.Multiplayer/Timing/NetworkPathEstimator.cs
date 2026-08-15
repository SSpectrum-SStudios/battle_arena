namespace BattleArena.Multiplayer.Timing;

public sealed class NetworkPathEstimator : INetworkPathEstimator
{
    private const double RttSmoothing = 0.125d;
    private const double JitterSmoothing = 0.25d;
    private const double LossSmoothing = 0.1d;

    private readonly double _expectedPacketIntervalMicroseconds;
    private bool _hasRtt;
    private ulong _latestSequence;
    private ulong _latestArrivalTimestampMicroseconds;
    private double _smoothedRttMilliseconds;
    private double _rttJitterMilliseconds;
    private double _arrivalJitterMilliseconds;
    private double _estimatedLossRate;
    private long _missingPackets;
    private long _reorderedPackets;

    public NetworkPathEstimator(double expectedPacketsPerSecond)
    {
        if (!double.IsFinite(expectedPacketsPerSecond) || expectedPacketsPerSecond <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedPacketsPerSecond));
        }

        _expectedPacketIntervalMicroseconds = 1_000_000d / expectedPacketsPerSecond;
    }

    public NetworkPathEstimate Current => new(
        _smoothedRttMilliseconds,
        _rttJitterMilliseconds,
        _arrivalJitterMilliseconds,
        _estimatedLossRate,
        _missingPackets,
        _reorderedPackets,
        _latestSequence);

    public void ObserveRoundTrip(double milliseconds)
    {
        if (!double.IsFinite(milliseconds) || milliseconds < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(milliseconds));
        }

        if (!_hasRtt)
        {
            _smoothedRttMilliseconds = milliseconds;
            _hasRtt = true;
            return;
        }

        var deviation = Math.Abs(milliseconds - _smoothedRttMilliseconds);
        _rttJitterMilliseconds +=
            (deviation - _rttJitterMilliseconds) * JitterSmoothing;
        _smoothedRttMilliseconds +=
            (milliseconds - _smoothedRttMilliseconds) * RttSmoothing;
    }

    public void ObservePacket(ulong sequence, ulong arrivalTimestampMicroseconds)
    {
        if (sequence == 0 || arrivalTimestampMicroseconds == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sequence),
                "Packet sequence and arrival timestamp must be positive.");
        }

        if (_latestSequence == 0)
        {
            _latestSequence = sequence;
            _latestArrivalTimestampMicroseconds = arrivalTimestampMicroseconds;
            return;
        }

        if (sequence <= _latestSequence)
        {
            _reorderedPackets++;
            return;
        }

        var sequenceDelta = sequence - _latestSequence;
        var missing = checked((long)sequenceDelta - 1);
        _missingPackets += missing;
        var lossSample = missing / (double)sequenceDelta;
        _estimatedLossRate +=
            (lossSample - _estimatedLossRate) * LossSmoothing;

        var arrivalDelta = arrivalTimestampMicroseconds >=
                           _latestArrivalTimestampMicroseconds
            ? arrivalTimestampMicroseconds - _latestArrivalTimestampMicroseconds
            : 0UL;
        var expectedDelta = _expectedPacketIntervalMicroseconds * sequenceDelta;
        var deviationMilliseconds =
            Math.Abs(arrivalDelta - expectedDelta) / 1_000d;
        _arrivalJitterMilliseconds +=
            (deviationMilliseconds - _arrivalJitterMilliseconds) * JitterSmoothing;

        _latestSequence = sequence;
        _latestArrivalTimestampMicroseconds = arrivalTimestampMicroseconds;
    }
}
