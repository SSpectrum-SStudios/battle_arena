using BattleArena.Core.Movement.Simulation;

namespace BattleArena.Core.Tests.Movement.Simulation;

/// <summary>
/// The identity that makes cross-process contact comparison possible at all.
/// </summary>
/// <remarks>
/// The defect these guard against survived four phases because every earlier check
/// re-queried the same collider inside one process, which a Godot RID satisfies
/// perfectly. The property that actually matters is that two <em>separate</em>
/// processes derive the same value, so these assert against fixed expected numbers
/// rather than against another call in the same run.
/// </remarks>
public sealed class SceneColliderIdentityTests
{
    private const string BodyPath = "/root/MovementTestArena/Course/MainGround";

    [Fact]
    public void TheSamePathAndOrdinalAlwaysProduceTheSameIdentity()
    {
        var first = SceneColliderIdentity.FromBodyPath(BodyPath, 0);
        var second = SceneColliderIdentity.FromBodyPath(BodyPath, 0);

        Assert.Equal(first, second);
        Assert.True(first.IsValid);
    }

    [Fact]
    public void TheDerivationIsPinnedToAnExactValue()
    {
        // A fixed expectation, not a self-comparison. Two processes agreeing is the
        // whole point, and a test that only compares one run against itself would
        // pass with a per-process random seed baked in -- which is exactly what
        // string.GetHashCode would have given us, since .NET randomises string
        // hashing per process by default.
        var identity = SceneColliderIdentity.FromBodyPath(BodyPath, 3);

        Assert.Equal(Fnv1a(BodyPath), identity.ColliderId);
        Assert.Equal(3, identity.ShapeIndex);
    }

    [Fact]
    public void DifferentBodiesDoNotCollide()
    {
        var ground = SceneColliderIdentity.FromBodyPath("/root/Arena/MainGround", 0);
        var wall = SceneColliderIdentity.FromBodyPath("/root/Arena/Wall", 0);

        Assert.NotEqual(ground.ColliderId, wall.ColliderId);
    }

    [Fact]
    public void TheShapeOrdinalStaysOutOfTheColliderHash()
    {
        // ExcludedCollider excludes a whole body, and DeterministicCollisionWorld
        // matches on ColliderId alone. Folding the ordinal into the hash would
        // silently narrow that to a single shape.
        var first = SceneColliderIdentity.FromBodyPath(BodyPath, 0);
        var second = SceneColliderIdentity.FromBodyPath(BodyPath, 7);

        Assert.Equal(first.ColliderId, second.ColliderId);
        Assert.NotEqual(first.ShapeIndex, second.ShapeIndex);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnUnusablePathIsRefusedRatherThanHashed(string path) =>
        Assert.Throws<ArgumentException>(() => SceneColliderIdentity.FromBodyPath(path, 0));

    [Fact]
    public void ANegativeOrdinalIsRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SceneColliderIdentity.FromBodyPath(BodyPath, -1));

    [Fact]
    public void ARealColliderNeverPresentsItselfAsNoSupport()
    {
        // Zero means SupportIdentity.None. A real collider hashing to it would read
        // as a character standing on nothing.
        Assert.True(SceneColliderIdentity.FromBodyPath(BodyPath, 0).IsValid);
        Assert.True(SceneColliderIdentity.FromBodyPath("/", 0).IsValid);
    }

    /// <summary>Independent FNV-1a, so the test does not restate the implementation.</summary>
    private static ulong Fnv1a(string value)
    {
        var hash = 14695981039346656037UL;
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(value))
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }

        return hash;
    }
}
