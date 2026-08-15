using System.Collections;
using BattleArena.Multiplayer.OwnerPrediction;

namespace BattleArena.Multiplayer.Tests.OwnerPrediction;

public sealed class PredictionTelemetryRingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CapacityMustBePositive(int capacity)
    {
        Assert.Equal(
            "capacity",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new PredictionTelemetryRing<int>(capacity)).ParamName);
    }

    [Fact]
    public void SamplesRemainOldestToNewestBeforeCapacity()
    {
        var ring = new PredictionTelemetryRing<int>(4);

        ring.Add(10);
        ring.Add(20);
        ring.Add(30);

        Assert.Equal(4, ring.Capacity);
        Assert.Equal(3, ring.Count);
        Assert.Equal([10, 20, 30], ring.ToArray());
        Assert.Equal(10, ring[0]);
        Assert.Equal(30, ring[2]);
    }

    [Fact]
    public void WrapOverwritesExactlyOldestSample()
    {
        var ring = new PredictionTelemetryRing<int>(3);

        foreach (var value in Enumerable.Range(1, 7))
        {
            ring.Add(value);
        }

        Assert.Equal(3, ring.Capacity);
        Assert.Equal(3, ring.Count);
        Assert.Equal([5, 6, 7], ring.ToArray());
    }

    [Fact]
    public void CapacityOneAlwaysRetainsLatestSample()
    {
        var ring = new PredictionTelemetryRing<int>(1);

        ring.Add(1);
        ring.Add(2);
        ring.Add(3);

        Assert.Single(ring);
        Assert.Equal(3, ring[0]);
    }

    [Fact]
    public void CopyToUsesChronologicalOrderAndCallerStorage()
    {
        var ring = new PredictionTelemetryRing<int>(3);
        ring.Add(1);
        ring.Add(2);
        ring.Add(3);
        ring.Add(4);
        Span<int> destination = stackalloc int[5];

        var copied = ring.CopyTo(destination);

        Assert.Equal(3, copied);
        Assert.Equal([2, 3, 4], destination[..copied].ToArray());
        Assert.Equal(0, destination[3]);
    }

    [Fact]
    public void CopyRejectsDestinationSmallerThanCurrentCount()
    {
        var ring = new PredictionTelemetryRing<int>(3);
        ring.Add(1);
        ring.Add(2);

        Assert.Equal(
            "destination",
            Assert.Throws<ArgumentException>(() => ring.CopyTo(new int[1])).ParamName);
    }

    [Fact]
    public void ClearResetsOrderingWithoutChangingCapacity()
    {
        var ring = new PredictionTelemetryRing<string>(3);
        ring.Add("old-1");
        ring.Add("old-2");
        ring.Add("old-3");
        ring.Add("old-4");

        ring.Clear();
        ring.Add("new-1");
        ring.Add("new-2");

        Assert.Equal(3, ring.Capacity);
        Assert.Equal(2, ring.Count);
        Assert.Equal(["new-1", "new-2"], ring.ToArray());
    }

    [Fact]
    public void IndexerRejectsEmptyNegativeAndCountBoundary()
    {
        var ring = new PredictionTelemetryRing<int>(2);

        Assert.Equal(
            "index",
            Assert.Throws<ArgumentOutOfRangeException>(() => ring[0]).ParamName);
        ring.Add(1);
        Assert.Equal(
            "index",
            Assert.Throws<ArgumentOutOfRangeException>(() => ring[-1]).ParamName);
        Assert.Equal(
            "index",
            Assert.Throws<ArgumentOutOfRangeException>(() => ring[1]).ParamName);
    }

    [Fact]
    public void MutationInvalidatesActiveEnumerator()
    {
        var ring = new PredictionTelemetryRing<int>(2);
        ring.Add(1);
        var enumerator = ring.GetEnumerator();
        Assert.True(enumerator.MoveNext());

        ring.Add(2);

        Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
    }

    [Fact]
    public void NonGenericCurrentThrowsOutsideValidEnumerationPosition()
    {
        var ring = new PredictionTelemetryRing<int>(2);
        ring.Add(42);
        var enumerator = ((IEnumerable)ring).GetEnumerator();

        Assert.Throws<InvalidOperationException>(() => enumerator.Current);
        Assert.True(enumerator.MoveNext());
        Assert.Equal(42, enumerator.Current);
        Assert.False(enumerator.MoveNext());
        Assert.Throws<InvalidOperationException>(() => enumerator.Current);
    }

    [Fact]
    public void ReferenceIdentityAndOrderSurviveWrapAndClear()
    {
        var first = new Sample("first");
        var second = new Sample("second");
        var third = new Sample("third");
        var afterClear = new Sample("after-clear");
        var ring = new PredictionTelemetryRing<Sample>(2);

        ring.Add(first);
        ring.Add(second);
        ring.Add(third);

        Assert.Same(second, ring[0]);
        Assert.Same(third, ring[1]);

        ring.Clear();
        ring.Add(afterClear);

        Assert.Single(ring);
        Assert.Same(afterClear, ring[0]);
    }

    [Fact]
    public void RepeatedAddsNeverGrowOrAllocate()
    {
        var ring = new PredictionTelemetryRing<int>(8);
        for (var value = 0; value < 32; value++)
        {
            ring.Add(value);
        }

        var bytesBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var value = 0; value < 100_000; value++)
        {
            ring.Add(value);
        }

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - bytesBefore;

        Assert.Equal(0, allocatedBytes);
        Assert.Equal(8, ring.Capacity);
        Assert.Equal(8, ring.Count);
        Assert.Equal(99_999, ring[^1]);
    }

    [Fact]
    public void ConcreteEnumerationAndSpanCopyDoNotAllocate()
    {
        var ring = new PredictionTelemetryRing<int>(8);
        for (var value = 0; value < ring.Capacity; value++)
        {
            ring.Add(value);
        }

        var destination = new int[ring.Capacity];
        _ = SumConcrete(ring);
        _ = ring.CopyTo(destination);

        var bytesBefore = GC.GetAllocatedBytesForCurrentThread();
        var sum = 0;
        for (var iteration = 0; iteration < 10_000; iteration++)
        {
            sum += SumConcrete(ring);
            ring.CopyTo(destination);
        }

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - bytesBefore;

        Assert.Equal(280_000, sum);
        Assert.Equal(0, allocatedBytes);
        Assert.Equal(7, destination[^1]);
    }

    private static int SumConcrete(PredictionTelemetryRing<int> ring)
    {
        var sum = 0;
        foreach (var value in ring)
        {
            sum += value;
        }

        return sum;
    }

    private sealed record Sample(string Name);
}
