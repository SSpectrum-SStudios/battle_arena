namespace BattleArena.Multiplayer.Prediction;

public interface IPredictionHandshakeSessionFactory
{
    IPredictionHandshakeSession Create(AuthorizedPredictionRoute route);
}
