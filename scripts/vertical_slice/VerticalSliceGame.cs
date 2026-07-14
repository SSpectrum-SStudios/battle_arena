#nullable enable

using BattleArena.Core.Actions;
using BattleArena.Core.Application;
using BattleArena.Core.Combat;
using BattleArena.Core.Common;
using BattleArena.Core.Effects;
using BattleArena.Core.Influences;
using Godot;

namespace BattleArena.VerticalSlice;

public partial class VerticalSliceGame : Node3D
{
    private static readonly CombatActionDefinitionId SwordActionId = new("base:vertical_slice_sword");
    private static readonly ActiveEffectInfluenceDefinitionId PoisonAuraId = new("base:vertical_slice_poison_aura");
    private static readonly CombatantId DummyCombatantId = new(2);
    private const long RespawnDelayTicks = 120;

    [Export]
    public NodePath HudPath { get; set; } = "";

    private readonly Dictionary<CombatantId, CombatantView> _views = [];
    private readonly Dictionary<CombatantId, long> _respawnsDueAt = [];
    private CombatApplicationFacade _facade = null!;
    private VerticalSliceHud _hud = null!;
    private bool _auraActive;

    public override void _Ready()
    {
        AddToGroup("vertical_slice_game");
        VerticalSliceInput.EnsureDefaultBindings();
        _hud = GetNode<VerticalSliceHud>(HudPath);

        var swordAction = CreateSwordAction();
        var poisonAura = CreatePoisonAura();
        _facade = new CombatApplicationFacade(
            new CombatActionCatalog([swordAction]),
            new CombatResolver(),
            new ActiveEffectInfluenceCatalog([poisonAura]));

        foreach (var node in GetTree().GetNodesInGroup("vertical_slice_combatant"))
        {
            if (node is not CombatantView view)
            {
                continue;
            }

            _views.Add(view.CombatantId, view);
            _facade.RegisterCombatant(
                new Combatant(view.CombatantId, view.MaximumHealth),
                new ResistanceProfileCompiler().Compile([]));
        }

        SynchronizePresentation();
    }

    public override void _PhysicsProcess(double delta)
    {
        _facade.AdvanceOneTick();
        ProcessFacts(_facade.DrainFacts());

        foreach (var combatantId in _respawnsDueAt
                     .Where(pair => pair.Value <= _facade.CurrentTime.Tick)
                     .Select(static pair => pair.Key)
                     .ToArray())
        {
            _facade.Respawn(combatantId);
            _respawnsDueAt.Remove(combatantId);
        }

        ProcessFacts(_facade.DrainFacts());
        SynchronizePresentation();
    }

    public ActionExecutionId? BeginSwordAction(CombatantId sourceCombatantId)
    {
        var result = _facade.BeginAction(sourceCombatantId, SwordActionId);
        return result.ExecutionId;
    }

    public void RegisterHit(ActionExecutionId executionId, CombatantId targetCombatantId)
    {
        _facade.RegisterHit(executionId, targetCombatantId);
        ProcessFacts(_facade.DrainFacts());
        SynchronizePresentation();
    }

    public void EndAction(ActionExecutionId executionId) => _facade.EndAction(executionId);

    public ActiveEffectInfluenceId? BeginPoisonAura(CombatantId sourceCombatantId) =>
        _facade.BeginInfluence(sourceCombatantId, PoisonAuraId).InfluenceId;

    public void EnterInfluence(
        ActiveEffectInfluenceId influenceId,
        CombatantId targetCombatantId)
    {
        _facade.EnterInfluence(influenceId, targetCombatantId);
        ProcessFacts(_facade.DrainFacts());
        SynchronizePresentation();
    }

    public void ExitInfluence(
        ActiveEffectInfluenceId influenceId,
        CombatantId targetCombatantId)
    {
        _facade.ExitInfluence(influenceId, targetCombatantId);
        ProcessFacts(_facade.DrainFacts());
        SynchronizePresentation();
    }

    public void EndInfluence(ActiveEffectInfluenceId influenceId)
    {
        _facade.EndInfluence(influenceId);
        ProcessFacts(_facade.DrainFacts());
        SynchronizePresentation();
    }

    public void SetAuraActive(bool active)
    {
        _auraActive = active;
        SynchronizePresentation();
    }

    private void ProcessFacts(IEnumerable<CombatFact> facts)
    {
        foreach (var fact in facts)
        {
            switch (fact)
            {
                case CombatFact.CombatantEliminated eliminated:
                    _respawnsDueAt[eliminated.CombatantId] =
                        checked(_facade.CurrentTime.Tick + RespawnDelayTicks);
                    break;
                case CombatFact.CombatantRespawned respawned:
                    _respawnsDueAt.Remove(respawned.Health.CombatantId);
                    break;
            }
        }
    }

    private void SynchronizePresentation()
    {
        foreach (var pair in _views)
        {
            if (_facade.TryGetCombatantSnapshot(pair.Key, out var snapshot))
            {
                pair.Value.ApplyHealth(snapshot!);
            }
        }

        _facade.TryGetCombatantSnapshot(DummyCombatantId, out var dummy);
        var dummyEffects = _facade.GetPeriodicDamageEffectSnapshots()
            .Where(effect => effect.Effect.TargetCombatantId == DummyCombatantId)
            .ToArray();
        _hud.UpdateDisplay(dummy, dummyEffects, _auraActive);
    }

    private static CombatActionDefinition CreateSwordAction()
    {
        var poison = new PeriodicDamageEffectDefinition(
            new EffectDefinitionId("base:vertical_slice_poison"),
            [new PeriodicDamagePortionDefinition(
                new DamagePortionId("primary_poison"),
                DamageType.Poison,
                5d)],
            new SimulationDuration(60),
            FirstTickPolicy.AfterInterval,
            new PeriodicCompletionPolicy.AfterTickCount(5),
            EffectLifetimeScope.PerLife,
            [new EffectTag("base:poison")]);

        return new CombatActionDefinition(
            SwordActionId,
            new TargetHitPolicy.Limited(1),
            [
                new CombatActionEffectDefinition.ImmediateDamage(
                    [new DamagePortion(DamageType.Physical, 15d)]),
                new CombatActionEffectDefinition.ApplyPeriodicDamage(poison),
            ]);
    }

    private static ActiveEffectInfluenceDefinition CreatePoisonAura() =>
        new(
            PoisonAuraId,
            CombatantRelationshipFilter.Everyone,
            CombatantRelationshipFilter.Everyone,
            new EffectTagSpecification(requiredAll: [new EffectTag("base:poison")]),
            [
                new PeriodicDamageModifierDefinition.DamageAmount(
                    new DamagePortionSelector.ByDamageType(DamageType.Poison),
                    new ValueOperation(ValueOperationKind.Multiply, 2d)),
                new PeriodicDamageModifierDefinition.Interval(
                    new ValueOperation(ValueOperationKind.Multiply, 0.5d)),
            ]);
}
