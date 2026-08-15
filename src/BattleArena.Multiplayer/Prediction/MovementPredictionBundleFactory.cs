using BattleArena.Multiplayer.Protocol;
using BattleArena.Protocol.V1;

namespace BattleArena.Multiplayer.Prediction;

public sealed class MovementPredictionBundleFactory : IMovementPredictionBundleFactory
{
    public MovementPredictionBundle Create(
        ulong bundleSequence,
        ulong sourceCombatantId,
        ulong sourceLifeId,
        IReadOnlyCollection<ClientInputFrame> commandHistory,
        PredictedMovementState? rollbackState)
    {
        ArgumentNullException.ThrowIfNull(commandHistory);
        if (bundleSequence == 0 || sourceCombatantId == 0 || sourceLifeId == 0 ||
            commandHistory.Count == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bundleSequence));
        }

        var commands = commandHistory
            .OrderBy(command => command.InputSequence)
            .TakeLast(ProtocolConstants.MaxPredictionCommandsPerBundle)
            .Select(command => command.Clone())
            .ToArray();
        var bundle = new MovementPredictionBundle
        {
            BundleSequence = bundleSequence,
            SourceCombatantId = sourceCombatantId,
            SourceLifeId = sourceLifeId,
            RollbackState = rollbackState?.Clone(),
        };
        bundle.Commands.AddRange(commands);
        var validation = InboundMessageValidator.ValidateMovementPredictionBundle(bundle);
        if (!validation.IsValid)
        {
            throw new ArgumentException(validation.Violation!.Message, nameof(commandHistory));
        }

        return bundle;
    }
}
