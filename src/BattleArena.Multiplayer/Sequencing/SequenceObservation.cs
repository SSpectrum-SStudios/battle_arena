namespace BattleArena.Multiplayer.Sequencing;

public readonly record struct SequenceObservation(
    SequenceObservationKind Kind,
    ulong Sequence,
    ulong MissingBeforeSequence)
{
    public bool ShouldProcess => Kind != SequenceObservationKind.DuplicateOrStale;
}
