using System.Runtime.CompilerServices;
using BattleArena.Core.Common;

namespace BattleArena.Core.Movement.Simulation;

/// <summary>
/// The gameplay-relevant facts about one contact a frame resolved against,
/// retained in the rewind unit.
/// </summary>
/// <remarks>
/// <para>
/// Narrower than <see cref="CollisionContactState"/> on purpose. The full
/// contact carries a travel fraction and a contact point, which are artefacts of
/// how one sweep iteration resolved; what survives into state is what later
/// frames and the reconciliation comparer actually reason about — which surface,
/// what kind, and which way it faced.
/// </para>
/// <para>
/// Contacts are derivable from position, profile, and the world, so storing them
/// is not what makes replay correct. It is what makes a correction
/// <em>explainable</em>: the comparer can say the two simulations disagreed about
/// standing on collider 12 rather than only that they disagreed about position,
/// and the policy can classify that as contact divergence rather than numeric
/// drift.
/// </para>
/// </remarks>
public readonly record struct FrameContactRecord
{
    public FrameContactRecord(
        SupportIdentity collider,
        SurfaceNormal normal,
        ContactSurfaceKind surfaceKind)
    {
        if (!normal.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(normal));
        }
        if (!Enum.IsDefined(surfaceKind))
        {
            throw new ArgumentOutOfRangeException(nameof(surfaceKind));
        }

        Collider = collider;
        Normal = normal;
        SurfaceKind = surfaceKind;
    }

    public SupportIdentity Collider { get; }
    public SurfaceNormal Normal { get; }
    public ContactSurfaceKind SurfaceKind { get; }

    public bool IsValid => Normal.IsValid && Enum.IsDefined(SurfaceKind);

    public static FrameContactRecord From(in CollisionContactState contact) => new(
        contact.Collider,
        contact.Normal,
        contact.SurfaceKind);
}

/// <summary>
/// The contacts one frame resolved against, in stable order.
/// </summary>
/// <remarks>
/// <para>
/// Fixed capacity and inline storage, because this is copied as part of the
/// rewind unit every frame. Four is the design's stated bound for
/// gameplay-relevant contact facts and covers the realistic worst case — a floor
/// and three walls in a corner.
/// </para>
/// <para>
/// Order is the motor's stable contact order rather than the world's report
/// order. That was originally taken to mean two endpoints resolving the same
/// frame store the same sequence, so a comparer could compare positionally.
/// <b>P5B-01 disproved that in-engine and it must not be relied on.</b> The Godot
/// adapter gives every contact of a sweep the same travel fraction, so
/// <see cref="CollisionContactState.CompareForStableResolution"/>'s primary key is
/// constant and the real ordering falls through to a physics-server RID — which
/// is allocation order, and differs between processes.
/// </para>
/// <para>
/// Two consequences for anything comparing frames. First, contacts must be
/// compared as a <em>set</em> keyed by content, never positionally. Second,
/// <see cref="Equals(FrameContactBuffer)"/> and <see cref="GetHashCode"/> below
/// <em>are</em> positional, and so is the synthesized equality of any record
/// struct containing one — including <see cref="CharacterSimulationState"/>. Any
/// canonical hash over this buffer must therefore sort before hashing, or two
/// endpoints that agree about a corner will report different hashes and the
/// difference will be misread as a real divergence.
/// </para>
/// </remarks>
public struct FrameContactBuffer : IEquatable<FrameContactBuffer>
{
    public const int Capacity = 4;

    private Storage _contacts;
    private int _count;

    [InlineArray(Capacity)]
    private struct Storage
    {
        private FrameContactRecord _element0;
    }

    public int Count => _count;
    public bool IsFull => _count >= Capacity;

    public FrameContactRecord this[int index] => (uint)index < (uint)_count
        ? _contacts[index]
        : throw new ArgumentOutOfRangeException(nameof(index));

    /// <summary>
    /// Appends a contact, in the order the motor resolved it.
    /// </summary>
    /// <returns>
    /// False when full. Dropping the overflow rather than replacing keeps the
    /// retained set the <em>earliest</em> contacts in stable order, which is the
    /// set that actually shaped the frame; replacing would keep whichever
    /// happened to come last.
    /// </returns>
    public bool TryAdd(in FrameContactRecord contact)
    {
        if (!contact.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(contact));
        }
        if (IsFull)
        {
            return false;
        }

        _contacts[_count++] = contact;
        return true;
    }

    public void Clear()
    {
        for (var index = 0; index < _count; index++)
        {
            _contacts[index] = default;
        }

        _count = 0;
    }

    /// <summary>
    /// P06-A2: whether two buffers hold the same contacts, ignoring insertion
    /// order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Equals(FrameContactBuffer)"/> is a positional walk and
    /// <see cref="GetHashCode"/> folds in order, so this cannot be expressed by
    /// equality — and the record structs that contain this buffer inherit that
    /// positional equality too.
    /// </para>
    /// <para>
    /// Needed because contact order is not a reliable cross-endpoint fact. It will
    /// become one once <see cref="StaticCollisionWorld"/> supplies genuine
    /// per-contact travel fractions and visits geometry in identity order, but
    /// comparison must not depend on that having landed: an ordering difference is
    /// not a simulation disagreement, and reporting one as a divergence would
    /// correct the player for nothing.
    /// </para>
    /// <para>
    /// A nested scan rather than a set, because <see cref="Capacity"/> is 4 and this
    /// runs on an allocation-free path where a HashSet would allocate per compared
    /// frame.
    /// </para>
    /// </remarks>
    public bool DescribesSameContacts(in FrameContactBuffer other) =>
        throw new NotImplementedException();

    /// <summary>
    /// P06-A2: contacts in a canonical order that is a pure function of content.
    /// </summary>
    /// <remarks>
    /// For hashing. <c>CanonicalMovementStateHash</c> must fold contacts in an
    /// order both endpoints agree on, or two endpoints that agree about a corner
    /// report different hashes — which the comparer classifies as
    /// <c>DiagnosticHashOnly</c>, documented as "a bug to investigate". P06-12
    /// would spend its first week investigating a non-bug on every corner frame.
    /// </remarks>
    /// <returns>How many were written.</returns>
    public int CopyCanonical(Span<FrameContactRecord> destination) =>
        throw new NotImplementedException();

    /// <summary>Whether any retained contact is on the given collider.</summary>
    public bool Touches(SupportIdentity collider)
    {
        for (var index = 0; index < _count; index++)
        {
            if (_contacts[index].Collider == collider)
            {
                return true;
            }
        }

        return false;
    }

    public bool Equals(FrameContactBuffer other)
    {
        if (_count != other._count)
        {
            return false;
        }

        for (var index = 0; index < _count; index++)
        {
            if (!_contacts[index].Equals(other._contacts[index]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is FrameContactBuffer other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_count);
        for (var index = 0; index < _count; index++)
        {
            hash.Add(_contacts[index]);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(FrameContactBuffer left, FrameContactBuffer right) =>
        left.Equals(right);

    public static bool operator !=(FrameContactBuffer left, FrameContactBuffer right) =>
        !left.Equals(right);
}
