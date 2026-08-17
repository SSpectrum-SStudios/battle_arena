using BattleArena.Core.Common;
using BattleArena.Core.Movement;
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

    /// <summary>
    /// Quantization step for positions and velocities, in metres or metres/second.
    /// </summary>
    /// <remarks>
    /// A millimetre. Hashing raw doubles would make the hash differ whenever the
    /// last bit of arithmetic differed, which is every frame between two machines
    /// — so a raw hash would report a mismatch continuously and mean nothing.
    /// Quantizing makes agreement possible while staying far finer than any
    /// correction tolerance, so a real divergence still changes the value.
    /// </remarks>
    public const double QuantizationStep = 1e-3d;

    public CanonicalMovementStateHash(uint schemaVersion, ulong value)
    {
        Schema = schemaVersion;
        Value = value;
    }

    public uint Schema { get; }
    public ulong Value { get; }

    /// <remarks>
    /// A default hash is not valid, so an unset field cannot be mistaken for a
    /// computed one that happened to be zero.
    /// </remarks>
    public bool IsValid => Schema != 0;

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
        in CombatantAuthorityPredictionEpoch epoch)
    {
        Span<byte> canonical = stackalloc byte[MaximumCanonicalBytes];
        var written = WriteCanonicalBytes(state, epoch, canonical);

        var hash = 14695981039346656037UL;
        foreach (var value in canonical[..written])
        {
            hash ^= value;
            hash *= 1099511628211UL;
        }

        return new CanonicalMovementStateHash(SchemaVersion, hash);
    }

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
        Span<byte> destination)
    {
        if (destination.Length < MaximumCanonicalBytes)
        {
            throw new ArgumentException(
                $"The canonical encoding needs {MaximumCanonicalBytes} bytes.",
                nameof(destination));
        }

        var offset = 0;

        // Schema first, so a version change alters every hash rather than only
        // those whose fields happened to move.
        WriteUInt32(destination, ref offset, SchemaVersion);

        // Epoch. Included because two states can be numerically identical and still
        // belong to different lives or timelines, and comparing those is meaningless.
        WriteUInt64(destination, ref offset, epoch.MatchFrameEpoch.Value);
        WriteInt64(destination, ref offset, epoch.Life.Value);
        WriteUInt64(destination, ref offset, epoch.AuthorityDiscontinuity.Value);
        WriteUInt64(destination, ref offset, epoch.OwnerControl.Value);

        WriteInt64(destination, ref offset, state.Frame.Tick);

        var kinematic = state.Kinematic;
        WriteQuantized(destination, ref offset, kinematic.Position.X);
        WriteQuantized(destination, ref offset, kinematic.Position.Y);
        WriteQuantized(destination, ref offset, kinematic.Position.Z);
        WriteQuantized(destination, ref offset, kinematic.HorizontalVelocity.X);
        WriteQuantized(destination, ref offset, kinematic.HorizontalVelocity.Z);
        WriteQuantized(destination, ref offset, kinematic.VerticalVelocity);

        destination[offset++] = kinematic.IsGrounded ? (byte)1 : (byte)0;
        WriteUInt64(destination, ref offset, kinematic.Support.ColliderId);
        WriteInt32(destination, ref offset, kinematic.Support.ShapeIndex);

        destination[offset++] = (byte)state.LocomotionMode;
        destination[offset++] = (byte)state.PostureMode;
        destination[offset++] = (byte)state.JumpPhase;
        destination[offset++] = (byte)state.Profile.Current;

        // Contacts in canonical order, never storage order. The buffer's own
        // equality is positional and its order is the motor's insertion order, so
        // hashing as stored would make two endpoints that agree about a corner
        // report different hashes — which the comparer classifies as
        // DiagnosticHashOnly and documents as "a bug to investigate". P06-12 would
        // spend its first week investigating a non-bug on every corner frame.
        Span<FrameContactRecord> contacts = stackalloc FrameContactRecord[FrameContactBuffer.Capacity];
        var contactCount = state.Contacts.CopyCanonical(contacts);
        destination[offset++] = (byte)contactCount;
        for (var index = 0; index < contactCount; index++)
        {
            var contact = contacts[index];
            WriteUInt64(destination, ref offset, contact.Collider.ColliderId);
            WriteInt32(destination, ref offset, contact.Collider.ShapeIndex);
            destination[offset++] = (byte)contact.SurfaceKind;
        }

        return offset;
    }

    private static void WriteQuantized(Span<byte> destination, ref int offset, double value)
    {
        // Away-from-zero rounding so the quantization is symmetric: banker's
        // rounding would map +0.0005 and -0.0005 to different magnitudes and make
        // the encoding depend on which side of the origin a character stood.
        var quantized = double.IsFinite(value)
            ? (long)Math.Round(value / QuantizationStep, MidpointRounding.AwayFromZero)
            : long.MinValue;
        WriteInt64(destination, ref offset, quantized);
    }

    private static void WriteInt64(Span<byte> destination, ref int offset, long value) =>
        WriteUInt64(destination, ref offset, unchecked((ulong)value));

    private static void WriteUInt64(Span<byte> destination, ref int offset, ulong value)
    {
        // Little-endian explicitly, not BitConverter, so the encoding does not
        // depend on the machine's endianness. Two endpoints on different
        // architectures must produce identical bytes or the hash compares nothing.
        for (var shift = 0; shift < 64; shift += 8)
        {
            destination[offset++] = (byte)(value >> shift);
        }
    }

    private static void WriteInt32(Span<byte> destination, ref int offset, int value) =>
        WriteUInt32(destination, ref offset, unchecked((uint)value));

    private static void WriteUInt32(Span<byte> destination, ref int offset, uint value)
    {
        for (var shift = 0; shift < 32; shift += 8)
        {
            destination[offset++] = (byte)(value >> shift);
        }
    }
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
        OwnerCorrectionError error)
    {
        Kind = kind;
        Field = field;
        Error = error;
    }

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
    /// <remarks>
    /// <see cref="OwnerStateDifferenceKind.DiagnosticHashOnly"/> deliberately does
    /// <em>not</em> require attention. Every compared field agreed, so the hash
    /// covering something the comparer does not is a bug to investigate rather than
    /// a player to snap.
    /// </remarks>
    public bool RequiresAttention =>
        Kind is OwnerStateDifferenceKind.NumericBeyondTolerance
            or OwnerStateDifferenceKind.DiscreteMismatch;

    /// <summary>Whether the differing field is a contact or support fact.</summary>
    public bool IsContactDivergence =>
        Field is OwnerMismatchField.Contact or OwnerMismatchField.Support;

    /// <summary>
    /// Whether the differing field is a grounding fact —
    /// <see cref="OwnerMismatchField.Grounded"/>,
    /// <see cref="OwnerMismatchField.Support"/>, or
    /// <see cref="OwnerMismatchField.Contact"/>.
    /// </summary>
    /// <remarks>
    /// Separated from numeric drift because P5B found what a motor bug reaching
    /// reconciliation actually looks like: the step-solver blocker flickered
    /// exactly these three fields on every frame of a step approach while position
    /// stayed plausible. A correction storm on a staircase should therefore name
    /// grounding rather than position, so the next person to see one goes looking
    /// at the motor instead of at tolerances.
    /// </remarks>
    public bool IsGroundingDivergence =>
        Field is OwnerMismatchField.Grounded
            or OwnerMismatchField.Support
            or OwnerMismatchField.Contact;

    public static OwnerStateDifference None => new(
        OwnerStateDifferenceKind.None,
        OwnerMismatchField.None,
        OwnerCorrectionError.Zero);
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

    public bool IsValid =>
        HorizontalPositionMetres > 0d && double.IsFinite(HorizontalPositionMetres) &&
        VerticalPositionMetres > 0d && double.IsFinite(VerticalPositionMetres) &&
        VelocityMetresPerSecond > 0d && double.IsFinite(VelocityMetresPerSecond) &&
        FacingRadians > 0d && double.IsFinite(FacingRadians) &&
        ContactNormalRadians > 0d && double.IsFinite(ContactNormalRadians) &&
        ExtremeDivergenceMetres > HorizontalPositionMetres &&
        double.IsFinite(ExtremeDivergenceMetres);

    /// <summary>
    /// Starting values, chosen against measurements rather than intuition.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Horizontal 5 cm and vertical 3 cm. For scale, P5B-02 measured the explicit
    /// and legacy motors agreeing to 2.6 cm over thirty frames at their best
    /// alignment, and the motor's own surface skin is 5 mm — so these sit above
    /// reproducible arithmetic noise and well below anything a player would see as
    /// a snap.
    /// </para>
    /// <para>
    /// Vertical is tighter than horizontal because it is more visible: a few
    /// centimetres of height error near a jump apex or on a step reads as floating
    /// or sinking, while the same error in plan does not read at all.
    /// </para>
    /// <para>
    /// Extreme divergence at 2 m — roughly a third of a second of running at top
    /// speed. Past that, replaying looks worse than snapping because the character
    /// visibly retraces a path it never took.
    /// </para>
    /// </remarks>
    public static OwnerReconciliationTolerances Default => new()
    {
        HorizontalPositionMetres = 0.05d,
        VerticalPositionMetres = 0.03d,
        VelocityMetresPerSecond = 0.5d,
        FacingRadians = 0.05d,
        ContactNormalRadians = 0.15d,
        ExtremeDivergenceMetres = 2d,
    };
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
/// <para>
/// <b>Two constraints from P5B that this must honour.</b>
/// </para>
/// <para>
/// First, <em>contacts are compared as a set, never as a sequence.</em> The Godot
/// adapter reports one shared travel fraction for every contact of a sweep, so
/// <c>CollisionContactState.CompareForStableResolution</c>'s primary key is
/// constant in-engine and the real ordering falls through to a raw
/// physics-server RID. Two processes can order the same corner's contacts
/// differently for reasons that have nothing to do with simulation, so an
/// ordered comparison would report a divergence on geometry both sides agree
/// about. This is a workaround for a recorded adapter defect rather than the
/// desired end state — see P5B-01's finding 1.
/// </para>
/// <para>
/// Second, <em>frame identity is asserted, not assumed.</em> The explicit and
/// legacy motors were measured one frame out of phase, and a phase error here
/// would read as a divergence on every single frame and correct the player
/// continuously — which is indistinguishable from a tolerance set too tight, and
/// so among the most expensive mistakes available. Comparing states from
/// different frames is a caller bug and must fault rather than produce a
/// difference.
/// </para>
/// </remarks>
public sealed class OwnerReconciliationComparer
{
    public OwnerReconciliationComparer(OwnerReconciliationTolerances tolerances)
    {
        if (!tolerances.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(tolerances));
        }

        Tolerances = tolerances;
    }

    public OwnerReconciliationTolerances Tolerances { get; }

    /// <summary>
    /// Whether two frames' contact sets describe the same surfaces, ignoring order.
    /// </summary>
    /// <remarks>
    /// Keyed by collider identity and surface kind. Bounded at
    /// <c>FrameContactBuffer.Capacity</c> (4), so the set comparison is a nested
    /// scan rather than an allocation — this runs on a path documented as
    /// allocation-free, and a HashSet here would allocate per compared frame.
    /// </remarks>
    public bool ContactsDescribeTheSameSurfaces(
        in FrameContactBuffer predicted,
        in FrameContactBuffer authoritative) =>
        predicted.DescribesSameContacts(authoritative);

    /// <param name="authoritativeHash">
    /// The hash the authority reported for this frame, when it sent one. Compared
    /// only after every field agreed, so a mismatch here yields
    /// <see cref="OwnerStateDifferenceKind.DiagnosticHashOnly"/> and never a
    /// correction. The parameter exists now, before the wire carries it in
    /// P06-08, so the shape does not have to change later.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The two states are for different frames. This is the phase-error guard: it
    /// faults rather than returning a difference, because a difference would be
    /// acted on and a fault will not be. See the class remarks.
    /// </exception>
    public OwnerStateDifference Compare(
        in CharacterSimulationState predicted,
        in CharacterSimulationState authoritative,
        in CombatantAuthorityPredictionEpoch epoch,
        CanonicalMovementStateHash? authoritativeHash)
    {
        // The phase guard. Comparing states from different frames is a caller bug,
        // and it must fault rather than produce a difference: a difference would be
        // acted on, and a one-frame phase error anywhere in this path reads as a
        // divergence on every frame and corrects the player continuously —
        // indistinguishable from a tolerance set too tight. P5B-02 measured the two
        // motors exactly one frame out of phase, so this is a live hazard.
        if (predicted.Frame != authoritative.Frame)
        {
            throw new ArgumentException(
                $"Predicted frame {predicted.Frame.Tick} cannot be compared against authority " +
                $"frame {authoritative.Frame.Tick}.",
                nameof(authoritative));
        }

        var horizontal = Math.Sqrt(
            Square(predicted.Kinematic.Position.X - authoritative.Kinematic.Position.X) +
            Square(predicted.Kinematic.Position.Z - authoritative.Kinematic.Position.Z));
        var vertical = Math.Abs(
            predicted.Kinematic.Position.Y - authoritative.Kinematic.Position.Y);
        var velocity = Math.Sqrt(
            Square(predicted.Kinematic.HorizontalVelocity.X -
                   authoritative.Kinematic.HorizontalVelocity.X) +
            Square(predicted.Kinematic.HorizontalVelocity.Z -
                   authoritative.Kinematic.HorizontalVelocity.Z) +
            Square(predicted.Kinematic.VerticalVelocity -
                   authoritative.Kinematic.VerticalVelocity));
        var facing = Math.Abs(
            MovementMath.WrapAngle(predicted.Kinematic.FacingYawRadians - authoritative.Kinematic.FacingYawRadians));

        var error = new OwnerCorrectionError(horizontal, vertical, velocity, facing, 0d);

        // Discrete facts first, and deliberately so: a character standing on a
        // different surface is a bigger statement than one standing a centimetre
        // away, and reporting the centimetre would hide it. This ordering is also
        // what makes a motor bug diagnosable — P5B's step defect flickered exactly
        // these fields while position stayed plausible.
        var discrete = FirstDiscreteMismatch(predicted, authoritative);
        if (discrete != OwnerMismatchField.None)
        {
            return new OwnerStateDifference(
                OwnerStateDifferenceKind.DiscreteMismatch, discrete, error);
        }

        if (horizontal > Tolerances.HorizontalPositionMetres)
        {
            return new OwnerStateDifference(
                OwnerStateDifferenceKind.NumericBeyondTolerance,
                OwnerMismatchField.HorizontalPosition,
                error);
        }

        if (vertical > Tolerances.VerticalPositionMetres)
        {
            return new OwnerStateDifference(
                OwnerStateDifferenceKind.NumericBeyondTolerance,
                OwnerMismatchField.VerticalPosition,
                error);
        }

        if (velocity > Tolerances.VelocityMetresPerSecond)
        {
            return new OwnerStateDifference(
                OwnerStateDifferenceKind.NumericBeyondTolerance,
                OwnerMismatchField.LinearVelocity,
                error);
        }

        if (facing > Tolerances.FacingRadians)
        {
            return new OwnerStateDifference(
                OwnerStateDifferenceKind.NumericBeyondTolerance,
                OwnerMismatchField.Facing,
                error);
        }

        var withinTolerance = horizontal > 0d || vertical > 0d || velocity > 0d || facing > 0d;

        // The hash is consulted only after every compared field agreed, so a
        // mismatch here can never produce a correction. It means the hashed field
        // set covers something the comparer does not, which is a bug to investigate
        // rather than a player to snap.
        if (authoritativeHash is { } reported &&
            reported.IsValid &&
            reported.Schema == CanonicalMovementStateHash.SchemaVersion)
        {
            var local = CanonicalMovementStateHash.Compute(predicted, epoch);
            if (local.Value != reported.Value)
            {
                return new OwnerStateDifference(
                    OwnerStateDifferenceKind.DiagnosticHashOnly,
                    OwnerMismatchField.None,
                    error);
            }
        }

        return withinTolerance
            ? new OwnerStateDifference(
                OwnerStateDifferenceKind.WithinTolerance, OwnerMismatchField.None, error)
            : OwnerStateDifference.None;
    }

    /// <summary>
    /// The first discrete disagreement in canonical order, or none.
    /// </summary>
    /// <remarks>
    /// Grounding facts lead because they are the ones a motor defect moves, and
    /// because standing on a different surface explains a positional difference
    /// rather than being explained by it.
    /// </remarks>
    private OwnerMismatchField FirstDiscreteMismatch(
        in CharacterSimulationState predicted,
        in CharacterSimulationState authoritative)
    {
        if (predicted.Kinematic.IsGrounded != authoritative.Kinematic.IsGrounded)
        {
            return OwnerMismatchField.Grounded;
        }

        // Support is compared only while grounded. An airborne character's support
        // is meaningless, and comparing it would manufacture a mismatch out of two
        // different flavours of "nothing".
        if (predicted.Kinematic.IsGrounded &&
            predicted.Kinematic.Support != authoritative.Kinematic.Support)
        {
            return OwnerMismatchField.Support;
        }

        if (predicted.Kinematic.IsGrounded &&
            !IsSameSurface(
                predicted.Kinematic.GroundNormal, authoritative.Kinematic.GroundNormal))
        {
            return OwnerMismatchField.SurfaceBehavior;
        }

        if (!ContactsDescribeTheSameSurfaces(predicted.Contacts, authoritative.Contacts))
        {
            return OwnerMismatchField.Contact;
        }

        if (predicted.LocomotionMode != authoritative.LocomotionMode)
        {
            return OwnerMismatchField.Locomotion;
        }

        if (predicted.PostureMode != authoritative.PostureMode)
        {
            return OwnerMismatchField.Posture;
        }

        if (predicted.JumpPhase != authoritative.JumpPhase)
        {
            return OwnerMismatchField.JumpState;
        }

        return predicted.Profile.Current != authoritative.Profile.Current
            ? OwnerMismatchField.CollisionProfile
            : OwnerMismatchField.None;
    }

    private static double Square(double value) => value * value;

    /// <summary>
    /// Whether two contact normals describe the same surface within tolerance.
    /// </summary>
    /// <remarks>
    /// Separate because seam equivalence is the case that makes a naive
    /// comparison wrong: two colliders meeting at a seam report different
    /// normals for one continuous floor, and treating that as a support change
    /// would correct the player for walking across a join.
    /// </remarks>
    public bool IsSameSurface(SurfaceNormal predicted, SurfaceNormal authoritative)
    {
        var dot = Math.Clamp(
            (predicted.X * authoritative.X) +
            (predicted.Y * authoritative.Y) +
            (predicted.Z * authoritative.Z),
            -1d,
            1d);
        return Math.Acos(dot) <= Tolerances.ContactNormalRadians;
    }
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
        SimulationInstant frame)
    {
        Disposition = disposition;
        Reason = reason;
        Difference = difference;
        Frame = frame;
    }

    public OwnerCorrectionDisposition Disposition { get; }
    public OwnerCorrectionReason Reason { get; }
    public OwnerStateDifference Difference { get; }
    public SimulationInstant Frame { get; }

    public bool RequiresReplay =>
        Disposition is OwnerCorrectionDisposition.OrdinaryReplay
            or OwnerCorrectionDisposition.ContactReplay;

    public bool RequiresRebase => Disposition == OwnerCorrectionDisposition.HardRebase;
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
    private readonly OwnerReconciliationTolerances _tolerances;

    public OwnerReconciliationPolicy(OwnerReconciliationTolerances tolerances)
    {
        if (!tolerances.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(tolerances));
        }

        _tolerances = tolerances;
    }

    /// <param name="motionOutcome">
    /// What the motor did on the predicted frame. An unrecoverable penetration
    /// cannot be replayed from and becomes a rebase regardless of how small the
    /// state difference looks.
    /// </param>
    public OwnerCorrectionDecision Decide(
        in OwnerStateDifference difference,
        CapsuleMotionOutcome motionOutcome,
        SimulationInstant frame)
    {
        // An unrecoverable penetration cannot be replayed from: resimulating the
        // frame reproduces the same stuck state, so replay would burn the budget and
        // change nothing. Checked before the difference because it holds however
        // small the difference looks.
        if (motionOutcome == CapsuleMotionOutcome.UnrecoverablePenetration)
        {
            return new OwnerCorrectionDecision(
                OwnerCorrectionDisposition.HardRebase,
                OwnerCorrectionReason.UnrecoverablePenetration,
                difference,
                frame);
        }

        if (!difference.RequiresAttention)
        {
            return new OwnerCorrectionDecision(
                OwnerCorrectionDisposition.Confirmed,
                OwnerCorrectionReason.ConfirmedWithinTolerance,
                difference,
                frame);
        }

        // Past the extreme threshold, replaying looks worse than snapping: the
        // character visibly retraces a path it never took. Measured on total
        // positional error rather than per axis, because that is what the player
        // sees.
        var positional = Math.Sqrt(
            (difference.Error.HorizontalPositionMeters *
             difference.Error.HorizontalPositionMeters) +
            (difference.Error.VerticalPositionMeters *
             difference.Error.VerticalPositionMeters));
        if (positional > _tolerances.ExtremeDivergenceMetres)
        {
            return new OwnerCorrectionDecision(
                OwnerCorrectionDisposition.HardRebase,
                OwnerCorrectionReason.ExtremeError,
                difference,
                frame);
        }

        // Contact and support divergence replays as its own class so a correction
        // storm on a staircase names grounding rather than position, and whoever
        // sees one goes looking at the motor instead of at tolerances. That is
        // exactly the shape P5B's step defect had.
        return difference.IsContactDivergence
            ? new OwnerCorrectionDecision(
                OwnerCorrectionDisposition.ContactReplay,
                OwnerCorrectionReason.ContactDivergence,
                difference,
                frame)
            : new OwnerCorrectionDecision(
                OwnerCorrectionDisposition.OrdinaryReplay,
                OwnerCorrectionReason.OrdinaryStateDivergence,
                difference,
                frame);
    }

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
        SimulationInstant frame)
    {
        if (lookup == OwnerHistoryLookupDecision.Found)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lookup),
                lookup,
                "History served the frame, so the comparing entry point applies.");
        }

        // NotYetSimulated is not a correction at all: authority is ahead of the
        // owner, there is nothing yet to compare, and the answer should be queued.
        // Rebasing on it would snap the player backwards for being behind.
        if (lookup == OwnerHistoryLookupDecision.NotYetSimulated)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lookup),
                lookup,
                "A frame the owner has not simulated is queued, not corrected.");
        }

        return new OwnerCorrectionDecision(
            OwnerCorrectionDisposition.HardRebase,
            OwnerCorrectionReason.HistoryMiss,
            OwnerStateDifference.None,
            frame);
    }

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
        SimulationInstant frame)
    {
        // Only the four lifecycle causes. A critic required these stay separate
        // when P01-02 was built, because collapsing them into one "epoch changed"
        // makes a respawn indistinguishable from a control handover in a trace, and
        // those have completely different explanations.
        if (lifecycleReason is not (OwnerCorrectionReason.MatchFrameEpochChanged
            or OwnerCorrectionReason.LifeEpochChanged
            or OwnerCorrectionReason.AuthorityDiscontinuityChanged
            or OwnerCorrectionReason.OwnerControlEpochChanged))
        {
            throw new ArgumentOutOfRangeException(
                nameof(lifecycleReason),
                lifecycleReason,
                "Only a lifecycle identity change is decided here.");
        }

        return new OwnerCorrectionDecision(
            OwnerCorrectionDisposition.HardRebase,
            lifecycleReason,
            OwnerStateDifference.None,
            frame);
    }
}
