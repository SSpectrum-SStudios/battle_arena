using System.Globalization;
using System.Text;
using System.Text.Json;
using BattleArena.Core.Common;
using BattleArena.Multiplayer.OwnerPrediction;

namespace BattleArena.Multiplayer.Tests.OwnerPrediction;

public sealed class PredictionTraceFormatterTests
{
    private const string SourceRevision =
        "0123456789abcdef0123456789abcdef01234567";
    private const string MovementProfileSha256 =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private static readonly HashSet<string> SensitivePropertySegments = new(
        [
            "account",
            "address",
            "auth",
            "authentication",
            "authorization",
            "binary",
            "blob",
            "body",
            "buffer",
            "bytes",
            "content",
            "credential",
            "display",
            "endpoint",
            "ip",
            "key",
            "password",
            "packet",
            "payload",
            "peer",
            "persona",
            "player",
            "route",
            "secret",
            "session",
            "steam",
            "ticket",
            "token",
            "user",
        ],
        StringComparer.OrdinalIgnoreCase);

    private const string ExpectedTrace = """
        {
          "header": {
            "trace_schema_version": 2,
            "trace_kind": "battle_arena.owner_prediction",
            "source_revision": "0123456789abcdef0123456789abcdef01234567",
            "source_is_dirty": true,
            "build_id": 456789,
            "protocol_version": 8,
            "simulation_ticks_per_second": 60,
            "movement_profile_sha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
          },
          "input_telemetry": {
            "generated_commands": 2,
            "command_send_attempts": 3,
            "newly_accepted_commands": 1,
            "late_commands": 0,
            "duplicate_commands": 0,
            "rejected_commands": 0,
            "applied_received_commands": 1,
            "repeated_continuous_decisions": 0,
            "neutral_fallback_decisions": 0,
            "authority_override_decisions": 0
          },
          "corrections": [
            {
              "disposition": "ordinary_replay",
              "reason": "ordinary_state_divergence",
              "replay_depth": 2,
              "first_mismatch": "vertical_position",
              "authority_frame_error_before": {
                "frame": 10,
                "error": {
                  "horizontal_position_meters": 0.25,
                  "vertical_position_meters": 0.5,
                  "linear_velocity_meters_per_second": 1.5,
                  "facing_radians": 0.1,
                  "contact_normal_radians": 0.05
                }
              },
              "applied_correction": {
                "before_frame": 12,
                "after_frame": 12,
                "delta": {
                  "horizontal_position_meters": 0.1,
                  "vertical_position_meters": 0.2,
                  "linear_velocity_meters_per_second": 0.3,
                  "facing_radians": 0.01,
                  "contact_normal_radians": 0.02
                }
              }
            }
          ]
        }
        """;

    [Fact]
    public void FormatProducesStableGoldenTrace()
    {
        var trace = PredictionTraceFormatter.Format(
            CreateBaseline(),
            CreateInputTelemetry(),
            CreateCorrectionRing());

        Assert.Equal(ExpectedTrace, trace);
        Assert.DoesNotContain('\r', trace);
    }

    [Fact]
    public void IdenticalInputsProduceIdenticalTrace()
    {
        Assert.Equal(
            PredictionTraceFormatter.Format(
                CreateBaseline(),
                CreateInputTelemetry(),
                CreateCorrectionRing()),
            PredictionTraceFormatter.Format(
                CreateBaseline(),
                CreateInputTelemetry(),
                CreateCorrectionRing()));
    }

    [Fact]
    public void WrappedRingExportsOnlyRetainedCorrectionsOldestToNewest()
    {
        var ring = new PredictionTelemetryRing<OwnerCorrectionTelemetry>(2);
        ring.Add(CreateHistoryMiss(10));
        ring.Add(CreateHistoryMiss(20));
        ring.Add(CreateHistoryMiss(30));

        using var document = JsonDocument.Parse(
            PredictionTraceFormatter.Format(
                CreateBaseline(),
                OwnerInputTelemetry.Empty,
                ring));
        var corrections = document.RootElement.GetProperty("corrections");

        Assert.Equal(2, corrections.GetArrayLength());
        Assert.Equal(
            20,
            corrections[0].GetProperty("applied_correction").GetProperty("after_frame").GetInt64());
        Assert.Equal(
            30,
            corrections[1].GetProperty("applied_correction").GetProperty("after_frame").GetInt64());
    }

    [Fact]
    public void TraceSchemaContainsNoSensitiveOrPersonalPropertyNames()
    {
        using var document = JsonDocument.Parse(
            PredictionTraceFormatter.Format(
                CreateBaseline(),
                CreateInputTelemetry(),
                CreateCorrectionRing()));

        AssertNoSensitiveProperties(document.RootElement);
    }

    [Theory]
    [InlineData("steam_auth_ticket")]
    [InlineData("password")]
    [InlineData("private_key")]
    [InlineData("account_id")]
    [InlineData("user_persona")]
    [InlineData("peer_endpoint")]
    [InlineData("ip_address")]
    [InlineData("display_name")]
    [InlineData("session_route_token")]
    public void SensitiveFieldGuardRecognizesRealisticRegressions(string propertyName)
    {
        Assert.True(IsSensitivePropertyName(propertyName));
    }

    [Theory]
    [InlineData("raw_payload")]
    [InlineData("packet_bytes")]
    [InlineData("binary_blob")]
    [InlineData("message_body")]
    [InlineData("content_buffer")]
    public void RawPayloadFieldGuardRecognizesRealisticRegressions(string propertyName)
    {
        Assert.True(IsSensitivePropertyName(propertyName));
    }

    [Fact]
    public void ArbitraryCredentialAndPersonaCanariesCannotEnterBaseline()
    {
        Assert.ThrowsAny<ArgumentException>(
            () => new PredictionBaselineIdentity(
                "STEAM-TICKET-CANARY-7fd892",
                sourceIsDirty: false,
                buildId: 1,
                protocolVersion: 8,
                SimulationRate.Default,
                MovementProfileSha256));
        Assert.ThrowsAny<ArgumentException>(
            () => new PredictionBaselineIdentity(
                SourceRevision,
                sourceIsDirty: false,
                buildId: 1,
                protocolVersion: 8,
                SimulationRate.Default,
                "Robert-Persona-Canary"));
    }

    [Fact]
    public void EmptyCorrectionRingProducesEmptyArray()
    {
        var ring = new PredictionTelemetryRing<OwnerCorrectionTelemetry>(1);

        using var document = JsonDocument.Parse(
            PredictionTraceFormatter.Format(
                CreateBaseline(),
                OwnerInputTelemetry.Empty,
                ring));

        Assert.Equal(
            0,
            document.RootElement.GetProperty("corrections").GetArrayLength());
    }

    [Fact]
    public void CorrectionExportHasAnAbsoluteSampleLimit()
    {
        var ring = new PredictionTelemetryRing<OwnerCorrectionTelemetry>(
            PredictionTraceFormatter.MaximumCorrectionSamples + 1);
        for (var index = 0; index < ring.Capacity; index++)
        {
            ring.Add(CreateHistoryMiss(index));
        }

        Assert.Equal(
            "correctionTelemetry",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => PredictionTraceFormatter.Format(
                    CreateBaseline(),
                    OwnerInputTelemetry.Empty,
                    ring)).ParamName);
    }

    [Fact]
    public void ExactMaximumCorrectionCountSucceedsWithinEncodedByteLimit()
    {
        var ring = new PredictionTelemetryRing<OwnerCorrectionTelemetry>(
            PredictionTraceFormatter.MaximumCorrectionSamples);
        for (var index = 0; index < ring.Capacity; index++)
        {
            ring.Add(CreateHistoryMiss(index));
        }

        var trace = PredictionTraceFormatter.Format(
            CreateBaseline(),
            OwnerInputTelemetry.Empty,
            ring);

        Assert.True(
            Encoding.UTF8.GetByteCount(trace) <= PredictionTraceFormatter.MaximumEncodedBytes);
        using var document = JsonDocument.Parse(trace);
        Assert.Equal(
            PredictionTraceFormatter.MaximumCorrectionSamples,
            document.RootElement.GetProperty("corrections").GetArrayLength());
    }

    [Fact]
    public void GoldenTraceIsInvariantUnderNonInvariantCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");

            Assert.Equal(
                ExpectedTrace,
                PredictionTraceFormatter.Format(
                    CreateBaseline(),
                    CreateInputTelemetry(),
                    CreateCorrectionRing()));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void DefaultCorrectionObservationIsRejected()
    {
        var ring = new PredictionTelemetryRing<OwnerCorrectionTelemetry>(1);
        ring.Add(default);

        Assert.Throws<ArgumentException>(
            () => PredictionTraceFormatter.Format(
                CreateBaseline(),
                OwnerInputTelemetry.Empty,
                ring));
    }

    [Fact]
    public void EveryCorrectionReasonAndDispositionHasAStableWireName()
    {
        foreach (var reason in Enum.GetValues<OwnerCorrectionReason>())
        {
            var correction = CreateValidCorrection(reason);
            var element = FormatSingleCorrection(correction);

            Assert.Equal(ExpectedWireName(reason), element.GetProperty("reason").GetString());
            Assert.Equal(
                ExpectedWireName(correction.Disposition),
                element.GetProperty("disposition").GetString());
        }
    }

    [Fact]
    public void EveryMismatchFieldHasAStableWireName()
    {
        foreach (var field in Enum.GetValues<OwnerMismatchField>())
        {
            var correction = CreateCorrectionForMismatch(field);
            var element = FormatSingleCorrection(correction);

            Assert.Equal(
                ExpectedWireName(field),
                element.GetProperty("first_mismatch").GetString());
        }
    }

    [Fact]
    public void NullBaselineAndRingAreRejected()
    {
        Assert.Throws<ArgumentNullException>(
            () => PredictionTraceFormatter.Format(
                null!,
                OwnerInputTelemetry.Empty,
                CreateCorrectionRing()));
        Assert.Throws<ArgumentNullException>(
            () => PredictionTraceFormatter.Format(
                CreateBaseline(),
                OwnerInputTelemetry.Empty,
                null!));
    }

    private static PredictionBaselineIdentity CreateBaseline() =>
        new(
            sourceRevision: SourceRevision,
            sourceIsDirty: true,
            buildId: 456_789,
            protocolVersion: 8,
            simulationRate: SimulationRate.Default,
            movementProfileSha256: MovementProfileSha256);

    private static OwnerInputTelemetry CreateInputTelemetry() =>
        OwnerInputTelemetry.Empty
            .Record(OwnerInputOriginEvent.CommandGenerated, 2)
            .Record(OwnerInputOriginEvent.CommandSendAttempted, 3)
            .Record(OwnerInputArrivalDisposition.NewCommandAccepted)
            .Record(AuthorityInputApplicationKind.ReceivedCommand);

    private static PredictionTelemetryRing<OwnerCorrectionTelemetry> CreateCorrectionRing()
    {
        var ring = new PredictionTelemetryRing<OwnerCorrectionTelemetry>(4);
        var before = new OwnerCorrectionError(0.25d, 0.5d, 1.5d, 0.1d, 0.05d);
        var delta = new OwnerCorrectionError(0.1d, 0.2d, 0.3d, 0.01d, 0.02d);
        ring.Add(OwnerCorrectionTelemetry.Create(
            OwnerCorrectionReason.OrdinaryStateDivergence,
            new AuthorityFrameErrorMeasurement(new SimulationInstant(10), before),
            new AppliedCorrectionDelta(
                new SimulationInstant(12),
                new SimulationInstant(12),
                delta),
            OwnerMismatchField.VerticalPosition));
        return ring;
    }

    private static OwnerCorrectionTelemetry CreateHistoryMiss(long authorityFrame) =>
        CreateMissingEvidence(OwnerCorrectionReason.HistoryMiss, authorityFrame);

    private static OwnerCorrectionTelemetry CreateMissingEvidence(
        OwnerCorrectionReason reason,
        long authorityFrame) =>
        OwnerCorrectionTelemetry.Create(
            reason,
            null,
            new AppliedCorrectionDelta(
                new SimulationInstant(authorityFrame + 1),
                new SimulationInstant(authorityFrame),
                new OwnerCorrectionError(1d, 0d, 0d, 0d, 0d)),
            OwnerMismatchField.None);

    private static OwnerCorrectionTelemetry CreateValidCorrection(
        OwnerCorrectionReason reason) =>
        reason switch
        {
            OwnerCorrectionReason.ConfirmedWithinTolerance =>
                OwnerCorrectionTelemetry.Create(
                    reason,
                    new AuthorityFrameErrorMeasurement(
                        new SimulationInstant(10),
                        new OwnerCorrectionError(0.01d, 0d, 0d, 0d, 0d)),
                    new AppliedCorrectionDelta(
                        new SimulationInstant(12),
                        new SimulationInstant(12),
                        OwnerCorrectionError.Zero),
                    OwnerMismatchField.None),
            OwnerCorrectionReason.OrdinaryStateDivergence =>
                CreateReplay(reason, OwnerMismatchField.HorizontalPosition),
            OwnerCorrectionReason.ContactDivergence =>
                CreateReplay(reason, OwnerMismatchField.Contact),
            OwnerCorrectionReason.HistoryMiss or
            OwnerCorrectionReason.ConfigurationHistoryPolicyExhausted =>
                CreateMissingEvidence(reason, 10),
            OwnerCorrectionReason.MatchFrameEpochChanged =>
                CreateEpoch(reason, OwnerMismatchField.MatchFrameEpoch),
            OwnerCorrectionReason.LifeEpochChanged =>
                CreateEpoch(reason, OwnerMismatchField.LifeEpoch),
            OwnerCorrectionReason.AuthorityDiscontinuityChanged =>
                CreateEpoch(reason, OwnerMismatchField.AuthorityDiscontinuity),
            OwnerCorrectionReason.OwnerControlEpochChanged =>
                CreateEpoch(reason, OwnerMismatchField.OwnerControlEpoch),
            OwnerCorrectionReason.UnrecoverablePenetration =>
                CreateComparisonRebase(reason, OwnerMismatchField.Contact),
            OwnerCorrectionReason.ExtremeError =>
                CreateComparisonRebase(reason, OwnerMismatchField.HorizontalPosition),
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
        };

    private static OwnerCorrectionTelemetry CreateCorrectionForMismatch(
        OwnerMismatchField field) =>
        field switch
        {
            OwnerMismatchField.None =>
                CreateValidCorrection(OwnerCorrectionReason.ConfirmedWithinTolerance),
            OwnerMismatchField.MatchFrameEpoch =>
                CreateEpoch(
                    OwnerCorrectionReason.MatchFrameEpochChanged,
                    OwnerMismatchField.MatchFrameEpoch),
            OwnerMismatchField.LifeEpoch =>
                CreateEpoch(
                    OwnerCorrectionReason.LifeEpochChanged,
                    OwnerMismatchField.LifeEpoch),
            OwnerMismatchField.AuthorityDiscontinuity =>
                CreateEpoch(
                    OwnerCorrectionReason.AuthorityDiscontinuityChanged,
                    OwnerMismatchField.AuthorityDiscontinuity),
            OwnerMismatchField.OwnerControlEpoch =>
                CreateEpoch(
                    OwnerCorrectionReason.OwnerControlEpochChanged,
                    OwnerMismatchField.OwnerControlEpoch),
            _ => CreateReplay(OwnerCorrectionReason.OrdinaryStateDivergence, field),
        };

    private static OwnerCorrectionTelemetry CreateReplay(
        OwnerCorrectionReason reason,
        OwnerMismatchField mismatch) =>
        OwnerCorrectionTelemetry.Create(
            reason,
            new AuthorityFrameErrorMeasurement(
                new SimulationInstant(10),
                new OwnerCorrectionError(0.1d, 0d, 0d, 0d, 0d)),
            new AppliedCorrectionDelta(
                new SimulationInstant(12),
                new SimulationInstant(12),
                new OwnerCorrectionError(0.05d, 0d, 0d, 0d, 0d)),
            mismatch);

    private static OwnerCorrectionTelemetry CreateEpoch(
        OwnerCorrectionReason reason,
        OwnerMismatchField mismatch) =>
        OwnerCorrectionTelemetry.Create(
            reason,
            null,
            new AppliedCorrectionDelta(
                new SimulationInstant(12),
                new SimulationInstant(10),
                new OwnerCorrectionError(1d, 0d, 0d, 0d, 0d)),
            mismatch);

    private static OwnerCorrectionTelemetry CreateComparisonRebase(
        OwnerCorrectionReason reason,
        OwnerMismatchField mismatch) =>
        OwnerCorrectionTelemetry.Create(
            reason,
            new AuthorityFrameErrorMeasurement(
                new SimulationInstant(10),
                new OwnerCorrectionError(1d, 0d, 0d, 0d, 0d)),
            new AppliedCorrectionDelta(
                new SimulationInstant(12),
                new SimulationInstant(10),
                new OwnerCorrectionError(1d, 0d, 0d, 0d, 0d)),
            mismatch);

    private static JsonElement FormatSingleCorrection(OwnerCorrectionTelemetry correction)
    {
        var ring = new PredictionTelemetryRing<OwnerCorrectionTelemetry>(1);
        ring.Add(correction);
        using var document = JsonDocument.Parse(
            PredictionTraceFormatter.Format(
                CreateBaseline(),
                OwnerInputTelemetry.Empty,
                ring));
        return document.RootElement.GetProperty("corrections")[0].Clone();
    }

    private static string ExpectedWireName(OwnerCorrectionDisposition disposition) =>
        disposition switch
        {
            OwnerCorrectionDisposition.Confirmed => "confirmed",
            OwnerCorrectionDisposition.OrdinaryReplay => "ordinary_replay",
            OwnerCorrectionDisposition.ContactReplay => "contact_replay",
            OwnerCorrectionDisposition.HardRebase => "hard_rebase",
            _ => throw new ArgumentOutOfRangeException(nameof(disposition), disposition, null),
        };

    private static string ExpectedWireName(OwnerCorrectionReason reason) =>
        reason switch
        {
            OwnerCorrectionReason.ConfirmedWithinTolerance => "confirmed_within_tolerance",
            OwnerCorrectionReason.OrdinaryStateDivergence => "ordinary_state_divergence",
            OwnerCorrectionReason.ContactDivergence => "contact_divergence",
            OwnerCorrectionReason.HistoryMiss => "history_miss",
            OwnerCorrectionReason.MatchFrameEpochChanged => "match_frame_epoch_changed",
            OwnerCorrectionReason.LifeEpochChanged => "life_epoch_changed",
            OwnerCorrectionReason.AuthorityDiscontinuityChanged =>
                "authority_discontinuity_changed",
            OwnerCorrectionReason.OwnerControlEpochChanged => "owner_control_epoch_changed",
            OwnerCorrectionReason.ConfigurationHistoryPolicyExhausted =>
                "configuration_history_policy_exhausted",
            OwnerCorrectionReason.UnrecoverablePenetration => "unrecoverable_penetration",
            OwnerCorrectionReason.ExtremeError => "extreme_error",
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
        };

    private static string ExpectedWireName(OwnerMismatchField field) =>
        field switch
        {
            OwnerMismatchField.None => "none",
            OwnerMismatchField.HorizontalPosition => "horizontal_position",
            OwnerMismatchField.VerticalPosition => "vertical_position",
            OwnerMismatchField.LinearVelocity => "linear_velocity",
            OwnerMismatchField.Facing => "facing",
            OwnerMismatchField.Contact => "contact",
            OwnerMismatchField.Grounded => "grounded",
            OwnerMismatchField.Locomotion => "locomotion",
            OwnerMismatchField.Posture => "posture",
            OwnerMismatchField.JumpState => "jump_state",
            OwnerMismatchField.RollState => "roll_state",
            OwnerMismatchField.ActionState => "action_state",
            OwnerMismatchField.CollisionProfile => "collision_profile",
            OwnerMismatchField.Support => "support",
            OwnerMismatchField.SurfaceBehavior => "surface_behavior",
            OwnerMismatchField.MovementSources => "movement_sources",
            OwnerMismatchField.TransitionCursor => "transition_cursor",
            OwnerMismatchField.MatchFrameEpoch => "match_frame_epoch",
            OwnerMismatchField.LifeEpoch => "life_epoch",
            OwnerMismatchField.AuthorityDiscontinuity => "authority_discontinuity",
            OwnerMismatchField.OwnerControlEpoch => "owner_control_epoch",
            OwnerMismatchField.MovementRevision => "movement_revision",
            OwnerMismatchField.CapabilityRevision => "capability_revision",
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
        };

    private static void AssertNoSensitiveProperties(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Assert.False(
                        IsSensitivePropertyName(property.Name),
                        $"Trace property '{property.Name}' appears to contain sensitive data.");
                    AssertNoSensitiveProperties(property.Value);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    AssertNoSensitiveProperties(item);
                }

                break;
        }
    }

    private static bool IsSensitivePropertyName(string propertyName)
    {
        var segments = propertyName.Split(
            ['_', '-', '.'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Any(SensitivePropertySegments.Contains);
    }
}
