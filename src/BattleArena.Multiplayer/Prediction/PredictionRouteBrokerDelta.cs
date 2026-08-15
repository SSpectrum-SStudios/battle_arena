using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Prediction;

public sealed record PredictionRouteBrokerDelta(
    PeerRosterUpdate? Roster,
    IReadOnlyList<PredictionRouteAuthorization> Authorizations,
    IReadOnlyList<PredictionRouteRevoked> Revocations)
{
    public static PredictionRouteBrokerDelta Empty { get; } = new(null, [], []);
}
