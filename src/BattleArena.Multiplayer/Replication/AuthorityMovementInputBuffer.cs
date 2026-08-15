using BattleArena.Core.Common;
using BattleArena.Core.Movement;

namespace BattleArena.Multiplayer.Replication;

/// <summary>
/// Selects current client intent for one authority simulation tick. Continuous
/// input is replaceable, so stale commands are compacted to the freshest state
/// rather than becoming permanent latency. One-shot movement edges survive
/// compaction; authoritative attacks remain separately authorized.
/// </summary>
public sealed class AuthorityMovementInputBuffer : IAuthorityMovementInputBuffer
{
    private readonly AuthorityInputBufferPolicy _policy;
    private readonly SortedDictionary<ulong, RevisionedMovementCommand> _pending = [];
    private readonly HashSet<long> _authorizedAttackTicks = [];
    private RevisionedMovementCommand? _lastCommand;
    private MovementButtons _deferredReleasedButtons;
    private int _ticksWithoutFreshInput;
    private bool _forceAuthorizedAttack;

    public AuthorityMovementInputBuffer(AuthorityInputBufferPolicy? policy = null)
    {
        _policy = policy ?? AuthorityInputBufferPolicy.Default;
    }

    public ulong LastProcessedSequence { get; private set; }

    public int PendingCommandCount => _pending.Count;

    public int LastCompactedCommandCount { get; private set; }

    public long TotalCompactedCommandCount { get; private set; }

    public void Enqueue(MovementCommand command)
    {
        Enqueue(new RevisionedMovementCommand(command, 1, 1));
    }

    public void Enqueue(RevisionedMovementCommand revisioned)
    {
        ArgumentNullException.ThrowIfNull(revisioned);
        var command = revisioned.Command;
        if (command.Sequence == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(command));
        }

        if (command.Sequence <= LastProcessedSequence || _pending.ContainsKey(command.Sequence))
        {
            return;
        }

        _pending.Add(command.Sequence, revisioned);
        while (_pending.Count > _policy.MaximumPendingCommands)
        {
            var oldest = _pending.First();
            _pending.Remove(oldest.Key);
            TotalCompactedCommandCount++;
        }
    }

    public void AuthorizeAttack(SimulationInstant clientTick)
    {
        _authorizedAttackTicks.Add(clientTick.Tick);
        if (_pending.Values.All(input => input.Command.ClientTick != clientTick) &&
            _lastCommand is not null &&
            _lastCommand.Command.ClientTick >= clientTick)
        {
            _forceAuthorizedAttack = true;
            _authorizedAttackTicks.Remove(clientTick.Tick);
        }
    }

    public MovementCommand Consume(SimulationInstant authorityTick) =>
        ConsumeRevisioned(authorityTick).Command;

    public RevisionedMovementCommand ConsumeRevisioned(SimulationInstant authorityTick)
    {
        LastCompactedCommandCount = 0;
        if (_pending.Count > 0)
        {
            return ConsumeFreshest();
        }

        _ticksWithoutFreshInput++;
        _lastCommand ??= new RevisionedMovementCommand(
            Neutral(0, authorityTick, Math.PI, 0d), 1, 1);
        var last = _lastCommand.Command;
        var nextClientTick = last.ClientTick + new SimulationDuration(1);
        var retainInput = _ticksWithoutFreshInput <= _policy.StaleInputHoldTicks;
        var pressed = MovementButtons.None;
        if (_forceAuthorizedAttack)
        {
            _forceAuthorizedAttack = false;
            pressed |= MovementButtons.Attack;
        }

        var released = _deferredReleasedButtons;
        _deferredReleasedButtons = MovementButtons.None;
        _lastCommand = new RevisionedMovementCommand(new MovementCommand(
            last.Sequence,
            nextClientTick,
            retainInput ? last.Movement : HorizontalVector.Zero,
            last.ViewYawRadians,
            last.ViewPitchRadians,
            retainInput ? last.HeldButtons : MovementButtons.None,
            pressed,
            released),
            _lastCommand.MovementProfileRevision,
            _lastCommand.MovementCapabilityRevision);
        return _lastCommand;
    }

    public void DiscardPending()
    {
        _pending.Clear();
        _authorizedAttackTicks.Clear();
        _deferredReleasedButtons = MovementButtons.None;
        _ticksWithoutFreshInput = 0;
        _forceAuthorizedAttack = false;
        if (_lastCommand is not null)
        {
            var last = _lastCommand.Command;
            _lastCommand = new RevisionedMovementCommand(
                Neutral(
                    LastProcessedSequence,
                    last.ClientTick,
                    last.ViewYawRadians,
                    last.ViewPitchRadians),
                _lastCommand.MovementProfileRevision,
                _lastCommand.MovementCapabilityRevision);
        }
    }

    private RevisionedMovementCommand ConsumeFreshest()
    {
        var commands = _pending.Values.ToArray();
        var selected = commands[^1];
        var selectedCommand = selected.Command;
        var pressed = MovementButtons.None;
        var released = _deferredReleasedButtons;
        _deferredReleasedButtons = MovementButtons.None;
        var attackAuthorized = false;

        foreach (var revisioned in commands.TakeLast(_policy.EdgePreservationCommandCount))
        {
            var command = revisioned.Command;
            pressed |= command.PressedButtons & ~MovementButtons.Attack;
            released |= command.ReleasedButtons & ~MovementButtons.Attack;
            // Attack intent travels on the reliable authority action channel,
            // not in untrusted movement bundles. Merge the authorized edge at
            // its matching client tick regardless of movement button bits.
            if (_authorizedAttackTicks.Remove(command.ClientTick.Tick))
            {
                attackAuthorized = true;
            }
        }

        var eligibleAttackTicks = _authorizedAttackTicks
            .Where(tick => tick <= selectedCommand.ClientTick.Tick)
            .OrderBy(tick => tick)
            .ToArray();
        if (!attackAuthorized && eligibleAttackTicks.Length > 0)
        {
            // The unreliable movement frame matching a reliable action may be
            // lost. Apply the authorization to the first newer movement frame.
            attackAuthorized = true;
            _authorizedAttackTicks.Remove(eligibleAttackTicks[0]);
            eligibleAttackTicks = eligibleAttackTicks[1..];
        }

        foreach (var staleTick in eligibleAttackTicks)
        {
            _authorizedAttackTicks.Remove(staleTick);
            _forceAuthorizedAttack = true;
        }

        if (attackAuthorized || _forceAuthorizedAttack)
        {
            pressed |= MovementButtons.Attack;
            _forceAuthorizedAttack = false;
        }

        // A full press/release tap may be compacted into one authority tick.
        // Preserve the press now and deliver its release on the following tick.
        var tappedButtons = pressed & released;
        released &= ~tappedButtons;
        _deferredReleasedButtons |= tappedButtons;

        LastProcessedSequence = selectedCommand.Sequence;
        LastCompactedCommandCount = Math.Max(0, commands.Length - 1);
        TotalCompactedCommandCount += LastCompactedCommandCount;
        _pending.Clear();
        _ticksWithoutFreshInput = 0;
        _lastCommand = new RevisionedMovementCommand(new MovementCommand(
            selectedCommand.Sequence,
            selectedCommand.ClientTick,
            selectedCommand.Movement,
            selectedCommand.ViewYawRadians,
            selectedCommand.ViewPitchRadians,
            selectedCommand.HeldButtons,
            pressed,
            released),
            selected.MovementProfileRevision,
            selected.MovementCapabilityRevision);
        return _lastCommand;
    }

    private static MovementCommand Neutral(
        ulong sequence,
        SimulationInstant clientTick,
        double yaw,
        double pitch) => new(
            sequence,
            clientTick,
            HorizontalVector.Zero,
            yaw,
            pitch);
}
