namespace BattleArena.Multiplayer.Sequencing;

public sealed class MonotonicSequenceTracker
{
    public ulong? HighestObserved { get; private set; }

    public ulong TotalMissing { get; private set; }

    public SequenceObservation Observe(ulong sequence)
    {
        if (sequence == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), "A protocol sequence must be positive.");
        }

        if (HighestObserved is null)
        {
            HighestObserved = sequence;
            return new SequenceObservation(SequenceObservationKind.First, sequence, 0);
        }

        var previous = HighestObserved.Value;
        if (sequence <= previous)
        {
            return new SequenceObservation(SequenceObservationKind.DuplicateOrStale, sequence, 0);
        }

        var missing = sequence - previous - 1;
        HighestObserved = sequence;
        TotalMissing = checked(TotalMissing + missing);

        return new SequenceObservation(
            missing == 0 ? SequenceObservationKind.Consecutive : SequenceObservationKind.Gap,
            sequence,
            missing);
    }
}
