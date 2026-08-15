using BattleArena.Core.Common;
using BattleArena.Multiplayer.OwnerPrediction;

namespace BattleArena.Multiplayer.Tests.OwnerPrediction;

public sealed class PredictionBaselineIdentityTests
{
    private const string Sha1 = "0123456789abcdef0123456789abcdef01234567";
    private const string Sha256 =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void CanonicalIdentityPreservesEveryDomainValue()
    {
        var identity = new PredictionBaselineIdentity(
            sourceRevision: Sha1,
            sourceIsDirty: true,
            buildId: 456_789,
            protocolVersion: 8,
            simulationRate: new SimulationRate(60),
            movementProfileSha256: Sha256);

        Assert.Equal(Sha1, identity.SourceRevision);
        Assert.True(identity.SourceIsDirty);
        Assert.Equal(456_789UL, identity.BuildId);
        Assert.Equal(8U, identity.ProtocolVersion);
        Assert.Equal(60, identity.SimulationRate.TicksPerSecond);
        Assert.Equal(Sha256, identity.MovementProfileSha256);
    }

    [Fact]
    public void GitSha256RevisionIsSupported()
    {
        Assert.Equal(Sha256, Create(sourceRevision: Sha256).SourceRevision);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0123456789abcdef0123456789abcdef0123456")]
    [InlineData("0123456789abcdef0123456789abcdef012345678")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF01234567")]
    [InlineData("g123456789abcdef0123456789abcdef01234567")]
    [InlineData("203.0.113.42:27015")]
    [InlineData("STEAM-TICKET-CANARY-7fd892")]
    public void SourceRevisionRejectsNonCanonicalAndCanaryText(string? sourceRevision)
    {
        Assert.ThrowsAny<ArgumentException>(() => Create(sourceRevision: sourceRevision!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcde")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF")]
    [InlineData("z123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("Robert-Persona-Canary")]
    [InlineData("session-route-token-canary")]
    public void MovementProfileRejectsNonSha256AndCanaryText(string? movementHash)
    {
        Assert.ThrowsAny<ArgumentException>(
            () => Create(movementProfileSha256: movementHash!));
    }

    [Fact]
    public void BuildAndProtocolIdentifiersMustBePositive()
    {
        Assert.Equal(
            "buildId",
            Assert.Throws<ArgumentOutOfRangeException>(() => Create(buildId: 0)).ParamName);
        Assert.Equal(
            "protocolVersion",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => Create(protocolVersion: 0)).ParamName);
    }

    [Fact]
    public void DefaultSimulationRateIsRejected()
    {
        Assert.Equal(
            "simulationRate",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new PredictionBaselineIdentity(
                    Sha1,
                    sourceIsDirty: false,
                    buildId: 456_789,
                    protocolVersion: 8,
                    simulationRate: default,
                    movementProfileSha256: Sha256)).ParamName);
    }

    [Fact]
    public void PublicContractContainsOnlyCanonicalDiagnosticComponents()
    {
        var properties = typeof(PredictionBaselineIdentity).GetProperties();
        var propertyNames = properties.Select(property => property.Name).ToArray();
        string[] expectedPropertyNames =
        [
            nameof(PredictionBaselineIdentity.SourceRevision),
            nameof(PredictionBaselineIdentity.SourceIsDirty),
            nameof(PredictionBaselineIdentity.BuildId),
            nameof(PredictionBaselineIdentity.ProtocolVersion),
            nameof(PredictionBaselineIdentity.SimulationRate),
            nameof(PredictionBaselineIdentity.MovementProfileSha256),
        ];

        Assert.Equal(
            expectedPropertyNames.Order(StringComparer.Ordinal),
            propertyNames.Order(StringComparer.Ordinal));
        Assert.All(
            properties,
            property => Assert.True(
                property.PropertyType.IsValueType || property.PropertyType == typeof(string)));
    }

    private static PredictionBaselineIdentity Create(
        string sourceRevision = Sha1,
        bool sourceIsDirty = false,
        ulong buildId = 456_789,
        uint protocolVersion = 8,
        SimulationRate? simulationRate = null,
        string movementProfileSha256 = Sha256) =>
        new(
            sourceRevision,
            sourceIsDirty,
            buildId,
            protocolVersion,
            simulationRate ?? SimulationRate.Default,
            movementProfileSha256);
}
