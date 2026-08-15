#nullable enable

using BattleArena.Protocol.V1;

namespace BattleArena.GodotNetworking;

/// <summary>
/// Stable presentation state for one combatant. The avatar entity survives
/// death and respawn; only its life-owned state is reset.
/// </summary>
public readonly record struct NetworkAvatarStatus(
    string DisplayLabel,
    long CurrentHealth,
    long MaximumHealth,
    ReplicatedLifeState LifeState,
    double RespawnSecondsRemaining);
