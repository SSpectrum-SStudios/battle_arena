using BattleArena.Core.Combat;
using BattleArena.Core.Common;

namespace BattleArena.Core.Tests.Combat;

public sealed class CombatResolverTests
{
    private readonly CombatResolver _resolver = new();
    private readonly ResistanceProfileCompiler _profileCompiler = new();

    [Fact]
    public void ResolvesMixedDamageAndOverResistanceHealingIndependently()
    {
        var packet = Packet(
            new DamagePortion(DamageType.Physical, 100d),
            new DamagePortion(DamageType.Fire, 100d));
        var profile = Profile(
            TypePercentage(1, 1, DamageType.Physical, 0.25d),
            TypePercentage(2, 2, DamageType.Fire, 1.20d));

        var result = _resolver.Resolve(packet, profile);

        Assert.Equal(75d, result.TotalDamage, precision: 10);
        Assert.Equal(20d, result.TotalHealing, precision: 10);
        Assert.Collection(
            result.Portions,
            physical =>
            {
                Assert.Equal(DamageType.Physical, physical.Type);
                Assert.Equal(75d, physical.Damage, precision: 10);
                Assert.Equal(0d, physical.Healing);
            },
            fire =>
            {
                Assert.Equal(DamageType.Fire, fire.Type);
                Assert.Equal(0d, fire.Damage);
                Assert.Equal(20d, fire.Healing, precision: 10);
            });
    }

    [Fact]
    public void TypeSpecificResistanceHealsOnlyFromExcessAboveOneHundredPercent()
    {
        var result = _resolver.Resolve(
            Packet(new DamagePortion(DamageType.Fire, 100d)),
            Profile(TypePercentage(1, 1, DamageType.Fire, 1.20d)));

        Assert.Equal(0d, result.TotalDamage);
        Assert.Equal(20d, result.TotalHealing, precision: 10);
    }

    [Fact]
    public void NegativeTypeResistanceActsAsWeakness()
    {
        var result = _resolver.Resolve(
            Packet(new DamagePortion(DamageType.Fire, 100d)),
            Profile(TypePercentage(1, 1, DamageType.Fire, -0.5d)));

        Assert.Equal(150d, result.TotalDamage, precision: 10);
        Assert.Equal(0d, result.TotalHealing);
    }

    [Fact]
    public void ExactTypeImmunityCanResolveToZero()
    {
        var result = _resolver.Resolve(
            Packet(new DamagePortion(DamageType.Frost, 100d)),
            Profile(TypePercentage(1, 1, DamageType.Frost, 1d)));

        Assert.Equal(0d, result.TotalDamage);
        Assert.Equal(0d, result.TotalHealing);
    }

    [Fact]
    public void TypeSpecificFlatResistanceCanResolveToZero()
    {
        var result = _resolver.Resolve(
            Packet(new DamagePortion(DamageType.Physical, 5d)),
            Profile(TypeFlat(1, 1, DamageType.Physical, 10d)));

        Assert.Equal(0d, result.TotalDamage);
    }

    [Fact]
    public void GeneralResistanceCannotConvertDamageIntoHealing()
    {
        var result = _resolver.Resolve(
            Packet(new DamagePortion(DamageType.Arcane, 100d)),
            Profile(GeneralPercentage(1, 1, 1.5d)));

        Assert.Equal(1d, result.TotalDamage);
        Assert.Equal(0d, result.TotalHealing);
    }

    [Fact]
    public void GeneralFlatResistanceClampsASurvivingHitToOne()
    {
        var result = _resolver.Resolve(
            Packet(new DamagePortion(DamageType.Physical, 5d)),
            Profile(GeneralFlat(1, 1, 10d)));

        Assert.Equal(1d, result.TotalDamage);
    }

    [Fact]
    public void GeneralResistanceDoesNotResurrectDamageStoppedByTypedResistance()
    {
        var result = _resolver.Resolve(
            Packet(new DamagePortion(DamageType.Poison, 5d)),
            Profile(
                TypeFlat(1, 1, DamageType.Poison, 10d),
                GeneralPercentage(2, 2, 0.5d)));

        Assert.Equal(0d, result.TotalDamage);
    }

    [Fact]
    public void ZeroDamageRemainsZero()
    {
        var result = _resolver.Resolve(
            Packet(new DamagePortion(DamageType.Lightning, -50d)),
            Profile(GeneralPercentage(1, 1, 0.5d)));

        Assert.Equal(0d, result.TotalDamage);
        Assert.Equal(0d, result.TotalHealing);
    }

    [Fact]
    public void ResultPreservesSourceIdentity()
    {
        var source = new CombatantId(42);
        var packet = new DamagePacket(source, [new DamagePortion(DamageType.Physical, 10d)]);

        var result = _resolver.Resolve(packet, Profile());

        Assert.Equal(source, result.SourceId);
    }

    private static DamagePacket Packet(params DamagePortion[] portions) =>
        new(new CombatantId(1), portions);

    private ResistanceProfile Profile(params ResistanceContribution[] contributions) =>
        _profileCompiler.Compile(contributions);

    private static ResistanceContribution TypePercentage(
        long id,
        long sequence,
        DamageType type,
        double amount) =>
        Contribution(
            id,
            sequence,
            ResistanceStatKey.ForDamageType(type, ResistanceMeasure.Percentage),
            amount);

    private static ResistanceContribution TypeFlat(
        long id,
        long sequence,
        DamageType type,
        double amount) =>
        Contribution(
            id,
            sequence,
            ResistanceStatKey.ForDamageType(type, ResistanceMeasure.Flat),
            amount);

    private static ResistanceContribution GeneralPercentage(
        long id,
        long sequence,
        double amount) =>
        Contribution(
            id,
            sequence,
            ResistanceStatKey.General(ResistanceMeasure.Percentage),
            amount);

    private static ResistanceContribution GeneralFlat(
        long id,
        long sequence,
        double amount) =>
        Contribution(
            id,
            sequence,
            ResistanceStatKey.General(ResistanceMeasure.Flat),
            amount);

    private static ResistanceContribution Contribution(
        long id,
        long sequence,
        ResistanceStatKey stat,
        double amount) =>
        new(
            new ContributionId(id),
            new InstallationSequence(sequence),
            stat,
            new ValueOperation(ValueOperationKind.Add, amount));
}
