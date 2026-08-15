namespace BattleArena.Multiplayer.Prediction;

public enum PredictionHandshakeState
{
    ReadyToInitiate,
    WaitingForHello,
    WaitingForChallenge,
    WaitingForProof,
    WaitingForAccepted,
    Authenticated,
}
