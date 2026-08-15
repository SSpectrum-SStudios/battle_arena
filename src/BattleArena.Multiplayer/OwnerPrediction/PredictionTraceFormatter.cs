using System.Buffers;
using System.Text;
using System.Text.Json;

namespace BattleArena.Multiplayer.OwnerPrediction;

/// <summary>
/// Exports only the credential-free prediction diagnostic model. The API has no
/// parameter for Steam identities, tickets, route secrets, endpoints, display
/// names, or packet payloads.
/// </summary>
public static class PredictionTraceFormatter
{
    public const int MaximumCorrectionSamples = 4_096;
    public const int MaximumEncodedBytes = 8 * 1024 * 1024;

    public static string Format(
        PredictionBaselineIdentity baselineIdentity,
        OwnerInputTelemetry inputTelemetry,
        PredictionTelemetryRing<OwnerCorrectionTelemetry> correctionTelemetry)
    {
        ArgumentNullException.ThrowIfNull(baselineIdentity);
        ArgumentNullException.ThrowIfNull(correctionTelemetry);

        if (correctionTelemetry.Count > MaximumCorrectionSamples)
        {
            throw new ArgumentOutOfRangeException(
                nameof(correctionTelemetry),
                correctionTelemetry.Count,
                $"A trace cannot contain more than {MaximumCorrectionSamples} corrections.");
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(
                   buffer,
                   new JsonWriterOptions
                   {
                       Indented = true,
                       SkipValidation = false,
                   }))
        {
            writer.WriteStartObject();
            PredictionTraceHeader.Write(writer, baselineIdentity, "header");
            WriteInputTelemetry(writer, inputTelemetry);
            WriteCorrections(writer, correctionTelemetry);
            writer.WriteEndObject();
        }

        if (buffer.WrittenCount > MaximumEncodedBytes)
        {
            throw new InvalidOperationException(
                $"The encoded prediction trace exceeded {MaximumEncodedBytes} bytes.");
        }

        return Encoding.UTF8
            .GetString(buffer.WrittenSpan)
            .Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static void WriteInputTelemetry(
        Utf8JsonWriter writer,
        OwnerInputTelemetry telemetry)
    {
        writer.WriteStartObject("input_telemetry");
        writer.WriteNumber("generated_commands", telemetry.GeneratedCommands);
        writer.WriteNumber("command_send_attempts", telemetry.CommandSendAttempts);
        writer.WriteNumber("newly_accepted_commands", telemetry.NewlyAcceptedCommands);
        writer.WriteNumber("late_commands", telemetry.LateCommands);
        writer.WriteNumber("duplicate_commands", telemetry.DuplicateCommands);
        writer.WriteNumber("rejected_commands", telemetry.RejectedCommands);
        writer.WriteNumber("applied_received_commands", telemetry.AppliedReceivedCommands);
        writer.WriteNumber(
            "repeated_continuous_decisions",
            telemetry.RepeatedContinuousDecisions);
        writer.WriteNumber(
            "neutral_fallback_decisions",
            telemetry.NeutralFallbackDecisions);
        writer.WriteNumber(
            "authority_override_decisions",
            telemetry.AuthorityOverrideDecisions);
        writer.WriteEndObject();
    }

    private static void WriteCorrections(
        Utf8JsonWriter writer,
        PredictionTelemetryRing<OwnerCorrectionTelemetry> corrections)
    {
        writer.WriteStartArray("corrections");
        for (var index = 0; index < corrections.Count; index++)
        {
            var correction = corrections[index];
            if (!correction.IsInitialized)
            {
                throw new ArgumentException(
                    $"Correction sample {index} is uninitialized.",
                    nameof(corrections));
            }

            writer.WriteStartObject();
            writer.WriteString("disposition", WireName(correction.Disposition));
            writer.WriteString("reason", WireName(correction.Reason));
            writer.WriteNumber("replay_depth", correction.ReplayDepth);
            writer.WriteString("first_mismatch", WireName(correction.FirstMismatch));

            if (correction.AuthorityFrameErrorBefore is { } comparison)
            {
                writer.WriteStartObject("authority_frame_error_before");
                writer.WriteNumber("frame", comparison.Frame.Tick);
                WriteError(writer, "error", comparison.Error);
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteNull("authority_frame_error_before");
            }

            writer.WriteStartObject("applied_correction");
            writer.WriteNumber("before_frame", correction.AppliedCorrection.BeforeFrame.Tick);
            writer.WriteNumber("after_frame", correction.AppliedCorrection.AfterFrame.Tick);
            WriteError(writer, "delta", correction.AppliedCorrection.Error);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteError(
        Utf8JsonWriter writer,
        string propertyName,
        OwnerCorrectionError error)
    {
        writer.WriteStartObject(propertyName);
        writer.WriteNumber("horizontal_position_meters", error.HorizontalPositionMeters);
        writer.WriteNumber("vertical_position_meters", error.VerticalPositionMeters);
        writer.WriteNumber(
            "linear_velocity_meters_per_second",
            error.LinearVelocityMetersPerSecond);
        writer.WriteNumber("facing_radians", error.FacingRadians);
        writer.WriteNumber("contact_normal_radians", error.ContactNormalRadians);
        writer.WriteEndObject();
    }

    private static string WireName(OwnerCorrectionDisposition disposition) =>
        disposition switch
        {
            OwnerCorrectionDisposition.Confirmed => "confirmed",
            OwnerCorrectionDisposition.OrdinaryReplay => "ordinary_replay",
            OwnerCorrectionDisposition.ContactReplay => "contact_replay",
            OwnerCorrectionDisposition.HardRebase => "hard_rebase",
            _ => throw new ArgumentOutOfRangeException(
                nameof(disposition),
                disposition,
                "The correction disposition has no trace wire name."),
        };

    private static string WireName(OwnerCorrectionReason reason) =>
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
            _ => throw new ArgumentOutOfRangeException(
                nameof(reason),
                reason,
                "The correction reason has no trace wire name."),
        };

    private static string WireName(OwnerMismatchField field) =>
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
            _ => throw new ArgumentOutOfRangeException(
                nameof(field),
                field,
                "The mismatch field has no trace wire name."),
        };
}
