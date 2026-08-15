namespace BattleArena.Multiplayer.Replication;

public readonly record struct AttackPresentationIdentity
{
    public AttackPresentationIdentity(ulong executionId, int stepIndex)
    {
        if (executionId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(executionId));
        }

        if (stepIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stepIndex));
        }

        ExecutionId = executionId;
        StepIndex = stepIndex;
    }

    public ulong ExecutionId { get; }

    public int StepIndex { get; }
}
