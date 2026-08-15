namespace BattleArena.Multiplayer.Protocol;

public readonly record struct PredictionProtocolValidationContext(
    ulong ExpectedSessionId,
    ulong LocalSessionPeerId,
    ulong RemoteSessionPeerId,
    uint ExpectedLocalPeerSessionGeneration,
    uint ExpectedRemotePeerSessionGeneration,
    uint ExpectedPredictionRouteGeneration,
    ulong RouteExpiresAuthorityTick,
    ulong CurrentEstimatedAuthorityTick,
    bool RouteAuthenticated);
