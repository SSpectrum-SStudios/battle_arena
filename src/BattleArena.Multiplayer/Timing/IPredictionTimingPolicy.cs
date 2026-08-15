namespace BattleArena.Multiplayer.Timing;

public interface IPredictionTimingPolicy
{
    PredictionTimingDecision Evaluate(PredictionTimingContext context);
}
