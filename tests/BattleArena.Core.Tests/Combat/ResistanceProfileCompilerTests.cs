using BattleArena.Core.Combat;
using BattleArena.Core.Common;

namespace BattleArena.Core.Tests.Combat;

public sealed class ResistanceProfileCompilerTests
{
    private static readonly ResistanceStatKey FirePercentage =
        ResistanceStatKey.ForDamageType(DamageType.Fire, ResistanceMeasure.Percentage);

    [Fact]
    public void ContributionsApplyInInstallationOrder()
    {
        var compiler = new ResistanceProfileCompiler();
        var contributions = new[]
        {
            Contribution(2, 2, ValueOperationKind.Multiply, 1.5d),
            Contribution(1, 1, ValueOperationKind.Add, 0.5d),
        };

        var profile = compiler.Compile(contributions);

        Assert.Equal(0.75d, profile.Get(FirePercentage), precision: 10);
    }

    [Fact]
    public void ReversingAcquisitionOrderChangesMixedOperationResult()
    {
        var compiler = new ResistanceProfileCompiler();
        var contributions = new[]
        {
            Contribution(1, 1, ValueOperationKind.Multiply, 1.5d),
            Contribution(2, 2, ValueOperationKind.Add, 0.5d),
        };

        var profile = compiler.Compile(contributions);

        Assert.Equal(0.5d, profile.Get(FirePercentage), precision: 10);
    }

    [Fact]
    public void ContributionIdProvidesDeterministicTieBreaking()
    {
        var compiler = new ResistanceProfileCompiler();
        var contributions = new[]
        {
            Contribution(2, 1, ValueOperationKind.Multiply, 2d),
            Contribution(1, 1, ValueOperationKind.Add, 0.5d),
        };

        var profile = compiler.Compile(contributions);

        Assert.Equal(1d, profile.Get(FirePercentage), precision: 10);
    }

    [Fact]
    public void DuplicateContributionIdsAreRejected()
    {
        var compiler = new ResistanceProfileCompiler();
        var contributions = new[]
        {
            Contribution(1, 1, ValueOperationKind.Add, 0.25d),
            Contribution(1, 2, ValueOperationKind.Add, 0.25d),
        };

        Assert.Throws<ArgumentException>(() => compiler.Compile(contributions));
    }

    private static ResistanceContribution Contribution(
        long id,
        long sequence,
        ValueOperationKind operation,
        double operand) =>
        new(
            new ContributionId(id),
            new InstallationSequence(sequence),
            FirePercentage,
            new ValueOperation(operation, operand));
}
