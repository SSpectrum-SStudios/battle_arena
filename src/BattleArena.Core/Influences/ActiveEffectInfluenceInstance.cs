using BattleArena.Core.Common;

namespace BattleArena.Core.Influences;

public sealed class ActiveEffectInfluenceInstance
{
    private readonly HashSet<CombatantId> _members = [];

    public ActiveEffectInfluenceInstance(
        ActiveEffectInfluenceId id,
        ActiveEffectInfluenceDefinition definition,
        CombatantId sourceCombatantId,
        LifeGenerationId sourceLifeGenerationId)
    {
        Id = id;
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        SourceCombatantId = sourceCombatantId;
        SourceLifeGenerationId = sourceLifeGenerationId;
    }

    public ActiveEffectInfluenceId Id { get; }

    public ActiveEffectInfluenceDefinition Definition { get; }

    public CombatantId SourceCombatantId { get; }

    public LifeGenerationId SourceLifeGenerationId { get; }

    public IReadOnlySet<CombatantId> Members => _members;

    public bool IsEnded { get; private set; }

    public bool AddMember(CombatantId combatantId) => !IsEnded && _members.Add(combatantId);

    public bool RemoveMember(CombatantId combatantId) => _members.Remove(combatantId);

    public void End()
    {
        IsEnded = true;
        _members.Clear();
    }
}
