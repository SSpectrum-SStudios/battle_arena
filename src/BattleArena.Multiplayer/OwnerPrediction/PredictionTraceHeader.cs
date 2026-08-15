using System.Buffers;
using System.Text;
using System.Text.Json;

namespace BattleArena.Multiplayer.OwnerPrediction;

/// <summary>
/// The single canonical, credential-safe header writer shared by standalone
/// headers and complete prediction traces.
/// </summary>
public static class PredictionTraceHeader
{
    public const uint CurrentSchemaVersion = 2;
    public const string TraceKind = "battle_arena.owner_prediction";

    public static string Serialize(PredictionBaselineIdentity baselineIdentity)
    {
        ArgumentNullException.ThrowIfNull(baselineIdentity);

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(
                   buffer,
                   new JsonWriterOptions
                   {
                       Indented = true,
                       SkipValidation = false,
                   }))
        {
            Write(writer, baselineIdentity);
        }

        return Encoding.UTF8
            .GetString(buffer.WrittenSpan)
            .Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    internal static void Write(
        Utf8JsonWriter writer,
        PredictionBaselineIdentity baselineIdentity,
        string? propertyName = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(baselineIdentity);

        if (propertyName is null)
        {
            writer.WriteStartObject();
        }
        else
        {
            writer.WriteStartObject(propertyName);
        }

        writer.WriteNumber("trace_schema_version", CurrentSchemaVersion);
        writer.WriteString("trace_kind", TraceKind);
        writer.WriteString("source_revision", baselineIdentity.SourceRevision);
        writer.WriteBoolean("source_is_dirty", baselineIdentity.SourceIsDirty);
        writer.WriteNumber("build_id", baselineIdentity.BuildId);
        writer.WriteNumber("protocol_version", baselineIdentity.ProtocolVersion);
        writer.WriteNumber(
            "simulation_ticks_per_second",
            baselineIdentity.SimulationRate.TicksPerSecond);
        writer.WriteString(
            "movement_profile_sha256",
            baselineIdentity.MovementProfileSha256);
        writer.WriteEndObject();
    }
}
