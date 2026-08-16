using BattleArena.Core.Common;
using BattleArena.Core.Movement.Simulation;
using BattleArena.Multiplayer.OwnerPrediction;
using BattleArena.Multiplayer.Protocol;

namespace BattleArena.Multiplayer.AuthoritySimulation;

/// <summary>
/// Why an owner command was refused admission. These are ingress faults, not
/// gameplay outcomes: a refused command never becomes an input application and
/// never displaces an already-admitted command for the same frame.
/// </summary>
public enum AuthorityInputAdmissionFault : byte
{
    None = 0,

    /// <summary>Session, match-frame epoch, combatant, life, control, or authority discontinuity did not match.</summary>
    ForeignScope = 1,

    /// <summary>The target frame is beyond the bounded acceptance horizon. Bounds future-frame flooding.</summary>
    BeyondAcceptanceHorizon = 2,

    /// <summary>
    /// A different command already occupies the target frame. First admission
    /// wins; a later arrival can never substitute itself into an occupied frame.
    /// </summary>
    ConflictingCommandForFrame = 3,

    /// <summary>
    /// The command's input sequence is inconsistent with its target frame. The
    /// owner command builder advances sequence and target frame in lockstep
    /// within one owner-control scope, so the difference between them is a fixed
    /// offset. A violation means forged or corrupted identity, not packet loss.
    /// </summary>
    SequenceFrameSkew = 4,

    /// <summary>
    /// No scheduler exists for the addressed combatant: it never joined, already
    /// left, or is not under V2 scheduling. Distinct from
    /// <see cref="ForeignScope"/>, which means the combatant is scheduled but the
    /// command belongs to a different epoch — the two say different things about
    /// a peer and must stay separable on the telemetry surface.
    /// </summary>
    UnknownCombatant = 5,

    /// <summary>
    /// Admission was attempted while a frame run was in progress. Draining
    /// network evidence is a distinct pipeline step that happens before the fixed
    /// frame boundary; accepting mid-run would make whether a command reaches its
    /// target frame depend on the order combatants were simulated in.
    /// </summary>
    FrameRunInProgress = 6,
}

/// <summary>
/// The result of offering one owner command to the authority scheduler.
/// </summary>
public readonly record struct AuthorityInputAdmission
{
    private AuthorityInputAdmission(
        OwnerInputArrivalDisposition disposition,
        AuthorityInputAdmissionFault fault,
        AuthorityInputArrivalDecision? arrival)
    {
        Disposition = disposition;
        Fault = fault;
        Arrival = arrival;
    }

    public OwnerInputArrivalDisposition Disposition { get; }
    public AuthorityInputAdmissionFault Fault { get; }

    /// <summary>
    /// Present only when the command belongs to this scheduler's authenticated
    /// epoch. A foreign-scope command has no valid frame identity and therefore
    /// cannot be represented as an arrival decision at all.
    /// </summary>
    public AuthorityInputArrivalDecision? Arrival { get; }

    public bool WasStored => Disposition == OwnerInputArrivalDisposition.NewCommandAccepted;

    internal static AuthorityInputAdmission Accepted(AuthorityInputArrivalDecision arrival) =>
        new(OwnerInputArrivalDisposition.NewCommandAccepted, AuthorityInputAdmissionFault.None, arrival);

    internal static AuthorityInputAdmission Duplicate(AuthorityInputArrivalDecision arrival) =>
        new(OwnerInputArrivalDisposition.DuplicateCommand, AuthorityInputAdmissionFault.None, arrival);

    internal static AuthorityInputAdmission Late(AuthorityInputArrivalDecision arrival) =>
        new(OwnerInputArrivalDisposition.LateCommand, AuthorityInputAdmissionFault.None, arrival);

    internal static AuthorityInputAdmission Rejected(
        AuthorityInputAdmissionFault fault,
        AuthorityInputArrivalDecision? arrival) =>
        new(OwnerInputArrivalDisposition.RejectedCommand, fault, arrival);
}

/// <summary>Whether a consumed authority frame had an owner command available.</summary>
public enum AuthorityOwnerFrameAvailability : byte
{
    /// <summary>A validated command targeting exactly this frame was admitted before the deadline.</summary>
    CommandAvailable = 1,

    /// <summary>
    /// No command targeting this frame arrived in time. The frame is still
    /// consumed exactly once; the fallback policy supplies its input.
    /// </summary>
    CommandMissing = 2,
}

/// <summary>
/// One frame permanently leaving the scheduler. Every active authority frame
/// produces exactly one of these, in strict ascending order, with no gaps.
/// </summary>
public readonly record struct AuthorityOwnerFrameConsumption
{
    internal AuthorityOwnerFrameConsumption(
        AuthorityInputFrameIdentity identity,
        OwnerSimulationCommand? command)
    {
        Identity = identity;
        Command = command;
    }

    public AuthorityInputFrameIdentity Identity { get; }
    public OwnerSimulationCommand? Command { get; }
    public AuthorityOwnerFrameAvailability Availability => Command is null
        ? AuthorityOwnerFrameAvailability.CommandMissing
        : AuthorityOwnerFrameAvailability.CommandAvailable;
    public SimulationInstant Frame => Identity.Frame;
}

/// <summary>
/// The permanent record of how one authority frame was resolved. Once written it
/// never changes: a later arrival for that frame cannot reopen it.
/// </summary>
public readonly record struct AuthorityFrameTerminalDisposition
{
    internal AuthorityFrameTerminalDisposition(AuthorityInputFrameDecision decision)
    {
        Frame = decision.Identity.Frame;
        ApplicationKind = decision.ApplicationKind;
        AppliedInputSequence = decision.AppliedInputSequence;
        OverrideReason = decision.OverrideReason;
        ConsecutiveMissingFrames = decision.ConsecutiveMissingFrames;
    }

    public SimulationInstant Frame { get; }
    public AuthorityInputApplicationKind ApplicationKind { get; }

    /// <summary>Set only when a real owner command supplied this frame's input.</summary>
    public InputSequence? AppliedInputSequence { get; }
    public AuthorityInputOverrideReason? OverrideReason { get; }
    public uint ConsecutiveMissingFrames { get; }

    /// <summary>
    /// Whether the owner's own command drove this frame. The client needs this to
    /// distinguish "authority never heard me" from "authority heard me and
    /// disagreed", which are different corrections.
    /// </summary>
    public bool UsedOwnerCommand =>
        ApplicationKind == AuthorityInputApplicationKind.ReceivedCommand;
}

/// <summary>
/// Replaces <c>AuthorityMovementInputBuffer.ConsumeFreshest()</c> for one
/// combatant. Commands are stored by the exact match frame they target, never by
/// arrival order.
/// </summary>
/// <remarks>
/// <para>
/// The behaviour being removed is newest-command compaction: the legacy buffer
/// could skip intervening client ticks and integrate movement once, so logical
/// time advanced by several frames while displacement advanced by one. Here,
/// admission and consumption are separate. Admission places a command in the
/// cell its own <c>TargetFrame</c> names. Consumption always advances exactly one
/// frame and reports whether that cell was filled. A burst of four commands
/// therefore fills four cells and is consumed over four frames; it can never
/// collapse into a single integration step.
/// </para>
/// <para>
/// Storage is a preallocated ring of <see cref="CapacityFrames"/> cells with no
/// per-frame allocation. The ring is also the flood bound: a command targeting a
/// frame beyond the horizon is refused rather than growing a queue, so a
/// malicious peer cannot force unbounded memory or a permanent FIFO backlog.
/// </para>
/// <para>
/// This step owns admission, storage, and ordered consumption only. Choosing the
/// applied input for a missing frame (repeated-continuous versus neutral) and
/// publishing terminal dispositions belong to P04-03; durable transition and
/// action journal resolution belongs to P04-04.
/// </para>
/// </remarks>
public sealed class AuthorityOwnerInputScheduler
{
    public const int MinimumCapacityFrames = 2;
    public const int MaximumCapacityFrames = 512;
    public const int DefaultRetainedDispositionFrames = 128;
    public const int MaximumRetainedDispositionFrames = 1024;

    private readonly CombatantAuthorityPredictionEpoch _authorityEpoch;
    private readonly OwnerIntentScope _expectedScope;
    private readonly OwnerSimulationCommand[] _commands;
    private readonly long[] _occupiedFrameTicks;
    private readonly AuthorityFrameTerminalDisposition[] _dispositions;
    private readonly long[] _dispositionFrameTicks;
    private readonly AuthorityInputFallbackPolicy _fallbackPolicy;
    private readonly long _firstFrameTick;
    private readonly long _lastAcceptableFrameTick;

    private long _nextFrameToConsumeTick;
    private int _storedCommandCount;
    private bool _hasSequenceAnchor;
    private long _anchorFrameTick;
    private ulong _anchorSequence;
    private AuthorityInputFrameDecision? _previousDecision;
    private long _decisionsResolvedThroughTick;
    private long _lateCommandCount;
    private long _rejectedCommandCount;
    private long _duplicateCommandCount;
    private ulong _highestContiguousReceivedSequence;
    private ulong _followingReceivedSequenceMask;
    private bool _hasReceivedSequenceBase;
    private ulong _highestAdmittedSequence;
    private ulong _consumedThroughSequence;

    public AuthorityOwnerInputScheduler(
        CombatantAuthorityPredictionEpoch authorityEpoch,
        SimulationInstant firstFrameToConsume,
        int capacityFrames)
        : this(
            authorityEpoch,
            firstFrameToConsume,
            capacityFrames,
            new AuthorityInputFallbackPolicy(
                AuthorityInputFallbackPolicy.DefaultMaximumRepeatedContinuousFrames),
            DefaultRetainedDispositionFrames)
    {
    }

    public AuthorityOwnerInputScheduler(
        CombatantAuthorityPredictionEpoch authorityEpoch,
        SimulationInstant firstFrameToConsume,
        int capacityFrames,
        AuthorityInputFallbackPolicy fallbackPolicy,
        int retainedDispositionFrames)
    {
        if (!authorityEpoch.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(authorityEpoch));
        }
        if (firstFrameToConsume.Tick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(firstFrameToConsume));
        }
        if (capacityFrames is < MinimumCapacityFrames or > MaximumCapacityFrames)
        {
            throw new ArgumentOutOfRangeException(nameof(capacityFrames));
        }
        if (!fallbackPolicy.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(fallbackPolicy));
        }
        if (retainedDispositionFrames is < 1 or > MaximumRetainedDispositionFrames)
        {
            throw new ArgumentOutOfRangeException(nameof(retainedDispositionFrames));
        }
        if (long.MaxValue - firstFrameToConsume.Tick < capacityFrames)
        {
            throw new ArgumentOutOfRangeException(
                nameof(firstFrameToConsume),
                "The acceptance horizon for this capacity would overflow the match timeline.");
        }

        _authorityEpoch = authorityEpoch;
        _expectedScope = OwnerIntentScope.From(authorityEpoch);
        _commands = new OwnerSimulationCommand[capacityFrames];
        _occupiedFrameTicks = new long[capacityFrames];
        Array.Fill(_occupiedFrameTicks, EmptyCell);
        _dispositions = new AuthorityFrameTerminalDisposition[retainedDispositionFrames];
        _dispositionFrameTicks = new long[retainedDispositionFrames];
        Array.Fill(_dispositionFrameTicks, EmptyCell);
        _fallbackPolicy = fallbackPolicy;
        _firstFrameTick = firstFrameToConsume.Tick;
        _nextFrameToConsumeTick = firstFrameToConsume.Tick;
        _decisionsResolvedThroughTick = firstFrameToConsume.Tick - 1;
        _lastAcceptableFrameTick = long.MaxValue - capacityFrames;
    }

    private const long EmptyCell = -1;

    public CombatantAuthorityPredictionEpoch AuthorityEpoch => _authorityEpoch;
    public int CapacityFrames => _commands.Length;

    /// <summary>The single frame the next <see cref="ConsumeNextFrame"/> call will retire.</summary>
    public SimulationInstant NextFrameToConsume => new(_nextFrameToConsumeTick);

    /// <summary>
    /// The newest frame still admissible. A command past this is refused, which
    /// is what bounds the store rather than letting arrival pressure grow it.
    /// </summary>
    public SimulationInstant AcceptanceHorizon =>
        new(_nextFrameToConsumeTick + CapacityFrames - 1);

    public int StoredCommandCount => _storedCommandCount;

    /// <summary>
    /// How many admitted commands are waiting ahead of the consumption cursor.
    /// This is the real server input-buffer occupancy the lead controller needs
    /// in P04-05; it is deliberately measured, not inferred from RTT.
    /// </summary>
    public int BufferedFrameCount => _storedCommandCount;

    /// <summary>Total frames retired. Every active frame is retired exactly once.</summary>
    public long ConsumedFrameCount => _nextFrameToConsumeTick - _firstFrameTick;

    public AuthorityInputFallbackPolicy FallbackPolicy => _fallbackPolicy;

    /// <summary>
    /// The newest frame whose input application is final, or null before the
    /// first frame is resolved. Everything at or below this is permanently
    /// decided; the client prunes its send window against it.
    /// </summary>
    public SimulationInstant? ConsumedThroughFrame =>
        _decisionsResolvedThroughTick < _firstFrameTick
            ? null
            : new SimulationInstant(_decisionsResolvedThroughTick);

    public AuthorityInputFrameDecision? PreviousDecision => _previousDecision;
    public long LateCommandCount => _lateCommandCount;
    public long RejectedCommandCount => _rejectedCommandCount;
    public long DuplicateCommandCount => _duplicateCommandCount;
    public int RetainedDispositionFrames => _dispositions.Length;

    /// <summary>
    /// Highest-contiguous-plus-following-64-mask view of admitted command input
    /// sequences, for the owner receive acknowledgement. This is deliberately
    /// separate from <see cref="ConsumedThroughFrame"/>: a command can be received
    /// (stored ahead of the cursor) several frames before it is ever consumed, and
    /// the client needs both distinctions to prune its send window correctly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The cursor means "resolved", not "delivered": a sequence is at or below it
    /// once it was either admitted, or permanently superseded because its target
    /// frame was consumed without it. Both mean the same thing to the send window
    /// the design describes — stop resending — and the second is exactly the rule
    /// that a late copy of an already-consumed frame "cannot help and is not
    /// resent forever". Without retiring dead sequences the cursor would pin at
    /// the first lost command and the mask would saturate 64 frames later, which
    /// at 60 Hz makes the acknowledgement inert about one second into any session
    /// that drops a packet.
    /// </para>
    /// <para>
    /// Whether a frame actually ran on owner input or on fallback is NOT carried
    /// here. That distinction lives in the per-frame terminal dispositions, which
    /// is the only place the design puts it.
    /// </para>
    /// </remarks>
    public OwnerKnownJournalIdentityWindow ReceivedInputSequenceWindow =>
        new(
            _hasReceivedSequenceBase && _highestContiguousReceivedSequence != 0
                ? _highestContiguousReceivedSequence
                : null,
            _followingReceivedSequenceMask);

    public bool BelongsToScope(OwnerSimulationCommand command) =>
        command.IsValid &&
        command.MatchFrameEpoch == _authorityEpoch.MatchFrameEpoch &&
        command.Identity.Scope == _expectedScope &&
        command.AuthorityDiscontinuity == _authorityEpoch.AuthorityDiscontinuity;

    /// <summary>
    /// Offers one validated owner command to the store. Never throws for hostile
    /// or malformed input; every outcome is a classified disposition so ingress
    /// pressure is telemetry, not control flow.
    /// </summary>
    public AuthorityInputAdmission TryAdmit(OwnerSimulationCommand command)
    {
        if (!BelongsToScope(command))
        {
            return AuthorityInputAdmission.Rejected(
                AuthorityInputAdmissionFault.ForeignScope,
                arrival: null);
        }

        var targetTick = command.TargetFrame.Tick;

        // Already consumed. The frame's decision is permanent, so a late copy
        // cannot help and must never be simulated afterwards.
        if (targetTick < _nextFrameToConsumeTick)
        {
            _lateCommandCount++;
            return AuthorityInputAdmission.Late(
                Arrival(command, OwnerInputArrivalDisposition.LateCommand));
        }

        if (targetTick > _lastAcceptableFrameTick ||
            targetTick > _nextFrameToConsumeTick + CapacityFrames - 1)
        {
            _rejectedCommandCount++;
            return AuthorityInputAdmission.Rejected(
                AuthorityInputAdmissionFault.BeyondAcceptanceHorizon,
                Arrival(command, OwnerInputArrivalDisposition.RejectedCommand));
        }

        if (!HasConsistentSequenceOffset(command))
        {
            _rejectedCommandCount++;
            return AuthorityInputAdmission.Rejected(
                AuthorityInputAdmissionFault.SequenceFrameSkew,
                Arrival(command, OwnerInputArrivalDisposition.RejectedCommand));
        }

        var index = CellIndex(targetTick);
        if (_occupiedFrameTicks[index] == targetTick)
        {
            // Redundant resend of the same command is expected and idempotent.
            // A different command for an occupied frame is refused outright:
            // first admission wins, so no arrival can rewrite a frame's input.
            if (_commands[index] == command)
            {
                _duplicateCommandCount++;
                return AuthorityInputAdmission.Duplicate(
                    Arrival(command, OwnerInputArrivalDisposition.DuplicateCommand));
            }

            _rejectedCommandCount++;
            return AuthorityInputAdmission.Rejected(
                AuthorityInputAdmissionFault.ConflictingCommandForFrame,
                Arrival(command, OwnerInputArrivalDisposition.RejectedCommand));
        }

        _commands[index] = command;
        _occupiedFrameTicks[index] = targetTick;
        _storedCommandCount++;
        AnchorSequence(command);
        _highestAdmittedSequence = Math.Max(
            _highestAdmittedSequence,
            command.Sequence.Value);
        ResolveInputSequence(command.Sequence.Value);
        // A new admission can raise the bound above sequences whose frames were
        // already consumed, so retirement is re-evaluated rather than being a
        // one-shot decision taken when the frame left.
        DrainRetiredSequences();
        return AuthorityInputAdmission.Accepted(
            Arrival(command, OwnerInputArrivalDisposition.NewCommandAccepted));
    }

    /// <summary>
    /// Reads without consuming. Used by diagnostics and by the deadline policy to
    /// see whether a frame is already covered.
    /// </summary>
    public bool TryPeek(SimulationInstant frame, out OwnerSimulationCommand command)
    {
        var tick = frame.Tick;
        if (tick < _nextFrameToConsumeTick ||
            tick > _nextFrameToConsumeTick + CapacityFrames - 1)
        {
            command = default;
            return false;
        }

        var index = CellIndex(tick);
        if (_occupiedFrameTicks[index] != tick)
        {
            command = default;
            return false;
        }

        command = _commands[index];
        return true;
    }

    public bool IsFrameCovered(SimulationInstant frame) => TryPeek(frame, out _);

    /// <summary>
    /// Retires exactly one frame and advances the cursor by exactly one. There is
    /// no variant that skips, batches, or compacts frames: that absence is the
    /// point of this type.
    /// </summary>
    public AuthorityOwnerFrameConsumption ConsumeNextFrame()
    {
        var identity = NextFrameIdentity();
        var command = TryPeek(identity.Frame, out var stored) ? stored : (OwnerSimulationCommand?)null;
        AdvanceCursor();
        return new AuthorityOwnerFrameConsumption(identity, command);
    }

    /// <summary>
    /// The identity the next resolution will retire. Throws before any state is
    /// mutated when the timeline can no longer advance, so a failed call leaves
    /// the scheduler exactly as it was.
    /// </summary>
    private AuthorityInputFrameIdentity NextFrameIdentity()
    {
        if (_nextFrameToConsumeTick > _lastAcceptableFrameTick)
        {
            throw new InvalidOperationException(
                "The match timeline is exhausted for this combatant scope and requires an explicit timeline reset.");
        }

        return new AuthorityInputFrameIdentity(
            _authorityEpoch,
            new SimulationInstant(_nextFrameToConsumeTick));
    }

    private void AdvanceCursor()
    {
        var tick = _nextFrameToConsumeTick;
        var index = CellIndex(tick);
        if (_occupiedFrameTicks[index] == tick)
        {
            _storedCommandCount--;
        }

        if (SequenceForFrame(tick) is { } retiredSequence)
        {
            // This frame is leaving permanently, so its sequence can never be
            // admitted afterwards. Recording that lets the receive cursor move
            // past a lost command; otherwise one dropped packet pins the
            // acknowledgement forever. It is only evidence, though — the drain
            // still refuses to claim anything the owner never sent.
            _consumedThroughSequence = retiredSequence;
            DrainRetiredSequences();
        }

        _occupiedFrameTicks[index] = EmptyCell;
        _commands[index] = default;
        _nextFrameToConsumeTick = tick + 1;
    }

    /// <summary>
    /// Retires the next frame and commits its single input application. This is
    /// the normal authority entry point: it is impossible to advance the cursor
    /// through here without also producing exactly one decision.
    /// </summary>
    /// <param name="currentBasis">
    /// Authority-resolved view and configuration revisions effective on this
    /// frame. Fallback input is built from this, never from a stale command, so
    /// a missed frame cannot replay an old movement revision.
    /// </param>
    /// <remarks>
    /// Resolution is transactional: the decision is fully constructed before the
    /// cursor moves. If any validation throws, the frame is left unconsumed and
    /// the scheduler is unchanged. Consuming first and validating afterwards
    /// would retire a frame with no decision and permanently wedge the timeline.
    /// </remarks>
    public AuthorityInputFrameDecision ResolveNextFrame(
        AuthorityFallbackInputBasis currentBasis) =>
        ResolveNextFrame(currentBasis, intentSink: null);

    /// <param name="intentSink">
    /// Offered the committed decision exactly once, before this call returns.
    /// Passing the sink here rather than calling it afterwards is what makes
    /// "durable intents are resolved on the frame that referenced them"
    /// structural: a frame cannot be consumed without the sink seeing it, and
    /// the sink cannot see a frame that was not consumed.
    /// </param>
    public AuthorityInputFrameDecision ResolveNextFrame(
        AuthorityFallbackInputBasis currentBasis,
        IAuthorityFrameIntentSink? intentSink)
    {
        if (!currentBasis.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(currentBasis));
        }

        RequireDecisionContinuity();
        var identity = NextFrameIdentity();
        var decision = TryPeek(identity.Frame, out var command)
            ? AuthorityInputFrameDecision.ApplyReceived(identity, command)
            : _fallbackPolicy.ResolveMissingFrame(identity, _previousDecision, currentBasis);

        AdvanceCursor();
        return PublishCommitted(Commit(decision), intentSink);
    }

    /// <summary>
    /// Retires the next frame with a declared authority override, for a
    /// combatant that is eliminated, stunned, teleported, or otherwise not under
    /// owner control on this frame. An override consumes the frame exactly like
    /// any other decision, so the timeline stays contiguous.
    /// </summary>
    /// <remarks>
    /// Any owner command already stored for this frame is discarded rather than
    /// applied. That is deliberate: the override is the authority's declaration
    /// that owner intent does not govern this frame, and applying both would be
    /// two applications for one frame.
    /// </remarks>
    public AuthorityInputFrameDecision ResolveNextFrameAsAuthorityOverride(
        CharacterSimulationInput overrideInput,
        AuthorityInputOverrideReason reason) =>
        ResolveNextFrameAsAuthorityOverride(overrideInput, reason, intentSink: null);

    public AuthorityInputFrameDecision ResolveNextFrameAsAuthorityOverride(
        CharacterSimulationInput overrideInput,
        AuthorityInputOverrideReason reason,
        IAuthorityFrameIntentSink? intentSink)
    {
        RequireDecisionContinuity();
        var identity = NextFrameIdentity();
        var decision = AuthorityInputFrameDecision.ApplyAuthorityOverride(
            identity,
            overrideInput,
            reason);

        AdvanceCursor();
        return PublishCommitted(Commit(decision), intentSink);
    }

    /// <remarks>
    /// <para>
    /// The sink runs after the frame's decision is already final, because durable
    /// intents are resolved against a decided frame, not a candidate one.
    /// </para>
    /// <para>
    /// A sink is therefore expected not to throw, and the supplied intent
    /// resolver classifies every policy and journal failure rather than raising.
    /// If a custom sink does throw, the timeline stays correct — the frame is
    /// consumed exactly once and its disposition is already recorded — but the
    /// return value is lost. The committed decision remains readable through
    /// <see cref="PreviousDecision"/>, so a caller can still drive that frame's
    /// simulation instead of skipping it, which the design forbids.
    /// </para>
    /// </remarks>
    private static AuthorityInputFrameDecision PublishCommitted(
        AuthorityInputFrameDecision decision,
        IAuthorityFrameIntentSink? intentSink)
    {
        intentSink?.OnFrameDecisionCommitted(in decision);
        return decision;
    }

    /// <summary>
    /// Reads back the permanent record for a frame, while it remains within the
    /// bounded retention window.
    /// </summary>
    public bool TryGetDisposition(
        SimulationInstant frame,
        out AuthorityFrameTerminalDisposition disposition)
    {
        var tick = frame.Tick;
        if (tick < 0)
        {
            disposition = default;
            return false;
        }

        var index = (int)(tick % _dispositions.Length);
        if (_dispositionFrameTicks[index] != tick)
        {
            disposition = default;
            return false;
        }

        disposition = _dispositions[index];
        return true;
    }

    /// <summary>
    /// Copies the retained dispositions from oldest to newest into
    /// <paramref name="destination"/>, for the bounded terminal-disposition list
    /// the authority returns to the owner.
    /// </summary>
    public int CopyRetainedDispositions(
        Span<AuthorityFrameTerminalDisposition> destination)
    {
        var available = (int)Math.Min(
            _decisionsResolvedThroughTick - _firstFrameTick + 1,
            _dispositions.Length);
        if (available <= 0)
        {
            return 0;
        }

        var count = Math.Min(available, destination.Length);
        var oldestTick = _decisionsResolvedThroughTick - count + 1;
        for (var i = 0; i < count; i++)
        {
            var tick = oldestTick + i;
            destination[i] = _dispositions[(int)(tick % _dispositions.Length)];
        }

        return count;
    }

    private AuthorityInputFrameDecision Commit(AuthorityInputFrameDecision decision)
    {
        var tick = decision.Identity.Frame.Tick;
        var index = (int)(tick % _dispositions.Length);
        _dispositions[index] = new AuthorityFrameTerminalDisposition(decision);
        _dispositionFrameTicks[index] = tick;
        _previousDecision = decision;
        _decisionsResolvedThroughTick = tick;
        return decision;
    }

    /// <summary>
    /// Fails closed if the cursor was moved by the raw
    /// <see cref="ConsumeNextFrame"/> primitive without committing a decision.
    /// Silently treating that gap as "no predecessor" would understate
    /// <c>ConsecutiveMissingFrames</c> and let stale held input be repeated past
    /// its policy bound, so it is a programming error rather than a degraded mode.
    /// </summary>
    private void RequireDecisionContinuity()
    {
        if (_decisionsResolvedThroughTick != _nextFrameToConsumeTick - 1)
        {
            throw new InvalidOperationException(
                "Authority frames were consumed without committing a decision. " +
                "Use ResolveNextFrame or ResolveNextFrameAsAuthorityOverride for every active frame.");
        }
    }

    private AuthorityInputArrivalDecision Arrival(
        OwnerSimulationCommand command,
        OwnerInputArrivalDisposition disposition) =>
        new(_authorityEpoch, command, disposition);

    private int CellIndex(long frameTick) => (int)(frameTick % CapacityFrames);

    /// <summary>
    /// The owner command builder advances input sequence and target frame
    /// together, so within one owner-control scope
    /// <c>sequence - frame</c> is invariant. Checking it against a single anchor
    /// is O(1) and rejects an identity that reuses one sequence across frames,
    /// which would otherwise defeat per-frame deduplication.
    /// </summary>
    private bool HasConsistentSequenceOffset(OwnerSimulationCommand command)
    {
        if (!_hasSequenceAnchor)
        {
            return true;
        }

        var frameDelta = (decimal)command.TargetFrame.Tick - _anchorFrameTick;
        var sequenceDelta = (decimal)command.Sequence.Value - _anchorSequence;
        return frameDelta == sequenceDelta;
    }

    private void AnchorSequence(OwnerSimulationCommand command)
    {
        if (_hasSequenceAnchor)
        {
            return;
        }

        _hasSequenceAnchor = true;
        _anchorFrameTick = command.TargetFrame.Tick;
        _anchorSequence = command.Sequence.Value;
    }

    /// <summary>
    /// Marks one owner input sequence resolved. Called on admission, and again at
    /// consumption for a frame whose command never arrived.
    /// </summary>
    /// <remarks>
    /// The base is seeded from the sequence this scheduler's own consumption
    /// cursor names, not from the first sequence to arrive. Owner input sequences
    /// live in <see cref="OwnerIntentScope"/>, which deliberately excludes the
    /// authority discontinuity, so they continue across a teleport while this
    /// scheduler is rebuilt; a fixed base of zero would put every sequence of a
    /// mid-stream scheduler past the 64-wide mask and record nothing at all. But
    /// seeding from the first arrival is equally wrong in the other direction:
    /// if that packet was reordered or its predecessors were lost, the base
    /// would sit above sequences that are still live and advertise them as
    /// resolved when they were neither received nor consumed.
    /// </remarks>
    private void ResolveInputSequence(ulong id)
    {
        if (id == 0)
        {
            return;
        }

        if (!_hasReceivedSequenceBase)
        {
            if (!_hasSequenceAnchor)
            {
                return;
            }

            var firstLiveSequence = (decimal)_anchorSequence +
                (_nextFrameToConsumeTick - (decimal)_anchorFrameTick);
            if (firstLiveSequence > ulong.MaxValue)
            {
                return;
            }

            _hasReceivedSequenceBase = true;
            // Clamped at zero rather than refused. Owner sequence numbering
            // restarts near 1 while match frames are already high — the normal
            // shape at spawn, respawn, reconnect, or control renewal — so the
            // frame at the cursor maps to a sequence below 1. Zero is not a
            // legal sequence, so this base claims nothing, but it lets the
            // first commands land inside the mask immediately instead of
            // leaving the window empty for a whole prediction lead.
            _highestContiguousReceivedSequence =
                firstLiveSequence < 1 ? 0 : (ulong)(firstLiveSequence - 1);
        }

        if (id <= _highestContiguousReceivedSequence)
        {
            return;
        }

        var distance = id - _highestContiguousReceivedSequence;
        if (distance > 64)
        {
            // Beyond the bounded mask. Frames are consumed in order and each
            // retires its own sequence, so the cursor keeps advancing and this
            // sequence falls inside the window before it is consumed.
            return;
        }

        _followingReceivedSequenceMask |= 1UL << (int)(distance - 1);
        while ((_followingReceivedSequenceMask & 1UL) != 0)
        {
            _highestContiguousReceivedSequence++;
            _followingReceivedSequenceMask >>= 1;
        }
    }

    /// <summary>
    /// Advances the cursor through sequences whose frames are permanently gone,
    /// but never past the highest sequence the owner actually sent.
    /// </summary>
    /// <remarks>
    /// The bound matters because the authority keeps consuming frames on its own
    /// clock whether or not the client is still generating. After a client-side
    /// stall longer than the prediction lead, extrapolating the frame/sequence
    /// offset forward would claim sequences the owner never originated. The
    /// client's own receive gate rejects such an acknowledgement outright — and
    /// with it the consumed cursor riding alongside — so the send window would
    /// stop pruning at exactly the moment recovery matters most.
    /// </remarks>
    private void DrainRetiredSequences()
    {
        if (!_hasReceivedSequenceBase)
        {
            return;
        }

        var bound = Math.Min(_consumedThroughSequence, _highestAdmittedSequence);
        while (_highestContiguousReceivedSequence < bound)
        {
            // Always the immediate successor, so this both terminates and stays
            // inside the mask's 64-wide reach.
            ResolveInputSequence(_highestContiguousReceivedSequence + 1);
        }
    }

    /// <summary>
    /// The input sequence a command for <paramref name="frameTick"/> would carry,
    /// derived from the invariant <c>sequence - frame</c> offset this scheduler
    /// already enforces in <see cref="HasConsistentSequenceOffset"/>. Null before
    /// any command has anchored that offset, or if the value would fall outside
    /// the positive sequence range.
    /// </summary>
    private ulong? SequenceForFrame(long frameTick)
    {
        if (!_hasSequenceAnchor)
        {
            return null;
        }

        var sequence = (decimal)_anchorSequence + (frameTick - (decimal)_anchorFrameTick);
        return sequence is >= 1 and <= ulong.MaxValue ? (ulong)sequence : null;
    }
}
