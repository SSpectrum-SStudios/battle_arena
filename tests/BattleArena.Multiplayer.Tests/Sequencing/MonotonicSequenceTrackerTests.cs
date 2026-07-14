using BattleArena.Multiplayer.Sequencing;

namespace BattleArena.Multiplayer.Tests.Sequencing;

public sealed class MonotonicSequenceTrackerTests
{
    [Fact]
    public void TrackerReportsGapsAndRejectsOldPackets()
    {
        var tracker = new MonotonicSequenceTracker();

        var first = tracker.Observe(10);
        var gap = tracker.Observe(13);
        var stale = tracker.Observe(12);
        var next = tracker.Observe(14);

        Assert.Equal(SequenceObservationKind.First, first.Kind);
        Assert.Equal(SequenceObservationKind.Gap, gap.Kind);
        Assert.Equal(2UL, gap.MissingBeforeSequence);
        Assert.Equal(SequenceObservationKind.DuplicateOrStale, stale.Kind);
        Assert.False(stale.ShouldProcess);
        Assert.Equal(SequenceObservationKind.Consecutive, next.Kind);
        Assert.Equal(2UL, tracker.TotalMissing);
        Assert.Equal(14UL, tracker.HighestObserved);
    }

    [Fact]
    public void ZeroSequenceIsRejected()
    {
        var tracker = new MonotonicSequenceTracker();

        Assert.Throws<ArgumentOutOfRangeException>(() => tracker.Observe(0));
    }
}
