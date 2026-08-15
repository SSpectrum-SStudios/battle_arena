namespace BattleArena.Multiplayer.Replication;

public readonly record struct AttackPresentationDecision(
    AttackPresentationAction Action,
    bool RestartAnimation)
{
    public static AttackPresentationDecision Ignore { get; } =
        new(AttackPresentationAction.Ignore, false);

    public static AttackPresentationDecision PreserveLocalPrediction { get; } =
        new(AttackPresentationAction.PreserveLocalPrediction, false);

    public static AttackPresentationDecision Clear { get; } =
        new(AttackPresentationAction.Clear, false);
}
