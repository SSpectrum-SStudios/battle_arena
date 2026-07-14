using BattleArena.Multiplayer.Transport;

namespace BattleArena.Multiplayer.Connection;

public sealed record ConnectedPlayer(
    NetworkPeerId PeerId,
    ulong PlayerId,
    ulong CombatantId,
    string DisplayName,
    byte[] ReconnectToken);
