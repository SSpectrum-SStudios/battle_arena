using BattleArena.Core.Common;
using BattleArena.Multiplayer.OwnerPrediction;

namespace BattleArena.Multiplayer.Tests.OwnerPrediction;

public sealed class OwnerCorrectionTelemetryTests
{
    private static readonly SimulationInstant AuthorityFrame = new(100);
    private static readonly SimulationInstant CurrentFrame = new(112);
    private static readonly OwnerCorrectionError BeforeError =
        new(0.5d, 0.2d, 1.5d, 0.1d, 0.05d);
    private static readonly OwnerCorrectionError CorrectionError =
        new(0.2d, 0.1d, 0.5d, 0.02d, 0.01d);
    private static readonly AuthorityFrameErrorMeasurement Comparison =
        new(AuthorityFrame, BeforeError);

    public static IEnumerable<object[]> AllReasons =>
        Enum.GetValues<OwnerCorrectionReason>().Select(reason => new object[] { reason });

    public static IEnumerable<object[]> ReplayReasons()
    {
        yield return
        [
            OwnerCorrectionReason.OrdinaryStateDivergence,
            OwnerCorrectionDisposition.OrdinaryReplay,
            OwnerMismatchField.VerticalPosition,
        ];
        yield return
        [
            OwnerCorrectionReason.ContactDivergence,
            OwnerCorrectionDisposition.ContactReplay,
            OwnerMismatchField.Support,
        ];
    }

    public static IEnumerable<object[]> EpochReasons()
    {
        yield return
        [
            OwnerCorrectionReason.MatchFrameEpochChanged,
            OwnerMismatchField.MatchFrameEpoch,
        ];
        yield return
        [
            OwnerCorrectionReason.LifeEpochChanged,
            OwnerMismatchField.LifeEpoch,
        ];
        yield return
        [
            OwnerCorrectionReason.AuthorityDiscontinuityChanged,
            OwnerMismatchField.AuthorityDiscontinuity,
        ];
        yield return
        [
            OwnerCorrectionReason.OwnerControlEpochChanged,
            OwnerMismatchField.OwnerControlEpoch,
        ];
    }

    [Theory]
    [MemberData(nameof(AllReasons))]
    public void EveryReasonHasExactlyOneIntentionalDisposition(OwnerCorrectionReason reason)
    {
        var telemetry = CreateValid(reason);

        Assert.Equal(ExpectedDisposition(reason), telemetry.Disposition);
        Assert.Equal(reason, telemetry.Reason);
    }

    [Fact]
    public void ConfirmationRecordsAuthorityFrameAndAppliesNoCorrection()
    {
        var telemetry = OwnerCorrectionTelemetry.Create(
            OwnerCorrectionReason.ConfirmedWithinTolerance,
            Comparison,
            ConfirmedDelta(),
            OwnerMismatchField.None);

        Assert.Equal(Comparison, telemetry.AuthorityFrameErrorBefore);
        Assert.Equal(OwnerCorrectionError.Zero, telemetry.AppliedCorrection.Error);
        Assert.Equal(0L, telemetry.ReplayDepth);
        Assert.Equal(OwnerMismatchField.None, telemetry.FirstMismatch);
    }

    [Theory]
    [MemberData(nameof(ReplayReasons))]
    public void ReplayDerivesExactPositiveDepthFromFrameReferences(
        OwnerCorrectionReason reason,
        OwnerCorrectionDisposition disposition,
        OwnerMismatchField mismatch)
    {
        var telemetry = OwnerCorrectionTelemetry.Create(
            reason,
            Comparison,
            ReplayDelta(),
            mismatch);

        Assert.Equal(disposition, telemetry.Disposition);
        Assert.Equal(12L, telemetry.ReplayDepth);
        Assert.Equal(CurrentFrame, telemetry.AppliedCorrection.BeforeFrame);
        Assert.Equal(CurrentFrame, telemetry.AppliedCorrection.AfterFrame);
    }

    [Theory]
    [MemberData(nameof(ReplayReasons))]
    public void DivergenceAtCurrentAuthorityFrameHasZeroReplayDepth(
        OwnerCorrectionReason reason,
        OwnerCorrectionDisposition disposition,
        OwnerMismatchField mismatch)
    {
        var currentComparison = new AuthorityFrameErrorMeasurement(CurrentFrame, BeforeError);
        var telemetry = OwnerCorrectionTelemetry.Create(
            reason,
            currentComparison,
            ReplayDelta(),
            mismatch);

        Assert.Equal(disposition, telemetry.Disposition);
        Assert.Equal(0L, telemetry.ReplayDepth);
    }

    [Fact]
    public void HistoryMissAndExhaustedConfigurationRepresentUnavailableComparison()
    {
        foreach (var reason in new[]
        {
            OwnerCorrectionReason.HistoryMiss,
            OwnerCorrectionReason.ConfigurationHistoryPolicyExhausted,
        })
        {
            var telemetry = OwnerCorrectionTelemetry.Create(
                reason,
                null,
                RebaseDelta(),
                OwnerMismatchField.None);

            Assert.Null(telemetry.AuthorityFrameErrorBefore);
            Assert.Equal(OwnerCorrectionDisposition.HardRebase, telemetry.Disposition);
            Assert.Equal(0L, telemetry.ReplayDepth);
        }
    }

    [Theory]
    [MemberData(nameof(EpochReasons))]
    public void EpochRebaseRetainsItsExactIdentityCause(
        OwnerCorrectionReason reason,
        OwnerMismatchField mismatch)
    {
        var telemetry = OwnerCorrectionTelemetry.Create(
            reason,
            null,
            RebaseDelta(),
            mismatch);

        Assert.Null(telemetry.AuthorityFrameErrorBefore);
        Assert.Equal(mismatch, telemetry.FirstMismatch);
    }

    [Theory]
    [InlineData(OwnerCorrectionReason.UnrecoverablePenetration, OwnerMismatchField.Contact)]
    [InlineData(OwnerCorrectionReason.ExtremeError, OwnerMismatchField.HorizontalPosition)]
    public void ComparisonBackedRebaseAdoptsExactAuthorityFrame(
        OwnerCorrectionReason reason,
        OwnerMismatchField mismatch)
    {
        var telemetry = OwnerCorrectionTelemetry.Create(
            reason,
            Comparison,
            RebaseDelta(),
            mismatch);

        Assert.Equal(AuthorityFrame, telemetry.AppliedCorrection.AfterFrame);
        Assert.Equal(0L, telemetry.ReplayDepth);
    }

    [Theory]
    [InlineData(OwnerCorrectionReason.ConfirmedWithinTolerance, OwnerMismatchField.None)]
    [InlineData(OwnerCorrectionReason.OrdinaryStateDivergence, OwnerMismatchField.HorizontalPosition)]
    [InlineData(OwnerCorrectionReason.ContactDivergence, OwnerMismatchField.Contact)]
    [InlineData(OwnerCorrectionReason.UnrecoverablePenetration, OwnerMismatchField.Contact)]
    [InlineData(OwnerCorrectionReason.ExtremeError, OwnerMismatchField.HorizontalPosition)]
    public void AuthorityComparisonCannotBeLaterThanCurrentLocalFrame(
        OwnerCorrectionReason reason,
        OwnerMismatchField mismatch)
    {
        var futureComparison = new AuthorityFrameErrorMeasurement(
            new SimulationInstant(CurrentFrame.Tick + 1),
            BeforeError);
        var delta = reason is OwnerCorrectionReason.UnrecoverablePenetration or
            OwnerCorrectionReason.ExtremeError
            ? new AppliedCorrectionDelta(CurrentFrame, futureComparison.Frame, CorrectionError)
            : reason == OwnerCorrectionReason.ConfirmedWithinTolerance
                ? ConfirmedDelta()
                : ReplayDelta();

        Assert.Throws<ArgumentException>(
            () => OwnerCorrectionTelemetry.Create(reason, futureComparison, delta, mismatch));
    }

    [Theory]
    [InlineData(OwnerCorrectionReason.UnrecoverablePenetration, OwnerMismatchField.Contact)]
    [InlineData(OwnerCorrectionReason.ExtremeError, OwnerMismatchField.HorizontalPosition)]
    public void ComparisonBackedRebaseRejectsDifferentTargetFrame(
        OwnerCorrectionReason reason,
        OwnerMismatchField mismatch)
    {
        var wrongTarget = new AppliedCorrectionDelta(
            CurrentFrame,
            new SimulationInstant(AuthorityFrame.Tick + 1),
            CorrectionError);

        Assert.Throws<ArgumentException>(
            () => OwnerCorrectionTelemetry.Create(
                reason,
                Comparison,
                wrongTarget,
                mismatch));
    }

    [Fact]
    public void ConfirmationRequiresComparisonNoMismatchAndZeroSameFrameDelta()
    {
        Assert.Throws<ArgumentException>(
            () => OwnerCorrectionTelemetry.Create(
                OwnerCorrectionReason.ConfirmedWithinTolerance,
                null,
                ConfirmedDelta(),
                OwnerMismatchField.None));
        Assert.Throws<ArgumentException>(
            () => OwnerCorrectionTelemetry.Create(
                OwnerCorrectionReason.ConfirmedWithinTolerance,
                Comparison,
                ConfirmedDelta(),
                OwnerMismatchField.HorizontalPosition));
        Assert.Throws<ArgumentException>(
            () => OwnerCorrectionTelemetry.Create(
                OwnerCorrectionReason.ConfirmedWithinTolerance,
                Comparison,
                ReplayDelta(),
                OwnerMismatchField.None));
        Assert.Throws<ArgumentException>(
            () => OwnerCorrectionTelemetry.Create(
                OwnerCorrectionReason.ConfirmedWithinTolerance,
                Comparison,
                RebaseDelta(),
                OwnerMismatchField.None));
    }

    [Theory]
    [MemberData(nameof(ReplayReasons))]
    public void ReplayRequiresComparisonMismatchAndSameCurrentFrame(
        OwnerCorrectionReason reason,
        OwnerCorrectionDisposition _,
        OwnerMismatchField mismatch)
    {
        Assert.Throws<ArgumentException>(
            () => OwnerCorrectionTelemetry.Create(reason, null, ReplayDelta(), mismatch));
        Assert.Throws<ArgumentException>(
            () => OwnerCorrectionTelemetry.Create(
                reason,
                Comparison,
                ReplayDelta(),
                OwnerMismatchField.None));
        Assert.Throws<ArgumentException>(
            () => OwnerCorrectionTelemetry.Create(reason, Comparison, RebaseDelta(), mismatch));
    }

    [Fact]
    public void MissingEvidenceReasonsRejectFabricatedComparisonOrMismatch()
    {
        foreach (var reason in new[]
        {
            OwnerCorrectionReason.HistoryMiss,
            OwnerCorrectionReason.ConfigurationHistoryPolicyExhausted,
        })
        {
            Assert.Throws<ArgumentException>(
                () => OwnerCorrectionTelemetry.Create(
                    reason,
                    Comparison,
                    RebaseDelta(),
                    OwnerMismatchField.None));
            Assert.Throws<ArgumentException>(
                () => OwnerCorrectionTelemetry.Create(
                    reason,
                    null,
                    RebaseDelta(),
                    OwnerMismatchField.HorizontalPosition));
        }
    }

    [Theory]
    [InlineData(OwnerCorrectionReason.HistoryMiss)]
    [InlineData(OwnerCorrectionReason.ConfigurationHistoryPolicyExhausted)]
    public void SameEpochMissingEvidenceCannotRebaseIntoFuture(
        OwnerCorrectionReason reason)
    {
        var futureRebase = new AppliedCorrectionDelta(
            AuthorityFrame,
            CurrentFrame,
            CorrectionError);

        Assert.Throws<ArgumentException>(
            () => OwnerCorrectionTelemetry.Create(
                reason,
                null,
                futureRebase,
                OwnerMismatchField.None));
    }

    [Theory]
    [MemberData(nameof(EpochReasons))]
    public void EpochCauseRejectsComparisonAndWrongIdentityField(
        OwnerCorrectionReason reason,
        OwnerMismatchField mismatch)
    {
        Assert.Throws<ArgumentException>(
            () => OwnerCorrectionTelemetry.Create(reason, Comparison, RebaseDelta(), mismatch));
        Assert.Throws<ArgumentException>(
            () => OwnerCorrectionTelemetry.Create(
                reason,
                null,
                RebaseDelta(),
                OwnerMismatchField.HorizontalPosition));
    }

    [Fact]
    public void PenetrationAndExtremeErrorRejectIncoherentComparisonEvidence()
    {
        Assert.Throws<ArgumentException>(
            () => OwnerCorrectionTelemetry.Create(
                OwnerCorrectionReason.UnrecoverablePenetration,
                null,
                RebaseDelta(),
                OwnerMismatchField.Contact));
        Assert.Throws<ArgumentException>(
            () => OwnerCorrectionTelemetry.Create(
                OwnerCorrectionReason.UnrecoverablePenetration,
                Comparison,
                RebaseDelta(),
                OwnerMismatchField.HorizontalPosition));
        Assert.Throws<ArgumentException>(
            () => OwnerCorrectionTelemetry.Create(
                OwnerCorrectionReason.ExtremeError,
                null,
                RebaseDelta(),
                OwnerMismatchField.HorizontalPosition));
        Assert.Throws<ArgumentException>(
            () => OwnerCorrectionTelemetry.Create(
                OwnerCorrectionReason.ExtremeError,
                Comparison,
                RebaseDelta(),
                OwnerMismatchField.None));
        var zeroComparison = new AuthorityFrameErrorMeasurement(
            AuthorityFrame,
            OwnerCorrectionError.Zero);
        Assert.Throws<ArgumentException>(
            () => OwnerCorrectionTelemetry.Create(
                OwnerCorrectionReason.ExtremeError,
                zeroComparison,
                RebaseDelta(),
                OwnerMismatchField.HorizontalPosition));
    }

    [Theory]
    [InlineData(-0.01d, 0d, 0d, 0d, 0d, "horizontalPositionMeters")]
    [InlineData(0d, double.NaN, 0d, 0d, 0d, "verticalPositionMeters")]
    [InlineData(0d, 0d, double.PositiveInfinity, 0d, 0d, "linearVelocityMetersPerSecond")]
    [InlineData(0d, 0d, 0d, double.NegativeInfinity, 0d, "facingRadians")]
    [InlineData(0d, 0d, 0d, 0d, double.NaN, "contactNormalRadians")]
    public void ErrorMagnitudesMustBeFiniteAndNonNegative(
        double horizontal,
        double vertical,
        double velocity,
        double facing,
        double contact,
        string parameterName)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new OwnerCorrectionError(horizontal, vertical, velocity, facing, contact));

        Assert.Equal(parameterName, exception.ParamName);
    }

    [Fact]
    public void UndefinedReasonAndMismatchAreRejected()
    {
        Assert.Equal(
            "reason",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => OwnerCorrectionTelemetry.Create(
                    (OwnerCorrectionReason)99,
                    Comparison,
                    ReplayDelta(),
                    OwnerMismatchField.HorizontalPosition)).ParamName);
        Assert.Equal(
            "firstMismatch",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => OwnerCorrectionTelemetry.Create(
                    OwnerCorrectionReason.OrdinaryStateDivergence,
                    Comparison,
                    ReplayDelta(),
                    (OwnerMismatchField)99)).ParamName);
    }

    private static OwnerCorrectionTelemetry CreateValid(OwnerCorrectionReason reason) =>
        reason switch
        {
            OwnerCorrectionReason.ConfirmedWithinTolerance =>
                OwnerCorrectionTelemetry.Create(
                    reason,
                    Comparison,
                    ConfirmedDelta(),
                    OwnerMismatchField.None),
            OwnerCorrectionReason.OrdinaryStateDivergence =>
                OwnerCorrectionTelemetry.Create(
                    reason,
                    Comparison,
                    ReplayDelta(),
                    OwnerMismatchField.VerticalPosition),
            OwnerCorrectionReason.ContactDivergence =>
                OwnerCorrectionTelemetry.Create(
                    reason,
                    Comparison,
                    ReplayDelta(),
                    OwnerMismatchField.Support),
            OwnerCorrectionReason.HistoryMiss or
            OwnerCorrectionReason.ConfigurationHistoryPolicyExhausted =>
                OwnerCorrectionTelemetry.Create(
                    reason,
                    null,
                    RebaseDelta(),
                    OwnerMismatchField.None),
            OwnerCorrectionReason.MatchFrameEpochChanged =>
                EpochRebase(reason, OwnerMismatchField.MatchFrameEpoch),
            OwnerCorrectionReason.LifeEpochChanged =>
                EpochRebase(reason, OwnerMismatchField.LifeEpoch),
            OwnerCorrectionReason.AuthorityDiscontinuityChanged =>
                EpochRebase(reason, OwnerMismatchField.AuthorityDiscontinuity),
            OwnerCorrectionReason.OwnerControlEpochChanged =>
                EpochRebase(reason, OwnerMismatchField.OwnerControlEpoch),
            OwnerCorrectionReason.UnrecoverablePenetration =>
                OwnerCorrectionTelemetry.Create(
                    reason,
                    Comparison,
                    RebaseDelta(),
                    OwnerMismatchField.Contact),
            OwnerCorrectionReason.ExtremeError =>
                OwnerCorrectionTelemetry.Create(
                    reason,
                    Comparison,
                    RebaseDelta(),
                    OwnerMismatchField.HorizontalPosition),
            OwnerCorrectionReason.ReplayDepthExceeded =>
                OwnerCorrectionTelemetry.Create(
                    reason,
                    Comparison,
                    RebaseDelta(),
                    OwnerMismatchField.HorizontalPosition),
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
        };

    private static OwnerCorrectionTelemetry EpochRebase(
        OwnerCorrectionReason reason,
        OwnerMismatchField mismatch) =>
        OwnerCorrectionTelemetry.Create(reason, null, RebaseDelta(), mismatch);

    private static OwnerCorrectionDisposition ExpectedDisposition(
        OwnerCorrectionReason reason) =>
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
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
        };

    private static AppliedCorrectionDelta ConfirmedDelta() =>
        new(CurrentFrame, CurrentFrame, OwnerCorrectionError.Zero);

    private static AppliedCorrectionDelta ReplayDelta() =>
        new(CurrentFrame, CurrentFrame, CorrectionError);

    private static AppliedCorrectionDelta RebaseDelta() =>
        new(CurrentFrame, AuthorityFrame, CorrectionError);
}
