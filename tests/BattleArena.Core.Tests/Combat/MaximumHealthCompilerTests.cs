using BattleArena.Core.Combat;
using BattleArena.Core.Common;

namespace BattleArena.Core.Tests.Combat;

public sealed class MaximumHealthCompilerTests
{
    [Fact]
    public void ContributionsApplyInAcquisitionOrder()
    {
        var compiler = new MaximumHealthCompiler();
        var contributions = new[]
        {
            Contribution(2, 2, ValueOperationKind.Multiply, 2d),
            Contribution(1, 1, ValueOperationKind.Add, 50d),
        };

        var snapshot = compiler.Compile(100d, contributions, []);

        Assert.Equal(300d, snapshot.EquipmentMaximumHealth, precision: 10);
        Assert.Equal(300d, snapshot.EffectiveMaximumHealth, precision: 10);
    }

    [Fact]
    public void ReversingAcquisitionOrderChangesMixedOperationResult()
    {
        var compiler = new MaximumHealthCompiler();
        var contributions = new[]
        {
            Contribution(1, 1, ValueOperationKind.Multiply, 2d),
            Contribution(2, 2, ValueOperationKind.Add, 50d),
        };

        var snapshot = compiler.Compile(100d, contributions, []);

        Assert.Equal(250d, snapshot.EquipmentMaximumHealth, precision: 10);
        Assert.Equal(250d, snapshot.EffectiveMaximumHealth, precision: 10);
    }

    [Fact]
    public void EffectiveMaximumHealthClampsToOne()
    {
        var compiler = new MaximumHealthCompiler();

        var snapshot = compiler.Compile(
            100d,
            [Contribution(1, 1, ValueOperationKind.Replace, -500d)],
            []);

        Assert.Equal(1d, snapshot.EquipmentMaximumHealth);
        Assert.Equal(1d, snapshot.EffectiveMaximumHealth);
    }

    [Fact]
    public void DuplicateContributionIdsAreRejected()
    {
        var compiler = new MaximumHealthCompiler();
        var contributions = new[]
        {
            Contribution(1, 1, ValueOperationKind.Add, 50d),
            Contribution(1, 2, ValueOperationKind.Add, 25d),
        };

        Assert.Throws<ArgumentException>(() => compiler.Compile(100d, contributions, []));
    }

    [Fact]
    public void RuntimeContributionsApplyAfterEquipmentContributions()
    {
        var compiler = new MaximumHealthCompiler();
        var equipment = new[]
        {
            Contribution(1, 1, ValueOperationKind.Multiply, 1.5d),
        };
        var runtime = new[]
        {
            Contribution(2, 1, ValueOperationKind.Add, 100d),
            Contribution(3, 2, ValueOperationKind.Multiply, 2d),
        };

        var snapshot = compiler.Compile(100d, equipment, runtime);

        Assert.Equal(100d, snapshot.BaseMaximumHealth);
        Assert.Equal(150d, snapshot.EquipmentMaximumHealth, precision: 10);
        Assert.Equal(500d, snapshot.EffectiveMaximumHealth, precision: 10);
    }

    [Fact]
    public void EachLayerClampsToAValidCheckpointBeforeTheNextLayer()
    {
        var compiler = new MaximumHealthCompiler();
        var equipment = new[]
        {
            Contribution(1, 1, ValueOperationKind.Replace, -500d),
        };
        var runtime = new[]
        {
            Contribution(2, 1, ValueOperationKind.Add, 100d),
        };

        var snapshot = compiler.Compile(100d, equipment, runtime);

        Assert.Equal(1d, snapshot.EquipmentMaximumHealth);
        Assert.Equal(101d, snapshot.EffectiveMaximumHealth, precision: 10);
    }

    private static MaximumHealthContribution Contribution(
        long id,
        long sequence,
        ValueOperationKind operation,
        double operand) =>
        new(
            new ContributionId(id),
            new InstallationSequence(sequence),
            new ValueOperation(operation, operand));
}
