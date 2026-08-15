using BattleArena.Multiplayer.Replication;

namespace BattleArena.Multiplayer.Tests.Replication;

public sealed class ReplicatedAttackPresentationTrackerTests
{
    [Fact]
    public void OlderSnapshotCannotClearNewerAttackEvent()
    {
        var tracker = new ReplicatedAttackPresentationTracker();
        var attack = new AttackPresentationIdentity(7, 0);
        tracker.Observe(101, attack, null, false);

        var decision = tracker.Observe(100, null, attack, false);

        Assert.Equal(AttackPresentationAction.Ignore, decision.Action);
        Assert.Equal(attack, tracker.ConfirmedIdentity);
    }

    [Fact]
    public void SameAttackRepairDoesNotRestartAnimation()
    {
        var tracker = new ReplicatedAttackPresentationTracker();
        var attack = new AttackPresentationIdentity(7, 0);
        tracker.Observe(101, attack, null, false);

        var decision = tracker.Observe(102, attack, attack, false);

        Assert.Equal(AttackPresentationAction.Apply, decision.Action);
        Assert.False(decision.RestartAnimation);
    }

    [Fact]
    public void PreConfirmationSnapshotPreservesLocalPredictionAndWatermark()
    {
        var tracker = new ReplicatedAttackPresentationTracker();
        var predicted = new AttackPresentationIdentity(7, 0);

        var snapshot = tracker.Observe(100, null, predicted, true);
        var action = tracker.Observe(99, predicted, predicted, true);

        Assert.Equal(
            AttackPresentationAction.PreserveLocalPrediction,
            snapshot.Action);
        Assert.Equal(AttackPresentationAction.Apply, action.Action);
        Assert.False(action.RestartAnimation);
        Assert.Equal(99UL, tracker.LastAuthorityTick);
    }

    [Fact]
    public void NewerAuthoritativeEndClearsConfirmedAttack()
    {
        var tracker = new ReplicatedAttackPresentationTracker();
        var attack = new AttackPresentationIdentity(7, 0);
        tracker.Observe(101, attack, null, false);

        var decision = tracker.Observe(120, null, attack, false);

        Assert.Equal(AttackPresentationAction.Clear, decision.Action);
        Assert.Null(tracker.ConfirmedIdentity);
    }
}
