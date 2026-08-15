namespace BattleArena.Multiplayer.OwnerPrediction;

/// <summary>
/// Authority-owned identity for a spawn, respawn, teleport, or level transition
/// that discontinuously changes one combatant's simulation state. Values may be
/// ordered only for the same combatant in the same match; the owning epoch gate
/// establishes that scope before comparison.
/// </summary>
public readonly record struct AuthorityDiscontinuityId : IComparable<AuthorityDiscontinuityId>
{
    public static AuthorityDiscontinuityId Initial { get; } = new(1);

    public AuthorityDiscontinuityId(ulong value)
    {
        PredictionEpochIdGuard.RequirePositive(value, nameof(value), "authority discontinuity");
        Value = value;
    }

    public ulong Value { get; }
    public bool IsValid => Value != 0;

    public AuthorityDiscontinuityId Next() => new(
        PredictionEpochIdGuard.CheckedNext(Value, "authority discontinuity"));

    public int CompareTo(AuthorityDiscontinuityId other)
    {
        PredictionEpochIdGuard.RequireComparable(Value, other.Value, "authority discontinuity");
        return Value.CompareTo(other.Value);
    }

    public static bool operator <(AuthorityDiscontinuityId left, AuthorityDiscontinuityId right) =>
        left.CompareTo(right) < 0;

    public static bool operator >(AuthorityDiscontinuityId left, AuthorityDiscontinuityId right) =>
        left.CompareTo(right) > 0;

    public static bool operator <=(AuthorityDiscontinuityId left, AuthorityDiscontinuityId right) =>
        left.CompareTo(right) <= 0;

    public static bool operator >=(AuthorityDiscontinuityId left, AuthorityDiscontinuityId right) =>
        left.CompareTo(right) >= 0;

    public override string ToString() => IsValid
        ? $"authority-discontinuity:{Value}"
        : "authority-discontinuity:invalid";
}

/// <summary>
/// Authority-issued identity for one owner's contiguous control session. It
/// changes for initial control, reconnect, respawn control, or timeline restart.
/// Values may be ordered only for the same controlled combatant in the same
/// match; the owning epoch gate establishes that scope before comparison.
/// </summary>
public readonly record struct OwnerControlEpoch : IComparable<OwnerControlEpoch>
{
    public static OwnerControlEpoch Initial { get; } = new(1);

    public OwnerControlEpoch(ulong value)
    {
        PredictionEpochIdGuard.RequirePositive(value, nameof(value), "owner-control epoch");
        Value = value;
    }

    public ulong Value { get; }
    public bool IsValid => Value != 0;

    public OwnerControlEpoch Next() => new(
        PredictionEpochIdGuard.CheckedNext(Value, "owner-control epoch"));

    public int CompareTo(OwnerControlEpoch other)
    {
        PredictionEpochIdGuard.RequireComparable(Value, other.Value, "owner-control epoch");
        return Value.CompareTo(other.Value);
    }

    public static bool operator <(OwnerControlEpoch left, OwnerControlEpoch right) =>
        left.CompareTo(right) < 0;

    public static bool operator >(OwnerControlEpoch left, OwnerControlEpoch right) =>
        left.CompareTo(right) > 0;

    public static bool operator <=(OwnerControlEpoch left, OwnerControlEpoch right) =>
        left.CompareTo(right) <= 0;

    public static bool operator >=(OwnerControlEpoch left, OwnerControlEpoch right) =>
        left.CompareTo(right) >= 0;

    public override string ToString() => IsValid
        ? $"owner-control-epoch:{Value}"
        : "owner-control-epoch:invalid";
}

/// <summary>
/// Client-only identity for one local prediction-history/presentation repair.
/// It must never be serialized or represented as authority state.
/// Values may be ordered only inside one local combatant prediction controller
/// and its current authority/control scope.
/// </summary>
public readonly record struct LocalPredictionRebaseId : IComparable<LocalPredictionRebaseId>
{
    public static LocalPredictionRebaseId Initial { get; } = new(1);

    public LocalPredictionRebaseId(ulong value)
    {
        PredictionEpochIdGuard.RequirePositive(value, nameof(value), "local prediction rebase");
        Value = value;
    }

    public ulong Value { get; }
    public bool IsValid => Value != 0;

    public LocalPredictionRebaseId Next() => new(
        PredictionEpochIdGuard.CheckedNext(Value, "local prediction rebase"));

    public int CompareTo(LocalPredictionRebaseId other)
    {
        PredictionEpochIdGuard.RequireComparable(Value, other.Value, "local prediction rebase");
        return Value.CompareTo(other.Value);
    }

    public static bool operator <(LocalPredictionRebaseId left, LocalPredictionRebaseId right) =>
        left.CompareTo(right) < 0;

    public static bool operator >(LocalPredictionRebaseId left, LocalPredictionRebaseId right) =>
        left.CompareTo(right) > 0;

    public static bool operator <=(LocalPredictionRebaseId left, LocalPredictionRebaseId right) =>
        left.CompareTo(right) <= 0;

    public static bool operator >=(LocalPredictionRebaseId left, LocalPredictionRebaseId right) =>
        left.CompareTo(right) >= 0;

    public override string ToString() => IsValid
        ? $"local-prediction-rebase:{Value}"
        : "local-prediction-rebase:invalid";
}

internal static class PredictionEpochIdGuard
{
    public static void RequirePositive(ulong value, string parameterName, string identityName)
    {
        if (value == 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"A {identityName} identity must be positive.");
        }
    }

    public static void RequireComparable(ulong left, ulong right, string identityName)
    {
        if (left == 0 || right == 0)
        {
            throw new InvalidOperationException(
                $"A default/invalid {identityName} identity cannot be ordered.");
        }
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
}
