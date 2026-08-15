using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Multiplayer.Replication;

namespace BattleArena.Multiplayer.Tests.Replication;

public sealed class AuthorityMovementInputBufferTests
{
    [Fact]
    public void BacklogCompactsToFreshestIntentInsteadOfBecomingLatency()
    {
        var buffer = new AuthorityMovementInputBuffer();
        for (ulong sequence = 1; sequence <= 180; sequence++)
        {
            buffer.Enqueue(Command(
                sequence,
                moveX: sequence == 180 ? 1d : 0d));
        }

        var consumed = buffer.Consume(new SimulationInstant(1));

        Assert.Equal(180UL, consumed.Sequence);
        Assert.Equal(1d, consumed.Movement.X);
        Assert.Equal(180UL, buffer.LastProcessedSequence);
        Assert.Equal(0, buffer.PendingCommandCount);
        Assert.True(buffer.TotalCompactedCommandCount >= 179);
    }

    [Fact]
    public void CompactedTapPreservesPressThenReleaseOnFollowingTick()
    {
        var buffer = new AuthorityMovementInputBuffer();
        buffer.Enqueue(Command(
            1,
            pressed: MovementButtons.Jump,
            held: MovementButtons.Jump));
        buffer.Enqueue(Command(
            2,
            released: MovementButtons.Jump));

        var pressed = buffer.Consume(new SimulationInstant(1));
        var released = buffer.Consume(new SimulationInstant(2));

        Assert.True(pressed.WasPressed(MovementButtons.Jump));
        Assert.False(pressed.WasReleased(MovementButtons.Jump));
        Assert.True(released.WasReleased(MovementButtons.Jump));
    }

    [Fact]
    public void StaleOneShotEdgeIsNotExecutedAfterLargeBacklog()
    {
        var buffer = new AuthorityMovementInputBuffer();
        buffer.Enqueue(Command(1, pressed: MovementButtons.Jump));
        for (ulong sequence = 2; sequence <= 180; sequence++)
        {
            buffer.Enqueue(Command(sequence, moveX: 1d));
        }

        var consumed = buffer.Consume(new SimulationInstant(1));

        Assert.False(consumed.WasPressed(MovementButtons.Jump));
        Assert.Equal(180UL, consumed.Sequence);
    }

    [Fact]
    public void AttackEdgeRequiresSeparateAuthorityAuthorization()
    {
        var buffer = new AuthorityMovementInputBuffer();
        buffer.Enqueue(Command(1, pressed: MovementButtons.Attack));

        var rejected = buffer.Consume(new SimulationInstant(1));
        buffer.AuthorizeAttack(new SimulationInstant(1));
        var accepted = buffer.Consume(new SimulationInstant(2));

        Assert.False(rejected.WasPressed(MovementButtons.Attack));
        Assert.True(accepted.WasPressed(MovementButtons.Attack));
    }

    [Fact]
    public void ReliableAttackAuthorizationMergesWithMovementOnlyCommand()
    {
        var buffer = new AuthorityMovementInputBuffer();
        buffer.AuthorizeAttack(new SimulationInstant(5));
        buffer.Enqueue(new MovementCommand(
            5,
            new SimulationInstant(5),
            HorizontalVector.Zero,
            0,
            0));

        var accepted = buffer.Consume(new SimulationInstant(10));

        Assert.True(accepted.WasPressed(MovementButtons.Attack));
    }

    [Fact]
    public void ReliableAttackSurvivesLossOfItsMatchingMovementFrame()
    {
        var buffer = new AuthorityMovementInputBuffer();
        buffer.AuthorizeAttack(new SimulationInstant(5));
        buffer.Enqueue(new MovementCommand(
            6,
            new SimulationInstant(6),
            HorizontalVector.Zero,
            0,
            0));

        var accepted = buffer.Consume(new SimulationInstant(10));

        Assert.True(accepted.WasPressed(MovementButtons.Attack));
    }

    [Fact]
    public void MissingPacketsRetainLastSequenceAndEventuallyNeutralizeIntent()
    {
        var buffer = new AuthorityMovementInputBuffer(
            new AuthorityInputBufferPolicy(64, staleInputHoldTicks: 2));
        buffer.Enqueue(Command(7, moveX: 1d, held: MovementButtons.Sprint));
        buffer.Consume(new SimulationInstant(1));

        var held = buffer.Consume(new SimulationInstant(2));
        buffer.Consume(new SimulationInstant(3));
        var neutral = buffer.Consume(new SimulationInstant(4));

        Assert.Equal(7UL, held.Sequence);
        Assert.Equal(1d, held.Movement.X);
        Assert.Equal(7UL, neutral.Sequence);
        Assert.Equal(HorizontalVector.Zero, neutral.Movement);
        Assert.Equal(MovementButtons.None, neutral.HeldButtons);
    }

    [Fact]
    public void ReorderedOrAcknowledgedCommandsCannotReenterBuffer()
    {
        var buffer = new AuthorityMovementInputBuffer();
        buffer.Enqueue(Command(2));
        buffer.Enqueue(Command(1));
        buffer.Consume(new SimulationInstant(1));

        buffer.Enqueue(Command(1));
        buffer.Enqueue(Command(2));

        Assert.Equal(0, buffer.PendingCommandCount);
        Assert.Equal(2UL, buffer.LastProcessedSequence);
    }

    [Fact]
    public void OneClientsBacklogCannotDelayAnotherClientsInput()
    {
        var firstClient = new AuthorityMovementInputBuffer();
        var secondClient = new AuthorityMovementInputBuffer();
        for (ulong sequence = 1; sequence <= 180; sequence++)
        {
            firstClient.Enqueue(Command(sequence, moveX: 1d));
        }

        secondClient.Enqueue(Command(1, moveX: -1d));

        var first = firstClient.Consume(new SimulationInstant(1));
        var second = secondClient.Consume(new SimulationInstant(1));

        Assert.Equal(180UL, first.Sequence);
        Assert.Equal(1UL, second.Sequence);
        Assert.Equal(-1d, second.Movement.X);
        Assert.Equal(0, firstClient.PendingCommandCount);
        Assert.Equal(0, secondClient.PendingCommandCount);
    }

    [Fact]
    public void PendingInputIsBoundedEvenBeforeAuthorityConsumesIt()
    {
        var buffer = new AuthorityMovementInputBuffer(
            new AuthorityInputBufferPolicy(
                maximumPendingCommands: 8,
                staleInputHoldTicks: 6,
                edgePreservationCommandCount: 3));
        for (ulong sequence = 1; sequence <= 180; sequence++)
        {
            buffer.Enqueue(Command(sequence));
        }

        Assert.Equal(8, buffer.PendingCommandCount);
        Assert.Equal(172, buffer.TotalCompactedCommandCount);
        Assert.Equal(180UL, buffer.Consume(new SimulationInstant(1)).Sequence);
    }

    [Fact]
    public void SelectedCommandRetainsItsMovementConfigurationRevisions()
    {
        var buffer = new AuthorityMovementInputBuffer();
        buffer.Enqueue(new RevisionedMovementCommand(Command(1), 4, 7));
        buffer.Enqueue(new RevisionedMovementCommand(Command(2), 5, 9));

        var selected = buffer.ConsumeRevisioned(new SimulationInstant(10));

        Assert.Equal(2UL, selected.Command.Sequence);
        Assert.Equal(5UL, selected.MovementProfileRevision);
        Assert.Equal(9UL, selected.MovementCapabilityRevision);
    }

    private static MovementCommand Command(
        ulong sequence,
        double moveX = 0d,
        MovementButtons held = MovementButtons.None,
        MovementButtons pressed = MovementButtons.None,
        MovementButtons released = MovementButtons.None) => new(
            sequence,
            new SimulationInstant(checked((long)sequence)),
            new HorizontalVector(moveX, 0d),
            0d,
            0d,
            held,
            pressed,
            released);
}
