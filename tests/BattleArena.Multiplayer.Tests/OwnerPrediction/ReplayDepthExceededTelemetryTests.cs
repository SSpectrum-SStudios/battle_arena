using BattleArena.Core.Common;
using BattleArena.Multiplayer.OwnerPrediction;

namespace BattleArena.Multiplayer.Tests.OwnerPrediction;

/// <summary>
/// P06-A4: the outcome for "the owner was wrong, but the frame is too old to
/// replay".
/// </summary>
/// <remarks>
/// This is the common correction at real latency once the replay span exceeds the
/// depth cap, not an exceptional one, so both properties below are load-bearing
/// rather than tidiness.
/// </remarks>
public sealed class ReplayDepthExceededTelemetryTests
{
    private static readonly SimulationInstant AuthorityFrame = new(100);
    private static readonly SimulationInstant CurrentFrame = new(132);
    private static readonly OwnerCorrectionError BeforeError = new(0.02d, 0d, 0.05d, 0d, 0d);
    private static readonly OwnerCorrectionError CorrectionError = new(0.02d, 0d, 0.05d, 0d, 0d);

    private static readonly AuthorityFrameErrorMeasurement Comparison =
        new(AuthorityFrame, BeforeError);

    [Fact]
    public void ItPermitsAdoptingAuthorityStateAndFastForwardingToThePresent()
    {
        // The correction lands on the present frame, not on the 32-frame-old
        // comparison frame. Pinning it to the comparison would plant the character
        // where authority saw it a third of a second ago -- metres backwards at
        // sprint speed -- and that is the only response the stricter validation
        // would have allowed.
        var telemetry = OwnerCorrectionTelemetry.Create(
            OwnerCorrectionReason.ReplayDepthExceeded,
            Comparison,
            new AppliedCorrectionDelta(CurrentFrame, CurrentFrame, CorrectionError),
            OwnerMismatchField.HorizontalPosition);

        Assert.Equal(OwnerCorrectionDisposition.HardRebase, telemetry.Disposition);
        Assert.True(telemetry.IsInitialized);
    }

    [Fact]
    public void ItCarriesTheReplayDepthRatherThanReportingZero()
    {
        // The reason exists to signal that the replay budget is mis-tuned. A rate
        // with no magnitude cannot say how far past the cap the common case sits,
        // which is precisely the number needed to decide whether the cap moves.
        var telemetry = OwnerCorrectionTelemetry.Create(
            OwnerCorrectionReason.ReplayDepthExceeded,
            Comparison,
            new AppliedCorrectionDelta(CurrentFrame, AuthorityFrame, CorrectionError),
            OwnerMismatchField.HorizontalPosition);

        Assert.Equal(CurrentFrame.Tick - AuthorityFrame.Tick, telemetry.ReplayDepth);
        Assert.True(
            telemetry.ReplayDepth > 0,
            "A depth-exceeded correction with no depth cannot tune the cap it exists to report on.");
    }

    [Fact]
    public void OtherHardRebaseReasonsStillReportNoDepth()
    {
        // A history miss or an epoch change never compared frames to span, so zero
        // is the honest answer there and the new behaviour must not leak into them.
        var telemetry = OwnerCorrectionTelemetry.Create(
            OwnerCorrectionReason.HistoryMiss,
            null,
            new AppliedCorrectionDelta(CurrentFrame, AuthorityFrame, CorrectionError),
            OwnerMismatchField.None);

        Assert.Equal(0, telemetry.ReplayDepth);
    }

    [Fact]
    public void ASmallErrorIsAcceptedRatherThanRequiringAnExtremeOne()
    {
        // The whole point: two centimetres, too old to replay. ExtremeError could
        // not stand in because it demands a large error, and calling this extreme
        // would put a false statement in the trace.
        var tiny = new OwnerCorrectionError(0.02d, 0d, 0d, 0d, 0d);
        var telemetry = OwnerCorrectionTelemetry.Create(
            OwnerCorrectionReason.ReplayDepthExceeded,
            new AuthorityFrameErrorMeasurement(AuthorityFrame, tiny),
            new AppliedCorrectionDelta(CurrentFrame, AuthorityFrame, tiny),
            OwnerMismatchField.HorizontalPosition);

        Assert.Equal(OwnerCorrectionReason.ReplayDepthExceeded, telemetry.Reason);
    }

    [Fact]
    public void ItStillRefusesToAdoptAFutureAuthorityFrame()
    {
        Assert.Throws<ArgumentException>(() => OwnerCorrectionTelemetry.Create(
            OwnerCorrectionReason.ReplayDepthExceeded,
            Comparison,
            new AppliedCorrectionDelta(
                CurrentFrame, new SimulationInstant(CurrentFrame.Tick + 1), CorrectionError),
            OwnerMismatchField.HorizontalPosition));
    }

    [Fact]
    public void ItStillRequiresAComparisonAndANamedField()
    {
        Assert.Throws<ArgumentException>(() => OwnerCorrectionTelemetry.Create(
            OwnerCorrectionReason.ReplayDepthExceeded,
            null,
            new AppliedCorrectionDelta(CurrentFrame, AuthorityFrame, CorrectionError),
            OwnerMismatchField.HorizontalPosition));

        Assert.Throws<ArgumentException>(() => OwnerCorrectionTelemetry.Create(
            OwnerCorrectionReason.ReplayDepthExceeded,
            Comparison,
            new AppliedCorrectionDelta(CurrentFrame, AuthorityFrame, CorrectionError),
            OwnerMismatchField.None));
    }
}
