using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Multiplayer.Replication;

namespace BattleArena.Multiplayer.Tests.Replication;

public sealed class MovementConfigurationProtocolMapperTests
{
    [Fact]
    public void ResolvedMovementConfigurationRoundTripsEveryImplementedAttributeGroup()
    {
        var attributes = new MovementAttributeSnapshot(
            7,
            new GroundMovementAttributes(
                6, 13, 8, 10, 12, 20, 7, 2, -0.4,
                0.4, 0.5, Math.PI * 50 / 180, 0.08));
        var capabilities = MovementCapabilitySnapshot.CreateBaseFighter(revision: 9);
        var source = new NetworkMovementConfiguration(
            4, 5, new SimulationInstant(300), attributes, capabilities);

        var protocol = MovementConfigurationProtocolMapper.ToProtocol(source);
        var result = MovementConfigurationProtocolMapper.FromProtocol(protocol);

        Assert.Equal(7UL, result.Attributes.Revision);
        Assert.Equal(9UL, result.Capabilities.Revision);
        Assert.Equal(13d, result.Attributes.Ground.MaximumSprintSpeed);
        Assert.Equal(attributes.Air.LateralAirAcceleration, result.Attributes.Air.LateralAirAcceleration);
        Assert.Equal(attributes.Jump.FallingGravity, result.Attributes.Jump.FallingGravity);
        Assert.Equal(attributes.CrouchRoll.RollCooldown, result.Attributes.CrouchRoll.RollCooldown);
        Assert.True(result.Capabilities.CanMantle);
        Assert.Equal(300L, result.EffectiveAuthorityTick.Tick);
    }

    [Fact]
    public void InvalidResolvedConfigurationIsRejectedBeforeDomainConstruction()
    {
        var valid = MovementConfigurationProtocolMapper.ToProtocol(
            new NetworkMovementConfiguration(
                4,
                5,
                new SimulationInstant(300),
                new MovementAttributeSnapshot(
                    7,
                    new GroundMovementAttributes(6, 13, 8, 10, 12, 20, 7, 2, -0.4)),
                MovementCapabilitySnapshot.CreateBaseFighter(9)));
        valid.Jump.FallingGravity = double.NaN;

        Assert.Throws<ArgumentException>(() =>
            MovementConfigurationProtocolMapper.FromProtocol(valid));
    }
}
