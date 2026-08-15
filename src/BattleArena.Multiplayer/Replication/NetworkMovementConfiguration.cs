using BattleArena.Core.Common;
using BattleArena.Core.Movement;

namespace BattleArena.Multiplayer.Replication;

public sealed record NetworkMovementConfiguration(
    ulong CombatantId,
    ulong LifeId,
    SimulationInstant EffectiveAuthorityTick,
    MovementAttributeSnapshot Attributes,
    MovementCapabilitySnapshot Capabilities);
