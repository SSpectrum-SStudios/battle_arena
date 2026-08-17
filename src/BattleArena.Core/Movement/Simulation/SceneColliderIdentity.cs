using System.Text;

namespace BattleArena.Core.Movement.Simulation;

/// <summary>
/// Derives a <see cref="SupportIdentity"/> that means the same thing in every
/// process.
/// </summary>
/// <remarks>
/// <para>
/// <b>The problem this exists for.</b> The Godot adapter identifies colliders by
/// <c>RID.Id</c>, a physics-server allocation handle. P01-09 and P5B-01 both
/// verified it "stable", but both only re-queried the same collider inside a
/// single process, which is all a RID guarantees. Authority and owner are two
/// processes — that is the premise of P06-11 and P06-12 — and
/// <c>Kinematic.Support</c> is a discrete comparison field checked before numeric
/// ones. So without this, every grounded frame reports a discrete mismatch and the
/// owner is corrected continuously, which no tolerance can hide.
/// </para>
/// <para>
/// <b>Why the body's path and not the shape's.</b> Colliders in this project are
/// created in code: <c>MovementTestCourse</c> adds shapes as
/// <c>body.AddChild(new CollisionShape3D { ... })</c> without setting a name, so
/// Godot assigns <c>@CollisionShape3D@&lt;global counter&gt;</c> — instantiation
/// order, which is precisely the process-locality being escaped. The bodies do get
/// stable names. A walker's natural <c>shapeNode.GetPath()</c> would pass every
/// single-process test and fail across processes, which is exactly how this defect
/// class survived four phases.
/// </para>
/// <para>
/// Lives in Core, with no engine reference, so the derivation is testable against
/// exact expected values and so the authority can derive the same identity whether
/// or not it is running a Godot scene tree.
/// </para>
/// </remarks>
public static class SceneColliderIdentity
{
    /// <summary>
    /// Bumped when the derivation changes.
    /// </summary>
    /// <remarks>
    /// Two endpoints on different derivations must not compare supports at all
    /// rather than compare them and disagree. A silent derivation change would
    /// present as a permanent divergence on every contact, which is the most
    /// expensive kind of defect to attribute — the same reasoning behind
    /// <c>CanonicalMovementStateHash.SchemaVersion</c>.
    /// </remarks>
    public const uint DerivationVersion = 1;

    /// <summary>
    /// Builds an identity from a body's authored scene path and a shape ordinal.
    /// </summary>
    /// <param name="bodyScenePath">
    /// The owning body's authored node path. Must be stable content, not a
    /// runtime-generated name.
    /// </param>
    /// <param name="shapeOrdinal">
    /// The shape's index within that body, carried separately rather than folded
    /// into the hash so <c>ExcludedCollider</c> keeps excluding a whole body — which
    /// is what the motor and <c>DeterministicCollisionWorld.Body.Matches</c> both
    /// assume.
    /// </param>
    /// <exception cref="ArgumentException">The path is null, empty, or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The ordinal is negative.</exception>
    public static SupportIdentity FromBodyPath(string bodyScenePath, int shapeOrdinal)
    {
        if (string.IsNullOrWhiteSpace(bodyScenePath))
        {
            throw new ArgumentException(
                "A collider identity needs the owning body's authored scene path.",
                nameof(bodyScenePath));
        }

        if (shapeOrdinal < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(shapeOrdinal));
        }

        return new SupportIdentity(HashPath(bodyScenePath), shapeOrdinal);
    }

    /// <summary>
    /// FNV-1a over the path's UTF-8 bytes, with zero remapped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hand-written rather than <c>string.GetHashCode</c> because .NET randomises
    /// string hashing per process by default — which would produce a
    /// process-local identity while looking like a stable one, reintroducing the
    /// exact bug this class removes and passing every single-process test while
    /// doing it.
    /// </para>
    /// <para>
    /// UTF-8 bytes rather than chars so the value does not depend on endianness or
    /// on how the runtime happens to store strings.
    /// </para>
    /// </remarks>
    private static ulong HashPath(string path)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        var hash = offsetBasis;
        var byteCount = Encoding.UTF8.GetByteCount(path);
        Span<byte> bytes = byteCount <= 256 ? stackalloc byte[byteCount] : new byte[byteCount];
        Encoding.UTF8.GetBytes(path, bytes);

        foreach (var value in bytes)
        {
            hash ^= value;
            hash *= prime;
        }

        // Zero is SupportIdentity.None, so a real collider must never produce it.
        // Remapping is safe: the alternative is a valid support that reports itself
        // as "no support", which would read as a character standing on nothing.
        return hash == 0UL ? offsetBasis : hash;
    }
}
