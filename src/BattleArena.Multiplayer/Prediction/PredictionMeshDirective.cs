using BattleArena.Multiplayer.Connection;

namespace BattleArena.Multiplayer.Prediction;

public enum PredictionMeshDirectiveKind
{
    Listen,
    Initiate,
    Remove,
}

public sealed record PredictionMeshDirective(
    PredictionMeshDirectiveKind Kind,
    SessionPeerId RemotePeerId,
    AuthorizedPredictionRoute? Route,
    ulong AttemptId);
