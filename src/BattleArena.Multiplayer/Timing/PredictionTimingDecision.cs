namespace BattleArena.Multiplayer.Timing;

public readonly record struct PredictionTimingDecision(
    int PresentationDelayTicks,
    int NormalPredictionLimitTicks,
    int FreezeAfterTicks);
