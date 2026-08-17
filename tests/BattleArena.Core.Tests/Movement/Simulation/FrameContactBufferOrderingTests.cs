using BattleArena.Core.Movement.Simulation;

namespace BattleArena.Core.Tests.Movement.Simulation;

/// <summary>
/// P06-A2: contact comparison and hashing must not depend on insertion order.
/// </summary>
/// <remarks>
/// <see cref="FrameContactBuffer.Equals(FrameContactBuffer)"/> is a positional walk
/// and <c>GetHashCode</c> folds in order, and the record structs containing a buffer
/// inherit that. Order is the motor's insertion order, which is reproducible for one
/// motor — but a canonical hash compared between two endpoints, and a comparer
/// deciding whether two frames describe the same surfaces, must be order-independent
/// or an ordering difference reads as a divergence and corrects the player for
/// nothing.
/// </remarks>
public sealed class FrameContactBufferOrderingTests
{
    private static readonly FrameContactRecord Floor = new(
        new SupportIdentity(10, 0), SurfaceNormal.Up, ContactSurfaceKind.WalkableGround);

    private static readonly FrameContactRecord Wall = new(
        new SupportIdentity(20, 0), new SurfaceNormal(-1d, 0d, 0d), ContactSurfaceKind.Wall);

    private static readonly FrameContactRecord SecondShape = new(
        new SupportIdentity(10, 1), SurfaceNormal.Up, ContactSurfaceKind.WalkableGround);

    [Fact]
    public void BuffersWithTheSameContactsInDifferentOrdersDescribeTheSameSurfaces()
    {
        var forward = Buffer(Floor, Wall, SecondShape);
        var reversed = Buffer(SecondShape, Wall, Floor);

        Assert.True(forward.DescribesSameContacts(reversed));
        Assert.True(reversed.DescribesSameContacts(forward));

        // And positional equality genuinely does not hold, so the test is
        // exercising the difference rather than passing for free.
        Assert.NotEqual(forward, reversed);
    }

    [Fact]
    public void ACanonicalCopyIsTheSameSequenceWhateverTheInsertionOrder()
    {
        Span<FrameContactRecord> first = stackalloc FrameContactRecord[4];
        Span<FrameContactRecord> second = stackalloc FrameContactRecord[4];

        var forwardCount = Buffer(Floor, Wall, SecondShape).CopyCanonical(first);
        var reversedCount = Buffer(SecondShape, Wall, Floor).CopyCanonical(second);

        Assert.Equal(forwardCount, reversedCount);
        for (var index = 0; index < forwardCount; index++)
        {
            Assert.Equal(first[index], second[index]);
        }
    }

    [Fact]
    public void DifferentContactsAreStillDistinguished()
    {
        // The ordering fix must not make everything look equal.
        Assert.False(Buffer(Floor, Wall).DescribesSameContacts(Buffer(Floor, SecondShape)));
        Assert.False(Buffer(Floor).DescribesSameContacts(Buffer(Floor, Wall)));
        Assert.False(Buffer(Floor, Wall).DescribesSameContacts(default));
    }

    [Fact]
    public void TheSameColliderOnDifferentShapesIsNotConflated()
    {
        // Floor and SecondShape share a collider and differ only by shape ordinal,
        // which is exactly the case a collider-only key would lose.
        Assert.False(Buffer(Floor).DescribesSameContacts(Buffer(SecondShape)));
    }

    [Fact]
    public void AnEmptyBufferMatchesOnlyAnotherEmptyOne()
    {
        Assert.True(default(FrameContactBuffer).DescribesSameContacts(default));
        Assert.False(default(FrameContactBuffer).DescribesSameContacts(Buffer(Floor)));
    }

    [Fact]
    public void ACanonicalCopyRefusesADestinationTooSmallRatherThanTruncating()
    {
        // Truncation would change the hash on one endpoint only, which is the
        // divergence this exists to prevent.
        var buffer = Buffer(Floor, Wall, SecondShape);
        Assert.Throws<ArgumentException>(() =>
        {
            Span<FrameContactRecord> tooSmall = stackalloc FrameContactRecord[2];
            buffer.CopyCanonical(tooSmall);
        });
    }

    [Fact]
    public void OrderingIsATotalOrderOverContent()
    {
        Assert.True(FrameContactRecord.CompareByContent(Floor, Wall) < 0);
        Assert.True(FrameContactRecord.CompareByContent(Wall, Floor) > 0);
        Assert.Equal(0, FrameContactRecord.CompareByContent(Floor, Floor));
        Assert.True(FrameContactRecord.CompareByContent(Floor, SecondShape) < 0);
    }

    private static FrameContactBuffer Buffer(params FrameContactRecord[] contacts)
    {
        var buffer = default(FrameContactBuffer);
        foreach (var contact in contacts)
        {
            Assert.True(buffer.TryAdd(contact));
        }

        return buffer;
    }
}
