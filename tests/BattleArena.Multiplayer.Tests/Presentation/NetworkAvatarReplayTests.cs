using BattleArena.Multiplayer.Presentation;

namespace BattleArena.Multiplayer.Tests.Presentation;

public sealed class NetworkAvatarReplayTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string AvatarSourcePath = Path.Combine(
        RepositoryRoot,
        "scripts",
        "multiplayer",
        "NetworkAvatar.cs");
    private static readonly string ArenaSourcePath = Path.Combine(
        RepositoryRoot,
        "scripts",
        "multiplayer",
        "NetworkArena.cs");

    [Fact]
    public void HistoricalMovementReplayIsSimulationOnly()
    {
        var replay = ExtractMethod(
            File.ReadAllText(AvatarSourcePath),
            "ReplayPredictedMovement");

        Assert.Contains("_movementDriver.Simulate(", replay);
        foreach (var forbidden in PresentationSideEffectTokens)
        {
            Assert.DoesNotContain(forbidden, replay);
        }
    }

    [Fact]
    public void AuthorityMovementRestoreIsSimulationOnly()
    {
        var restore = ExtractMethod(
            File.ReadAllText(AvatarSourcePath),
            "ApplyAuthoritativeMovementState");

        Assert.Contains("Position = ToGodot(state.Position);", restore);
        Assert.Contains("_movementDriver.Restore(", restore);
        foreach (var forbidden in PresentationSideEffectTokens)
        {
            Assert.DoesNotContain(forbidden, restore);
        }

        Assert.DoesNotContain("_yaw =", restore);
        Assert.DoesNotContain("_pitch =", restore);
    }

    [Fact]
    public void FinalFrameBoundaryPublishesViewAnchorsAndPresentationOnce()
    {
        var publish = ExtractMethod(
            File.ReadAllText(AvatarSourcePath),
            "PublishCommittedPresentation");

        Assert.Equal(1, CountOccurrences(publish, "ApplyView(yaw, pitch);"));
        Assert.Equal(1, CountOccurrences(publish, "CommitLivePresentationAnchors();"));
        Assert.Equal(1, CountOccurrences(publish, "UpdatePresentation(delta);"));
        Assert.DoesNotContain("_movementDriver", publish);
    }

    [Fact]
    public void ThirtyReplayedCommandsLeadToZeroHistoricalAndOneAuthorizedPublication()
    {
        var coordinator = new PredictionFramePresentationCoordinator();
        var presentationOperations = 0;
        coordinator.BeginFrame();
        coordinator.RecordStateOnlyStep(PredictionFrameStateOnlyStep.AuthorityRestore);
        for (var index = 0; index < 30; index++)
        {
            coordinator.RecordStateOnlyStep(PredictionFrameStateOnlyStep.HistoricalReplay);
        }

        Assert.Equal(0, presentationOperations);
        Assert.Equal(1, coordinator.AuthorityRestoreCount);
        Assert.Equal(30, coordinator.HistoricalReplayCount);
        Assert.Equal(0, coordinator.CurrentSimulationCount);
        if (coordinator.CompleteFrame() ==
            PredictionFramePublicationDecision.PublishFinal)
        {
            presentationOperations++;
        }

        Assert.Equal(1, presentationOperations);
        Assert.Throws<InvalidOperationException>(() => coordinator.CompleteFrame());
    }

    [Theory]
    [InlineData(false, 0, false)]
    [InlineData(false, 0, true)]
    [InlineData(true, 0, true)]
    [InlineData(true, 30, true)]
    [InlineData(true, 0, false)]
    public void ZeroHistoryAliveAndEliminatedFramesAuthorizeExactlyOnePublication(
        bool hasAuthorityRestore,
        int replayCount,
        bool simulateCurrent)
    {
        var coordinator = new PredictionFramePresentationCoordinator();
        coordinator.BeginFrame();
        if (hasAuthorityRestore)
        {
            coordinator.RecordStateOnlyStep(PredictionFrameStateOnlyStep.AuthorityRestore);
        }

        for (var index = 0; index < replayCount; index++)
        {
            coordinator.RecordStateOnlyStep(PredictionFrameStateOnlyStep.HistoricalReplay);
        }

        if (simulateCurrent)
        {
            coordinator.RecordStateOnlyStep(PredictionFrameStateOnlyStep.CurrentSimulation);
        }

        var publications = coordinator.CompleteFrame() ==
            PredictionFramePublicationDecision.PublishFinal ? 1 : 0;
        Assert.Equal(1, publications);
        Assert.Equal(hasAuthorityRestore ? 1 : 0, coordinator.AuthorityRestoreCount);
        Assert.Equal(replayCount, coordinator.HistoricalReplayCount);
        Assert.Equal(simulateCurrent ? 1 : 0, coordinator.CurrentSimulationCount);
    }

    [Fact]
    public void ClientOuterFrameOwnsTheOnlyAliveOrEliminatedPublication()
    {
        var arenaSource = File.ReadAllText(ArenaSourcePath);
        var simulateClient = ExtractMethod(arenaSource, "SimulateClient");
        var reconcile = ExtractMethod(arenaSource, "ReconcileLocalPrediction");

        Assert.Equal(1, CountOccurrences(simulateClient, "BeginFrame();"));
        Assert.Equal(1, CountOccurrences(simulateClient, "CompleteFrame()"));
        Assert.Equal(1, CountOccurrences(simulateClient, "PublishCommittedPresentation("));
        Assert.Equal(1, CountOccurrences(simulateClient, "SimulateStateOnly("));
        Assert.DoesNotContain("_localAvatar.Simulate(", simulateClient);
        Assert.DoesNotContain("PublishCommittedPresentation(", reconcile);
        Assert.DoesNotContain("_localAvatar.ApplyView", reconcile);
    }

    [Fact]
    public void ClientCurrentSimulationDefersEveryPresentationSideEffect()
    {
        var stateOnly = ExtractMethod(
            File.ReadAllText(AvatarSourcePath),
            "SimulateStateOnly");

        Assert.Contains("_movementDriver.Simulate(", stateOnly);
        foreach (var forbidden in PresentationSideEffectTokens)
        {
            Assert.DoesNotContain(forbidden, stateOnly);
        }
    }

    [Fact]
    public void RemoteCollisionRestoreAndVisualPublicationUseSeparateClocks()
    {
        var arenaSource = File.ReadAllText(ArenaSourcePath);
        var physics = ExtractMethod(arenaSource, "_PhysicsProcess");
        var receive = ExtractMethod(arenaSource, "OnPacketReceived");
        var queue = ExtractMethod(arenaSource, "QueueClientAuthorityPacket");
        var drain = ExtractMethod(
            arenaSource,
            "DrainClientAuthorityPacketsAtPhysicsBoundary");
        var collisionCommit = ExtractMethod(
            arenaSource,
            "CommitRemoteCollisionBodiesAtPhysicsBoundary");
        var interpolate = ExtractMethod(
            arenaSource,
            "InterpolateRemoteAvatars");

        var drainIndex = physics.IndexOf(
            "DrainClientAuthorityPacketsAtPhysicsBoundary();",
            StringComparison.Ordinal);
        var commitIndex = physics.IndexOf(
            "CommitRemoteCollisionBodiesAtPhysicsBoundary();",
            StringComparison.Ordinal);
        Assert.True(drainIndex >= 0);
        Assert.True(commitIndex > drainIndex);
        Assert.Contains("QueueClientAuthorityPacket(packet);", receive);
        Assert.DoesNotContain("ReceiveAuthoritySnapshot(", receive);
        Assert.Contains("_networkTimeSource.GetTimestampMicroseconds()", queue);
        Assert.Contains("Engine.IsInPhysicsFrame()", drain);
        Assert.Contains(
            "ReceiveAuthoritySnapshot(packet, pending.ReceivedAtMicroseconds);",
            drain);
        Assert.Contains("ReceiveAuthorityEvents(packet);", drain);
        Assert.Contains(
            "ReceiveAuthorityMovementPacket(packet, pending.ReceivedAtMicroseconds);",
            drain);
        Assert.Contains(
            "ReceiveClockSyncReply(packet, pending.ReceivedAtMicroseconds);",
            drain);
        Assert.Contains("Engine.IsInPhysicsFrame()", collisionCommit);
        Assert.Equal(
            1,
            CountOccurrences(
                collisionCommit,
                "avatar.ApplyAuthoritativeMovementState(latestFrame.State);"));
        Assert.DoesNotContain("ApplyAuthoritativeMovementState(", interpolate);
        Assert.Equal(1, CountOccurrences(interpolate, "ApplyRemotePresentation("));
    }

    [Fact]
    public void CoordinatorLifecycleFailsClosed()
    {
        var coordinator = new PredictionFramePresentationCoordinator();
        Assert.Equal(PredictionFramePublicationDecision.Unspecified,
            default(PredictionFramePublicationDecision));
        Assert.Throws<InvalidOperationException>(() => coordinator.RecordStateOnlyStep(
            PredictionFrameStateOnlyStep.HistoricalReplay));
        Assert.Throws<InvalidOperationException>(() => coordinator.CompleteFrame());
        coordinator.BeginFrame();
        Assert.Throws<InvalidOperationException>(() => coordinator.BeginFrame());
        Assert.Throws<ArgumentOutOfRangeException>(() => coordinator.RecordStateOnlyStep(
            PredictionFrameStateOnlyStep.Unspecified));
    }

    private static readonly string[] PresentationSideEffectTokens =
    [
        "ApplyView(",
        "UpdatePresentation(",
        "CommitLivePresentationAnchors(",
        "SnapPresentationAnchorsToSimulation(",
        "_visualAnchor",
        "_cameraAnchor",
        "_characterView",
        "RefreshStatusPresentation(",
        "StatusChanged",
        "CreateTween(",
        ".Play(",
        ".Flash(",
    ];

    private static int CountOccurrences(string value, string substring)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(substring, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += substring.Length;
        }

        return count;
    }

    private static string ExtractMethod(string source, string methodName)
    {
        var signature = source.IndexOf(
            $"public void {methodName}(",
            StringComparison.Ordinal);
        if (signature < 0)
        {
            signature = source.IndexOf(
                $"public override void {methodName}(",
                StringComparison.Ordinal);
        }

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
                    "scripts",
                    "multiplayer",
                    "NetworkAvatar.cs")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the battle_arena repository from the test output directory.");
    }
}
