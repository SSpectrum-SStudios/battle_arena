using BattleArena.Core.Common;

namespace BattleArena.Core.Actions;

public abstract record TargetHitPolicy
{
    private TargetHitPolicy()
    {
    }

    internal abstract bool Allows(
        int acceptedHitCount,
        SimulationInstant? lastAcceptedAt,
        SimulationInstant currentTime);

    public sealed record Limited : TargetHitPolicy
    {
        public Limited(int maximumHitsPerTarget)
        {
            if (maximumHitsPerTarget <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumHitsPerTarget),
                    "Maximum hits per target must be positive.");
            }

            MaximumHitsPerTarget = maximumHitsPerTarget;
        }

        public int MaximumHitsPerTarget { get; }

        internal override bool Allows(
            int acceptedHitCount,
            SimulationInstant? lastAcceptedAt,
            SimulationInstant currentTime) =>
            acceptedHitCount < MaximumHitsPerTarget;
    }

    public sealed record Repeating : TargetHitPolicy
    {
        public Repeating(SimulationDuration minimumInterval, int? maximumHitsPerTarget = null)
        {
            if (minimumInterval == SimulationDuration.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimumInterval),
                    "A repeating hit interval must be positive.");
            }

            if (maximumHitsPerTarget <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumHitsPerTarget),
                    "An optional maximum hit count must be positive.");
            }

            MinimumInterval = minimumInterval;
            MaximumHitsPerTarget = maximumHitsPerTarget;
        }

        public SimulationDuration MinimumInterval { get; }

        public int? MaximumHitsPerTarget { get; }

        internal override bool Allows(
            int acceptedHitCount,
            SimulationInstant? lastAcceptedAt,
            SimulationInstant currentTime)
        {
            if (MaximumHitsPerTarget is { } maximum && acceptedHitCount >= maximum)
            {
                return false;
            }

            return lastAcceptedAt is null || currentTime - lastAcceptedAt.Value >= MinimumInterval;
        }
    }
}
