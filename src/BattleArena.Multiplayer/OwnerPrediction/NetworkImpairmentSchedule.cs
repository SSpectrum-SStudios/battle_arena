namespace BattleArena.Multiplayer.OwnerPrediction;

public enum NetworkImpairmentDirection
{
    Upstream = 0,
    Downstream = 1,
}

public enum NetworkLossReason
{
    None = 0,
    Independent = 1,
    Burst = 2,
}

/// <summary>
/// Immutable unreliable-application impairment policy. Reliable transport
/// retransmission and head-of-line behavior are intentionally outside this
/// model.
/// </summary>
public sealed class NetworkImpairmentPolicy
{
    public static NetworkImpairmentPolicy None { get; } = new();

    public NetworkImpairmentPolicy(
        TimeSpan upstreamBaseDelay = default,
        TimeSpan downstreamBaseDelay = default,
        TimeSpan maximumJitter = default,
        double independentLossProbability = 0d,
        int burstIntervalPackets = 0,
        int burstLengthPackets = 0,
        double duplicateProbability = 0d,
        TimeSpan duplicateSpacing = default,
        double reorderProbability = 0d,
        TimeSpan reorderAdditionalDelay = default,
        double stallProbability = 0d,
        TimeSpan stallDuration = default)
    {
        UpstreamBaseDelay = ValidateDuration(
            upstreamBaseDelay,
            nameof(upstreamBaseDelay));
        DownstreamBaseDelay = ValidateDuration(
            downstreamBaseDelay,
            nameof(downstreamBaseDelay));
        MaximumJitter = ValidateDuration(maximumJitter, nameof(maximumJitter));
        IndependentLossProbability = ValidateProbability(
            independentLossProbability,
            nameof(independentLossProbability));
        DuplicateProbability = ValidateProbability(
            duplicateProbability,
            nameof(duplicateProbability));
        ReorderProbability = ValidateProbability(
            reorderProbability,
            nameof(reorderProbability));
        StallProbability = ValidateProbability(
            stallProbability,
            nameof(stallProbability));
        DuplicateSpacing = ValidateConditionalDuration(
            duplicateSpacing,
            duplicateProbability,
            nameof(duplicateSpacing));
        ReorderAdditionalDelay = ValidateConditionalDuration(
            reorderAdditionalDelay,
            reorderProbability,
            nameof(reorderAdditionalDelay));
        StallDuration = ValidateConditionalDuration(
            stallDuration,
            stallProbability,
            nameof(stallDuration));

        if (burstIntervalPackets == 0 && burstLengthPackets == 0)
        {
            BurstIntervalPackets = 0;
            BurstLengthPackets = 0;
        }
        else if (burstIntervalPackets <= 0 ||
                 burstLengthPackets <= 0 ||
                 burstLengthPackets > burstIntervalPackets)
        {
            throw new ArgumentOutOfRangeException(
                nameof(burstLengthPackets),
                burstLengthPackets,
                "Burst length must be positive and no larger than its positive interval.");
        }
        else
        {
            BurstIntervalPackets = burstIntervalPackets;
            BurstLengthPackets = burstLengthPackets;
        }
    }

    public TimeSpan UpstreamBaseDelay { get; }

    public TimeSpan DownstreamBaseDelay { get; }

    public TimeSpan MaximumJitter { get; }

    public double IndependentLossProbability { get; }

    public int BurstIntervalPackets { get; }

    public int BurstLengthPackets { get; }

    public double DuplicateProbability { get; }

    public TimeSpan DuplicateSpacing { get; }

    public double ReorderProbability { get; }

    public TimeSpan ReorderAdditionalDelay { get; }

    public double StallProbability { get; }

    public TimeSpan StallDuration { get; }

    private static TimeSpan ValidateDuration(TimeSpan value, string parameterName)
    {
        if (value < TimeSpan.Zero || value > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "An impairment duration must be between zero and one hour.");
        }

        return value;
    }

    private static TimeSpan ValidateConditionalDuration(
        TimeSpan value,
        double probability,
        string parameterName)
    {
        ValidateDuration(value, parameterName);
        if ((probability == 0d) != (value == TimeSpan.Zero))
        {
            throw new ArgumentException(
                "A probabilistic impairment and its duration must be enabled together.",
                parameterName);
        }

        return value;
    }

    private static double ValidateProbability(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0d || value > 1d)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "An impairment probability must be finite and between zero and one.");
        }

        return value;
    }
}

public readonly record struct NetworkImpairmentDecision(
    NetworkLossReason LossReason,
    TimeSpan PrimaryDelay,
    TimeSpan? DuplicateDelay,
    bool Reordered,
    bool Stalled)
{
    public bool IsDropped => LossReason != NetworkLossReason.None;
}

/// <summary>
/// Stateless deterministic schedule keyed by seed, direction, packet ordinal,
/// and independent random stream. Evaluation order and thread interleaving do
/// not affect a packet's result.
/// </summary>
public sealed class NetworkImpairmentSchedule
{
    private const ulong DirectionSalt = 0x9E3779B97F4A7C15UL;
    private const ulong OrdinalSalt = 0xD1B54A32D192ED03UL;

    private readonly ulong _seed;
    private readonly NetworkImpairmentPolicy _policy;

    public NetworkImpairmentSchedule(ulong seed, NetworkImpairmentPolicy policy)
    {
        _seed = seed;
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    public NetworkImpairmentDecision Evaluate(
        NetworkImpairmentDirection direction,
        ulong packetOrdinal)
    {
        ValidateDirection(direction);

        var independentLoss = Hits(
            direction,
            packetOrdinal,
            stream: 1,
            _policy.IndependentLossProbability);
        var burstLoss = IsBurstLoss(direction, packetOrdinal);
        if (independentLoss || burstLoss)
        {
            return new NetworkImpairmentDecision(
                independentLoss ? NetworkLossReason.Independent : NetworkLossReason.Burst,
                TimeSpan.Zero,
                null,
                Reordered: false,
                Stalled: false);
        }

        var reordered = Hits(
            direction,
            packetOrdinal,
            stream: 2,
            _policy.ReorderProbability);
        var stalled = Hits(
            direction,
            packetOrdinal,
            stream: 3,
            _policy.StallProbability);
        var duplicated = Hits(
            direction,
            packetOrdinal,
            stream: 4,
            _policy.DuplicateProbability);
        var delayTicks = CalculateBaseAndJitterTicks(direction, packetOrdinal);
        if (reordered)
        {
            delayTicks = checked(delayTicks + _policy.ReorderAdditionalDelay.Ticks);
        }

        if (stalled)
        {
            delayTicks = checked(delayTicks + _policy.StallDuration.Ticks);
        }

        var primaryDelay = TimeSpan.FromTicks(delayTicks);
        TimeSpan? duplicateDelay = duplicated
            ? TimeSpan.FromTicks(checked(delayTicks + _policy.DuplicateSpacing.Ticks))
            : null;

        return new NetworkImpairmentDecision(
            NetworkLossReason.None,
            primaryDelay,
            duplicateDelay,
            reordered,
            stalled);
    }

    private long CalculateBaseAndJitterTicks(
        NetworkImpairmentDirection direction,
        ulong packetOrdinal)
    {
        var baseTicks = direction == NetworkImpairmentDirection.Upstream
            ? _policy.UpstreamBaseDelay.Ticks
            : _policy.DownstreamBaseDelay.Ticks;
        var maximumJitterTicks = _policy.MaximumJitter.Ticks;
        if (maximumJitterTicks == 0)
        {
            return baseTicks;
        }

        var range = checked((ulong)(maximumJitterTicks * 2L + 1L));
        var offset = (long)(Sample(direction, packetOrdinal, stream: 5) % range) -
                     maximumJitterTicks;
        return Math.Max(0L, checked(baseTicks + offset));
    }

    private bool IsBurstLoss(NetworkImpairmentDirection direction, ulong packetOrdinal)
    {
        if (_policy.BurstIntervalPackets == 0)
        {
            return false;
        }

        var interval = (ulong)_policy.BurstIntervalPackets;
        var phase = Sample(direction, packetOrdinal: 0, stream: 6) % interval;
        var position = ((packetOrdinal % interval) + interval - phase) % interval;
        return position < (ulong)_policy.BurstLengthPackets;
    }

    private bool Hits(
        NetworkImpairmentDirection direction,
        ulong packetOrdinal,
        ulong stream,
        double probability)
    {
        if (probability <= 0d)
        {
            return false;
        }

        if (probability >= 1d)
        {
            return true;
        }

        var unit = (Sample(direction, packetOrdinal, stream) >> 11) *
                   (1d / 9_007_199_254_740_992d);
        return unit < probability;
    }

    private ulong Sample(
        NetworkImpairmentDirection direction,
        ulong packetOrdinal,
        ulong stream)
    {
        var value = unchecked(
            _seed ^
            ((ulong)direction + 1UL) * DirectionSalt ^
            (packetOrdinal + 1UL) * OrdinalSalt ^
            stream * 0x94D049BB133111EBUL);
        value = unchecked(value + 0x9E3779B97F4A7C15UL);
        value = unchecked((value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL);
        value = unchecked((value ^ (value >> 27)) * 0x94D049BB133111EBUL);
        return value ^ (value >> 31);
    }

    private static void ValidateDirection(NetworkImpairmentDirection direction)
    {
        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction), direction, null);
        }
    }
}
