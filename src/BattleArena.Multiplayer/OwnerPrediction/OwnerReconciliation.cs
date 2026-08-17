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
/// job is to make a real divergence cheap to <em>detect</em> in a trace, after
/// which <see cref="OwnerReconciliationComparer"/> names the field.
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

    /// <summary>Upper bound on the canonical encoding, so a caller can size a buffer.</summary>
    public const int MaximumCanonicalBytes = 256;

    public CanonicalMovementStateHash(uint schemaVersion, ulong value) =>
        throw new NotImplementedException();

    public uint Schema { get; }
    public ulong Value { get; }
    public bool IsValid => throw new NotImplementedException();

    /// <summary>
    /// Hashes the prediction-relevant fields of a state, in fixed order.
    /// </summary>
    /// <param name="epoch">
    /// The authority epoch the state belongs to. Required because the design's
    /// hashed field set includes match-frame epoch, life, authority
    /// discontinuity, and owner control — and
    /// <see cref="CharacterSimulationState"/> deliberately excludes the last two,
    /// since Phase 4's epoch gate owns that scoping.
    /// </param>
    public static CanonicalMovementStateHash Compute(
        in CharacterSimulationState state,
        in CombatantAuthorityPredictionEpoch epoch) => throw new NotImplementedException();

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
        in CombatantAuthorityPredictionEpoch epoch,
        Span<byte> destination) => throw new NotImplementedException();
}

/// <summary>Which class of difference the comparer found.</summary>
/// <remarks>
/// The distinction that matters is between differences that are <em>discrete</em>
/// — a different support surface, collision profile, or jump phase — and
/// differences that are numeric. A discrete mismatch means the two simulations
/// disagree about what happened; a numeric one may just be arithmetic.
/// </remarks>
public enum OwnerStateDifferenceKind : byte
{
    /// <summary>Identical within every tolerance. Nothing to do but prune.</summary>
    None = 1,

    /// <summary>Numeric drift inside the authored tolerance.</summary>
    WithinTolerance = 2,

    /// <summary>
    /// Every compared field agreed, but the authority's reported hash did not
    /// match. Real, unattributable, and explicitly not grounds for correction:
    /// it means the hashed field set covers something the comparer does not, and
    /// that is a bug to investigate rather than a player to snap.
    /// </summary>
    DiagnosticHashOnly = 3,

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
/// The field is <see cref="OwnerMismatchField"/> rather than a string for three
/// reasons: the policy must decide contact-versus-ordinary replay from it
/// structurally, <see cref="OwnerCorrectionTelemetry"/> already requires that
/// enum, and comparison runs on a path documented as allocation-free.
/// </remarks>
public readonly record struct OwnerStateDifference
{
    internal OwnerStateDifference(
        OwnerStateDifferenceKind kind,
        OwnerMismatchField field,
        OwnerCorrectionError error) => throw new NotImplementedException();

    public OwnerStateDifferenceKind Kind { get; }

    /// <summary>The first differing field in canonical order.</summary>
    public OwnerMismatchField Field { get; }

    /// <summary>
    /// Per-axis magnitudes. Separate axes because a ten-centimetre vertical error
    /// near a jump apex is far more visible than the same error in plan, and
    /// because telemetry records them separately.
    /// </summary>
    public OwnerCorrectionError Error { get; }

    /// <summary>Whether this difference is a candidate for correction at all.</summary>
    public bool RequiresAttention => throw new NotImplementedException();

    /// <summary>Whether the differing field is a contact or support fact.</summary>
    public bool IsContactDivergence => throw new NotImplementedException();

    public static OwnerStateDifference None => throw new NotImplementedException();
}

/// <summary>Authored tolerances for owner/authority comparison.</summary>
/// <remarks>
/// These are content: too tight and the owner corrects constantly on arithmetic
/// noise, too loose and real divergence is invisible until it is large. Carried
/// per revision for the same reason the motor policy is. Horizontal and vertical
/// are separate because they are not equally visible.
/// </remarks>
public readonly record struct OwnerReconciliationTolerances
{
    public double HorizontalPositionMetres { get; init; }
    public double VerticalPositionMetres { get; init; }
    public double VelocityMetresPerSecond { get; init; }
    public double FacingRadians { get; init; }

    /// <summary>
    /// How far a contact normal may differ and still count as the same surface.
    /// Seams between two authored colliders legitimately report slightly
    /// different normals for what is one flat floor.
    /// </summary>
    public double ContactNormalRadians { get; init; }

    /// <summary>
    /// Beyond this, replaying would look worse than snapping, so the correction
    /// becomes a hard rebase with <see cref="OwnerCorrectionReason.ExtremeError"/>.
    /// </summary>
    public double ExtremeDivergenceMetres { get; init; }

    public bool IsValid => throw new NotImplementedException();
    public static OwnerReconciliationTolerances Default => throw new NotImplementedException();
}

/// <summary>
/// Compares the owner's predicted post-state against the authority's answer for
/// the same frame.
/// </summary>
/// <remarks>
/// Comparison is ordered and short-circuits on the first difference that
/// matters, so the reported field is always the <em>first</em> divergence in
/// canonical order rather than an arbitrary one — which is what P06-12's
/// separate-process parity needs to identify an exact first diverging field.
/// Discrete facts are checked before numeric ones: a character standing on a
/// different surface is a bigger statement than one standing a centimetre away,
/// and reporting the centimetre would hide it.
/// </remarks>
public sealed class OwnerReconciliationComparer
{
    public OwnerReconciliationComparer(OwnerReconciliationTolerances tolerances) =>
        throw new NotImplementedException();

    public OwnerReconciliationTolerances Tolerances => throw new NotImplementedException();

    /// <param name="authoritativeHash">
    /// The hash the authority reported for this frame, when it sent one. Compared
    /// only after every field agreed, so a mismatch here yields
    /// <see cref="OwnerStateDifferenceKind.DiagnosticHashOnly"/> and never a
    /// correction. The parameter exists now, before the wire carries it in
    /// P06-08, so the shape does not have to change later.
    /// </param>
    public OwnerStateDifference Compare(
        in CharacterSimulationState predicted,
        in CharacterSimulationState authoritative,
        in CombatantAuthorityPredictionEpoch epoch,
        CanonicalMovementStateHash? authoritativeHash) => throw new NotImplementedException();

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

/// <summary>The decision, with everything needed to explain and record it.</summary>
/// <remarks>
/// Carries the Phase 1 taxonomy rather than a parallel one:
/// <see cref="OwnerCorrectionDisposition"/> and
/// <see cref="OwnerCorrectionReason"/> are what
/// <see cref="OwnerCorrectionTelemetry"/> records, and a second set of enums
/// would be two sources of truth for why a correction happened. In particular the
/// four lifecycle causes stay separate here because a critic required exactly
/// that separation when P01-02 was built.
/// </remarks>
public readonly record struct OwnerCorrectionDecision
{
    internal OwnerCorrectionDecision(
        OwnerCorrectionDisposition disposition,
        OwnerCorrectionReason reason,
        OwnerStateDifference difference,
        SimulationInstant frame) => throw new NotImplementedException();

    public OwnerCorrectionDisposition Disposition { get; }
    public OwnerCorrectionReason Reason { get; }
    public OwnerStateDifference Difference { get; }
    public SimulationInstant Frame { get; }
    public bool RequiresReplay => throw new NotImplementedException();
    public bool RequiresRebase => throw new NotImplementedException();
}

/// <summary>
/// Turns a comparison into a decision.
/// </summary>
/// <remarks>
/// <para>
/// The rule this type exists to enforce: a correction is a claim that the
/// owner's simulation was wrong, and it must be justified by a named, classified
/// difference.
/// </para>
/// <para>
/// That rule is structural rather than documented. <see cref="Decide"/> receives
/// an <see cref="OwnerStateDifference"/> and a motion outcome — never the
/// predicted frame — so it physically cannot read a canonical hash and cannot
/// correct on one. Every unnecessary correction is a visible snap the player did
/// nothing to earn, so the policy is deliberately biased toward confirming.
/// </para>
/// </remarks>
public sealed class OwnerReconciliationPolicy
{
    public OwnerReconciliationPolicy(OwnerReconciliationTolerances tolerances) =>
        throw new NotImplementedException();

    /// <param name="motionOutcome">
    /// What the motor did on the predicted frame. An unrecoverable penetration
    /// cannot be replayed from and becomes a rebase regardless of how small the
    /// state difference looks.
    /// </param>
    public OwnerCorrectionDecision Decide(
        in OwnerStateDifference difference,
        CapsuleMotionOutcome motionOutcome,
        SimulationInstant frame) => throw new NotImplementedException();

    /// <summary>
    /// The decision when history cannot serve the frame at all.
    /// </summary>
    /// <remarks>
    /// Separate entry point because there is nothing to compare: the caller
    /// already knows the lookup failed, and why it failed is why the rebase is
    /// required.
    /// </remarks>
    public OwnerCorrectionDecision DecideWithoutHistory(
        OwnerHistoryLookupDecision lookup,
        SimulationInstant frame) => throw new NotImplementedException();

    /// <summary>
    /// The decision when a lifecycle identity changed.
    /// </summary>
    /// <remarks>
    /// Takes the specific changed identity so the four lifecycle reasons stay
    /// distinct on the wire and in telemetry, rather than collapsing into one
    /// "epoch changed" that cannot be diagnosed.
    /// </remarks>
    public OwnerCorrectionDecision DecideForEpochChange(
        OwnerCorrectionReason lifecycleReason,
        SimulationInstant frame) => throw new NotImplementedException();
}
