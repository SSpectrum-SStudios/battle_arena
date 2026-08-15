namespace BattleArena.Multiplayer.Presentation;

public enum PredictionFrameStateOnlyStep
{
    Unspecified = 0,
    AuthorityRestore = 1,
    HistoricalReplay = 2,
    CurrentSimulation = 3,
}

public enum PredictionFramePublicationDecision
{
    Unspecified = 0,
    PublishFinal = 1,
}

/// <summary>
/// Executable frame transaction authorizing one presentation publication after
/// every state-only authority restore, historical replay, and current simulation
/// step has completed. It performs no presentation operation itself.
/// </summary>
public sealed class PredictionFramePresentationCoordinator
{
    private FrameState _state;

    public int AuthorityRestoreCount { get; private set; }
    public int HistoricalReplayCount { get; private set; }
    public int CurrentSimulationCount { get; private set; }

    public void BeginFrame()
    {
        if (_state == FrameState.Open)
        {
            throw new InvalidOperationException(
                "A prediction presentation frame cannot begin before the prior frame completes.");
        }

        _state = FrameState.Open;
        AuthorityRestoreCount = 0;
        HistoricalReplayCount = 0;
        CurrentSimulationCount = 0;
    }

    public void RecordStateOnlyStep(PredictionFrameStateOnlyStep step)
    {
        RequireOpen();
        switch (step)
        {
            case PredictionFrameStateOnlyStep.AuthorityRestore:
                AuthorityRestoreCount = checked(AuthorityRestoreCount + 1);
                break;
            case PredictionFrameStateOnlyStep.HistoricalReplay:
                HistoricalReplayCount = checked(HistoricalReplayCount + 1);
                break;
            case PredictionFrameStateOnlyStep.CurrentSimulation:
                CurrentSimulationCount = checked(CurrentSimulationCount + 1);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(step));
        }
    }

    public PredictionFramePublicationDecision CompleteFrame()
    {
        RequireOpen();
        _state = FrameState.Completed;
        return PredictionFramePublicationDecision.PublishFinal;
    }

    private void RequireOpen()
    {
        if (_state != FrameState.Open)
        {
            throw new InvalidOperationException(
                "Prediction frame presentation operations require one open frame.");
        }
    }

    private enum FrameState
    {
        Idle = 0,
        Open = 1,
        Completed = 2,
    }
}
