#nullable enable

using BattleArena.Core.Movement;
using BattleArena.Multiplayer.Replication;
using BattleArena.Protocol.V1;

namespace BattleArena.GodotNetworking;

public interface IRemoteMovementPredictor
{
    RemoteMovementPrediction Predict(
        RemoteMovementFrame<AuthoritativeMovementState> baseline,
        IReadOnlyList<AcceptedMovementCommand> commands,
        double targetAuthorityTick,
        MovementAttributeSnapshot attributes);
}
