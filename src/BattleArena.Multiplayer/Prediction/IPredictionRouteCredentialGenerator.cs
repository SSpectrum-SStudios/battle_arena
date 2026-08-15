namespace BattleArena.Multiplayer.Prediction;

public interface IPredictionRouteCredentialGenerator
{
    byte[] CreateRouteCredential();
    byte[] CreateHandshakeNonce();
}
