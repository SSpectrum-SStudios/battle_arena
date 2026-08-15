using BattleArena.Core.Common;

namespace BattleArena.Multiplayer.OwnerPrediction;

/// <summary>
/// Identifies a prediction build using canonical diagnostic domains rather than
/// caller-defined text. It cannot carry persona, endpoint, route, or credential
/// strings.
/// </summary>
public sealed record PredictionBaselineIdentity
{
    public const int GitSha1Characters = 40;
    public const int GitSha256Characters = 64;
    public const int Sha256Characters = 64;

    public PredictionBaselineIdentity(
        string sourceRevision,
        bool sourceIsDirty,
        ulong buildId,
        uint protocolVersion,
        SimulationRate simulationRate,
        string movementProfileSha256)
    {
        SourceRevision = ValidateGitRevision(sourceRevision);
        SourceIsDirty = sourceIsDirty;

        if (buildId == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(buildId),
                "A diagnostic build ID must be positive.");
        }

        if (protocolVersion == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(protocolVersion),
                "A protocol version must be positive.");
        }

        if (simulationRate.TicksPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(simulationRate),
                "A simulation rate must be initialized and positive.");
        }

        BuildId = buildId;
        ProtocolVersion = protocolVersion;
        SimulationRate = simulationRate;
        MovementProfileSha256 = ValidateLowerHex(
            movementProfileSha256,
            Sha256Characters,
            nameof(movementProfileSha256));
    }

    public string SourceRevision { get; }

    public bool SourceIsDirty { get; }

    public ulong BuildId { get; }

    public uint ProtocolVersion { get; }

    public SimulationRate SimulationRate { get; }

    public string MovementProfileSha256 { get; }

    private static string ValidateGitRevision(string value)
    {
        ArgumentNullException.ThrowIfNull(value, "sourceRevision");
        if (value.Length is not (GitSha1Characters or GitSha256Characters))
        {
            throw new ArgumentOutOfRangeException(
                "sourceRevision",
                value.Length,
                $"A Git revision must contain {GitSha1Characters} or " +
                $"{GitSha256Characters} lowercase hexadecimal characters.");
        }

        return ValidateLowerHex(value, value.Length, "sourceRevision");
    }

    private static string ValidateLowerHex(
        string value,
        int requiredCharacters,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.Length != requiredCharacters)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value.Length,
                $"The value must contain exactly {requiredCharacters} characters.");
        }

        if (value.Any(character =>
                character is not (>= '0' and <= '9') and
                not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException(
                "The value must use canonical lowercase hexadecimal characters.",
                parameterName);
        }

        return value;
    }
}
