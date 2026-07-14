using BattleArena.Core.Actions;
using BattleArena.Core.Combat;
using BattleArena.Core.Common;
using BattleArena.Core.Effects;
using BattleArena.Core.Influences;
using System.Collections.ObjectModel;

namespace BattleArena.Core.Application;

public sealed class CombatApplicationFacade
{
    private readonly CombatActionCatalog _actionCatalog;
    private readonly ActiveEffectInfluenceCatalog _influenceCatalog;
    private readonly CombatResolver _combatResolver;
    private readonly PeriodicDamageEffectFactory _periodicDamageFactory = new();
    private readonly PeriodicDamageEffectExecutor _periodicDamageExecutor;
    private readonly EffectScheduler _effectScheduler = new();
    private readonly Dictionary<CombatantId, CombatantState> _combatants = [];
    private readonly Dictionary<ActionExecutionId, ActionExecution> _actionExecutions = [];
    private readonly Dictionary<ActiveEffectInfluenceId, ActiveEffectInfluenceInstance> _influences = [];
    private readonly Queue<CombatFact> _facts = [];
    private long _nextActionExecutionId = 1;
    private long _nextActiveEffectId = 1;
    private long _nextEffectChainId = 1;
    private long _nextInfluenceId = 1;
    private long _nextContributionId = 1;
    private long _nextInstallationSequence = 1;

    public CombatApplicationFacade(
        CombatActionCatalog actionCatalog,
        CombatResolver combatResolver,
        ActiveEffectInfluenceCatalog? influenceCatalog = null)
    {
        _actionCatalog = actionCatalog ?? throw new ArgumentNullException(nameof(actionCatalog));
        _combatResolver = combatResolver ?? throw new ArgumentNullException(nameof(combatResolver));
        _influenceCatalog = influenceCatalog ?? new ActiveEffectInfluenceCatalog([]);
        _periodicDamageExecutor = new PeriodicDamageEffectExecutor(_combatResolver);
    }

    public SimulationInstant CurrentTime { get; private set; }

    public void RegisterCombatant(
        Combatant combatant,
        ResistanceProfile resistanceProfile,
        TeamId? teamId = null)
    {
        ArgumentNullException.ThrowIfNull(combatant);
        ArgumentNullException.ThrowIfNull(resistanceProfile);

        if (!_combatants.TryAdd(combatant.Id, new CombatantState(combatant, resistanceProfile, teamId)))
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

    public BeginInfluenceResult BeginInfluence(
        CombatantId sourceCombatantId,
        ActiveEffectInfluenceDefinitionId definitionId)
    {
        if (!_influenceCatalog.TryGet(definitionId, out var definition))
        {
            return new BeginInfluenceResult(BeginInfluenceStatus.UnknownDefinition, null);
        }

        if (!_combatants.TryGetValue(sourceCombatantId, out var source))
        {
            return new BeginInfluenceResult(BeginInfluenceStatus.SourceNotFound, null);
        }

        if (source.Combatant.IsEliminated)
        {
            return new BeginInfluenceResult(BeginInfluenceStatus.SourceEliminated, null);
        }

        var influenceId = NextInfluenceId();
        var influence = new ActiveEffectInfluenceInstance(
            influenceId,
            definition!,
            sourceCombatantId,
            source.Combatant.CurrentLifeGenerationId);
        _influences.Add(influenceId, influence);
        _facts.Enqueue(
            new CombatFact.InfluenceStarted(
                CurrentTime,
                influenceId,
                definitionId,
                sourceCombatantId,
                source.Combatant.CurrentLifeGenerationId));

        return new BeginInfluenceResult(BeginInfluenceStatus.Started, influenceId);
    }

    public InfluenceMembershipResult EnterInfluence(
        ActiveEffectInfluenceId influenceId,
        CombatantId targetCombatantId)
    {
        if (!_influences.TryGetValue(influenceId, out var influence))
        {
            return MembershipResult(
                InfluenceMembershipStatus.InfluenceNotFound,
                influenceId,
                targetCombatantId);
        }

        if (influence.IsEnded)
        {
            return MembershipResult(
                InfluenceMembershipStatus.InfluenceEnded,
                influenceId,
                targetCombatantId);
        }

        if (influence.Definition.RequiresActiveSourceLife &&
            !IsSourceLifeActive(influence.SourceCombatantId, influence.SourceLifeGenerationId))
        {
            EndInfluenceInternal(influence);
            return MembershipResult(
                InfluenceMembershipStatus.SourceLifeInactive,
                influenceId,
                targetCombatantId);
        }

        if (!_combatants.TryGetValue(targetCombatantId, out var target))
        {
            return MembershipResult(
                InfluenceMembershipStatus.TargetNotFound,
                influenceId,
                targetCombatantId);
        }

        if (target.Combatant.IsEliminated)
        {
            return MembershipResult(
                InfluenceMembershipStatus.TargetEliminated,
                influenceId,
                targetCombatantId);
        }

        if (!influence.Definition.TargetFilter.Includes(
                GetRelationship(influence.SourceCombatantId, targetCombatantId)))
        {
            return MembershipResult(
                InfluenceMembershipStatus.TargetRejected,
                influenceId,
                targetCombatantId);
        }

        if (!influence.AddMember(targetCombatantId))
        {
            return MembershipResult(
                InfluenceMembershipStatus.AlreadyEntered,
                influenceId,
                targetCombatantId);
        }

        foreach (var effect in target.Combatant.ActiveEffects.Effects
                     .OfType<PeriodicDamageEffectInstance>()
                     .ToArray())
        {
            if (ApplyInfluenceToEffect(influence, effect))
            {
                ProcessEffectAfterModifierChange(target, effect);
            }
        }

        return MembershipResult(
            InfluenceMembershipStatus.Entered,
            influenceId,
            targetCombatantId);
    }

    public InfluenceMembershipResult ExitInfluence(
        ActiveEffectInfluenceId influenceId,
        CombatantId targetCombatantId)
    {
        if (!_influences.TryGetValue(influenceId, out var influence))
        {
            return MembershipResult(
                InfluenceMembershipStatus.InfluenceNotFound,
                influenceId,
                targetCombatantId);
        }

        if (!influence.RemoveMember(targetCombatantId))
        {
            return MembershipResult(
                InfluenceMembershipStatus.AlreadyExited,
                influenceId,
                targetCombatantId);
        }

        RemoveInfluenceFromTarget(influence, targetCombatantId);
        return MembershipResult(
            InfluenceMembershipStatus.Exited,
            influenceId,
            targetCombatantId);
    }

    public bool EndInfluence(ActiveEffectInfluenceId influenceId)
    {
        if (!_influences.TryGetValue(influenceId, out var influence))
        {
            return false;
        }

        EndInfluenceInternal(influence);
        return true;
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

    public IReadOnlyList<PeriodicDamageEffectSnapshot> GetPeriodicDamageEffectSnapshots() =>
        _combatants.Values
            .SelectMany(static state => state.Combatant.ActiveEffects.Effects)
            .OfType<PeriodicDamageEffectInstance>()
            .OrderBy(static effect => effect.Id.Value)
            .Select(static effect => effect.CreatePeriodicSnapshot())
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

        ApplyActiveInfluencesToEffect(effect);

        if (target.Combatant.ActiveEffects.TryGet(effect.Id, out _) &&
            effect.Schedule.NextActionAt == CurrentTime)
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

        foreach (var influence in _influences.Values
                     .Where(influence =>
                         influence.Definition.RequiresActiveSourceLife &&
                         influence.SourceCombatantId == target.Id &&
                         influence.SourceLifeGenerationId == target.CurrentLifeGenerationId)
                     .ToArray())
        {
            EndInfluenceInternal(influence);
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

    private ActiveEffectInfluenceId NextInfluenceId() =>
        new(checked(_nextInfluenceId++));

    private ContributionId NextContributionId() =>
        new(checked(_nextContributionId++));

    private InstallationSequence NextInstallationSequence() =>
        new(checked(_nextInstallationSequence++));

    private static RegisterHitResult HitResult(
        RegisterHitStatus status,
        ActionExecutionId executionId,
        CombatantId targetCombatantId) =>
        new(status, executionId, targetCombatantId);

    private void ApplyActiveInfluencesToEffect(PeriodicDamageEffectInstance effect)
    {
        var target = _combatants[effect.TargetCombatantId];
        var changed = false;
        foreach (var influence in _influences.Values
                     .Where(influence =>
                         !influence.IsEnded &&
                         influence.Members.Contains(effect.TargetCombatantId))
                     .OrderBy(static influence => influence.Id.Value))
        {
            changed |= ApplyInfluenceToEffect(influence, effect);
        }

        if (changed)
        {
            ProcessEffectAfterModifierChange(target, effect);
        }
    }

    private bool ApplyInfluenceToEffect(
        ActiveEffectInfluenceInstance influence,
        PeriodicDamageEffectInstance effect)
    {
        if (!influence.Definition.EffectTags.IsSatisfiedBy(effect.Definition.Tags) ||
            !influence.Definition.EffectSourceFilter.Includes(
                GetRelationship(influence.SourceCombatantId, effect.SourceCombatantId)))
        {
            return false;
        }

        var ownerId = new ModifierOwnerId(influence.Id.Value);
        var contributions = influence.Definition.Modifiers
            .Select(modifier => modifier.CreateContribution(
                NextContributionId(),
                ownerId,
                NextInstallationSequence()))
            .ToArray();
        effect.InstallModifiers(contributions, CurrentTime);
        _facts.Enqueue(new CombatFact.ActiveEffectModified(CurrentTime, effect.CreatePeriodicSnapshot()));
        return true;
    }

    private void RemoveInfluenceFromTarget(
        ActiveEffectInfluenceInstance influence,
        CombatantId targetCombatantId)
    {
        if (!_combatants.TryGetValue(targetCombatantId, out var target))
        {
            return;
        }

        foreach (var effect in target.Combatant.ActiveEffects.Effects
                     .OfType<PeriodicDamageEffectInstance>()
                     .ToArray())
        {
            if (effect.RemoveModifiersOwnedBy(new ModifierOwnerId(influence.Id.Value), CurrentTime) > 0)
            {
                _facts.Enqueue(new CombatFact.ActiveEffectModified(CurrentTime, effect.CreatePeriodicSnapshot()));
                ProcessEffectAfterModifierChange(target, effect);
            }
        }
    }

    private void EndInfluenceInternal(ActiveEffectInfluenceInstance influence)
    {
        foreach (var member in influence.Members.ToArray())
        {
            RemoveInfluenceFromTarget(influence, member);
        }

        influence.End();
        _influences.Remove(influence.Id);
        _facts.Enqueue(new CombatFact.InfluenceEnded(CurrentTime, influence.Id));
    }

    private void ProcessEffectAfterModifierChange(
        CombatantState target,
        PeriodicDamageEffectInstance effect)
    {
        if (effect.Schedule.IsExpired)
        {
            if (target.Combatant.ActiveEffects.Remove(effect.Id))
            {
                PublishEffectRemoved(target.Combatant.Id, effect.Id, ActiveEffectRemovalReason.Expired);
            }

            return;
        }

        if (effect.Schedule.NextActionAt is { } dueAt && dueAt <= CurrentTime)
        {
            ExecutePeriodicDamage(effect, target);
        }
    }

    private CombatantRelationship GetRelationship(
        CombatantId originCombatantId,
        CombatantId candidateCombatantId)
    {
        if (originCombatantId == candidateCombatantId)
        {
            return CombatantRelationship.Self;
        }

        var originTeam = _combatants.GetValueOrDefault(originCombatantId)?.TeamId;
        var candidateTeam = _combatants.GetValueOrDefault(candidateCombatantId)?.TeamId;
        return originTeam is not null && originTeam == candidateTeam
            ? CombatantRelationship.Ally
            : CombatantRelationship.Enemy;
    }

    private static InfluenceMembershipResult MembershipResult(
        InfluenceMembershipStatus status,
        ActiveEffectInfluenceId influenceId,
        CombatantId targetCombatantId) =>
        new(status, influenceId, targetCombatantId);

    private sealed record CombatantState(
        Combatant Combatant,
        ResistanceProfile ResistanceProfile,
        TeamId? TeamId);
}
