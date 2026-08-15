namespace BattleArena.Multiplayer.OwnerPrediction;

/// <summary>
/// Selects the locally owned character prediction implementation. This does not
/// describe remote-player presentation prediction.
/// </summary>
public enum OwnerPredictionMode
{
    Legacy = 0,
    FrameRewindV2 = 1,
}

/// <summary>
/// Immutable, validated selection policy for locally owned character prediction.
/// </summary>
public sealed record OwnerPredictionModePolicy
{
    public static OwnerPredictionModePolicy Default { get; } = new(OwnerPredictionMode.Legacy);

    public OwnerPredictionModePolicy(OwnerPredictionMode selectedMode)
    {
        if (selectedMode is not OwnerPredictionMode.Legacy and
            not OwnerPredictionMode.FrameRewindV2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(selectedMode),
                selectedMode,
                "The owner prediction mode is not supported.");
        }

        SelectedMode = selectedMode;
    }

    public OwnerPredictionMode SelectedMode { get; }
}
