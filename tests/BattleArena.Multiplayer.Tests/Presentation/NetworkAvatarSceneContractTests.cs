namespace BattleArena.Multiplayer.Tests.Presentation;

public sealed class NetworkAvatarSceneContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string ScenePath = Path.Combine(
        RepositoryRoot,
        "scenes",
        "multiplayer",
        "network_avatar.tscn");
    private static readonly string AvatarSourcePath = Path.Combine(
        RepositoryRoot,
        "scripts",
        "multiplayer",
        "NetworkAvatar.cs");

    [Fact]
    public void SceneUsesSiblingSimulationVisualAndCameraAnchors()
    {
        var scene = File.ReadAllText(ScenePath);

        Assert.Contains("[node name=\"NetworkAvatar\" type=\"Node3D\"]", scene);
        Assert.Contains(
            "[node name=\"SimulationBody\" type=\"CharacterBody3D\" parent=\".\"]",
            scene);
        Assert.Contains(
            "[node name=\"VisualAnchor\" type=\"Node3D\" parent=\".\"]",
            scene);
        Assert.Contains(
            "[node name=\"CameraAnchor\" type=\"Node3D\" parent=\".\"]",
            scene);
        Assert.DoesNotContain("parent=\"SimulationBody/Visual", scene);
        Assert.DoesNotContain("parent=\"SimulationBody/Camera", scene);
    }

    [Fact]
    public void CollisionModelAndCameraLiveUnderTheirOwnAnchors()
    {
        var scene = File.ReadAllText(ScenePath);

        Assert.Contains(
            "[node name=\"BodyCollision\" type=\"CollisionShape3D\" parent=\"SimulationBody\"]",
            scene);
        Assert.Contains(
            "[node name=\"VisualRoot\" type=\"Node3D\" parent=\"VisualAnchor\"]",
            scene);
        Assert.Contains(
            "[node name=\"KnightCharacterView\" parent=\"VisualAnchor/VisualRoot\"",
            scene);
        Assert.Contains(
            "[node name=\"CameraYaw\" type=\"Node3D\" parent=\"CameraAnchor\"]",
            scene);
        Assert.Contains(
            "parent=\"CameraAnchor/CameraYaw/CameraPitch/SpringArm\"",
            scene);
    }

    [Theory]
    [InlineData("SimulationBodyPath", "SimulationBody")]
    [InlineData("VisualAnchorPath", "VisualAnchor")]
    [InlineData("CameraAnchorPath", "CameraAnchor")]
    [InlineData("VisualRootPath", "VisualAnchor/VisualRoot")]
    [InlineData("CharacterViewPath", "VisualAnchor/VisualRoot/KnightCharacterView")]
    [InlineData("CameraYawPath", "CameraAnchor/CameraYaw")]
    [InlineData("CameraPitchPath", "CameraAnchor/CameraYaw/CameraPitch")]
    [InlineData(
        "ThirdPersonCameraPath",
        "CameraAnchor/CameraYaw/CameraPitch/SpringArm/ThirdPersonCamera")]
    [InlineData("CollisionPath", "SimulationBody/BodyCollision")]
    [InlineData("LabelPath", "VisualAnchor/StatusLabel")]
    public void ExportedFacadePathResolvesToComposedNode(
        string property,
        string nodePath)
    {
        var scene = File.ReadAllText(ScenePath);

        Assert.Contains($"{property} = NodePath(\"{nodePath}\")", scene);
    }

    [Fact]
    public void RuntimeFacadeDelegatesGameplayStateOnlyToSimulationBody()
    {
        var source = File.ReadAllText(AvatarSourcePath);

        Assert.Contains("class NetworkAvatar : Node3D", source);
        Assert.Contains("get => _simulationBody.Position;", source);
        Assert.Contains("_simulationBody.Position = value;", source);
        Assert.Contains(
            "BeginBlockingBodyCommit(BlockingBodyCommitKind.Motion);",
            source);
        Assert.Contains("get => _simulationBody.Velocity;", source);
        Assert.Contains("public Rid GetRid() => _simulationBody.GetRid();", source);
        Assert.Contains("public bool IsOnFloor() => _simulationBody.IsOnFloor();", source);
        Assert.Contains(
            "new GodotCharacterMovementDriver(\n            _simulationBody,",
            source.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [Fact]
    public void RoutineAuthorityCorrectionCannotCommitVisualOrCameraPosition()
    {
        var source = File.ReadAllText(AvatarSourcePath);
        var snapshot = ExtractMethod(source, "ApplyAuthoritativeSnapshot");
        var movement = ExtractMethod(source, "ApplyAuthoritativeMovementState");

        foreach (var method in new[] { snapshot, movement })
        {
            Assert.Contains("Position =", method);
            Assert.DoesNotContain("_visualAnchor.GlobalPosition", method);
            Assert.DoesNotContain("_cameraAnchor.GlobalPosition", method);
            Assert.DoesNotContain("CommitLivePresentationAnchors", method);
            Assert.DoesNotContain("SnapPresentationAnchorsToSimulation", method);
        }
    }

    [Fact]
    public void LiveSimulationAndExplicitDiscontinuityUseDifferentCommitPolicies()
    {
        var source = File.ReadAllText(AvatarSourcePath);
        var simulate = ExtractMethod(source, "Simulate");
        var stateOnly = ExtractMethod(source, "SimulateStateOnly");
        var publish = ExtractMethod(source, "PublishCommittedPresentation");
        var spawn = ExtractMethod(source, "ApplyReplicatedTransform");
        var respawn = ExtractMethod(source, "ResetForRespawn");
        var respawnTravel = ExtractMethod(source, "SetRespawnTransitionPosition");
        var hardSnap = ExtractMethod(source, "SnapPresentationAnchorsToSimulation");

        Assert.Contains("SimulateStateOnly(", simulate);
        Assert.Contains("PublishCommittedPresentation(", simulate);
        Assert.DoesNotContain("CommitLivePresentationAnchors();", stateOnly);
        Assert.Contains("CommitLivePresentationAnchors();", publish);
        Assert.Contains("SnapPresentationAnchorsToSimulation();", spawn);
        Assert.Contains("SnapPresentationAnchorsToSimulation();", respawn);
        Assert.Contains("CommitLivePresentationAnchors();", respawnTravel);
        Assert.DoesNotContain("SnapPresentationAnchorsToSimulation", respawnTravel);
        Assert.DoesNotContain("ResetPhysicsInterpolation", respawnTravel);
        Assert.Contains("_simulationBody.ResetPhysicsInterpolation();", hardSnap);
        Assert.Contains("_visualAnchor.ResetPhysicsInterpolation();", hardSnap);
        Assert.Contains("_cameraAnchor.ResetPhysicsInterpolation();", hardSnap);
    }

    [Fact]
    public void CameraSafetyUsesRenderedModelRatherThanAuthorityCollider()
    {
        var source = File.ReadAllText(AvatarSourcePath);
        var visibility = ExtractMethod(source, "UpdateCameraSafeVisibility");

        Assert.Contains("_visualAnchor.GlobalPosition", visibility);
        Assert.DoesNotContain("_simulationBody.GlobalPosition", visibility);
    }

    [Fact]
    public void RemotePredictionWritesOnlyTheVisualAnchorPosition()
    {
        var source = File.ReadAllText(AvatarSourcePath);
        var remote = ExtractMethod(source, "ApplyRemotePresentation");

        Assert.Contains("_visualAnchor.GlobalPosition = prediction.Position;", remote);
        Assert.DoesNotContain("_simulationBody", remote);
        Assert.DoesNotContain("_cameraAnchor", remote);
        Assert.DoesNotContain("CommitLivePresentationAnchors", remote);
        Assert.DoesNotContain("SnapPresentationAnchorsToSimulation", remote);
    }

    [Fact]
    public void PostSpawnBlockingBodyWritesAreGuardedByFixedPhysics()
    {
        var source = File.ReadAllText(AvatarSourcePath);
        var guard = ExtractMethod(source, "BeginBlockingBodyCommit");
        var collision = ExtractMethod(source, "SetCollisionDisabled");

        Assert.Contains("Engine.IsInPhysicsFrame()", guard);
        Assert.Contains("BlockingCommitPhaseViolationCount", source);
        Assert.Contains("RequireFixedPhysicsBlockingCommits", source);
        Assert.Contains("BeginBlockingBodyCommit", collision);
        Assert.Contains("CaptureBlockingBodySnapshot", source);
        Assert.Contains("capsule?.Height", source);
        Assert.Contains("capsule?.Radius", source);
    }

    private static string ExtractMethod(string source, string methodName)
    {
        var signature = source.IndexOf(
            $"public void {methodName}(",
            StringComparison.Ordinal);
        if (signature < 0)
        {
            signature = source.IndexOf(
                $"private void {methodName}(",
                StringComparison.Ordinal);
        }

        Assert.True(signature >= 0, $"Method '{methodName}' was not found.");
        var openingBrace = source.IndexOf('{', signature);
        Assert.True(openingBrace >= 0, $"Method '{methodName}' has no body.");
        var depth = 0;
        for (var index = openingBrace; index < source.Length; index++)
        {
            switch (source[index])
            {
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return source[openingBrace..(index + 1)];
                    }

                    break;
            }
        }

        throw new InvalidOperationException($"Method '{methodName}' has an unterminated body.");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "scenes",
                    "multiplayer",
                    "network_avatar.tscn")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the battle_arena repository from the test output directory.");
    }
}
