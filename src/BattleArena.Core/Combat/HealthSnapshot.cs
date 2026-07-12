using BattleArena.Core.Common;

namespace BattleArena.Core.Combat;

public sealed record HealthSnapshot(
    CombatantId CombatantId,
    double BaseMaximumHealth,
    double EquipmentMaximumHealth,
    double EffectiveMaximumHealth,
    double CurrentHealth,
    long Revision,
    bool IsEliminated);
