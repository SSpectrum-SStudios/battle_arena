using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Prediction;

public interface IMovementPredictionBundleFactory
{
    MovementPredictionBundle Create(
        ulong bundleSequence,
        ulong sourceCombatantId,
        ulong sourceLifeId,
        IReadOnlyCollection<ClientInputFrame> commandHistory,
        PredictedMovementState? rollbackState);
}
