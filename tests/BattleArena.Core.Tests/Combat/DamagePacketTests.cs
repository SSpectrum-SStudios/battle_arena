using BattleArena.Core.Combat;
using BattleArena.Core.Common;

namespace BattleArena.Core.Tests.Combat;

public sealed class DamagePacketTests
{
    [Fact]
    public void NegativeDamageIsNormalizedToZero()
    {
        var portion = new DamagePortion(DamageType.Physical, -25d);

        Assert.Equal(0d, portion.Amount);
    }

    [Fact]
    public void NonFiniteDamageIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DamagePortion(DamageType.Fire, double.NaN));
    }

    [Fact]
    public void PacketDefensivelyCopiesItsPortions()
    {
        var source = new CombatantId(1);
        var original = new[] { new DamagePortion(DamageType.Physical, 10d) };
        var packet = new DamagePacket(source, original);

        original[0] = new DamagePortion(DamageType.Fire, 999d);

        Assert.Equal(DamageType.Physical, packet.Portions[0].Type);
        Assert.Equal(10d, packet.Portions[0].Amount);
    }

    [Fact]
    public void PacketRequiresAtLeastOnePortion()
    {
        Assert.Throws<ArgumentException>(
            () => new DamagePacket(new CombatantId(1), []));
    }

    [Fact]
    public void CombatantIdMustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CombatantId(0));
    }
}
