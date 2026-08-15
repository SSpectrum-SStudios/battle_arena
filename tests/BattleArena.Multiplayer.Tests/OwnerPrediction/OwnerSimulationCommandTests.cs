using System.Reflection;
using System.Runtime.CompilerServices;
using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Core.Movement.Simulation;
using BattleArena.Multiplayer.OwnerPrediction;

namespace BattleArena.Multiplayer.Tests.OwnerPrediction;

public sealed class OwnerSimulationCommandTests
{
    [Fact]
    public void CommandComposesCompleteCoreInputWithMultiplayerIdentity()
    {
        var input = CreateInput(
            movement: MovementAxes.FromUnitVector(new HorizontalVector(0.25d, -1d)),
            view: ViewOrientation.FromRadians(Math.PI * 1.5d, -Math.PI / 4d),
            movementHeld: new MovementHeldState(
                MovementHeldButtons.Jump | MovementHeldButtons.Sprint),
            transitions: Transitions(3, 7),
            combat: new CombatInputState(
                CombatHeldButtons.Attack | CombatHeldButtons.ActivateAmulet),
            actions: Actions(4, 9));
        var command = CreateCommand(input: input);

        Assert.True(command.IsValid);
        Assert.Equal(new CombatantId(3), command.Life.CombatantId);
        Assert.Equal(new LifeGenerationId(2), command.Life.Life);
        Assert.Equal(new OwnerControlEpoch(5), command.ControlEpoch);
        Assert.Equal(new InputSequence(11), command.Sequence);
        Assert.Equal(new SimulationInstant(107), command.TargetFrame);
        Assert.True(command.Input.MovementHeld.IsHeld(MovementHeldButtons.Jump));
        Assert.True(command.Input.CombatInput.IsHeld(CombatHeldButtons.Attack));
        Assert.Equal(new MovementTransitionId(7), command.Input.TransitionReferences[1]);
        Assert.Equal(new PredictedActionId(9), command.Input.ActionReferences[1]);
        Assert.Equal(13UL, command.Input.MovementRevision.Value);
        Assert.Equal(17UL, command.Input.CapabilityRevision.Value);
    }

    [Fact]
    public void SimulationInputTypesAreCoreOwnedAndDeliveryIdentityIsNot()
    {
        var coreAssembly = typeof(CharacterSimulationInput).Assembly;
        var multiplayerAssembly = typeof(OwnerSimulationCommand).Assembly;

        Assert.Equal(coreAssembly, typeof(MovementAxes).Assembly);
        Assert.Equal(coreAssembly, typeof(ViewOrientation).Assembly);
        Assert.Equal(coreAssembly, typeof(MovementHeldState).Assembly);
        Assert.Equal(coreAssembly, typeof(CombatInputState).Assembly);
        Assert.Equal(coreAssembly, typeof(MovementConfigurationRevision).Assembly);
        Assert.Equal(coreAssembly, typeof(MovementCapabilityRevision).Assembly);
        Assert.Equal(multiplayerAssembly, typeof(OwnerInputIdentity).Assembly);
        Assert.DoesNotContain(
            coreAssembly.GetReferencedAssemblies(),
            reference => reference.Name == multiplayerAssembly.GetName().Name);
    }

    [Fact]
    public void FrameZeroIsAValidCommandTarget()
    {
        var command = CreateCommand(targetFrame: SimulationInstant.Zero);

        Assert.True(command.IsValid);
        Assert.Equal(SimulationInstant.Zero, command.TargetFrame);
        Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationInstant(-1));
    }

    [Fact]
    public void MovementAxesAreCanonicalAtAllImportantBoundaries()
    {
        Assert.Equal(default, MovementAxes.FromUnitVector(HorizontalVector.Zero));
        Assert.Equal(
            new MovementAxes(short.MaxValue, 0),
            MovementAxes.FromUnitVector(new HorizontalVector(1d, 0d)));
        Assert.Equal(
            new MovementAxes(-short.MaxValue, 0),
            MovementAxes.FromUnitVector(new HorizontalVector(-1d, 0d)));

        var halfQuantum = 0.5d / MovementAxes.MaximumMagnitude;
        Assert.Equal((short)1, MovementAxes.FromUnitVector(
            new HorizontalVector(halfQuantum, 0d)).XQ15);
        Assert.Equal((short)-1, MovementAxes.FromUnitVector(
            new HorizontalVector(-halfQuantum, 0d)).XQ15);

        var diagonal = MovementAxes.FromUnitVector(new HorizontalVector(1d, 1d));
        var huge = MovementAxes.FromUnitVector(
            new HorizontalVector(double.MaxValue, double.MaxValue));
        Assert.Equal(diagonal, huge);
        Assert.True(diagonal.ToUnitVector().Length <= 1d);
        Assert.Equal(diagonal, MovementAxes.FromUnitVector(diagonal.ToUnitVector()));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MovementAxes(short.MaxValue, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MovementAxes.FromUnitVector(new HorizontalVector(double.NaN, 0d)));
    }

    [Fact]
    public void ViewOrientationIsCanonicalAtAllImportantBoundaries()
    {
        Assert.Equal(default, ViewOrientation.FromRadians(0d, 0d));
        Assert.Equal(default, ViewOrientation.FromRadians(Math.Tau, 0d));
        Assert.Equal(default, ViewOrientation.FromRadians(-Math.Tau, 0d));
        Assert.Equal(short.MaxValue, ViewOrientation.FromRadians(
            0d, Math.PI / 2d).PitchI16);
        Assert.Equal(-short.MaxValue, ViewOrientation.FromRadians(
            0d, -Math.PI / 2d).PitchI16);

        var yawHalfQuantum = Math.Tau / (ushort.MaxValue + 1d) / 2d;
        var pitchHalfQuantum = Math.PI / 2d / short.MaxValue / 2d;
        Assert.Equal((ushort)1, ViewOrientation.FromRadians(
            yawHalfQuantum, 0d).YawU16);
        Assert.Equal((short)1, ViewOrientation.FromRadians(
            0d, pitchHalfQuantum).PitchI16);
        Assert.Equal((short)-1, ViewOrientation.FromRadians(
            0d, -pitchHalfQuantum).PitchI16);

        var sample = ViewOrientation.FromRadians(Math.PI * 1.5d, -Math.PI / 4d);
        Assert.Equal(sample, ViewOrientation.FromRadians(
            sample.YawRadians, sample.PitchRadians));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ViewOrientation.FromRadians(0d, Math.PI));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ViewOrientation.FromRadians(double.PositiveInfinity, 0d));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ViewOrientation(0, short.MinValue));
    }

    [Fact]
    public void EverySupportedHeldControlRoundTrips()
    {
        foreach (var button in Enum.GetValues<MovementHeldButtons>().Where(value => value != 0))
        {
            var state = new MovementHeldState(button);
            Assert.Equal(button, state.Buttons);
            Assert.True(state.IsHeld(button));
        }

        foreach (var button in Enum.GetValues<CombatHeldButtons>().Where(value => value != 0))
        {
            var state = new CombatInputState(button);
            Assert.Equal(button, state.HeldButtons);
            Assert.True(state.IsHeld(button));
        }

        Assert.Equal(9, Enum.GetValues<CombatHeldButtons>().Count(value => value != 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MovementHeldState((MovementHeldButtons)0x80));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CombatInputState((CombatHeldButtons)0x8000));
    }

    [Fact]
    public void TransitionBufferCoversAllBoundariesAndOwnsItsValues()
    {
        var source = Enumerable.Range(1, TransitionReferenceBuffer.Capacity)
            .Select(value => new MovementTransitionId((ulong)value))
            .ToArray();
        var buffer = new TransitionReferenceBuffer(source);
        source[0] = new MovementTransitionId(99);
        var copy = new MovementTransitionId[buffer.Count];
        buffer.CopyTo(copy);

        Assert.Equal(OwnerSimulationLimits.MaximumTransitionReferences, buffer.Count);
        Assert.Equal(new MovementTransitionId(1), buffer[0]);
        Assert.Equal(new MovementTransitionId((ulong)buffer.Count), buffer[buffer.Count - 1]);
        Assert.Equal(new MovementTransitionId(1), copy[0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer[buffer.Count]);
        Assert.Throws<ArgumentException>(() =>
            buffer.CopyTo(new MovementTransitionId[buffer.Count - 1]));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TransitionReferenceBuffer(Enumerable.Range(1, buffer.Count + 1)
                .Select(value => new MovementTransitionId((ulong)value)).ToArray()));
        Assert.Throws<ArgumentException>(() =>
            new TransitionReferenceBuffer(new[]
                { new MovementTransitionId(2), new MovementTransitionId(2) }));
        Assert.Throws<ArgumentException>(() =>
            new TransitionReferenceBuffer(new[]
                { new MovementTransitionId(2), new MovementTransitionId(1) }));
        Assert.Throws<ArgumentException>(() =>
            new TransitionReferenceBuffer(new MovementTransitionId[] { default }));
    }

    [Fact]
    public void ActionBufferCoversAllBoundariesAndOwnsItsValues()
    {
        var source = Enumerable.Range(1, ActionReferenceBuffer.Capacity)
            .Select(value => new PredictedActionId((ulong)value))
            .ToArray();
        var buffer = new ActionReferenceBuffer(source);
        source[0] = new PredictedActionId(99);
        var copy = new PredictedActionId[buffer.Count];
        buffer.CopyTo(copy);

        Assert.Equal(OwnerSimulationLimits.MaximumActionReferences, buffer.Count);
        Assert.Equal(new PredictedActionId(1), buffer[0]);
        Assert.Equal(new PredictedActionId((ulong)buffer.Count), buffer[buffer.Count - 1]);
        Assert.Equal(new PredictedActionId(1), copy[0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer[buffer.Count]);
        Assert.Throws<ArgumentException>(() =>
            buffer.CopyTo(new PredictedActionId[buffer.Count - 1]));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ActionReferenceBuffer(Enumerable.Range(1, buffer.Count + 1)
                .Select(value => new PredictedActionId((ulong)value)).ToArray()));
        Assert.Throws<ArgumentException>(() =>
            new ActionReferenceBuffer(new[]
                { new PredictedActionId(2), new PredictedActionId(2) }));
        Assert.Throws<ArgumentException>(() =>
            new ActionReferenceBuffer(new[]
                { new PredictedActionId(2), new PredictedActionId(1) }));
        Assert.Throws<ArgumentException>(() =>
            new ActionReferenceBuffer(new PredictedActionId[] { default }));
    }

    [Fact]
    public void FixedBuffersHaveCompleteValueEquality()
    {
        var transitions = Transitions(1, 2);
        var equalTransitions = Transitions(1, 2);
        var differentTransitions = Transitions(1, 3);
        var shorterTransitions = Transitions(1);
        var actions = Actions(1, 2);
        var equalActions = Actions(1, 2);
        var differentActions = Actions(1, 3);
        var shorterActions = Actions(1);

        Assert.True(transitions == equalTransitions);
        Assert.False(transitions != equalTransitions);
        Assert.Equal(transitions.GetHashCode(), equalTransitions.GetHashCode());
        Assert.NotEqual(transitions, differentTransitions);
        Assert.NotEqual(transitions, shorterTransitions);
        Assert.True(actions == equalActions);
        Assert.False(actions != equalActions);
        Assert.Equal(actions.GetHashCode(), equalActions.GetHashCode());
        Assert.NotEqual(actions, differentActions);
        Assert.NotEqual(actions, shorterActions);
    }

    [Fact]
    public void InputsAndCommandsHaveCompleteValueEquality()
    {
        var firstInput = CreateInput(transitions: Transitions(1), actions: Actions(2));
        var equalInput = CreateInput(transitions: Transitions(1), actions: Actions(2));
        var differentInput = CreateInput(transitions: Transitions(3), actions: Actions(2));
        var first = CreateCommand(input: firstInput);
        var equal = CreateCommand(input: equalInput);
        var different = CreateCommand(input: differentInput);

        Assert.Equal(firstInput, equalInput);
        Assert.NotEqual(firstInput, differentInput);
        Assert.Equal(first, equal);
        Assert.NotEqual(first, different);
    }

    [Fact]
    public void CommandRejectsInvalidIdentityDiscontinuityAndInput()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateCommand(identity: new OwnerInputIdentity()));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateCommand(discontinuity: new AuthorityDiscontinuityId()));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateCommand(input: new CharacterSimulationInput()));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateInput(movementRevision: new MovementConfigurationRevision()));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateInput(capabilityRevision: new MovementCapabilityRevision()));
        Assert.False(default(CharacterSimulationInput).IsValid);
        Assert.False(default(OwnerSimulationCommand).IsValid);
    }

    [Fact]
    public void ValuesAreReadonlyAllocationFreeAndContainNoHiddenTiming()
    {
        AssertReadonlyValue<CharacterSimulationInput>();
        AssertReadonlyValue<OwnerSimulationCommand>();
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<TransitionReferenceBuffer>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<ActionReferenceBuffer>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<CharacterSimulationInput>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<OwnerSimulationCommand>());

        var forbiddenTimingNames = new[]
        {
            "clienttick", "estimatedauthority", "timestamp", "elapsed", "delta", "arrival", "wallclock",
        };
        Assert.DoesNotContain(
            typeof(OwnerSimulationCommand).GetProperties(),
            property => forbiddenTimingNames.Any(forbidden =>
                property.Name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(
            typeof(CharacterSimulationInput).GetProperties(),
            property => property.PropertyType == typeof(SimulationInstant));
        Assert.Single(
            typeof(OwnerSimulationCommand).GetProperties(),
            property => property.PropertyType == typeof(SimulationInstant));
    }

    private static void AssertReadonlyValue<T>() where T : struct
    {
        var type = typeof(T);
        Assert.True(type.GetCustomAttribute<IsReadOnlyAttribute>() is not null);
        Assert.All(
            type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public),
            field => Assert.True(field.IsInitOnly, field.Name));
    }

    private static TransitionReferenceBuffer Transitions(params ulong[] values) =>
        new(values.Select(value => new MovementTransitionId(value)).ToArray());

    private static ActionReferenceBuffer Actions(params ulong[] values) =>
        new(values.Select(value => new PredictedActionId(value)).ToArray());

    private static CharacterSimulationInput CreateInput(
        MovementAxes movement = default,
        ViewOrientation view = default,
        MovementHeldState movementHeld = default,
        TransitionReferenceBuffer transitions = default,
        CombatInputState combat = default,
        ActionReferenceBuffer actions = default,
        MovementConfigurationRevision? movementRevision = null,
        MovementCapabilityRevision? capabilityRevision = null) => new(
            movement,
            view,
            movementHeld,
            transitions,
            combat,
            actions,
            movementRevision ?? new MovementConfigurationRevision(13),
            capabilityRevision ?? new MovementCapabilityRevision(17));

    [Fact]
    public void MatchFrameEpochIsRequiredAndPreservedExactly()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateCommand(matchFrameEpoch: default(MatchFrameEpochId)));

        var command = CreateCommand(matchFrameEpoch: new MatchFrameEpochId(9));

        Assert.Equal(new MatchFrameEpochId(9), command.MatchFrameEpoch);
        Assert.True(command.IsValid);
        Assert.NotEqual(
            command,
            CreateCommand(matchFrameEpoch: new MatchFrameEpochId(10)));
    }

    private static OwnerSimulationCommand CreateCommand(
        OwnerInputIdentity? identity = null,
        AuthorityDiscontinuityId? discontinuity = null,
        SimulationInstant? targetFrame = null,
        CharacterSimulationInput? input = null,
        MatchFrameEpochId? matchFrameEpoch = null)
    {
        var scope = new OwnerIntentScope(
            sessionId: 31,
            new LifeEpoch(new CombatantId(3), new LifeGenerationId(2)),
            new OwnerControlEpoch(5));
        return new OwnerSimulationCommand(
            identity ?? new OwnerInputIdentity(scope, new InputSequence(11)),
            discontinuity ?? new AuthorityDiscontinuityId(7),
            matchFrameEpoch ?? new MatchFrameEpochId(1),
            targetFrame ?? new SimulationInstant(107),
            input ?? CreateInput());
    }
}
