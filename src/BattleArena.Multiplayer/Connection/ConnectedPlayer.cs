using BattleArena.Multiplayer.Transport;

namespace BattleArena.Multiplayer.Connection;

public sealed record ConnectedPlayer(
    TransportConnectionId ConnectionId,
    SessionPeerId SessionPeerId,
    ConnectionGeneration ConnectionGeneration,
    ulong PlayerId,
    ulong CombatantId,
    string DisplayName,
    byte[] ReconnectToken);
