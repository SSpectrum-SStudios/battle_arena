using BattleArena.Core.Common;

namespace BattleArena.Multiplayer.OwnerPrediction;

/// <summary>The simulation response chosen for one authority observation.</summary>
public enum OwnerCorrectionDisposition
{
    Confirmed = 0,
    OrdinaryReplay = 1,
    ContactReplay = 2,
    HardRebase = 3,
}

/// <summary>
/// The stable reason for one authority observation. Lifecycle identities remain
/// separate, and a configuration miss is not terminal until its bounded policy
/// has been exhausted.
/// </summary>
public enum OwnerCorrectionReason
{
    ConfirmedWithinTolerance = 0,
    OrdinaryStateDivergence = 1,
    ContactDivergence = 2,
    HistoryMiss = 3,
    MatchFrameEpochChanged = 4,
    LifeEpochChanged = 5,
    AuthorityDiscontinuityChanged = 6,
    OwnerControlEpochChanged = 7,
    ConfigurationHistoryPolicyExhausted = 8,
    UnrecoverablePenetration = 9,
    ExtremeError = 10,

    /// <summary>
    /// P06-A4: the owner and authority disagreed, the difference was ordinary, but
    /// the frame was too far back to replay within
    /// <c>OwnerPredictionWorkPolicy.MaximumReplayFrames</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Distinct from every existing reason, and none of them can stand in.
    /// <see cref="HistoryMiss"/> is wrong because history <em>had</em> the frame and
    /// a comparison happened; <see cref="ExtremeError"/> is wrong because the
    /// difference may be two centimetres and calling that extreme puts a false
    /// statement in the trace. Without this value the most common correction at
    /// real latency has no honest name, and the telemetry factories would throw on
    /// it either way.
    /// </para>
    /// <para>
    /// Also the signal that the replay budget is mis-tuned rather than that the
    /// network is bad: a rising rate here means the depth cap is below the
    /// prediction lead, which is what P06-A3 exists to retire.
    /// </para>
    /// </remarks>
    ReplayDepthExceeded = 11,
}

/// <summary>
/// The first canonical field whose comparison failed. <see cref="None"/> is
/// reserved for confirmation and for cases where no same-frame comparison was
/// possible.
/// </summary>
public enum OwnerMismatchField
{
    None = 0,
    HorizontalPosition = 1,
    VerticalPosition = 2,
    LinearVelocity = 3,
    Facing = 4,
    Contact = 5,
    Grounded = 6,
    Locomotion = 7,
    Posture = 8,
    JumpState = 9,
    RollState = 10,
    ActionState = 11,
    CollisionProfile = 12,
    Support = 13,
    SurfaceBehavior = 14,
    MovementSources = 15,
    TransitionCursor = 16,
    MatchFrameEpoch = 17,
    LifeEpoch = 18,
    AuthorityDiscontinuity = 19,
    OwnerControlEpoch = 20,
    MovementRevision = 21,
    CapabilityRevision = 22,
}

/// <summary>
/// Non-negative magnitudes comparing two movement states. Units are explicit so
/// traces cannot silently mix coordinate or angle units.
/// </summary>
public readonly record struct OwnerCorrectionError
{
    public static OwnerCorrectionError Zero => default;

    public OwnerCorrectionError(
        double horizontalPositionMeters,
        double verticalPositionMeters,
        double linearVelocityMetersPerSecond,
        double facingRadians,
        double contactNormalRadians)
    {
        HorizontalPositionMeters = ValidateMagnitude(
            horizontalPositionMeters,
            nameof(horizontalPositionMeters));
        VerticalPositionMeters = ValidateMagnitude(
            verticalPositionMeters,
            nameof(verticalPositionMeters));
        LinearVelocityMetersPerSecond = ValidateMagnitude(
            linearVelocityMetersPerSecond,
            nameof(linearVelocityMetersPerSecond));
        FacingRadians = ValidateMagnitude(facingRadians, nameof(facingRadians));
        ContactNormalRadians = ValidateMagnitude(
            contactNormalRadians,
            nameof(contactNormalRadians));
    }

    public double HorizontalPositionMeters { get; }

    public double VerticalPositionMeters { get; }

    public double LinearVelocityMetersPerSecond { get; }

    public double FacingRadians { get; }

    public double ContactNormalRadians { get; }

    public bool IsZero => this == Zero;

    private static double ValidateMagnitude(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0d)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Correction error magnitudes must be finite and non-negative.");
        }

        return value;
    }
}

/// <summary>
/// An optional same-frame comparison between local prediction and authority at
/// the authority frame being reconciled. It is absent when history or an exact
/// configuration is unavailable, or an epoch makes comparison invalid.
/// </summary>
public readonly record struct AuthorityFrameErrorMeasurement(
    SimulationInstant Frame,
    OwnerCorrectionError Error);

/// <summary>
/// The state delta actually applied by reconciliation. Ordinary replay compares
/// the old and corrected state at the same current frame. A hard rebase may move
/// to another frame, so both references are retained and the value is never
/// mislabeled as authority-relative residual error.
/// </summary>
public readonly record struct AppliedCorrectionDelta(
    SimulationInstant BeforeFrame,
    SimulationInstant AfterFrame,
    OwnerCorrectionError Error);

/// <summary>
/// One immutable reconciliation observation. Thresholds belong to the later
/// reconciliation policy; this type records and validates its resulting
/// classification.
/// </summary>
public readonly record struct OwnerCorrectionTelemetry
{
    private OwnerCorrectionTelemetry(
        OwnerCorrectionDisposition disposition,
        OwnerCorrectionReason reason,
        AuthorityFrameErrorMeasurement? authorityFrameErrorBefore,
        AppliedCorrectionDelta appliedCorrection,
        long replayDepth,
        OwnerMismatchField firstMismatch)
    {
        Disposition = disposition;
        Reason = reason;
        AuthorityFrameErrorBefore = authorityFrameErrorBefore;
        AppliedCorrection = appliedCorrection;
        ReplayDepth = replayDepth;
        FirstMismatch = firstMismatch;
        IsInitialized = true;
    }

    public OwnerCorrectionDisposition Disposition { get; }

    public OwnerCorrectionReason Reason { get; }

    public AuthorityFrameErrorMeasurement? AuthorityFrameErrorBefore { get; }

    public AppliedCorrectionDelta AppliedCorrection { get; }

    public long ReplayDepth { get; }

    public OwnerMismatchField FirstMismatch { get; }

    /// <summary>False only for the unusable all-default struct value.</summary>
    public bool IsInitialized { get; }

    public static OwnerCorrectionTelemetry Create(
        OwnerCorrectionReason reason,
        AuthorityFrameErrorMeasurement? authorityFrameErrorBefore,
        AppliedCorrectionDelta appliedCorrection,
        OwnerMismatchField firstMismatch)
    {
        var disposition = Classify(reason);
        ValidateMismatch(firstMismatch);
        var replayDepth = ValidateObservation(
            disposition,
            reason,
            authorityFrameErrorBefore,
            appliedCorrection,
            firstMismatch);

        return new OwnerCorrectionTelemetry(
            disposition,
            reason,
            authorityFrameErrorBefore,
            appliedCorrection,
            replayDepth,
            firstMismatch);
    }

    private static long ValidateObservation(
        OwnerCorrectionDisposition disposition,
        OwnerCorrectionReason reason,
        AuthorityFrameErrorMeasurement? authorityFrameErrorBefore,
        AppliedCorrectionDelta appliedCorrection,
        OwnerMismatchField firstMismatch)
    {
        switch (disposition)
        {
            case OwnerCorrectionDisposition.Confirmed:
                RequireComparison(authorityFrameErrorBefore);
                RequireComparisonNotFuture(
                    authorityFrameErrorBefore!.Value,
                    appliedCorrection);
                RequireMismatch(firstMismatch, OwnerMismatchField.None);
                RequireZeroCorrection(appliedCorrection);
                return 0;

            case OwnerCorrectionDisposition.OrdinaryReplay:
            case OwnerCorrectionDisposition.ContactReplay:
                RequireComparison(authorityFrameErrorBefore);
                RequireNonEmptyMismatch(firstMismatch);
                RequireSameFrameCorrection(appliedCorrection);
                return CalculateReplayDepth(
                    authorityFrameErrorBefore!.Value,
                    appliedCorrection);

            case OwnerCorrectionDisposition.HardRebase:
                ValidateHardRebase(
                    reason,
                    authorityFrameErrorBefore,
                    appliedCorrection,
                    firstMismatch);
                return 0;

            default:
                throw new ArgumentOutOfRangeException(nameof(disposition), disposition, null);
        }
    }

    private static void ValidateHardRebase(
        OwnerCorrectionReason reason,
        AuthorityFrameErrorMeasurement? authorityFrameErrorBefore,
        AppliedCorrectionDelta appliedCorrection,
        OwnerMismatchField firstMismatch)
    {
        switch (reason)
        {
            case OwnerCorrectionReason.HistoryMiss:
            case OwnerCorrectionReason.ConfigurationHistoryPolicyExhausted:
                RequireNoComparison(authorityFrameErrorBefore);
                RequireMismatch(firstMismatch, OwnerMismatchField.None);
                RequireRebaseNotFuture(appliedCorrection);
                break;
            case OwnerCorrectionReason.MatchFrameEpochChanged:
                RequireNoComparison(authorityFrameErrorBefore);
                RequireMismatch(firstMismatch, OwnerMismatchField.MatchFrameEpoch);
                break;
            case OwnerCorrectionReason.LifeEpochChanged:
                RequireNoComparison(authorityFrameErrorBefore);
                RequireMismatch(firstMismatch, OwnerMismatchField.LifeEpoch);
                break;
            case OwnerCorrectionReason.AuthorityDiscontinuityChanged:
                RequireNoComparison(authorityFrameErrorBefore);
                RequireMismatch(firstMismatch, OwnerMismatchField.AuthorityDiscontinuity);
                break;
            case OwnerCorrectionReason.OwnerControlEpochChanged:
                RequireNoComparison(authorityFrameErrorBefore);
                RequireMismatch(firstMismatch, OwnerMismatchField.OwnerControlEpoch);
                break;
            case OwnerCorrectionReason.UnrecoverablePenetration:
                RequireComparison(authorityFrameErrorBefore);
                RequireMismatch(firstMismatch, OwnerMismatchField.Contact);
                RequireRebaseTargetsComparison(
                    authorityFrameErrorBefore!.Value,
                    appliedCorrection);
                break;
            case OwnerCorrectionReason.ExtremeError:
                RequireComparison(authorityFrameErrorBefore);
                RequireNonEmptyMismatch(firstMismatch);
                RequireNonZeroComparison(authorityFrameErrorBefore!.Value);
                RequireRebaseTargetsComparison(
                    authorityFrameErrorBefore!.Value,
                    appliedCorrection);
                break;
            case OwnerCorrectionReason.ReplayDepthExceeded:
                // A comparison happened and named a field, so this is not a
                // HistoryMiss. But deliberately NOT RequireNonZeroComparison: the
                // difference may be two centimetres, and that is exactly the case
                // — small error, frame too old to replay. Demanding a large error
                // here is what made ExtremeError unusable as a stand-in.
                RequireComparison(authorityFrameErrorBefore);
                RequireNonEmptyMismatch(firstMismatch);
                RequireRebaseTargetsComparison(
                    authorityFrameErrorBefore!.Value,
                    appliedCorrection);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(reason),
                    reason,
                    "The hard-rebase reason is not supported.");
        }
    }

    private static OwnerCorrectionDisposition Classify(OwnerCorrectionReason reason) =>
        reason switch
        {
            OwnerCorrectionReason.ConfirmedWithinTolerance =>
                OwnerCorrectionDisposition.Confirmed,
            OwnerCorrectionReason.OrdinaryStateDivergence =>
                OwnerCorrectionDisposition.OrdinaryReplay,
            OwnerCorrectionReason.ContactDivergence =>
                OwnerCorrectionDisposition.ContactReplay,
            OwnerCorrectionReason.HistoryMiss or
            OwnerCorrectionReason.MatchFrameEpochChanged or
            OwnerCorrectionReason.LifeEpochChanged or
            OwnerCorrectionReason.AuthorityDiscontinuityChanged or
            OwnerCorrectionReason.OwnerControlEpochChanged or
            OwnerCorrectionReason.ConfigurationHistoryPolicyExhausted or
            OwnerCorrectionReason.UnrecoverablePenetration or
            OwnerCorrectionReason.ExtremeError or
            OwnerCorrectionReason.ReplayDepthExceeded => OwnerCorrectionDisposition.HardRebase,
            _ => throw new ArgumentOutOfRangeException(
                nameof(reason),
                reason,
                "The owner correction reason is not supported."),
        };

    private static void ValidateMismatch(OwnerMismatchField firstMismatch)
    {
        if (!Enum.IsDefined(firstMismatch))
        {
            throw new ArgumentOutOfRangeException(
                nameof(firstMismatch),
                firstMismatch,
                "The first mismatch field is not supported.");
        }
    }

    private static void RequireComparison(AuthorityFrameErrorMeasurement? comparison)
    {
        if (comparison is null)
        {
            throw new ArgumentException(
                "This correction reason requires a same-frame authority comparison.",
                nameof(AuthorityFrameErrorBefore));
        }
    }

    private static void RequireNoComparison(AuthorityFrameErrorMeasurement? comparison)
    {
        if (comparison is not null)
        {
            throw new ArgumentException(
                "This correction reason cannot claim a same-frame authority comparison.",
                nameof(AuthorityFrameErrorBefore));
        }
    }

    private static long CalculateReplayDepth(
        AuthorityFrameErrorMeasurement comparison,
        AppliedCorrectionDelta correction)
    {
        RequireComparisonNotFuture(comparison, correction);
        return correction.BeforeFrame.Tick - comparison.Frame.Tick;
    }

    private static void RequireComparisonNotFuture(
        AuthorityFrameErrorMeasurement comparison,
        AppliedCorrectionDelta correction)
    {
        if (comparison.Frame > correction.BeforeFrame)
        {
            throw new ArgumentException(
                "The authority comparison frame cannot be later than local state.",
                nameof(AuthorityFrameErrorBefore));
        }
    }

    private static void RequireRebaseTargetsComparison(
        AuthorityFrameErrorMeasurement comparison,
        AppliedCorrectionDelta correction)
    {
        RequireComparisonNotFuture(comparison, correction);
        if (correction.AfterFrame != comparison.Frame)
        {
            throw new ArgumentException(
                "A comparison-backed hard rebase must adopt that authority frame.",
                nameof(AppliedCorrection));
        }
    }

    private static void RequireRebaseNotFuture(AppliedCorrectionDelta correction)
    {
        if (correction.AfterFrame > correction.BeforeFrame)
        {
            throw new ArgumentException(
                "A same-epoch hard rebase cannot adopt a future authority frame.",
                nameof(AppliedCorrection));
        }
    }

    private static void RequireNonZeroComparison(
        AuthorityFrameErrorMeasurement comparison)
    {
        if (comparison.Error.IsZero)
        {
            throw new ArgumentException(
                "An extreme-error rebase requires a nonzero measured error.",
                nameof(AuthorityFrameErrorBefore));
        }
    }

    private static void RequireMismatch(
        OwnerMismatchField actual,
        OwnerMismatchField expected)
    {
        if (actual != expected)
        {
            throw new ArgumentException(
                $"This correction requires first mismatch {expected}.",
                nameof(FirstMismatch));
        }
    }

    private static void RequireNonEmptyMismatch(OwnerMismatchField firstMismatch)
    {
        if (firstMismatch == OwnerMismatchField.None)
        {
            throw new ArgumentException(
                "This correction must identify its first mismatch.",
                nameof(FirstMismatch));
        }
    }

    private static void RequireZeroCorrection(AppliedCorrectionDelta correction)
    {
        RequireSameFrameCorrection(correction);
        if (!correction.Error.IsZero)
        {
            throw new ArgumentException(
                "A confirmed observation cannot apply a state correction.",
                nameof(AppliedCorrection));
        }
    }

    private static void RequireSameFrameCorrection(AppliedCorrectionDelta correction)
    {
        if (correction.BeforeFrame != correction.AfterFrame)
        {
            throw new ArgumentException(
                "Replay preserves the current local frame reference.",
                nameof(AppliedCorrection));
        }
    }
}
