using BattleArena.Multiplayer.Connection;
using BattleArena.Multiplayer.Replication;
using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Prediction;

public interface IAuthorityMovementDistribution
{
    AuthorityAcceptedMovementCommand AcceptAppliedCommand(
        SessionPeer authenticatedSource,
        ulong expectedLifeId,
        MovementPredictionBundle bundle,
        ulong appliedInputSequence,
        ulong appliedAuthorityTick);

    AuthorityAcceptedMovementCommand AcceptAppliedCommand(
        SessionPeer authenticatedSource,
        ulong expectedLifeId,
        RevisionedMovementCommand appliedCommand,
        ulong appliedAuthorityTick);
}
