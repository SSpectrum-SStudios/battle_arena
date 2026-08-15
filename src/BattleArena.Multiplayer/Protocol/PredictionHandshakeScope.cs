namespace BattleArena.Multiplayer.Protocol;

public readonly record struct PredictionHandshakeScope(
    ulong SessionId,
    ulong InitiatorSessionPeerId,
    uint InitiatorPeerSessionGeneration,
    ulong ResponderSessionPeerId,
    uint ResponderPeerSessionGeneration,
    uint PredictionRouteGeneration)
{
    public void Validate()
    {
        if (SessionId == 0 ||
            InitiatorSessionPeerId == 0 ||
            ResponderSessionPeerId == 0 ||
            InitiatorSessionPeerId == ResponderSessionPeerId ||
            InitiatorPeerSessionGeneration == 0 ||
            ResponderPeerSessionGeneration == 0 ||
            PredictionRouteGeneration == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(SessionId), "Handshake scope must identify one live peer pair and route.");
        }
    }
}
