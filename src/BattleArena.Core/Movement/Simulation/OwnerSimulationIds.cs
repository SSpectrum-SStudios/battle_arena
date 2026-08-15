using BattleArena.Core.Common;

namespace BattleArena.Core.Movement.Simulation;

/// <summary>A combatant's exact life-scoped simulation identity.</summary>
public readonly record struct LifeEpoch
{
    public LifeEpoch(CombatantId combatantId, LifeGenerationId life)
    {
        if (combatantId.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(combatantId));
        }

        if (life.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(life));
        }

        CombatantId = combatantId;
        Life = life;
    }

    public CombatantId CombatantId { get; }
    public LifeGenerationId Life { get; }
    public bool IsValid => CombatantId.Value > 0 && Life.Value > 0;
}

/// <summary>Exact-once movement transition intent within one life/control scope.</summary>
public readonly record struct MovementTransitionId : IComparable<MovementTransitionId>
{
    public static MovementTransitionId Initial { get; } = new(1);
    public MovementTransitionId(ulong value) =>
        Value = SimulationIdGuard.RequirePositive(value, nameof(value), "movement transition");
    public ulong Value { get; }
    public bool IsValid => Value != 0;
    public MovementTransitionId Next() => new(
        SimulationIdGuard.CheckedNext(Value, "movement transition"));
    public int CompareTo(MovementTransitionId other) =>
        SimulationIdGuard.Compare(Value, other.Value, "movement transition");
}

/// <summary>Client-created action correlation identity used by replayable action state.</summary>
public readonly record struct PredictedActionId : IComparable<PredictedActionId>
{
    public static PredictedActionId Initial { get; } = new(1);
    public PredictedActionId(ulong value) =>
        Value = SimulationIdGuard.RequirePositive(value, nameof(value), "predicted action");
    public ulong Value { get; }
    public bool IsValid => Value != 0;
    public PredictedActionId Next() => new(
        SimulationIdGuard.CheckedNext(Value, "predicted action"));
    public int CompareTo(PredictedActionId other) =>
        SimulationIdGuard.Compare(Value, other.Value, "predicted action");
}

/// <summary>Authority-owned accepted action execution identity.</summary>
public readonly record struct AuthorityActionExecutionId :
    IComparable<AuthorityActionExecutionId>
{
    public static AuthorityActionExecutionId Initial { get; } = new(1);
    public AuthorityActionExecutionId(ulong value) =>
        Value = SimulationIdGuard.RequirePositive(
            value,
            nameof(value),
            "authority action execution");
    public ulong Value { get; }
    public bool IsValid => Value != 0;
    public AuthorityActionExecutionId Next() => new(
        SimulationIdGuard.CheckedNext(Value, "authority action execution"));
    public int CompareTo(AuthorityActionExecutionId other) =>
        SimulationIdGuard.Compare(Value, other.Value, "authority action execution");
}

internal static class SimulationIdGuard
{
    public static ulong RequirePositive(
        ulong value,
        string parameterName,
        string identityName)
    {
        if (value == 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"A {identityName} identity must be positive.");
        }

        return value;
    }

    public static ulong CheckedNext(ulong value, string identityName)
    {
        if (value == 0)
        {
            throw new InvalidOperationException(
                $"A default/invalid {identityName} identity cannot be advanced.");
        }

        return checked(value + 1);
    }

    public static int Compare(ulong left, ulong right, string identityName)
    {
        if (left == 0 || right == 0)
        {
            throw new InvalidOperationException(
                $"A default/invalid {identityName} identity cannot be ordered.");
        }

        return left.CompareTo(right);
    }
}
