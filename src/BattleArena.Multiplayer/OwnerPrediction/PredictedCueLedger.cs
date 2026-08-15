using BattleArena.Core.Common;

namespace BattleArena.Multiplayer.OwnerPrediction;

public enum PredictedCueKind
{
    Unspecified = 0,
    Jump = 1,
    Roll = 2,
    Swing = 3,
    Impact = 4,
    Landing = 5,
    Footstep = 6,
    ItemAbility = 7,
}

public enum PredictedCueOriginKind
{
    Unspecified = 0,
    MovementTransition = 1,
    PredictedAction = 2,
    AuthoritySimulationEvent = 3,
}

public enum PredictedCueRepairKind
{
    Unspecified = 0,
    CancelImmediately = 1,
    FadeOut = 2,
    SeekAuthorityState = 3,
}

public enum PredictedCueFinalityPolicy
{
    Unspecified = 0,
    AuthorityResolved = 1,
    ReplayFinalOnly = 2,
}

public enum PredictedCueLedgerDisposition
{
    Unspecified = 0,
    EmitPredicted = 1,
    SuppressAlreadyObserved = 2,
    ConfirmWithoutEmission = 3,
    EmitAuthority = 4,
    InvokeRepair = 5,
    RejectionRecordedWithoutCue = 6,
    RejectEpochMismatch = 7,
    SuppressRetired = 8,
}

public enum PredictedCueLedgerStatus
{
    Unspecified = 0,
    Predicted = 1,
    Confirmed = 2,
    AuthorityEmitted = 3,
    Rejected = 4,
}

/// <summary>
/// Stable identity of one discrete presentation cue. Local rebase identity is
/// deliberately absent: reconciliation must rediscover the same identity and be
/// suppressed rather than make it look new.
/// </summary>
public readonly record struct PredictedCueIdentity
{
    public PredictedCueIdentity(
        CombatantAuthorityPredictionEpoch epoch,
        PredictedCueKind kind,
        PredictedCueOriginKind originKind,
        ulong originId,
        uint eventOrdinal)
    {
        if (!epoch.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(epoch));
        }

        if (kind == PredictedCueKind.Unspecified ||
            !Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (originKind == PredictedCueOriginKind.Unspecified ||
            !Enum.IsDefined(originKind))
        {
            throw new ArgumentOutOfRangeException(nameof(originKind));
        }

        if (originId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(originId));
        }

        Epoch = epoch;
        Kind = kind;
        OriginKind = originKind;
        OriginId = originId;
        EventOrdinal = eventOrdinal;
    }

    public CombatantAuthorityPredictionEpoch Epoch { get; }
    public PredictedCueKind Kind { get; }
    public PredictedCueOriginKind OriginKind { get; }
    public ulong OriginId { get; }
    public uint EventOrdinal { get; }
    public PredictedCueFinalityPolicy FinalityPolicy => Kind switch
    {
        PredictedCueKind.Landing or PredictedCueKind.Footstep =>
            PredictedCueFinalityPolicy.ReplayFinalOnly,
        PredictedCueKind.Jump or PredictedCueKind.Roll or PredictedCueKind.Swing or
        PredictedCueKind.Impact or PredictedCueKind.ItemAbility =>
            PredictedCueFinalityPolicy.AuthorityResolved,
        _ => PredictedCueFinalityPolicy.Unspecified,
    };
    public bool IsValid =>
        Epoch.IsValid &&
        Kind != PredictedCueKind.Unspecified &&
        Enum.IsDefined(Kind) &&
        OriginKind != PredictedCueOriginKind.Unspecified &&
        Enum.IsDefined(OriginKind) &&
        OriginId != 0 &&
        FinalityPolicy != PredictedCueFinalityPolicy.Unspecified;
}

public readonly record struct PredictedCueLedgerDecision
{
    public PredictedCueLedgerDecision(
        PredictedCueLedgerDisposition disposition,
        PredictedCueRepairKind repair)
    {
        if (disposition == PredictedCueLedgerDisposition.Unspecified ||
            !Enum.IsDefined(disposition))
        {
            throw new ArgumentOutOfRangeException(nameof(disposition));
        }

        if (!Enum.IsDefined(repair))
        {
            throw new ArgumentOutOfRangeException(nameof(repair));
        }

        var repairIsActionable = repair != PredictedCueRepairKind.Unspecified;
        if ((disposition == PredictedCueLedgerDisposition.InvokeRepair) !=
            repairIsActionable)
        {
            throw new ArgumentException(
                "Only an InvokeRepair decision may carry one defined repair policy.",
                nameof(repair));
        }

        Disposition = disposition;
        Repair = repair;
    }

    public PredictedCueLedgerDisposition Disposition { get; }
    public PredictedCueRepairKind Repair { get; }

    public bool ShouldEmit => Disposition is
        PredictedCueLedgerDisposition.EmitPredicted or
        PredictedCueLedgerDisposition.EmitAuthority;

    public bool ShouldRepair =>
        Disposition == PredictedCueLedgerDisposition.InvokeRepair &&
        Repair != PredictedCueRepairKind.Unspecified &&
        Enum.IsDefined(Repair);
}

public readonly record struct PredictedCueLedgerEntrySnapshot(
    PredictedCueLedgerStatus Status,
    PredictedCueFinalityPolicy FinalityPolicy,
    SimulationInstant FirstRelevantFrame,
    SimulationInstant LatestRelevantFrame,
    PredictedCueRepairKind Repair);

/// <summary>
/// Bounded ledger separating simulation event discovery from one-shot audiovisual
/// presentation. It never executes presentation itself.
/// </summary>
public sealed class PredictedCueLedger
{
    public const int DefaultCapacity = 512;
    public const int MaximumCapacity = 4_096;

    private readonly CombatantPredictionEpochGate _epochGate;
    private readonly Dictionary<PredictedCueIdentity, Entry> _entries;
    private readonly PredictedCueIdentity[] _pruneKeys;
    private long _replayFinalThroughTick = -1;
    private long _authorityOutcomeFinalThroughTick = -1;

    public PredictedCueLedger(
        CombatantAuthorityPredictionEpoch initialEpoch,
        int capacity = DefaultCapacity)
    {
        if (!initialEpoch.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(initialEpoch));
        }

        if (capacity is < 1 or > MaximumCapacity)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        Capacity = capacity;
        _entries = new Dictionary<PredictedCueIdentity, Entry>(capacity);
        _pruneKeys = new PredictedCueIdentity[capacity];
        _epochGate = new CombatantPredictionEpochGate(
            new CombatantLocalPredictionEpoch(
                initialEpoch,
                LocalPredictionRebaseId.Initial));
    }

    public int Count => _entries.Count;
    public int Capacity { get; }
    public CombatantAuthorityPredictionEpoch CurrentEpoch =>
        _epochGate.Current.Authority;

    public PredictedCueLedgerDecision ObservePredicted(
        PredictedCueIdentity identity,
        SimulationInstant relevantSimulationFrame)
    {
        ValidateIdentity(identity);
        if (identity.Epoch != CurrentEpoch)
        {
            return EpochMismatch();
        }

        if (_entries.TryGetValue(identity, out var existing))
        {
            RequireSameFinalityPolicy(existing, identity.FinalityPolicy);
            existing.LatestRelevantFrame = Max(
                existing.LatestRelevantFrame,
                relevantSimulationFrame);
            _entries[identity] = existing;
            return Suppressed();
        }

        if (IsRetired(identity.FinalityPolicy, relevantSimulationFrame))
        {
            return Retired();
        }

        Add(identity, new Entry(
            PredictedCueLedgerStatus.Predicted,
            identity.FinalityPolicy,
            relevantSimulationFrame,
            relevantSimulationFrame,
            PredictedCueRepairKind.Unspecified));
        return new PredictedCueLedgerDecision(
            PredictedCueLedgerDisposition.EmitPredicted,
            PredictedCueRepairKind.Unspecified);
    }

    public PredictedCueLedgerDecision ObserveAuthorityConfirmation(
        PredictedCueIdentity identity,
        SimulationInstant relevantSimulationFrame)
    {
        ValidateIdentity(identity);
        if (identity.Epoch != CurrentEpoch)
        {
            return EpochMismatch();
        }

        if (identity.FinalityPolicy != PredictedCueFinalityPolicy.AuthorityResolved)
        {
            if (IsRetired(identity.FinalityPolicy, relevantSimulationFrame))
            {
                return Retired();
            }

            throw new InvalidOperationException(
                "A replay-final-only cue cannot receive an authority confirmation.");
        }

        if (!_entries.TryGetValue(identity, out var existing))
        {
            if (IsRetired(
                    PredictedCueFinalityPolicy.AuthorityResolved,
                    relevantSimulationFrame))
            {
                return Retired();
            }

            Add(identity, new Entry(
                PredictedCueLedgerStatus.AuthorityEmitted,
                PredictedCueFinalityPolicy.AuthorityResolved,
                relevantSimulationFrame,
                relevantSimulationFrame,
                PredictedCueRepairKind.Unspecified));
            return new PredictedCueLedgerDecision(
                PredictedCueLedgerDisposition.EmitAuthority,
                PredictedCueRepairKind.Unspecified);
        }

        RequireSameFinalityPolicy(
            existing,
            PredictedCueFinalityPolicy.AuthorityResolved);
        existing.LatestRelevantFrame = Max(
            existing.LatestRelevantFrame,
            relevantSimulationFrame);
        switch (existing.Status)
        {
            case PredictedCueLedgerStatus.Predicted:
                existing.Status = PredictedCueLedgerStatus.Confirmed;
                _entries[identity] = existing;
                return new PredictedCueLedgerDecision(
                    PredictedCueLedgerDisposition.ConfirmWithoutEmission,
                    PredictedCueRepairKind.Unspecified);
            case PredictedCueLedgerStatus.Confirmed:
            case PredictedCueLedgerStatus.AuthorityEmitted:
                _entries[identity] = existing;
                return Suppressed();
            case PredictedCueLedgerStatus.Rejected:
                throw new InvalidOperationException(
                    "One cue identity cannot be both authority-rejected and confirmed.");
            default:
                throw new InvalidOperationException("Cue ledger contains an invalid state.");
        }
    }

    public PredictedCueLedgerDecision ObserveAuthorityRejection(
        PredictedCueIdentity identity,
        SimulationInstant relevantSimulationFrame,
        PredictedCueRepairKind repair)
    {
        if (repair == PredictedCueRepairKind.Unspecified || !Enum.IsDefined(repair))
        {
            throw new ArgumentOutOfRangeException(nameof(repair));
        }

        ValidateIdentity(identity);
        if (identity.Epoch != CurrentEpoch)
        {
            return EpochMismatch();
        }

        if (identity.FinalityPolicy != PredictedCueFinalityPolicy.AuthorityResolved)
        {
            if (IsRetired(identity.FinalityPolicy, relevantSimulationFrame))
            {
                return Retired();
            }

            throw new InvalidOperationException(
                "A replay-final-only cue cannot receive an authority rejection.");
        }

        if (!_entries.TryGetValue(identity, out var existing))
        {
            if (IsRetired(
                    PredictedCueFinalityPolicy.AuthorityResolved,
                    relevantSimulationFrame))
            {
                return Retired();
            }

            Add(identity, new Entry(
                PredictedCueLedgerStatus.Rejected,
                PredictedCueFinalityPolicy.AuthorityResolved,
                relevantSimulationFrame,
                relevantSimulationFrame,
                repair));
            return new PredictedCueLedgerDecision(
                PredictedCueLedgerDisposition.RejectionRecordedWithoutCue,
                PredictedCueRepairKind.Unspecified);
        }

        RequireSameFinalityPolicy(
            existing,
            PredictedCueFinalityPolicy.AuthorityResolved);
        existing.LatestRelevantFrame = Max(
            existing.LatestRelevantFrame,
            relevantSimulationFrame);
        switch (existing.Status)
        {
            case PredictedCueLedgerStatus.Predicted:
                existing.Status = PredictedCueLedgerStatus.Rejected;
                existing.Repair = repair;
                _entries[identity] = existing;
                return new PredictedCueLedgerDecision(
                    PredictedCueLedgerDisposition.InvokeRepair,
                    repair);
            case PredictedCueLedgerStatus.Rejected:
                if (existing.Repair != repair)
                {
                    throw new InvalidOperationException(
                        "One rejected cue identity cannot carry conflicting repair policies.");
                }

                _entries[identity] = existing;
                return Suppressed();
            case PredictedCueLedgerStatus.Confirmed:
            case PredictedCueLedgerStatus.AuthorityEmitted:
                throw new InvalidOperationException(
                    "One cue identity cannot be both authority-confirmed and rejected.");
            default:
                throw new InvalidOperationException("Cue ledger contains an invalid state.");
        }
    }

    /// <summary>
    /// Clears the old authority epoch. Duplicate lifecycle baselines are a no-op;
    /// stale or incoherent baselines fail instead of resurrecting old cues.
    /// </summary>
    public bool ResetForAuthorityEpoch(CombatantAuthorityPredictionEpoch next)
    {
        var decision = _epochGate.ApplyAuthorityEpoch(next);
        switch (decision)
        {
            case PredictionEpochTransitionDecision.Applied:
                _entries.Clear();
                _replayFinalThroughTick = -1;
                _authorityOutcomeFinalThroughTick = -1;
                return true;
            case PredictionEpochTransitionDecision.Duplicate:
                return false;
            default:
                throw new InvalidOperationException(
                    $"Cue ledger rejected authority epoch reset: {decision}.");
        }
    }

    /// <summary>
    /// Authority-resolved entries retire only after a terminal result and after
    /// both replay and outcome journals cross their latest relevant frame.
    /// Replay-final-only entries need no per-cue authority result and retire when
    /// replay alone crosses that frame. The two monotonic watermarks prevent a
    /// delayed packet or snapshot from turning a discarded identity into a new
    /// presentation cue.
    /// </summary>
    public int RetireThrough(
        SimulationInstant replayFinalThrough,
        SimulationInstant authorityOutcomeFinalThrough)
    {
        _replayFinalThroughTick = Math.Max(
            _replayFinalThroughTick,
            replayFinalThrough.Tick);
        _authorityOutcomeFinalThroughTick = Math.Max(
            _authorityOutcomeFinalThroughTick,
            authorityOutcomeFinalThrough.Tick);

        var pruneCount = 0;
        foreach (var pair in _entries)
        {
            if (CanRetire(pair.Value))
            {
                _pruneKeys[pruneCount++] = pair.Key;
            }
        }

        var removed = 0;
        for (var index = 0; index < pruneCount; index++)
        {
            var identity = _pruneKeys[index];
            if (_entries.Remove(identity))
            {
                removed++;
            }

            _pruneKeys[index] = default;
        }

        return removed;
    }

    public bool TryGet(
        PredictedCueIdentity identity,
        out PredictedCueLedgerEntrySnapshot snapshot)
    {
        if (_entries.TryGetValue(identity, out var entry))
        {
            snapshot = new PredictedCueLedgerEntrySnapshot(
                entry.Status,
                entry.FinalityPolicy,
                entry.FirstRelevantFrame,
                entry.LatestRelevantFrame,
                entry.Repair);
            return true;
        }

        snapshot = default;
        return false;
    }

    private void Add(PredictedCueIdentity identity, Entry entry)
    {
        if (_entries.Count >= Capacity)
        {
            throw new InvalidOperationException(
                "Predicted cue ledger capacity was exhausted before replay history was pruned.");
        }

        _entries.Add(identity, entry);
    }

    private static PredictedCueLedgerDecision Suppressed() => new(
        PredictedCueLedgerDisposition.SuppressAlreadyObserved,
        PredictedCueRepairKind.Unspecified);

    private static PredictedCueLedgerDecision EpochMismatch() => new(
        PredictedCueLedgerDisposition.RejectEpochMismatch,
        PredictedCueRepairKind.Unspecified);

    private static PredictedCueLedgerDecision Retired() => new(
        PredictedCueLedgerDisposition.SuppressRetired,
        PredictedCueRepairKind.Unspecified);

    private static void ValidateIdentity(PredictedCueIdentity identity)
    {
        if (!identity.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(identity));
        }

    }

    private bool IsRetired(
        PredictedCueFinalityPolicy finalityPolicy,
        SimulationInstant relevantSimulationFrame) => finalityPolicy switch
        {
            PredictedCueFinalityPolicy.AuthorityResolved =>
                relevantSimulationFrame.Tick <= _replayFinalThroughTick &&
                relevantSimulationFrame.Tick <= _authorityOutcomeFinalThroughTick,
            PredictedCueFinalityPolicy.ReplayFinalOnly =>
                relevantSimulationFrame.Tick <= _replayFinalThroughTick,
            _ => throw new ArgumentOutOfRangeException(nameof(finalityPolicy)),
        };

    private bool CanRetire(Entry entry) => entry.FinalityPolicy switch
    {
        PredictedCueFinalityPolicy.AuthorityResolved =>
            IsTerminal(entry.Status) &&
            entry.LatestRelevantFrame.Tick <= _replayFinalThroughTick &&
            entry.LatestRelevantFrame.Tick <= _authorityOutcomeFinalThroughTick,
        PredictedCueFinalityPolicy.ReplayFinalOnly =>
            entry.LatestRelevantFrame.Tick <= _replayFinalThroughTick,
        _ => throw new InvalidOperationException("Cue ledger contains an invalid finality policy."),
    };

    private static void RequireSameFinalityPolicy(
        Entry entry,
        PredictedCueFinalityPolicy claimedPolicy)
    {
        if (entry.FinalityPolicy != claimedPolicy)
        {
            throw new InvalidOperationException(
                "One stable cue identity cannot change its finality policy.");
        }
    }

    private static bool IsTerminal(PredictedCueLedgerStatus status) => status is
        PredictedCueLedgerStatus.Confirmed or
        PredictedCueLedgerStatus.AuthorityEmitted or
        PredictedCueLedgerStatus.Rejected;

    private static SimulationInstant Max(SimulationInstant left, SimulationInstant right) =>
        left >= right ? left : right;

    private struct Entry
    {
        public Entry(
            PredictedCueLedgerStatus status,
            PredictedCueFinalityPolicy finalityPolicy,
            SimulationInstant firstRelevantFrame,
            SimulationInstant latestRelevantFrame,
            PredictedCueRepairKind repair)
        {
            Status = status;
            FinalityPolicy = finalityPolicy;
            FirstRelevantFrame = firstRelevantFrame;
            LatestRelevantFrame = latestRelevantFrame;
            Repair = repair;
        }

        public PredictedCueLedgerStatus Status { get; set; }
        public PredictedCueFinalityPolicy FinalityPolicy { get; }
        public SimulationInstant FirstRelevantFrame { get; }
        public SimulationInstant LatestRelevantFrame { get; set; }
        public PredictedCueRepairKind Repair { get; set; }
    }
}
