namespace BattleArena.Multiplayer.OwnerPrediction;

public enum OwnerInputOriginEvent
{
    CommandGenerated = 0,
    CommandSendAttempted = 1,
}

/// <summary>
/// The one terminal ingress disposition assigned to an owner command after it
/// reaches authority validation and deduplication.
/// </summary>
public enum OwnerInputArrivalDisposition
{
    NewCommandAccepted = 0,
    LateCommand = 1,
    DuplicateCommand = 2,
    RejectedCommand = 3,
}

/// <summary>
/// The one application decision made for an active combatant on an authority
/// simulation frame.
/// </summary>
public enum AuthorityInputApplicationKind
{
    ReceivedCommand = 0,
    RepeatedContinuous = 1,
    NeutralFallback = 2,
    AuthorityOverride = 3,
}

/// <summary>
/// Immutable counters for owner input origin, authority arrival disposition,
/// and authority frame application. A redundant copy is a send attempt, while
/// each authority ingress command and active combatant/frame receives exactly
/// one value from its corresponding typed category.
/// </summary>
public readonly record struct OwnerInputTelemetry
{
    public static OwnerInputTelemetry Empty => default;

    private OwnerInputTelemetry(
        ulong generatedCommands,
        ulong commandSendAttempts,
        ulong newlyAcceptedCommands,
        ulong lateCommands,
        ulong duplicateCommands,
        ulong rejectedCommands,
        ulong appliedReceivedCommands,
        ulong repeatedContinuousDecisions,
        ulong neutralFallbackDecisions,
        ulong authorityOverrideDecisions)
    {
        GeneratedCommands = generatedCommands;
        CommandSendAttempts = commandSendAttempts;
        NewlyAcceptedCommands = newlyAcceptedCommands;
        LateCommands = lateCommands;
        DuplicateCommands = duplicateCommands;
        RejectedCommands = rejectedCommands;
        AppliedReceivedCommands = appliedReceivedCommands;
        RepeatedContinuousDecisions = repeatedContinuousDecisions;
        NeutralFallbackDecisions = neutralFallbackDecisions;
        AuthorityOverrideDecisions = authorityOverrideDecisions;
    }

    public ulong GeneratedCommands { get; }

    public ulong CommandSendAttempts { get; }

    public ulong NewlyAcceptedCommands { get; }

    public ulong LateCommands { get; }

    public ulong DuplicateCommands { get; }

    public ulong RejectedCommands { get; }

    public ulong AppliedReceivedCommands { get; }

    public ulong RepeatedContinuousDecisions { get; }

    public ulong NeutralFallbackDecisions { get; }

    public ulong AuthorityOverrideDecisions { get; }

    public OwnerInputTelemetry Record(
        OwnerInputOriginEvent originEvent,
        ulong occurrences = 1) =>
        originEvent switch
        {
            OwnerInputOriginEvent.CommandGenerated => Copy(
                generatedCommands: Add(GeneratedCommands, occurrences)),
            OwnerInputOriginEvent.CommandSendAttempted => Copy(
                commandSendAttempts: Add(CommandSendAttempts, occurrences)),
            _ => throw Unsupported(nameof(originEvent), originEvent),
        };

    public OwnerInputTelemetry Record(
        OwnerInputArrivalDisposition disposition,
        ulong occurrences = 1) =>
        disposition switch
        {
            OwnerInputArrivalDisposition.NewCommandAccepted => Copy(
                newlyAcceptedCommands: Add(NewlyAcceptedCommands, occurrences)),
            OwnerInputArrivalDisposition.LateCommand => Copy(
                lateCommands: Add(LateCommands, occurrences)),
            OwnerInputArrivalDisposition.DuplicateCommand => Copy(
                duplicateCommands: Add(DuplicateCommands, occurrences)),
            OwnerInputArrivalDisposition.RejectedCommand => Copy(
                rejectedCommands: Add(RejectedCommands, occurrences)),
            _ => throw Unsupported(nameof(disposition), disposition),
        };

    public OwnerInputTelemetry Record(
        AuthorityInputApplicationKind applicationKind,
        ulong occurrences = 1) =>
        applicationKind switch
        {
            AuthorityInputApplicationKind.ReceivedCommand => Copy(
                appliedReceivedCommands: Add(AppliedReceivedCommands, occurrences)),
            AuthorityInputApplicationKind.RepeatedContinuous => Copy(
                repeatedContinuousDecisions: Add(RepeatedContinuousDecisions, occurrences)),
            AuthorityInputApplicationKind.NeutralFallback => Copy(
                neutralFallbackDecisions: Add(NeutralFallbackDecisions, occurrences)),
            AuthorityInputApplicationKind.AuthorityOverride => Copy(
                authorityOverrideDecisions: Add(AuthorityOverrideDecisions, occurrences)),
            _ => throw Unsupported(nameof(applicationKind), applicationKind),
        };

    private OwnerInputTelemetry Copy(
        ulong? generatedCommands = null,
        ulong? commandSendAttempts = null,
        ulong? newlyAcceptedCommands = null,
        ulong? lateCommands = null,
        ulong? duplicateCommands = null,
        ulong? rejectedCommands = null,
        ulong? appliedReceivedCommands = null,
        ulong? repeatedContinuousDecisions = null,
        ulong? neutralFallbackDecisions = null,
        ulong? authorityOverrideDecisions = null) =>
        new(
            generatedCommands ?? GeneratedCommands,
            commandSendAttempts ?? CommandSendAttempts,
            newlyAcceptedCommands ?? NewlyAcceptedCommands,
            lateCommands ?? LateCommands,
            duplicateCommands ?? DuplicateCommands,
            rejectedCommands ?? RejectedCommands,
            appliedReceivedCommands ?? AppliedReceivedCommands,
            repeatedContinuousDecisions ?? RepeatedContinuousDecisions,
            neutralFallbackDecisions ?? NeutralFallbackDecisions,
            authorityOverrideDecisions ?? AuthorityOverrideDecisions);

    private static ulong Add(ulong current, ulong occurrences) =>
        checked(current + occurrences);

    private static ArgumentOutOfRangeException Unsupported<T>(
        string parameterName,
        T value)
        where T : struct, Enum =>
        new(
            parameterName,
            value,
            "The owner input telemetry value is not supported.");
}
