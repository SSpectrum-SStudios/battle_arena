using BattleArena.Multiplayer.OwnerPrediction;

namespace BattleArena.Multiplayer.Tests.OwnerPrediction;

public sealed class OwnerInputTelemetryTests
{
    public static IEnumerable<object[]> OriginEvents =>
        Enum.GetValues<OwnerInputOriginEvent>().Select(value => new object[] { value });

    public static IEnumerable<object[]> ArrivalDispositions =>
        Enum.GetValues<OwnerInputArrivalDisposition>().Select(value => new object[] { value });

    public static IEnumerable<object[]> ApplicationKinds =>
        Enum.GetValues<AuthorityInputApplicationKind>().Select(value => new object[] { value });

    [Fact]
    public void EmptyStartsEveryCounterAtZero()
    {
        Assert.Equal(default, OwnerInputTelemetry.Empty);
        Assert.All(ReadAllCounters(OwnerInputTelemetry.Empty), count => Assert.Equal(0UL, count));
    }

    [Theory]
    [MemberData(nameof(OriginEvents))]
    public void OriginEventIncrementsOnlyItsExplicitCounter(OwnerInputOriginEvent originEvent)
    {
        var updated = OwnerInputTelemetry.Empty.Record(originEvent);

        Assert.Equal(1UL, ReadCounter(updated, originEvent));
        Assert.Equal(1, ReadAllCounters(updated).Count(count => count != 0));
    }

    [Theory]
    [MemberData(nameof(ArrivalDispositions))]
    public void ArrivalDispositionIncrementsOnlyItsExplicitCounter(
        OwnerInputArrivalDisposition disposition)
    {
        var updated = OwnerInputTelemetry.Empty.Record(disposition);

        Assert.Equal(1UL, ReadCounter(updated, disposition));
        Assert.Equal(1, ReadAllCounters(updated).Count(count => count != 0));
    }

    [Theory]
    [MemberData(nameof(ApplicationKinds))]
    public void ApplicationKindIncrementsOnlyItsExplicitCounter(
        AuthorityInputApplicationKind applicationKind)
    {
        var updated = OwnerInputTelemetry.Empty.Record(applicationKind);

        Assert.Equal(1UL, ReadCounter(updated, applicationKind));
        Assert.Equal(1, ReadAllCounters(updated).Count(count => count != 0));
    }

    [Fact]
    public void CategoriesAccumulateIndependentlyWithoutMutatingPriorSnapshot()
    {
        var original = OwnerInputTelemetry.Empty.Record(
            OwnerInputOriginEvent.CommandGenerated);

        var updated = original
            .Record(OwnerInputArrivalDisposition.NewCommandAccepted)
            .Record(AuthorityInputApplicationKind.ReceivedCommand);

        Assert.Equal(1UL, original.GeneratedCommands);
        Assert.Equal(0UL, original.NewlyAcceptedCommands);
        Assert.Equal(0UL, original.AppliedReceivedCommands);
        Assert.Equal(1UL, updated.GeneratedCommands);
        Assert.Equal(1UL, updated.NewlyAcceptedCommands);
        Assert.Equal(1UL, updated.AppliedReceivedCommands);
    }

    [Fact]
    public void BulkOccurrencesAccumulateExactly()
    {
        var telemetry = OwnerInputTelemetry.Empty
            .Record(OwnerInputOriginEvent.CommandSendAttempted, 5)
            .Record(OwnerInputArrivalDisposition.DuplicateCommand, 3)
            .Record(AuthorityInputApplicationKind.NeutralFallback, 2);

        Assert.Equal(5UL, telemetry.CommandSendAttempts);
        Assert.Equal(3UL, telemetry.DuplicateCommands);
        Assert.Equal(2UL, telemetry.NeutralFallbackDecisions);
    }

    [Fact]
    public void UndefinedValuesAreRejectedInEveryCategory()
    {
        var telemetry = OwnerInputTelemetry.Empty;

        Assert.Equal(
            "originEvent",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => telemetry.Record((OwnerInputOriginEvent)99)).ParamName);
        Assert.Equal(
            "disposition",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => telemetry.Record((OwnerInputArrivalDisposition)99)).ParamName);
        Assert.Equal(
            "applicationKind",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => telemetry.Record((AuthorityInputApplicationKind)99)).ParamName);
        Assert.Equal(OwnerInputTelemetry.Empty, telemetry);
    }

    [Theory]
    [MemberData(nameof(OriginEvents))]
    public void OriginCounterOverflowIsChecked(OwnerInputOriginEvent originEvent)
    {
        var atMaximum = OwnerInputTelemetry.Empty.Record(originEvent, ulong.MaxValue);

        Assert.Throws<OverflowException>(() => atMaximum.Record(originEvent));
        Assert.Equal(ulong.MaxValue, ReadCounter(atMaximum, originEvent));
    }

    [Theory]
    [MemberData(nameof(ArrivalDispositions))]
    public void ArrivalCounterOverflowIsChecked(OwnerInputArrivalDisposition disposition)
    {
        var atMaximum = OwnerInputTelemetry.Empty.Record(disposition, ulong.MaxValue);

        Assert.Throws<OverflowException>(() => atMaximum.Record(disposition));
        Assert.Equal(ulong.MaxValue, ReadCounter(atMaximum, disposition));
    }

    [Theory]
    [MemberData(nameof(ApplicationKinds))]
    public void ApplicationCounterOverflowIsChecked(
        AuthorityInputApplicationKind applicationKind)
    {
        var atMaximum = OwnerInputTelemetry.Empty.Record(applicationKind, ulong.MaxValue);

        Assert.Throws<OverflowException>(() => atMaximum.Record(applicationKind));
        Assert.Equal(ulong.MaxValue, ReadCounter(atMaximum, applicationKind));
    }

    private static ulong ReadCounter(
        OwnerInputTelemetry telemetry,
        OwnerInputOriginEvent originEvent) =>
        originEvent switch
        {
            OwnerInputOriginEvent.CommandGenerated => telemetry.GeneratedCommands,
            OwnerInputOriginEvent.CommandSendAttempted => telemetry.CommandSendAttempts,
            _ => throw new ArgumentOutOfRangeException(nameof(originEvent)),
        };

    private static ulong ReadCounter(
        OwnerInputTelemetry telemetry,
        OwnerInputArrivalDisposition disposition) =>
        disposition switch
        {
            OwnerInputArrivalDisposition.NewCommandAccepted => telemetry.NewlyAcceptedCommands,
            OwnerInputArrivalDisposition.LateCommand => telemetry.LateCommands,
            OwnerInputArrivalDisposition.DuplicateCommand => telemetry.DuplicateCommands,
            OwnerInputArrivalDisposition.RejectedCommand => telemetry.RejectedCommands,
            _ => throw new ArgumentOutOfRangeException(nameof(disposition)),
        };

    private static ulong ReadCounter(
        OwnerInputTelemetry telemetry,
        AuthorityInputApplicationKind applicationKind) =>
        applicationKind switch
        {
            AuthorityInputApplicationKind.ReceivedCommand => telemetry.AppliedReceivedCommands,
            AuthorityInputApplicationKind.RepeatedContinuous =>
                telemetry.RepeatedContinuousDecisions,
            AuthorityInputApplicationKind.NeutralFallback =>
                telemetry.NeutralFallbackDecisions,
            AuthorityInputApplicationKind.AuthorityOverride =>
                telemetry.AuthorityOverrideDecisions,
            _ => throw new ArgumentOutOfRangeException(nameof(applicationKind)),
        };

    private static ulong[] ReadAllCounters(OwnerInputTelemetry telemetry) =>
    [
        telemetry.GeneratedCommands,
        telemetry.CommandSendAttempts,
        telemetry.NewlyAcceptedCommands,
        telemetry.LateCommands,
        telemetry.DuplicateCommands,
        telemetry.RejectedCommands,
        telemetry.AppliedReceivedCommands,
        telemetry.RepeatedContinuousDecisions,
        telemetry.NeutralFallbackDecisions,
        telemetry.AuthorityOverrideDecisions,
    ];
}
