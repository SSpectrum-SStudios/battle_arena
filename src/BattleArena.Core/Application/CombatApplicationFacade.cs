using BattleArena.Core.Actions;
using BattleArena.Core.Combat;
using BattleArena.Core.Common;
using BattleArena.Core.Effects;
using System.Collections.ObjectModel;

namespace BattleArena.Core.Application;

public sealed class CombatApplicationFacade
{
    private readonly CombatActionCatalog _actionCatalog;
    private readonly CombatResolver _combatResolver;
    private readonly PeriodicDamageEffectFactory _periodicDamageFactory = new();
    private readonly PeriodicDamageEffectExecutor _periodicDamageExecutor;
    private readonly EffectScheduler _effectScheduler = new();
    private readonly Dictionary<CombatantId, CombatantState> _combatants = [];
    private readonly Dictionary<ActionExecutionId, ActionExecution> _actionExecutions = [];
    private readonly Queue<CombatFact> _facts = [];
    private long _nextActionExecutionId = 1;
    private long _nextActiveEffectId = 1;
    private long _nextEffectChainId = 1;

    public CombatApplicationFacade(
        CombatActionCatalog actionCatalog,
        CombatResolver combatResolver)
    {
        _actionCatalog = actionCatalog ?? throw new ArgumentNullException(nameof(actionCatalog));
        _combatResolver = combatResolver ?? throw new ArgumentNullException(nameof(combatResolver));
        _periodicDamageExecutor = new PeriodicDamageEffectExecutor(_combatResolver);
    }

    public SimulationInstant CurrentTime { get; private set; }

    public void RegisterCombatant(Combatant combatant, ResistanceProfile resistanceProfile)
    {
        ArgumentNullException.ThrowIfNull(combatant);
        ArgumentNullException.ThrowIfNull(resistanceProfile);

        if (!_combatants.TryAdd(combatant.Id, new CombatantState(combatant, resistanceProfile)))
        {
            throw new ArgumentException(
                $"Combatant ID {combatant.Id} is already registered.",
                nameof(combatant));
        }
    }

    public BeginActionResult BeginAction(
        CombatantId sourceCombatantId,
        CombatActionDefinitionId definitionId)
    {
        if (!_actionCatalog.TryGet(definitionId, out var definition))
        {
            return new BeginActionResult(BeginActionStatus.UnknownDefinition, null);
        }

        if (!_combatants.TryGetValue(sourceCombatantId, out var source))
        {
            return new BeginActionResult(BeginActionStatus.SourceNotFound, null);
        }

        if (source.Combatant.IsEliminated)
        {
            return new BeginActionResult(BeginActionStatus.SourceEliminated, null);
        }

        var executionId = NextActionExecutionId();
        var execution = new ActionExecution(
            executionId,
            definition!,
            sourceCombatantId,
            source.Combatant.CurrentLifeGenerationId,
            CurrentTime);
        _actionExecutions.Add(executionId, execution);
        _facts.Enqueue(
            new CombatFact.ActionStarted(
                CurrentTime,
                executionId,
                definitionId,
                sourceCombatantId,
                source.Combatant.CurrentLifeGenerationId));

        return new BeginActionResult(BeginActionStatus.Started, executionId);
    }

    public bool UpdateResistanceProfile(
        CombatantId combatantId,
        ResistanceProfile resistanceProfile)
    {
        ArgumentNullException.ThrowIfNull(resistanceProfile);

        if (!_combatants.TryGetValue(combatantId, out var state))
        {
            return false;
        }

        _combatants[combatantId] = state with { ResistanceProfile = resistanceProfile };
        return true;
    }

    public RegisterHitResult RegisterHit(
        ActionExecutionId executionId,
        CombatantId targetCombatantId)
    {
        if (!_actionExecutions.TryGetValue(executionId, out var execution))
        {
            return HitResult(RegisterHitStatus.ExecutionNotFound, executionId, targetCombatantId);
        }

        if (execution.IsEnded)
        {
            return HitResult(RegisterHitStatus.ExecutionEnded, executionId, targetCombatantId);
        }

        if (!IsSourceLifeActive(
                execution.SourceCombatantId,
                execution.SourceLifeGenerationId))
        {
            execution.End();
            return HitResult(RegisterHitStatus.SourceLifeInactive, executionId, targetCombatantId);
        }

        if (!_combatants.TryGetValue(targetCombatantId, out var target))
        {
            return HitResult(RegisterHitStatus.TargetNotFound, executionId, targetCombatantId);
        }

        if (target.Combatant.IsEliminated)
        {
            return HitResult(RegisterHitStatus.TargetEliminated, executionId, targetCombatantId);
        }

        if (!execution.TryAcceptHit(targetCombatantId, CurrentTime))
        {
            return HitResult(RegisterHitStatus.RejectedByHitPolicy, executionId, targetCombatantId);
        }

        foreach (var effectDefinition in execution.Definition.Effects)
        {
            if (target.Combatant.IsEliminated)
            {
                break;
            }

            switch (effectDefinition)
            {
                case CombatActionEffectDefinition.ImmediateDamage immediateDamage:
                    ResolveImmediateDamage(execution, target, immediateDamage);
                    break;
                case CombatActionEffectDefinition.ApplyPeriodicDamage periodicDamage:
                    ApplyPeriodicDamage(execution, target, periodicDamage.Effect);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported combat-action effect: {effectDefinition.GetType().Name}.");
            }
        }

        return HitResult(RegisterHitStatus.Accepted, executionId, targetCombatantId);
    }

    public bool EndAction(ActionExecutionId executionId)
    {
        if (!_actionExecutions.Remove(executionId, out var execution))
        {
            return false;
        }

        execution.End();
        return true;
    }

    public void AdvanceOneTick()
    {
        CurrentTime = new SimulationInstant(checked(CurrentTime.Tick + 1));
        ProcessDueEffects();
    }

    public RespawnCombatantResult Respawn(CombatantId combatantId)
    {
        if (!_combatants.TryGetValue(combatantId, out var state))
        {
            return new RespawnCombatantResult(
                RespawnStatus.CombatantNotFound,
                combatantId,
                null);
        }

        if (!state.Combatant.IsEliminated)
        {
            return new RespawnCombatantResult(
                RespawnStatus.CombatantStillAlive,
                combatantId,
                null);
        }

        var newLifeGenerationId = new LifeGenerationId(
            checked(state.Combatant.CurrentLifeGenerationId.Value + 1));
        var respawn = state.Combatant.Respawn(newLifeGenerationId);

        foreach (var removedEffectId in respawn.RemovedEffectIds)
        {
            PublishEffectRemoved(combatantId, removedEffectId, ActiveEffectRemovalReason.LifeEnded);
        }

        _facts.Enqueue(
            new CombatFact.CombatantRespawned(
                CurrentTime,
                respawn.After,
                newLifeGenerationId));

        return new RespawnCombatantResult(RespawnStatus.Respawned, combatantId, respawn);
    }

    public bool IsSourceLifeActive(
        CombatantId sourceCombatantId,
        LifeGenerationId sourceLifeGenerationId) =>
        _combatants.TryGetValue(sourceCombatantId, out var source) &&
        !source.Combatant.IsEliminated &&
        source.Combatant.CurrentLifeGenerationId == sourceLifeGenerationId;

    public bool TryGetCombatantSnapshot(
        CombatantId combatantId,
        out HealthSnapshot? snapshot)
    {
        if (_combatants.TryGetValue(combatantId, out var state))
        {
            snapshot = state.Combatant.CreateHealthSnapshot();
            return true;
        }

        snapshot = null;
        return false;
    }

    public IReadOnlyList<ActiveEffectSnapshot> GetActiveEffectSnapshots() =>
        _combatants.Values
            .SelectMany(static state => state.Combatant.ActiveEffects.Effects)
            .OrderBy(static effect => effect.Id.Value)
            .Select(static effect => effect.CreateSnapshot())
            .ToArray();

    public IReadOnlyList<CombatFact> DrainFacts()
    {
        var facts = _facts.ToArray();
        _facts.Clear();
        return Array.AsReadOnly(facts);
    }

    private void ResolveImmediateDamage(
        ActionExecution execution,
        CombatantState target,
        CombatActionEffectDefinition.ImmediateDamage immediateDamage)
    {
        var packet = new DamagePacket(
            execution.SourceCombatantId,
            immediateDamage.Portions);
        var resolution = _combatResolver.Resolve(packet, target.ResistanceProfile);
        var health = target.Combatant.Apply(resolution);

        PublishDamage(
            execution.SourceCombatantId,
            target.Combatant.Id,
            execution.Id,
            activeEffectId: null,
            execution.SourceLifeGenerationId,
            resolution,
            health);
        HandleElimination(
            target.Combatant,
            execution.SourceCombatantId,
            execution.SourceLifeGenerationId,
            health);
    }

    private void ApplyPeriodicDamage(
        ActionExecution execution,
        CombatantState target,
        PeriodicDamageEffectDefinition definition)
    {
        var effect = _periodicDamageFactory.Create(
            NextActiveEffectId(),
            definition,
            execution.SourceCombatantId,
            target.Combatant.Id,
            execution.SourceLifeGenerationId,
            CurrentTime);
        target.Combatant.ActiveEffects.Add(effect);
        _facts.Enqueue(new CombatFact.ActiveEffectApplied(CurrentTime, effect.CreateSnapshot()));

        if (effect.Schedule.NextActionAt == CurrentTime)
        {
            ExecutePeriodicDamage(effect, target);
        }
    }

    private void ProcessDueEffects()
    {
        var due = _effectScheduler.GetDueEffects(
            _combatants.Values.SelectMany(static state => state.Combatant.ActiveEffects.Effects),
            CurrentTime);

        foreach (var effect in due)
        {
            if (!_combatants.TryGetValue(effect.TargetCombatantId, out var target) ||
                !target.Combatant.ActiveEffects.TryGet(effect.Id, out var installed) ||
                !ReferenceEquals(installed, effect))
            {
                continue;
            }

            switch (effect)
            {
                case PeriodicDamageEffectInstance periodicDamage:
                    ExecutePeriodicDamage(periodicDamage, target);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported active-effect type: {effect.GetType().Name}.");
            }
        }
    }

    private void ExecutePeriodicDamage(
        PeriodicDamageEffectInstance effect,
        CombatantState target)
    {
        var result = _periodicDamageExecutor.ExecuteDue(
            effect,
            CurrentTime,
            target.Combatant,
            target.ResistanceProfile,
            new EffectChainContext(NextEffectChainId(), maximumOperations: 1000));

        if (result.AppliedTick)
        {
            PublishDamage(
                effect.SourceCombatantId,
                target.Combatant.Id,
                actionExecutionId: null,
                effect.Id,
                effect.SourceLifeGenerationId,
                result.CombatResolution!,
                result.HealthApplication!);
            HandleElimination(
                target.Combatant,
                effect.SourceCombatantId,
                effect.SourceLifeGenerationId,
                result.HealthApplication!);
        }

        if (effect.Schedule.IsExpired && target.Combatant.ActiveEffects.Remove(effect.Id))
        {
            PublishEffectRemoved(
                target.Combatant.Id,
                effect.Id,
                ActiveEffectRemovalReason.Expired);
        }
    }

    private void HandleElimination(
        Combatant target,
        CombatantId creditedSourceCombatantId,
        LifeGenerationId creditedSourceLifeGenerationId,
        HealthApplicationResult health)
    {
        if (!health.BecameEliminated)
        {
            return;
        }

        var removedEffectIds = target.EndCurrentLife();
        foreach (var effectId in removedEffectIds)
        {
            PublishEffectRemoved(target.Id, effectId, ActiveEffectRemovalReason.LifeEnded);
        }

        foreach (var execution in _actionExecutions.Values.Where(
                     execution =>
                         execution.SourceCombatantId == target.Id &&
                         execution.SourceLifeGenerationId == target.CurrentLifeGenerationId))
        {
            execution.End();
        }

        _facts.Enqueue(
            new CombatFact.CombatantEliminated(
                CurrentTime,
                target.Id,
                creditedSourceCombatantId,
                creditedSourceLifeGenerationId,
                health.Overkill));
    }

    private void PublishDamage(
        CombatantId sourceCombatantId,
        CombatantId targetCombatantId,
        ActionExecutionId? actionExecutionId,
        ActiveEffectId? activeEffectId,
        LifeGenerationId sourceLifeGenerationId,
        CombatResolutionResult resolution,
        HealthApplicationResult health) =>
        _facts.Enqueue(
            new CombatFact.DamageResolved(
                CurrentTime,
                sourceCombatantId,
                targetCombatantId,
                actionExecutionId,
                activeEffectId,
                sourceLifeGenerationId,
                resolution,
                health));

    private void PublishEffectRemoved(
        CombatantId targetCombatantId,
        ActiveEffectId effectId,
        ActiveEffectRemovalReason reason) =>
        _facts.Enqueue(
            new CombatFact.ActiveEffectRemoved(
                CurrentTime,
                targetCombatantId,
                effectId,
                reason));

    private ActionExecutionId NextActionExecutionId() =>
        new(checked(_nextActionExecutionId++));

    private ActiveEffectId NextActiveEffectId() =>
        new(checked(_nextActiveEffectId++));

    private EffectChainId NextEffectChainId() =>
        new(checked(_nextEffectChainId++));

    private static RegisterHitResult HitResult(
        RegisterHitStatus status,
        ActionExecutionId executionId,
        CombatantId targetCombatantId) =>
        new(status, executionId, targetCombatantId);

    private sealed record CombatantState(
        Combatant Combatant,
        ResistanceProfile ResistanceProfile);
}
