using BattleArena.Core.Common;
using BattleArena.Core.Movement.Simulation;

namespace BattleArena.Multiplayer.OwnerPrediction;

/// <summary>
/// Versioned canonical hash of a simulation state, for diagnostics only.
/// </summary>
/// <remarks>
/// <para>
/// Built from explicitly ordered, quantized fields rather than from raw doubles
/// or Protobuf bytes. Raw doubles differ between machines for arithmetic that is
/// semantically identical; Protobuf bytes differ when field presence or encoding
/// changes without any state changing. Either would make the hash disagree
/// across endpoints that actually simulated the same thing.
/// </para>
/// <para>
/// It is deliberately never a correction trigger. A hash says two states differ
/// but cannot say which field or by how much, so acting on it alone would
/// produce corrections nobody can explain and that may be inside tolerance. Its
/// job is to make a real divergence cheap to *detect* in a trace, after which
/// the comparer names the field.
/// </para>
/// </remarks>
public readonly record struct CanonicalMovementStateHash
{
    /// <summary>
    /// Bumped whenever the hashed field set or quantization changes. Two
    /// endpoints on different schema versions must not compare hashes at all
    /// rather than compare them and disagree.
    /// </summary>
    public const uint SchemaVersion = 1;

    public CanonicalMovementStateHash(uint schemaVersion, ulong value) =>
        throw new NotImplementedException();

    public uint Schema { get; }
    public ulong Value { get; }
    public bool IsValid => throw new NotImplementedException();

    /// <summary>Hashes the prediction-relevant fields of a state, in fixed order.</summary>
    public static CanonicalMovementStateHash Compute(in CharacterSimulationState state) =>
        throw new NotImplementedException();

    /// <summary>
    /// Writes the canonical byte encoding a hash is taken over.
    /// </summary>
    /// <remarks>
    /// Exposed so a golden test can assert the exact bytes. A hash test alone
    /// cannot distinguish "the field set changed" from "the hash function
    /// changed", and the first is a correctness problem while the second is not.
    /// </remarks>
    public static int WriteCanonicalBytes(
        in CharacterSimulationState state,
        Span<byte> destination) => throw new NotImplementedException();
}

/// <summary>Which class of difference the comparer found.</summary>
/// <remarks>
/// Ordered by how much a player would notice. The distinction that matters is
/// between differences that are *discrete* — a different support surface, a
/// different collision profile, a different jump phase — and differences that
/// are numeric. A discrete mismatch means the two simulations disagree about
/// what happened; a numeric one may just be arithmetic.
/// </remarks>
public enum OwnerStateDifferenceKind : byte
{
    /// <summary>Identical within every tolerance. Nothing to do but prune.</summary>
    None = 1,

    /// <summary>
    /// Only the diagnostic hash differs. Real but unattributable, and explicitly
    /// not grounds for correction on its own.
    /// </summary>
    DiagnosticOnly = 2,

    /// <summary>Numeric drift inside the authored tolerance.</summary>
    WithinTolerance = 3,

    /// <summary>Numeric difference beyond tolerance: position, velocity, or facing.</summary>
    NumericBeyondTolerance = 4,

    /// <summary>
    /// The two simulations disagree about a discrete fact — support, profile,
    /// grounded, locomotion mode, jump phase, or action phase. Never smoothed:
    /// these are collision and gameplay truth.
    /// </summary>
    DiscreteMismatch = 5,
}

/// <summary>One comparison result, naming the field that differed.</summary>
/// <remarks>
/// The field name is load-bearing rather than decorative. A correction that
/// cannot say which field caused it is a correction nobody can debug, and the
/// separate-process trace parity in P06-12 requires identifying the exact first
/// diverging field and frame.
/// </remarks>
public readonly record struct OwnerStateDifference
{
    internal OwnerStateDifference(
        OwnerStateDifferenceKind kind,
        string fieldName,
        double magnitude) => throw new NotImplementedException();

    public OwnerStateDifferenceKind Kind { get; }

    /// <summary>The first differing field in canonical order, or empty for none.</summary>
    public string FieldName { get; }

    /// <summary>How far apart, for numeric fields; zero for discrete ones.</summary>
    public double Magnitude { get; }

    public bool RequiresAttention => throw new NotImplementedException();
    public static OwnerStateDifference None => throw new NotImplementedException();
}

/// <summary>Authored tolerances for owner/authority comparison.</summary>
/// <remarks>
/// These are content: too tight and the owner corrects constantly on arithmetic
/// noise, too loose and real divergence is invisible until it is large. They are
/// carried per revision for the same reason the motor policy is.
/// </remarks>
public readonly record struct OwnerReconciliationTolerances
{
    public double PositionMetres { get; init; }
    public double VelocityMetresPerSecond { get; init; }
    public double FacingRadians { get; init; }

    /// <summary>
    /// How far a contact normal may differ and still count as the same surface.
    /// Seams between two authored colliders legitimately report slightly
    /// different normals for what is one flat floor.
    /// </summary>
    public double ContactNormalRadians { get; init; }

    public bool IsValid => throw new NotImplementedException();
    public static OwnerReconciliationTolerances Default => throw new NotImplementedException();
}

/// <summary>
/// Compares the owner's predicted post-state against the authority's answer for
/// the same frame.
/// </summary>
/// <remarks>
/// Comparison is ordered and short-circuits on the first difference that
/// matters, so the reported field is always the *first* divergence in canonical
/// order rather than an arbitrary one. Discrete facts are checked before numeric
/// ones: a character standing on a different surface is a bigger statement than
/// one standing a centimetre away, and reporting the centimetre would hide it.
/// </remarks>
public sealed class OwnerReconciliationComparer
{
    public OwnerReconciliationComparer(OwnerReconciliationTolerances tolerances) =>
        throw new NotImplementedException();

    public OwnerReconciliationTolerances Tolerances => throw new NotImplementedException();

    public OwnerStateDifference Compare(
        in CharacterSimulationState predicted,
        in CharacterSimulationState authoritative) => throw new NotImplementedException();

    /// <summary>
    /// Whether two contact normals describe the same surface within tolerance.
    /// </summary>
    /// <remarks>
    /// Separate because seam equivalence is the case that makes a naive
    /// comparison wrong: two colliders meeting at a seam report different
    /// normals for one continuous floor, and treating that as a support change
    /// would correct the player for walking across a join.
    /// </remarks>
    public bool IsSameSurface(SurfaceNormal predicted, SurfaceNormal authoritative) =>
        throw new NotImplementedException();
}

/// <summary>What the owner should do about a difference.</summary>
public enum OwnerCorrectionAction : byte
{
    /// <summary>Authority agreed. Prune history through this frame; no replay.</summary>
    Confirm = 1,

    /// <summary>Restore this frame and replay later commands through simulation.</summary>
    OrdinaryReplay = 2,

    /// <summary>
    /// Replay, but the divergence originated in collision. Distinguished because
    /// contact divergence tends to persist and because presentation must not
    /// smooth it — a character that authority says is on the other side of a
    /// wall has to actually go there.
    /// </summary>
    ContactReplay = 3,

    /// <summary>
    /// History cannot serve this frame. Snap to the authoritative state and
    /// restart prediction from it, with one enumerated reason.
    /// </summary>
    HardRebase = 4,
}

/// <summary>Why a hard rebase was required. Exactly one applies.</summary>
public enum OwnerRebaseReason : byte
{
    None = 0,

    /// <summary>The frame is older than retained history.</summary>
    HistoryMiss = 1,

    /// <summary>The authority epoch changed; retained frames belong to a dead timeline.</summary>
    EpochChanged = 2,

    /// <summary>Replay hit unrecoverable penetration and cannot continue from state.</summary>
    UnrecoverablePenetration = 3,

    /// <summary>Divergence so large that replaying it would look worse than snapping.</summary>
    ExtremeDivergence = 4,

    /// <summary>Authority explicitly demanded a rebase.</summary>
    AuthorityDemanded = 5,
}

/// <summary>The decision, with everything needed to explain it.</summary>
public readonly record struct OwnerCorrectionDecision
{
    internal OwnerCorrectionDecision(
        OwnerCorrectionAction action,
        OwnerRebaseReason rebaseReason,
        OwnerStateDifference difference,
        SimulationInstant frame) => throw new NotImplementedException();

    public OwnerCorrectionAction Action { get; }
    public OwnerRebaseReason RebaseReason { get; }
    public OwnerStateDifference Difference { get; }
    public SimulationInstant Frame { get; }
    public bool RequiresReplay => throw new NotImplementedException();
}

/// <summary>
/// Turns a comparison into a decision.
/// </summary>
/// <remarks>
/// <para>
/// The rule this type exists to enforce: a correction is a claim that the
/// owner's simulation was wrong, and it must be justified by a named,
/// classified difference. A hash mismatch alone never reaches
/// <see cref="OwnerCorrectionAction.OrdinaryReplay"/> — it is recorded as
/// diagnostic and nothing else — because the owner cannot tell the player which
/// field was wrong and may be snapping them for arithmetic.
/// </para>
/// <para>
/// Every unnecessary correction is a visible snap the player did nothing to
/// earn, so the policy is deliberately biased toward confirming.
/// </para>
/// </remarks>
public sealed class OwnerReconciliationPolicy
{
    public OwnerReconciliationPolicy(
        OwnerReconciliationComparer comparer,
        double extremeDivergenceMetres) => throw new NotImplementedException();

    public OwnerCorrectionDecision Decide(
        in OwnerPredictedFrame predicted,
        in CharacterSimulationState authoritative,
        SimulationInstant frame) => throw new NotImplementedException();

    /// <summary>
    /// The decision when history cannot serve the frame at all.
    /// </summary>
    /// <remarks>
    /// Separate entry point because there is nothing to compare: the caller
    /// already knows the lookup failed, and the reason it failed is the reason
    /// for the rebase.
    /// </remarks>
    public OwnerCorrectionDecision DecideWithoutHistory(
        OwnerHistoryLookupDecision lookup,
        SimulationInstant frame) => throw new NotImplementedException();
}
