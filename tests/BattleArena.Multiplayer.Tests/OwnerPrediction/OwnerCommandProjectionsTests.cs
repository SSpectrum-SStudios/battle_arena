using System.Reflection;
using System.Runtime.CompilerServices;
using BattleArena.Core.Common;
using BattleArena.Core.Movement.Simulation;
using BattleArena.Multiplayer.OwnerPrediction;

namespace BattleArena.Multiplayer.Tests.OwnerPrediction;

public sealed class OwnerCommandProjectionsTests
{
    private readonly IAuthorityOwnerCommandProjector _authority =
        new AuthorityOwnerCommandProjector();
    private readonly IRemoteMovementHintProjector _direct =
        new RemoteMovementHintProjector();

    [Fact]
    public void AuthorityProjectionPreservesCompleteCombatGrammarExactly()
    {
        // P03-06 binds these durable IDs to the attack-press and attack-release
        // payloads. This boundary must preserve both references and held state.
        var attackPressed = new PredictedActionId(41);
        var attackReleased = new PredictedActionId(42);
        var actions = new ActionReferenceBuffer(
            new[] { attackPressed, attackReleased });
        var everyHeldCombatControl = Enum.GetValues<CombatHeldButtons>()
            .Aggregate(CombatHeldButtons.None, (combined, next) => combined | next);
        var command = Command(
            combat: new CombatInputState(everyHeldCombatControl),
            actions: actions);

        var projection = _authority.Project(command);

        Assert.True(projection.IsValid);
        Assert.Equal(command.Identity, projection.Identity);
        Assert.Equal(command.AuthorityDiscontinuity, projection.AuthorityDiscontinuity);
        Assert.Equal(command.TargetFrame, projection.TargetFrame);
        Assert.Equal(command.Input, projection.Input);
        Assert.Equal(everyHeldCombatControl, projection.Input.CombatInput.HeldButtons);
        Assert.True(projection.Input.CombatInput.IsHeld(CombatHeldButtons.Attack));
        Assert.Equal(attackPressed, projection.Input.ActionReferences[0]);
        Assert.Equal(attackReleased, projection.Input.ActionReferences[1]);
    }

    [Fact]
    public void DirectProjectionPreservesEveryMovementOnlyFieldExactly()
    {
        var command = Command(
            movement: new MovementAxes(12_000, -7_000),
            view: new ViewOrientation(51_000, -4_000),
            movementHeld: new MovementHeldState(
                MovementHeldButtons.Jump |
                MovementHeldButtons.Sprint |
                MovementHeldButtons.CrouchOrRoll),
            transitions: new TransitionReferenceBuffer(new[]
            {
                new MovementTransitionId(5),
                new MovementTransitionId(8),
            }),
            combat: new CombatInputState(
                CombatHeldButtons.Attack |
                CombatHeldButtons.ActivateWeapon),
            actions: new ActionReferenceBuffer(new[] { new PredictedActionId(9) }));

        var projection = _direct.Project(command);

        Assert.True(projection.IsValid);
        Assert.Equal(command.Identity, projection.Identity);
        Assert.Equal(command.AuthorityDiscontinuity, projection.AuthorityDiscontinuity);
        Assert.Equal(command.TargetFrame, projection.TargetFrame);
        Assert.Equal(command.Input.Movement, projection.Movement);
        Assert.Equal(command.Input.View, projection.View);
        Assert.Equal(command.Input.MovementHeld, projection.MovementHeld);
        Assert.Equal(command.Input.TransitionReferences, projection.TransitionReferences);
        Assert.Equal(command.Input.MovementRevision, projection.MovementRevision);
        Assert.Equal(command.Input.CapabilityRevision, projection.CapabilityRevision);
    }

    [Fact]
    public void DirectProjectionStructurallyCannotCarryGameplayAuthority()
    {
        var publicProperties = typeof(RemoteMovementHintProjection)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public);
        var allowedProperties = new Dictionary<string, Type>
        {
            [nameof(RemoteMovementHintProjection.Identity)] = typeof(OwnerInputIdentity),
            [nameof(RemoteMovementHintProjection.AuthorityDiscontinuity)] = typeof(AuthorityDiscontinuityId),
            [nameof(RemoteMovementHintProjection.TargetFrame)] = typeof(SimulationInstant),
            [nameof(RemoteMovementHintProjection.Movement)] = typeof(MovementAxes),
            [nameof(RemoteMovementHintProjection.View)] = typeof(ViewOrientation),
            [nameof(RemoteMovementHintProjection.MovementHeld)] = typeof(MovementHeldState),
            [nameof(RemoteMovementHintProjection.TransitionReferences)] = typeof(TransitionReferenceBuffer),
            [nameof(RemoteMovementHintProjection.MovementRevision)] = typeof(MovementConfigurationRevision),
            [nameof(RemoteMovementHintProjection.CapabilityRevision)] = typeof(MovementCapabilityRevision),
            [nameof(RemoteMovementHintProjection.IsValid)] = typeof(bool),
        };

        Assert.Equal(allowedProperties.Count, publicProperties.Length);
        Assert.All(publicProperties, property =>
        {
            Assert.True(allowedProperties.TryGetValue(property.Name, out var expected));
            Assert.Equal(expected, property.PropertyType);
        });
        Assert.DoesNotContain(publicProperties, property =>
            property.PropertyType == typeof(CharacterSimulationInput) ||
            property.PropertyType == typeof(CombatInputState) ||
            property.PropertyType == typeof(CombatHeldButtons) ||
            property.PropertyType == typeof(ActionReferenceBuffer) ||
            property.PropertyType == typeof(PredictedActionId) ||
            property.PropertyType == typeof(OwnerSimulationCommand));
        Assert.DoesNotContain(publicProperties, property =>
            new[] { "combat", "action", "attack", "block", "activate", "hit", "damage", "health" }
                .Any(forbidden => property.Name.Contains(
                    forbidden,
                    StringComparison.OrdinalIgnoreCase)));

        AssertMovementOnlyPublicGraph(typeof(RemoteMovementHintProjection));
    }

    [Fact]
    public void DirectProjectionConstructorRejectsEveryInvalidIdentityBoundary()
    {
        var valid = _direct.Project(Command());

        Assert.Throws<ArgumentOutOfRangeException>(() => new RemoteMovementHintProjection(
            default,
            valid.AuthorityDiscontinuity,
            valid.TargetFrame,
            valid.Movement,
            valid.View,
            valid.MovementHeld,
            valid.TransitionReferences,
            valid.MovementRevision,
            valid.CapabilityRevision));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RemoteMovementHintProjection(
            valid.Identity,
            default,
            valid.TargetFrame,
            valid.Movement,
            valid.View,
            valid.MovementHeld,
            valid.TransitionReferences,
            valid.MovementRevision,
            valid.CapabilityRevision));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RemoteMovementHintProjection(
            valid.Identity,
            valid.AuthorityDiscontinuity,
            valid.TargetFrame,
            valid.Movement,
            valid.View,
            valid.MovementHeld,
            valid.TransitionReferences,
            default,
            valid.CapabilityRevision));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RemoteMovementHintProjection(
            valid.Identity,
            valid.AuthorityDiscontinuity,
            valid.TargetFrame,
            valid.Movement,
            valid.View,
            valid.MovementHeld,
            valid.TransitionReferences,
            valid.MovementRevision,
            default));
    }

    [Fact]
    public void ProjectorsFailClosedForDefaultCommand()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _authority.Project(default));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _direct.Project(default));
        Assert.False(default(AuthorityOwnerCommandProjection).IsValid);
        Assert.False(default(RemoteMovementHintProjection).IsValid);
    }

    [Fact]
    public void ProjectionIsReadonlyAndAllocatesNothingPerCommand()
    {
        AssertReadonlyValue<AuthorityOwnerCommandProjection>();
        AssertReadonlyValue<RemoteMovementHintProjection>();
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AuthorityOwnerCommandProjection>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<RemoteMovementHintProjection>());
        var command = Command();
        _ = _authority.Project(command);
        _ = _direct.Project(command);
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < 1_000; index++)
        {
            _ = _authority.Project(command);
            _ = _direct.Project(command);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static void AssertReadonlyValue<T>() where T : struct
    {
        var type = typeof(T);
        Assert.True(type.GetCustomAttribute<IsReadOnlyAttribute>() is not null);
        Assert.All(
            type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public),
            field => Assert.True(field.IsInitOnly, field.Name));
    }

    private static void AssertMovementOnlyPublicGraph(Type root)
    {
        var forbiddenTypes = new HashSet<Type>
        {
            typeof(CharacterSimulationInput),
            typeof(CombatInputState),
            typeof(CombatHeldButtons),
            typeof(ActionReferenceBuffer),
            typeof(PredictedActionId),
            typeof(AuthorityActionExecutionId),
            typeof(AuthorityActionExecutionScope),
            typeof(AuthorityActionExecutionIdentity),
            typeof(ActionResolutionSequence),
            typeof(ActionResolutionIdentity),
            typeof(OwnerSimulationCommand),
            typeof(AuthorityOwnerCommandProjection),
            typeof(BattleArena.Core.Movement.MovementButtons),
            typeof(CombatActionDefinitionId),
        };
        var forbiddenMemberTokens = new[]
        {
            "CombatInput",
            "ActionReference",
            "PredictedAction",
            "Attack",
            "Block",
            "Activate",
            "Hit",
            "Damage",
            "Health",
        };
        var visited = new HashSet<Type>();

        Walk(root, root.Name);
        return;

        void Walk(Type type, string path)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            Assert.DoesNotContain(type, forbiddenTypes);
            if (!visited.Add(type) || type.IsPrimitive || type == typeof(decimal))
            {
                return;
            }

            if (type.Namespace is null ||
                !type.Namespace.StartsWith("BattleArena.", StringComparison.Ordinal))
            {
                return;
            }

            if (type.IsEnum)
            {
                Assert.All(Enum.GetNames(type), name =>
                    Assert.DoesNotContain(
                        forbiddenMemberTokens,
                        token => name.Contains(token, StringComparison.OrdinalIgnoreCase)));
                return;
            }

            foreach (var property in type.GetProperties(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                Assert.DoesNotContain(
                    forbiddenMemberTokens,
                    token => property.Name.Contains(token, StringComparison.OrdinalIgnoreCase));
                Walk(property.PropertyType, $"{path}.{property.Name}");
            }

            foreach (var field in type.GetFields(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                Assert.DoesNotContain(
                    forbiddenMemberTokens,
                    token => field.Name.Contains(token, StringComparison.OrdinalIgnoreCase));
                Walk(field.FieldType, $"{path}.{field.Name}");
            }

            foreach (var method in type.GetMethods(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                     .Where(method => !method.IsSpecialName))
            {
                Assert.DoesNotContain(
                    forbiddenMemberTokens,
                    token => method.Name.Contains(token, StringComparison.OrdinalIgnoreCase));
                Walk(method.ReturnType, $"{path}.{method.Name} return");
                foreach (var parameter in method.GetParameters())
                {
                    Walk(parameter.ParameterType, $"{path}.{method.Name} parameter");
                }
            }
        }
    }

    private static OwnerSimulationCommand Command(
        MovementAxes movement = default,
        ViewOrientation view = default,
        MovementHeldState movementHeld = default,
        TransitionReferenceBuffer transitions = default,
        CombatInputState combat = default,
        ActionReferenceBuffer actions = default)
    {
        var scope = new OwnerIntentScope(
            71,
            new LifeEpoch(new CombatantId(2), new LifeGenerationId(3)),
            new OwnerControlEpoch(4));
        var input = new CharacterSimulationInput(
            movement,
            view,
            movementHeld,
            transitions,
            combat,
            actions,
            new MovementConfigurationRevision(6),
            new MovementCapabilityRevision(7));
        return new OwnerSimulationCommand(
            new OwnerInputIdentity(scope, new InputSequence(12)),
            new AuthorityDiscontinuityId(5),
            new MatchFrameEpochId(1),
            new SimulationInstant(90),
            input);
    }
}
