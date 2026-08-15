using BattleArena.Multiplayer.Protocol;
using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Tests.Protocol;

public sealed class PredictionInboundMessageValidatorTests
{
    private readonly PredictionInboundMessageValidator validator = new();

    [Fact]
    public void AuthenticatedRouteAcceptsValidMovementBundle()
    {
        var result = validator.Validate(
            PredictionProtocolTestData.MovementEnvelope(),
            PredictionProtocolTestData.Context());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void MovementIsRejectedBeforeHandshakeCompletes()
    {
        var result = validator.Validate(
            PredictionProtocolTestData.MovementEnvelope(),
            PredictionProtocolTestData.Context(authenticated: false));

        Assert.False(result.IsValid);
        Assert.Equal(ProtocolViolationCode.InvalidCredential, result.Violation?.Code);
    }

    [Theory]
    [InlineData(999UL, 2UL, 4U, 5U)]
    [InlineData(73UL, 99UL, 4U, 5U)]
    [InlineData(73UL, 2UL, 3U, 5U)]
    [InlineData(73UL, 2UL, 4U, 4U)]
    public void SessionPeerAndGenerationMismatchesAreRejected(
        ulong sessionId,
        ulong sourcePeer,
        uint peerGeneration,
        uint routeGeneration)
    {
        var envelope = PredictionProtocolTestData.MovementEnvelope();
        envelope.SessionId = sessionId;
        envelope.SourceSessionPeerId = sourcePeer;
        envelope.SourcePeerSessionGeneration = peerGeneration;
        envelope.PredictionRouteGeneration = routeGeneration;

        var result = validator.Validate(envelope, PredictionProtocolTestData.Context());

        Assert.False(result.IsValid);
        Assert.Equal(ProtocolViolationCode.InvalidSession, result.Violation?.Code);
    }

    [Fact]
    public void WrongDestinationPeerGenerationIsRejected()
    {
        var envelope = PredictionProtocolTestData.MovementEnvelope();
        envelope.DestinationPeerSessionGeneration--;

        var result = validator.Validate(envelope, PredictionProtocolTestData.Context());

        Assert.False(result.IsValid);
        Assert.Equal(ProtocolViolationCode.InvalidSession, result.Violation?.Code);
    }

    [Fact]
    public void ExpiredRouteRejectsEvenStructurallyValidTraffic()
    {
        var context = PredictionProtocolTestData.Context() with
        {
            CurrentEstimatedAuthorityTick = 10_000,
        };

        var result = validator.Validate(
            PredictionProtocolTestData.MovementEnvelope(),
            context);

        Assert.False(result.IsValid);
        Assert.Equal(ProtocolViolationCode.InvalidCredential, result.Violation?.Code);
    }

    [Fact]
    public void RedundantHistoryMaySpanMovementConfigurationRevisions()
    {
        var envelope = PredictionProtocolTestData.MovementEnvelope();
        envelope.MovementPredictionBundle.Commands[0].MovementProfileRevision = 2;
        envelope.MovementPredictionBundle.Commands[0].MovementCapabilityRevision = 3;

        var result = validator.Validate(envelope, PredictionProtocolTestData.Context());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void RollbackBaselineMustCorrelateToIncludedCommandTimingAndRevision()
    {
        var missingCommand = PredictionProtocolTestData.MovementEnvelope();
        missingCommand.MovementPredictionBundle.RollbackState.LastIncludedInputSequence = 99;
        var mismatchedTick = PredictionProtocolTestData.MovementEnvelope();
        mismatchedTick.MovementPredictionBundle.RollbackState.ClientTick--;

        var missingResult = validator.Validate(
            missingCommand,
            PredictionProtocolTestData.Context());
        var timingResult = validator.Validate(
            mismatchedTick,
            PredictionProtocolTestData.Context());

        Assert.Equal(ProtocolViolationCode.InvalidSequence, missingResult.Violation?.Code);
        Assert.Equal(ProtocolViolationCode.InvalidNumericValue, timingResult.Violation?.Code);
    }

    [Fact]
    public void DirectMovementRejectsAttackOrBlockButtons()
    {
        var envelope = PredictionProtocolTestData.MovementEnvelope();
        envelope.MovementPredictionBundle.Commands[^1].PressedButtonBits = 1U << 3;

        var result = validator.Validate(envelope, PredictionProtocolTestData.Context());

        Assert.False(result.IsValid);
        Assert.Equal(ProtocolViolationCode.InvalidNumericValue, result.Violation?.Code);
    }

    [Fact]
    public void DirectMovementRejectsMoreThanThreeRedundantCommands()
    {
        var envelope = PredictionProtocolTestData.MovementEnvelope();
        var fourth = envelope.MovementPredictionBundle.Commands[^1].Clone();
        fourth.InputSequence++;
        fourth.ClientTick++;
        fourth.EstimatedAuthorityTick++;
        envelope.MovementPredictionBundle.Commands.Add(fourth);
        envelope.ClientTick = fourth.ClientTick;
        envelope.EstimatedAuthorityTick = fourth.EstimatedAuthorityTick;

        var result = validator.Validate(envelope, PredictionProtocolTestData.Context());

        Assert.False(result.IsValid);
        Assert.Equal(ProtocolViolationCode.InvalidCollectionCount, result.Violation?.Code);
    }

    [Fact]
    public void EnvelopeTimingMustMatchNewestCommand()
    {
        var envelope = PredictionProtocolTestData.MovementEnvelope();
        envelope.EstimatedAuthorityTick++;

        var result = validator.Validate(envelope, PredictionProtocolTestData.Context());

        Assert.False(result.IsValid);
        Assert.Equal(ProtocolViolationCode.InvalidNumericValue, result.Violation?.Code);
    }

    [Fact]
    public void InvalidRollbackStateIsRejectedWithoutTrustingItsPosition()
    {
        var envelope = PredictionProtocolTestData.MovementEnvelope();
        envelope.MovementPredictionBundle.RollbackState.Position.X = float.NaN;

        var result = validator.Validate(envelope, PredictionProtocolTestData.Context());

        Assert.False(result.IsValid);
        Assert.Equal(ProtocolViolationCode.InvalidNumericValue, result.Violation?.Code);
    }

    [Fact]
    public void FiniteButUnboundedRollbackHintIsRejected()
    {
        var envelope = PredictionProtocolTestData.MovementEnvelope();
        envelope.MovementPredictionBundle.RollbackState.Position.X =
            ProtocolConstants.MaxPredictionCoordinateMagnitude + 1;

        var result = validator.Validate(envelope, PredictionProtocolTestData.Context());

        Assert.False(result.IsValid);
        Assert.Equal(ProtocolViolationCode.InvalidNumericValue, result.Violation?.Code);
    }

    [Fact]
    public void HelloBeginsAuthenticationButRequiresExactNonceSize()
    {
        var valid = validator.Validate(
            PredictionProtocolTestData.HelloEnvelope(),
            PredictionProtocolTestData.Context(authenticated: false));
        var invalid = validator.Validate(
            PredictionProtocolTestData.HelloEnvelope(16),
            PredictionProtocolTestData.Context(authenticated: false));

        Assert.True(valid.IsValid);
        Assert.False(invalid.IsValid);
        Assert.Equal(ProtocolViolationCode.InvalidCredential, invalid.Violation?.Code);
    }

    [Fact]
    public void ValidDirectClockProbeRequiresAuthenticatedRoute()
    {
        var envelope = PredictionProtocolTestData.HelloEnvelope();
        envelope.ClientTick = 100;
        envelope.EstimatedAuthorityTick = 200;
        envelope.DirectClockProbe = new DirectClockProbe
        {
            ProbeSequence = 4,
            SourceSendTimestampMicroseconds = 123_456,
        };

        var valid = validator.Validate(envelope, PredictionProtocolTestData.Context());
        var unauthenticated = validator.Validate(
            envelope,
            PredictionProtocolTestData.Context(authenticated: false));

        Assert.True(valid.IsValid);
        Assert.False(unauthenticated.IsValid);
    }
}
