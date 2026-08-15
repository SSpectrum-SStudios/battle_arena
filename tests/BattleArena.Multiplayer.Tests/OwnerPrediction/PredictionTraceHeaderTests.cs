using System.Text.Json;
using BattleArena.Core.Common;
using BattleArena.Multiplayer.OwnerPrediction;

namespace BattleArena.Multiplayer.Tests.OwnerPrediction;

public sealed class PredictionTraceHeaderTests
{
    private const string SourceRevision =
        "0123456789abcdef0123456789abcdef01234567";
    private const string MovementProfileSha256 =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string ExpectedHeader = """
        {
          "trace_schema_version": 2,
          "trace_kind": "battle_arena.owner_prediction",
          "source_revision": "0123456789abcdef0123456789abcdef01234567",
          "source_is_dirty": true,
          "build_id": 456789,
          "protocol_version": 8,
          "simulation_ticks_per_second": 60,
          "movement_profile_sha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
        }
        """;

    [Fact]
    public void SerializeProducesVersionTwoCanonicalGoldenHeader()
    {
        Assert.Equal(ExpectedHeader, PredictionTraceHeader.Serialize(CreateBaseline()));
    }

    [Fact]
    public void IdenticalBaselinesProduceIdenticalHeaders()
    {
        Assert.Equal(
            PredictionTraceHeader.Serialize(CreateBaseline()),
            PredictionTraceHeader.Serialize(CreateBaseline()));
    }

    [Fact]
    public void HeaderContainsOnlyCanonicalDiagnosticDomains()
    {
        using var document = JsonDocument.Parse(
            PredictionTraceHeader.Serialize(CreateBaseline()));
        var root = document.RootElement;

        Assert.Equal(2U, root.GetProperty("trace_schema_version").GetUInt32());
        Assert.Equal(SourceRevision, root.GetProperty("source_revision").GetString());
        Assert.True(root.GetProperty("source_is_dirty").GetBoolean());
        Assert.Equal(456_789UL, root.GetProperty("build_id").GetUInt64());
        Assert.Equal(
            MovementProfileSha256,
            root.GetProperty("movement_profile_sha256").GetString());
        Assert.DoesNotContain(
            root.EnumerateObject().Select(property => property.Name),
            name => name.Contains("fingerprint", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NullBaselineIsRejected()
    {
        Assert.Throws<ArgumentNullException>(
            () => PredictionTraceHeader.Serialize(null!));
    }

    private static PredictionBaselineIdentity CreateBaseline() =>
        new(
            sourceRevision: SourceRevision,
            sourceIsDirty: true,
            buildId: 456_789,
            protocolVersion: 8,
            simulationRate: SimulationRate.Default,
            movementProfileSha256: MovementProfileSha256);
}
