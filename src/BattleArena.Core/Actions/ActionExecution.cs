using BattleArena.Core.Common;

namespace BattleArena.Core.Actions;

public sealed class ActionExecution
{
    private readonly Dictionary<CombatantId, AcceptedTargetHits> _acceptedHits = [];

    public ActionExecution(
        ActionExecutionId id,
        CombatActionDefinition definition,
        CombatantId sourceCombatantId,
        LifeGenerationId sourceLifeGenerationId,
        SimulationInstant startedAt)
    {
        Id = id;
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        SourceCombatantId = sourceCombatantId;
        SourceLifeGenerationId = sourceLifeGenerationId;
        StartedAt = startedAt;
    }

    public ActionExecutionId Id { get; }

    public CombatActionDefinition Definition { get; }

    public CombatantId SourceCombatantId { get; }

    public LifeGenerationId SourceLifeGenerationId { get; }

    public SimulationInstant StartedAt { get; }

    public bool IsEnded { get; private set; }

    public bool TryAcceptHit(CombatantId targetId, SimulationInstant currentTime)
    {
        if (IsEnded)
        {
            return false;
        }

        _acceptedHits.TryGetValue(targetId, out var existing);
        if (!Definition.HitPolicy.Allows(existing.Count, existing.LastAcceptedAt, currentTime))
        {
            return false;
        }

        _acceptedHits[targetId] = new AcceptedTargetHits(existing.Count + 1, currentTime);
        return true;
    }

    public void End() => IsEnded = true;

    private readonly record struct AcceptedTargetHits(int Count, SimulationInstant? LastAcceptedAt);
}
