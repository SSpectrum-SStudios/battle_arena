using BattleArena.Core.Common;
using System.Collections.ObjectModel;

namespace BattleArena.Core.Effects;

public sealed class EffectChainContext
{
    private readonly Dictionary<TriggerInstanceId, int> _triggerActivations = [];

    public EffectChainContext(EffectChainId id, int maximumOperations)
    {
        if (maximumOperations <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumOperations),
                "The global operation budget must be positive.");
        }

        Id = id;
        MaximumOperations = maximumOperations;
    }

    public EffectChainId Id { get; }

    public int MaximumOperations { get; }

    public int ConsumedOperations { get; private set; }

    public IReadOnlyDictionary<TriggerInstanceId, int> TriggerActivations =>
        new ReadOnlyDictionary<TriggerInstanceId, int>(_triggerActivations);

    public TriggerBudgetConsumeStatus TryConsume(
        TriggerInstanceId triggerId,
        int maximumActivationsPerChain)
    {
        if (maximumActivationsPerChain <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumActivationsPerChain),
                "A trigger activation budget must be positive.");
        }

        if (ConsumedOperations >= MaximumOperations)
        {
            return TriggerBudgetConsumeStatus.GlobalChainBudgetExhausted;
        }

        var currentTriggerActivations = _triggerActivations.GetValueOrDefault(triggerId);
        if (currentTriggerActivations >= maximumActivationsPerChain)
        {
            return TriggerBudgetConsumeStatus.TriggerBudgetExhausted;
        }

        _triggerActivations[triggerId] = currentTriggerActivations + 1;
        ConsumedOperations++;
        return TriggerBudgetConsumeStatus.Consumed;
    }
}
