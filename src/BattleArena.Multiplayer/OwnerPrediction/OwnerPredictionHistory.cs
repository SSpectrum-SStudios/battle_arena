using System.Runtime.CompilerServices;
using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Core.Movement.Simulation;

namespace BattleArena.Multiplayer.OwnerPrediction;

/// <summary>
/// The durable transitions that applied on one frame, stored inline.
/// </summary>
/// <remarks>
/// <para>
/// This lives in history rather than being re-derived at replay time, and that
/// is the whole point. Authority may remap a transition to a later frame than
/// the owner predicted, so "which transitions applied on frame F" is a fact that
/// changes as resolutions arrive. Re-deriving it during replay would reproduce a
/// different frame than the one originally simulated and then report the
/// difference as a divergence the owner caused.
/// </para>
/// <para>
/// Capacity matches <see cref="TransitionReferenceBuffer"/> so a frame can never
/// carry more transitions than a command could reference.
/// </para>
/// </remarks>
// Stub-only: the inline storage is declared for the shape review but not yet
// read or written, because every member still throws. Both suppressions come off
// with the implementation.
public struct AppliedTransitionBuffer : IEquatable<AppliedTransitionBuffer>
{
    public const int Capacity = 16;

    private Storage _tags;
    private int _count;

    [InlineArray(Capacity)]
    private struct Storage
    {
        private MovementTransitionKindTag _element0;
    }

    public int Count => _count;
    public bool IsFull => _count >= Capacity;

    public MovementTransitionKindTag this[int index] => (uint)index < (uint)_count
        ? _tags[index]
        : throw new ArgumentOutOfRangeException(nameof(index));

    /// <returns>
    /// False when full. A frame cannot legitimately apply more transitions than a
    /// command could reference, so a refusal here means a caller bug rather than a
    /// busy frame, and dropping silently would make replay apply a different edge
    /// set than the first run.
    /// </returns>
    public bool TryAdd(MovementTransitionKindTag tag)
    {
        if (!Enum.IsDefined(tag))
        {
            throw new ArgumentOutOfRangeException(nameof(tag));
        }

        if (IsFull)
        {
            return false;
        }

        _tags[_count++] = tag;
        return true;
    }

    public int CopyTo(Span<MovementTransitionKindTag> destination)
    {
        if (destination.Length < _count)
        {
            throw new ArgumentException(
                $"The destination needs room for {_count} transitions.",
                nameof(destination));
        }

        for (var index = 0; index < _count; index++)
        {
            destination[index] = _tags[index];
        }

        return _count;
    }

    public void Clear()
    {
        for (var index = 0; index < _count; index++)
        {
            _tags[index] = default;
        }

        _count = 0;
    }

    /// <remarks>
    /// Ordered, unlike contacts. Transition order is the order the owner applied
    /// them, it is reproduced exactly by replay feeding the same command, and the
    /// order itself is meaningful — a jump press before a crouch press is a
    /// different frame from the reverse.
    /// </remarks>
    public bool Equals(AppliedTransitionBuffer other)
    {
        if (_count != other._count)
        {
            return false;
        }

        for (var index = 0; index < _count; index++)
        {
            if (_tags[index] != other._tags[index])
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) =>
        obj is AppliedTransitionBuffer other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_count);
        for (var index = 0; index < _count; index++)
        {
            hash.Add((int)_tags[index]);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(AppliedTransitionBuffer l, AppliedTransitionBuffer r) =>
        l.Equals(r);

    public static bool operator !=(AppliedTransitionBuffer l, AppliedTransitionBuffer r) =>
        !l.Equals(r);
}

/// <summary>
/// A deterministic simulation event a frame produced, identified stably enough
/// to be recognised again on replay.
/// </summary>
/// <remarks>
/// Cues — a jump, a landing, a roll start — must fire once even though their
/// frame may be simulated many times. <see cref="PredictedCueLedger"/> already
/// solves that deduplication; it needs the per-frame identities, and this is
/// where they are retained so a replayed frame offers the ledger the same ones
/// the first run did.
/// </remarks>
public readonly record struct OwnerSimulationEvent
{
    /// <summary>
    /// Carries every field <see cref="PredictedCueIdentity"/> needs except the
    /// epoch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The stub review caught this holding only an id and a kind, which is two of
    /// the five fields the ledger key requires — and the ledger throws on an
    /// unspecified origin or a zero id. Replay would have had to invent the
    /// missing two, which is the re-derivation this file's opening remark argues
    /// against, and a derivation that is not bit-identical makes the ledger see a
    /// new identity and fire the sound again. That is the ten-sounds-on-one-jump
    /// bug the design was built to prevent.
    /// </para>
    /// <para>
    /// The epoch is deliberately absent: history is bound to one epoch for its
    /// whole life, so storing it per event would be a field that can only ever hold
    /// one value and could drift from the container's.
    /// </para>
    /// </remarks>
    public OwnerSimulationEvent(
        PredictedCueKind kind,
        PredictedCueOriginKind originKind,
        ulong originId,
        uint eventOrdinal)
    {
        if (kind == PredictedCueKind.Unspecified || !Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (originKind == PredictedCueOriginKind.Unspecified || !Enum.IsDefined(originKind))
        {
            throw new ArgumentOutOfRangeException(nameof(originKind));
        }

        if (originId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(originId));
        }

        Kind = kind;
        OriginKind = originKind;
        OriginId = originId;
        EventOrdinal = eventOrdinal;
    }

    public PredictedCueKind Kind { get; }
    public PredictedCueOriginKind OriginKind { get; }
    public ulong OriginId { get; }
    public uint EventOrdinal { get; }

    public bool IsValid =>
        Kind != PredictedCueKind.Unspecified &&
        OriginKind != PredictedCueOriginKind.Unspecified &&
        OriginId != 0;

    /// <summary>
    /// Rebuilds the ledger key by supplying the epoch history is bound to.
    /// </summary>
    /// <remarks>
    /// The whole point of storing the wider record: replay offers the ledger
    /// exactly the identity the first run did, so a re-simulated frame's cue is
    /// recognised as already emitted rather than as a new one.
    /// </remarks>
    public PredictedCueIdentity ToIdentity(CombatantAuthorityPredictionEpoch epoch) =>
        new(epoch, Kind, OriginKind, OriginId, EventOrdinal);
}

/// <summary>Events produced by one frame. Bounded; a frame cannot emit unboundedly.</summary>
public struct OwnerSimulationEventBuffer : IEquatable<OwnerSimulationEventBuffer>
{
    public const int Capacity = 8;

    private Storage _events;
    private int _count;

    [InlineArray(Capacity)]
    private struct Storage
    {
        private OwnerSimulationEvent _element0;
    }

    public int Count => _count;
    public bool IsFull => _count >= Capacity;

    public OwnerSimulationEvent this[int index] => (uint)index < (uint)_count
        ? _events[index]
        : throw new ArgumentOutOfRangeException(nameof(index));

    public bool TryAdd(in OwnerSimulationEvent simulationEvent)
    {
        if (!simulationEvent.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(simulationEvent));
        }

        if (IsFull)
        {
            return false;
        }

        _events[_count++] = simulationEvent;
        return true;
    }

    public void Clear()
    {
        for (var index = 0; index < _count; index++)
        {
            _events[index] = default;
        }

        _count = 0;
    }

    public bool Equals(OwnerSimulationEventBuffer other)
    {
        if (_count != other._count)
        {
            return false;
        }

        for (var index = 0; index < _count; index++)
        {
            if (!_events[index].Equals(other._events[index]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) =>
        obj is OwnerSimulationEventBuffer other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_count);
        for (var index = 0; index < _count; index++)
        {
            hash.Add(_events[index]);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(OwnerSimulationEventBuffer l, OwnerSimulationEventBuffer r) =>
        l.Equals(r);

    public static bool operator !=(OwnerSimulationEventBuffer l, OwnerSimulationEventBuffer r) =>
        !l.Equals(r);
}

/// <summary>
/// Everything the owner retained about one frame it simulated locally.
/// </summary>
/// <remarks>
/// <para>
/// Both the pre-state and the post-state are stored. Replay restores the
/// <em>pre</em>-state of the first mismatched frame and resimulates forward;
/// comparison happens against the <em>post</em>-state, because that is what
/// authority's answer describes. Storing only one would force every replay to
/// start a frame earlier than necessary, or make comparison impossible.
/// </para>
/// <para>
/// Command, applied transitions, and events are all retained because replay must
/// feed the frame exactly what the first run consumed and must offer the cue
/// ledger exactly what it offered before. Anything re-derived from current state
/// instead would reproduce a different frame and then report the difference as a
/// divergence.
/// </para>
/// </remarks>
public readonly record struct OwnerPredictedFrame
{
    public OwnerPredictedFrame(
        SimulationInstant frame,
        CharacterSimulationState preState,
        CharacterSimulationState postState,
        OwnerSimulationCommand command,
        AppliedTransitionBuffer appliedTransitions,
        OwnerSimulationEventBuffer events,
        CapsuleMotionOutcome motionOutcome,
        int slideIterations,
        bool profileExpansionBlocked,
        CanonicalMovementStateHash canonicalHash)
    {
        // Four frame numbers arrive here and they must describe one frame. The stub
        // review flagged their coherence as unstated, and it is the guard whose
        // absence produces the failure the plan calls the worst available: a
        // one-frame phase error anywhere in this path reads as a divergence on
        // EVERY frame and corrects the player continuously, which is
        // indistinguishable from a tolerance set too tight. P5B-02 measured the two
        // motors exactly one frame out of phase, so this is a live hazard rather
        // than a theoretical one.
        if (postState.Frame != frame)
        {
            throw new ArgumentException(
                $"The post-state is for frame {postState.Frame.Tick}, not {frame.Tick}.",
                nameof(postState));
        }

        if (preState.Frame.Tick != frame.Tick - 1)
        {
            throw new ArgumentException(
                $"The pre-state is for frame {preState.Frame.Tick}, which does not immediately " +
                $"precede {frame.Tick}.",
                nameof(preState));
        }

        if (command.TargetFrame != frame)
        {
            throw new ArgumentException(
                $"The command targets frame {command.TargetFrame.Tick}, not {frame.Tick}.",
                nameof(command));
        }

        if (!Enum.IsDefined(motionOutcome))
        {
            throw new ArgumentOutOfRangeException(nameof(motionOutcome));
        }

        if (slideIterations < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(slideIterations));
        }

        Frame = frame;
        PreState = preState;
        PostState = postState;
        Command = command;
        AppliedTransitions = appliedTransitions;
        Events = events;
        MotionOutcome = motionOutcome;
        SlideIterations = slideIterations;
        ProfileExpansionBlocked = profileExpansionBlocked;
        CanonicalHash = canonicalHash;
    }

    public SimulationInstant Frame { get; }

    /// <summary>State this frame started from; the restore point for replay.</summary>
    public CharacterSimulationState PreState { get; }

    /// <summary>State this frame produced; what authority's answer is compared against.</summary>
    public CharacterSimulationState PostState { get; }

    /// <summary>The exact input this frame consumed, so replay feeds it the same one.</summary>
    public OwnerSimulationCommand Command { get; }

    /// <summary>The durable transitions that applied, so a remap cannot silently move an edge.</summary>
    public AppliedTransitionBuffer AppliedTransitions { get; }

    /// <summary>Cue identities this frame produced, so replay does not re-fire them.</summary>
    public OwnerSimulationEventBuffer Events { get; }

    /// <summary>
    /// What the motor did. Retained so a correction can be explained — a
    /// divergence on a frame that hit the slide iteration cap or refused a step
    /// has a different cause than one on a frame of free travel.
    /// </summary>
    public CapsuleMotionOutcome MotionOutcome { get; }

    public int SlideIterations { get; }
    public bool ProfileExpansionBlocked { get; }

    /// <summary>
    /// Diagnostic hash of the post-state, with its schema version. Never a
    /// correction trigger; <see cref="OwnerReconciliationPolicy"/> is
    /// structurally unable to read it.
    /// </summary>
    public CanonicalMovementStateHash CanonicalHash { get; }

    public bool IsValid =>
        PostState.Frame == Frame &&
        PreState.Frame.Tick == Frame.Tick - 1 &&
        Command.TargetFrame == Frame;

    /// <summary>Replaces the post-state after replay resimulated this frame.</summary>
    /// <remarks>
    /// Required because after a correction the stored post-state is the owner's
    /// wrong answer. Leaving it would make a duplicate authority state for the
    /// same frame mismatch a second time and correct again.
    /// </remarks>
    public OwnerPredictedFrame WithPostState(
        CharacterSimulationState postState,
        CanonicalMovementStateHash hash) => new(
            Frame,
            PreState,
            postState,
            Command,
            AppliedTransitions,
            Events,
            MotionOutcome,
            SlideIterations,
            ProfileExpansionBlocked,
            hash);
}

public enum OwnerHistoryInsertDecision : byte
{
    Inserted = 1,

    /// <summary>Older than the oldest retained frame; the window has moved past it.</summary>
    OlderThanRetention = 2,

    /// <summary>Not contiguous with the newest retained frame. History must stay gapless.</summary>
    NonContiguous = 3,
}

public enum OwnerHistoryLookupDecision : byte
{
    Found = 1,

    /// <summary>Older than retention. The owner cannot replay it and must rebase.</summary>
    OlderThanRetention = 2,

    /// <summary>Newer than anything simulated. Authority is ahead; the frame is queued.</summary>
    NotYetSimulated = 3,

    /// <summary>Nothing retained at all, as after a reset.</summary>
    Empty = 4,
}

/// <summary>
/// The owner's bounded record of frames it simulated, kept so an authority
/// answer about a past frame can be checked and, if wrong, replayed from.
/// </summary>
/// <remarks>
/// <para>
/// Preallocated and frame-indexed: a fixed ring sized to the retention window,
/// addressed by frame number rather than searched. Retention is what bounds
/// memory and also what defines the failure mode — an authority answer older
/// than the window cannot be replayed from and must become a hard rebase, which
/// is a visible correction. Sizing the window is therefore a gameplay decision,
/// not only a memory one.
/// </para>
/// <para>
/// History is gapless by construction. Every frame the owner simulates is
/// inserted contiguously; a gap would mean replay silently skipped a frame and
/// produced a state no simulation ever generated.
/// </para>
/// <para>
/// The history is bound to one epoch for its whole life and is reset wholesale
/// when the epoch changes, rather than storing an epoch per frame and filtering.
/// Frame numbers may be reused across a timeline reset, so a per-frame check
/// would have to compare identities the owner command cannot fully reconstruct;
/// binding the container is both simpler and impossible to get subtly wrong.
/// </para>
/// </remarks>
public sealed class OwnerPredictionHistory
{
    /// <summary>
    /// Frames retained. At 60 Hz this is the round-trip window a correction can
    /// still be replayed from rather than rebased.
    /// </summary>
    public const int DefaultCapacityFrames = 256;
    public const int MaximumCapacityFrames = 1024;

    private readonly OwnerPredictedFrame[] _frames;
    private long _oldestTick;
    private long _newestTick;
    private int _count;

    public OwnerPredictionHistory(
        CombatantAuthorityPredictionEpoch epoch,
        int capacityFrames = DefaultCapacityFrames)
    {
        if (!epoch.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(epoch));
        }

        if (capacityFrames is < 1 or > MaximumCapacityFrames)
        {
            throw new ArgumentOutOfRangeException(nameof(capacityFrames));
        }

        Epoch = epoch;
        _frames = new OwnerPredictedFrame[capacityFrames];
    }

    public CombatantAuthorityPredictionEpoch Epoch { get; private set; }
    public int CapacityFrames => _frames.Length;
    public int Count => _count;

    /// <summary>Oldest frame still replayable, or null when empty.</summary>
    public SimulationInstant? OldestFrame =>
        _count == 0 ? null : new SimulationInstant(_oldestTick);

    /// <summary>Newest frame simulated, or null when empty.</summary>
    public SimulationInstant? NewestFrame =>
        _count == 0 ? null : new SimulationInstant(_newestTick);

    /// <summary>
    /// Records one simulated frame. Refuses rather than accepts a gap, because a
    /// gap in history is indistinguishable later from a frame that simulated
    /// differently.
    /// </summary>
    public OwnerHistoryInsertDecision TryInsert(in OwnerPredictedFrame frame)
    {
        if (!frame.IsValid)
        {
            throw new ArgumentException(
                "A retained frame must be internally coherent about which frame it is.",
                nameof(frame));
        }

        var tick = frame.Frame.Tick;
        if (_count == 0)
        {
            _frames[Index(tick)] = frame;
            _oldestTick = tick;
            _newestTick = tick;
            _count = 1;
            return OwnerHistoryInsertDecision.Inserted;
        }

        // Re-inserting the newest frame, or any retained frame, is not a gap — but
        // it is also not an insert. Callers replacing a replayed frame must go
        // through TryReplacePostState, which keeps the command and transitions the
        // first run consumed.
        if (tick <= _newestTick)
        {
            return tick < _oldestTick
                ? OwnerHistoryInsertDecision.OlderThanRetention
                : OwnerHistoryInsertDecision.NonContiguous;
        }

        if (tick != _newestTick + 1)
        {
            return OwnerHistoryInsertDecision.NonContiguous;
        }

        _frames[Index(tick)] = frame;
        _newestTick = tick;
        if (_count == _frames.Length)
        {
            // The window slides. The evicted frame can no longer be replayed from,
            // which is what turns a late authority answer into a hard rebase.
            _oldestTick++;
        }
        else
        {
            _count++;
        }

        return OwnerHistoryInsertDecision.Inserted;
    }

    public OwnerHistoryLookupDecision TryGet(
        SimulationInstant frame,
        out OwnerPredictedFrame predicted)
    {
        predicted = default;
        if (_count == 0)
        {
            return OwnerHistoryLookupDecision.Empty;
        }

        var tick = frame.Tick;
        if (tick > _newestTick)
        {
            return OwnerHistoryLookupDecision.NotYetSimulated;
        }

        if (tick < _oldestTick)
        {
            return OwnerHistoryLookupDecision.OlderThanRetention;
        }

        predicted = _frames[Index(tick)];
        return OwnerHistoryLookupDecision.Found;
    }

    /// <summary>
    /// Frame number to ring slot.
    /// </summary>
    /// <remarks>
    /// Frame-indexed rather than searched, so a lookup is constant time regardless
    /// of window size. The modulo is taken on a non-negative value because
    /// simulation ticks are non-negative; C# remainder of a negative would index
    /// backwards and silently return a different frame.
    /// </remarks>
    private int Index(long tick) => (int)((ulong)tick % (ulong)_frames.Length);

    /// <summary>
    /// Replaces a frame's post-state after replay resimulated it.
    /// </summary>
    /// <remarks>
    /// Without this the corrected frame keeps the owner's original wrong answer,
    /// and a duplicate authority state for that frame mismatches again and
    /// corrects again.
    /// </remarks>
    public bool TryReplacePostState(
        SimulationInstant frame,
        in CharacterSimulationState postState,
        CanonicalMovementStateHash hash)
    {
        if (TryGet(frame, out var existing) != OwnerHistoryLookupDecision.Found)
        {
            return false;
        }

        if (postState.Frame != frame)
        {
            throw new ArgumentException(
                $"A replayed post-state for frame {frame.Tick} carries frame " +
                $"{postState.Frame.Tick}.",
                nameof(postState));
        }

        _frames[Index(frame.Tick)] = existing.WithPostState(postState, hash);
        return true;
    }

    /// <summary>
    /// Drops every frame at or below <paramref name="throughFrame"/>.
    /// </summary>
    /// <remarks>
    /// Called when authority confirms a frame matched. Pruning on confirmation
    /// rather than only on overflow is what keeps the window available for the
    /// frames that might still need replaying.
    /// </remarks>
    public int PruneThrough(SimulationInstant throughFrame)
    {
        if (_count == 0 || throughFrame.Tick < _oldestTick)
        {
            return 0;
        }

        if (throughFrame.Tick >= _newestTick)
        {
            var all = _count;
            _count = 0;
            return all;
        }

        var dropped = (int)(throughFrame.Tick - _oldestTick + 1);
        _oldestTick = throughFrame.Tick + 1;
        _count -= dropped;
        return dropped;
    }

    /// <summary>
    /// Discards frames after <paramref name="fromFrame"/>.
    /// </summary>
    /// <remarks>
    /// <b>For the rebase path only — never for replay.</b> The frames after the
    /// restore point are the only place the commands and applied transitions live,
    /// so truncating them deletes exactly what replay is about to read. Replay
    /// rewrites in place through <see cref="TryReplacePostState"/> instead. An
    /// earlier version of the controller's documentation recommended truncating
    /// here, which the stub review caught as a data-loss bug the comment actively
    /// advocated.
    /// </remarks>
    public int TruncateAfter(SimulationInstant fromFrame)
    {
        if (_count == 0 || fromFrame.Tick >= _newestTick)
        {
            return 0;
        }

        if (fromFrame.Tick < _oldestTick)
        {
            var all = _count;
            _count = 0;
            return all;
        }

        var dropped = (int)(_newestTick - fromFrame.Tick);
        _newestTick = fromFrame.Tick;
        _count -= dropped;
        return dropped;
    }

    /// <summary>
    /// Clears everything and rebinds to a new epoch.
    /// </summary>
    /// <remarks>
    /// An epoch change invalidates every retained frame: frame numbers may be
    /// reused and the scope the commands belonged to is gone. Keeping any of it
    /// would let a frame from the old timeline be replayed into the new one.
    /// </remarks>
    public void Reset(CombatantAuthorityPredictionEpoch epoch)
    {
        if (!epoch.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(epoch));
        }

        // Slots are cleared rather than only the count, so a stale frame cannot be
        // resurrected by a later insert landing on the same ring slot before the
        // window has moved past it.
        Array.Clear(_frames);
        Epoch = epoch;
        _oldestTick = 0;
        _newestTick = 0;
        _count = 0;
    }
}
