using System.Reflection;
using BattleArena.Core.Movement.Simulation;
using BattleArena.Multiplayer.OwnerPrediction;

namespace BattleArena.Multiplayer.Tests.OwnerPrediction;

public sealed class PredictionEpochIdsTests
{
    [Fact]
    public void EveryEpochIsPositiveAndHasAnExplicitInitialValue()
    {
        Assert.Equal(1UL, MatchFrameEpochId.Initial.Value);
        Assert.Equal(1UL, AuthorityDiscontinuityId.Initial.Value);
        Assert.Equal(1UL, OwnerControlEpoch.Initial.Value);
        Assert.Equal(1UL, LocalPredictionRebaseId.Initial.Value);

        Assert.Throws<ArgumentOutOfRangeException>(() => new MatchFrameEpochId(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AuthorityDiscontinuityId(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OwnerControlEpoch(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LocalPredictionRebaseId(0));

        Assert.Equal(ulong.MaxValue, new MatchFrameEpochId(ulong.MaxValue).Value);
        Assert.Equal(ulong.MaxValue, new AuthorityDiscontinuityId(ulong.MaxValue).Value);
        Assert.Equal(ulong.MaxValue, new OwnerControlEpoch(ulong.MaxValue).Value);
        Assert.Equal(ulong.MaxValue, new LocalPredictionRebaseId(ulong.MaxValue).Value);
    }

    [Fact]
    public void SameTypeOrderingAndEqualityUseOnlyTheMonotonicValue()
    {
        AssertOrdering(
            new MatchFrameEpochId(8), new MatchFrameEpochId(9),
            (left, right) => left < right,
            (left, right) => left > right,
            (left, right) => left <= right,
            (left, right) => left >= right);
        AssertOrdering(
            new AuthorityDiscontinuityId(8), new AuthorityDiscontinuityId(9),
            (left, right) => left < right,
            (left, right) => left > right,
            (left, right) => left <= right,
            (left, right) => left >= right);
        AssertOrdering(
            new OwnerControlEpoch(8), new OwnerControlEpoch(9),
            (left, right) => left < right,
            (left, right) => left > right,
            (left, right) => left <= right,
            (left, right) => left >= right);
        AssertOrdering(
            new LocalPredictionRebaseId(8), new LocalPredictionRebaseId(9),
            (left, right) => left < right,
            (left, right) => left > right,
            (left, right) => left <= right,
            (left, right) => left >= right);
    }

    [Fact]
    public void NextIsCheckedAndNeverAdvancesAnInvalidDefault()
    {
        Assert.Equal(new MatchFrameEpochId(2), MatchFrameEpochId.Initial.Next());
        Assert.Equal(new AuthorityDiscontinuityId(2), AuthorityDiscontinuityId.Initial.Next());
        Assert.Equal(new OwnerControlEpoch(2), OwnerControlEpoch.Initial.Next());
        Assert.Equal(new LocalPredictionRebaseId(2), LocalPredictionRebaseId.Initial.Next());

        Assert.Throws<OverflowException>(() => new MatchFrameEpochId(ulong.MaxValue).Next());
        Assert.Throws<OverflowException>(() => new AuthorityDiscontinuityId(ulong.MaxValue).Next());
        Assert.Throws<OverflowException>(() => new OwnerControlEpoch(ulong.MaxValue).Next());
        Assert.Throws<OverflowException>(() => new LocalPredictionRebaseId(ulong.MaxValue).Next());

        AssertInvalidDefault(default(MatchFrameEpochId), value => value.Next());
        AssertInvalidDefault(default(AuthorityDiscontinuityId), value => value.Next());
        AssertInvalidDefault(default(OwnerControlEpoch), value => value.Next());
        AssertInvalidDefault(default(LocalPredictionRebaseId), value => value.Next());
    }

    [Fact]
    public void InvalidDefaultsCannotParticipateInOrdering()
    {
        AssertInvalidOrdering(
            MatchFrameEpochId.Initial,
            (left, right) => left < right,
            (left, right) => left > right,
            (left, right) => left <= right,
            (left, right) => left >= right);
        AssertInvalidOrdering(
            AuthorityDiscontinuityId.Initial,
            (left, right) => left < right,
            (left, right) => left > right,
            (left, right) => left <= right,
            (left, right) => left >= right);
        AssertInvalidOrdering(
            OwnerControlEpoch.Initial,
            (left, right) => left < right,
            (left, right) => left > right,
            (left, right) => left <= right,
            (left, right) => left >= right);
        AssertInvalidOrdering(
            LocalPredictionRebaseId.Initial,
            (left, right) => left < right,
            (left, right) => left > right,
            (left, right) => left <= right,
            (left, right) => left >= right);
    }

    [Fact]
    public void EpochTypesExposeNoCrossTypeConversionOrMixedOperators()
    {
        var epochTypes = new[]
        {
            typeof(MatchFrameEpochId),
            typeof(AuthorityDiscontinuityId),
            typeof(OwnerControlEpoch),
            typeof(LocalPredictionRebaseId),
        };

        foreach (var source in epochTypes)
        {
            Assert.Equal(new[] { typeof(ulong) }, source.GetConstructors()
                .Single()
                .GetParameters()
                .Select(parameter => parameter.ParameterType));
            Assert.DoesNotContain(source.GetMethods(BindingFlags.Public | BindingFlags.Static),
                method => method.Name is "op_Implicit" or "op_Explicit");

            foreach (var method in source.GetMethods(BindingFlags.Public | BindingFlags.Static)
                         .Where(method => method.Name.StartsWith("op_", StringComparison.Ordinal)))
            {
                Assert.All(method.GetParameters(), parameter =>
                    Assert.Equal(source, parameter.ParameterType));
            }

            foreach (var target in epochTypes.Where(target => target != source))
            {
                Assert.False(target.IsAssignableFrom(source));
            }
        }
    }

    [Fact]
    public void DiagnosticTextNamesTheIdentityPlane()
    {
        Assert.Equal("match-frame-epoch:3", new MatchFrameEpochId(3).ToString());
        Assert.Equal("authority-discontinuity:3", new AuthorityDiscontinuityId(3).ToString());
        Assert.Equal("owner-control-epoch:3", new OwnerControlEpoch(3).ToString());
        Assert.Equal("local-prediction-rebase:3", new LocalPredictionRebaseId(3).ToString());
        Assert.Equal("match-frame-epoch:invalid", default(MatchFrameEpochId).ToString());
    }

    private static void AssertOrdering<T>(
        T earlier,
        T later,
        Func<T, T, bool> less,
        Func<T, T, bool> greater,
        Func<T, T, bool> lessOrEqual,
        Func<T, T, bool> greaterOrEqual)
        where T : struct, IComparable<T>
    {
        Assert.True(earlier.CompareTo(later) < 0);
        Assert.True(later.CompareTo(earlier) > 0);
        Assert.Equal(0, earlier.CompareTo(earlier));
        Assert.Equal(earlier, earlier);
        Assert.NotEqual(earlier, later);

        Assert.True(less(earlier, later));
        Assert.False(less(later, earlier));
        Assert.False(less(earlier, earlier));
        Assert.True(greater(later, earlier));
        Assert.False(greater(earlier, later));
        Assert.False(greater(later, later));
        Assert.True(lessOrEqual(earlier, later));
        Assert.True(lessOrEqual(earlier, earlier));
        Assert.False(lessOrEqual(later, earlier));
        Assert.True(greaterOrEqual(later, earlier));
        Assert.True(greaterOrEqual(later, later));
        Assert.False(greaterOrEqual(earlier, later));
    }

    private static void AssertInvalidOrdering<T>(
        T valid,
        Func<T, T, bool> less,
        Func<T, T, bool> greater,
        Func<T, T, bool> lessOrEqual,
        Func<T, T, bool> greaterOrEqual)
        where T : struct, IComparable<T>
    {
        var invalid = default(T);
        Assert.Throws<InvalidOperationException>(() => invalid.CompareTo(valid));
        Assert.Throws<InvalidOperationException>(() => valid.CompareTo(invalid));
        Assert.Throws<InvalidOperationException>(() => less(invalid, valid));
        Assert.Throws<InvalidOperationException>(() => less(valid, invalid));
        Assert.Throws<InvalidOperationException>(() => greater(invalid, valid));
        Assert.Throws<InvalidOperationException>(() => greater(valid, invalid));
        Assert.Throws<InvalidOperationException>(() => lessOrEqual(invalid, valid));
        Assert.Throws<InvalidOperationException>(() => lessOrEqual(valid, invalid));
        Assert.Throws<InvalidOperationException>(() => greaterOrEqual(invalid, valid));
        Assert.Throws<InvalidOperationException>(() => greaterOrEqual(valid, invalid));
    }

    private static void AssertInvalidDefault<T>(T value, Func<T, T> advance)
        where T : struct
    {
        var property = typeof(T).GetProperty("IsValid");
        Assert.NotNull(property);
        Assert.False((bool)property.GetValue(value)!);
        Assert.Throws<InvalidOperationException>(() => advance(value));
    }
}
