using BattleArena.Core.Movement;
using BattleArena.Multiplayer.Connection;

namespace BattleArena.Multiplayer.Replication;

/// <summary>
/// Owns ordering, life-boundary resets, deduplication, and prediction bounds
/// for one remote combatant. Rendering and physics remain adapter concerns.
/// </summary>
public sealed class RemoteMovementTimeline<TState> : IRemoteMovementTimeline<TState>
    where TState : class
{
    private const int MaximumAuthorityFrames = 64;
    private const int MaximumAcceptedCommands = 256;
    private const int MaximumDirectCommands = 256;
    private readonly SortedDictionary<ulong, RemoteMovementFrame<TState>> _frames = [];
    private readonly SortedDictionary<ulong, AcceptedMovementCommand> _commands = [];
    private readonly SortedDictionary<ulong, DirectMovementCommand> _directCommands = [];
    private SessionPeerId? _sourcePeerId;
    private ConnectionGeneration? _connectionGeneration;

    public ulong? CurrentLifeId { get; private set; }

    public ulong LatestAuthorityTick => _frames.Count == 0 ? 0 : _frames.Keys.Last();

    public RemoteMovementFrame<TState>? LatestAuthorityFrame =>
        _frames.Count == 0 ? null : _frames.Values.Last();

    public long DirectConfirmations { get; private set; }

    public long DirectMismatches { get; private set; }

    public long DuplicateDirectCommands { get; private set; }

    public long LateDirectCommands { get; private set; }

    public long AuthorityPrunedDirectCommands { get; private set; }

    public bool DirectStateHintsAllowed => DirectMismatches < 3;

    public bool DirectRouteQuarantineRecommended => DirectMismatches >= 8;

    public int PendingDirectCommandCount => _directCommands.Count;

    public void ObserveDirect(DirectMovementCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!AcceptConnection(command.SourcePeerId, command.ConnectionGeneration) ||
            CurrentLifeId != command.LifeId || command.CombatantId == 0)
        {
            return;
        }

        var canonicalSequence = Math.Max(
            LatestAuthorityFrame?.LastProcessedInputSequence ?? 0,
            _commands.Count == 0
                ? 0
                : _commands.Values.Max(value => value.Command.Sequence));
        if (command.EstimatedAuthorityTick <= LatestAuthorityTick ||
            command.Command.Sequence <= canonicalSequence)
        {
            LateDirectCommands++;
            return;
        }

        // Redundant bundles repeat prior inputs. The first authenticated
        // observation is retained until authority evidence replaces it.
        if (!_directCommands.TryAdd(command.Command.Sequence, command))
        {
            DuplicateDirectCommands++;
            return;
        }
        TrimOldest(_directCommands, MaximumDirectCommands);
    }

    public void ObserveAccepted(AcceptedMovementCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!AcceptConnection(command.SourcePeerId, command.ConnectionGeneration))
        {
            return;
        }

        if (!AcceptLife(command.LifeId, command.AppliedAuthorityTick))
        {
            return;
        }

        if (_commands.TryGetValue(command.AppliedAuthorityTick, out var existing))
        {
            // Authority relay redundancy is expected. A conflicting duplicate
            // is kept as the first canonical observation and repaired by state.
            if (existing == command)
            {
                return;
            }

            return;
        }

        if (_directCommands.TryGetValue(command.Command.Sequence, out var direct))
        {
            if (MovementSemanticsMatch(direct, command))
            {
                DirectConfirmations++;
            }
            else
            {
                DirectMismatches++;
            }
        }

        foreach (var sequence in _directCommands.Keys
                     .Where(sequence => sequence <= command.Command.Sequence)
                     .ToArray())
        {
            _directCommands.Remove(sequence);
        }

        _commands.Add(command.AppliedAuthorityTick, command);
        TrimOldest(_commands, MaximumAcceptedCommands);
    }

    public void ObserveAuthority(RemoteMovementFrame<TState> frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(frame.State);
        if (!AcceptLife(frame.LifeId, frame.AuthorityTick))
        {
            return;
        }

        _frames[frame.AuthorityTick] = frame;
        TrimOldest(_frames, MaximumAuthorityFrames);

        foreach (var tick in _commands.Keys
                     .Where(tick => tick <= frame.AuthorityTick)
                     .ToArray())
        {
            _commands.Remove(tick);
        }


        var prunedDirectSequences = _directCommands.Keys
                     .Where(sequence =>
                         sequence <= frame.LastProcessedInputSequence ||
                         _directCommands[sequence].EstimatedAuthorityTick <= frame.AuthorityTick)
                     .ToArray();
        foreach (var sequence in prunedDirectSequences)
        {
            _directCommands.Remove(sequence);
        }
        AuthorityPrunedDirectCommands += prunedDirectSequences.Length;
    }

    public RemoteMovementSampleWindow<TState>? Sample(
        double targetTick,
        int normalPredictionLimitTicks,
        int freezeAfterTicks)
    {
        if (!double.IsFinite(targetTick) || targetTick < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(targetTick));
        }

        if (normalPredictionLimitTicks < 0 || freezeAfterTicks < normalPredictionLimitTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(normalPredictionLimitTicks));
        }

        if (_frames.Count == 0)
        {
            return null;
        }

        var frames = _frames.Values.ToArray();
        var latest = frames[^1];
        if (targetTick <= latest.AuthorityTick)
        {
            var before = frames[0];
            var after = latest;
            foreach (var frame in frames)
            {
                if (frame.AuthorityTick <= targetTick)
                {
                    before = frame;
                }

                if (frame.AuthorityTick >= targetTick)
                {
                    after = frame;
                    break;
                }
            }

            return new RemoteMovementSampleWindow<TState>(
                before,
                after,
                [],
                targetTick,
                targetTick,
                PredictionLimited: false,
                Frozen: false);
        }

        var ticksBeyondAuthority = targetTick - latest.AuthorityTick;
        var frozen = ticksBeyondAuthority >= freezeAfterTicks;
        var predictionLimitTick = latest.AuthorityTick +
            checked((ulong)normalPredictionLimitTicks);
        var effectiveTargetTick = frozen
            ? predictionLimitTick
            : Math.Min(targetTick, predictionLimitTick);
        var acceptedCommands = _commands.Values
            .Where(command =>
                command.AppliedAuthorityTick > latest.AuthorityTick &&
                command.AppliedAuthorityTick <= Math.Ceiling(effectiveTargetTick))
            .ToArray();
        var acceptedSequences = acceptedCommands
            .Select(command => command.Command.Sequence)
            .ToHashSet();
        IEnumerable<DirectMovementCommand> directEvidence = DirectStateHintsAllowed
            ? _directCommands.Values
            : Enumerable.Empty<DirectMovementCommand>();
        var directCommands = directEvidence
            .Where(command =>
                command.EstimatedAuthorityTick > latest.AuthorityTick &&
                command.EstimatedAuthorityTick <= Math.Ceiling(effectiveTargetTick) &&
                !acceptedSequences.Contains(command.Command.Sequence))
            .Select(command => new AcceptedMovementCommand(
                command.SourcePeerId,
                command.ConnectionGeneration,
                command.CombatantId,
                command.LifeId,
                command.EstimatedAuthorityTick,
                command.Command,
                command.MovementProfileRevision,
                command.MovementCapabilityRevision));
        var commands = acceptedCommands
            .Concat(directCommands)
            .OrderBy(command => command.AppliedAuthorityTick)
            .ThenBy(command => command.Command.Sequence)
            .ToArray();
        return new RemoteMovementSampleWindow<TState>(
            latest,
            latest,
            commands,
            targetTick,
            effectiveTargetTick,
            PredictionLimited: effectiveTargetTick < targetTick,
            Frozen: frozen);
    }

    public void Clear()
    {
        _frames.Clear();
        _commands.Clear();
        _directCommands.Clear();
        CurrentLifeId = null;
        _sourcePeerId = null;
        _connectionGeneration = null;
    }

    private bool AcceptConnection(
        SessionPeerId sourcePeerId,
        ConnectionGeneration connectionGeneration)
    {
        if (_sourcePeerId is null)
        {
            _sourcePeerId = sourcePeerId;
            _connectionGeneration = connectionGeneration;
            return true;
        }

        if (_sourcePeerId.Value != sourcePeerId)
        {
            return false;
        }

        if (connectionGeneration.Value < _connectionGeneration!.Value.Value)
        {
            return false;
        }

        if (connectionGeneration.Value > _connectionGeneration.Value.Value)
        {
            _frames.Clear();
            _commands.Clear();
            _directCommands.Clear();
            CurrentLifeId = null;
            _connectionGeneration = connectionGeneration;
        }

        return true;
    }

    private bool AcceptLife(ulong lifeId, ulong authorityTick)
    {
        if (lifeId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lifeId));
        }

        if (CurrentLifeId is null)
        {
            CurrentLifeId = lifeId;
            return true;
        }

        if (lifeId == CurrentLifeId.Value)
        {
            return true;
        }

        if (lifeId < CurrentLifeId.Value)
        {
            return false;
        }

        if (_frames.Count > 0 && authorityTick < LatestAuthorityTick)
        {
            return false;
        }

        _frames.Clear();
        _commands.Clear();
        _directCommands.Clear();
        CurrentLifeId = lifeId;
        return true;
    }

    private static void TrimOldest<TValue>(SortedDictionary<ulong, TValue> values, int maximum)
    {
        while (values.Count > maximum)
        {
            values.Remove(values.Keys.First());
        }
    }

    private static bool MovementSemanticsMatch(
        DirectMovementCommand direct,
        AcceptedMovementCommand accepted)
    {
        const MovementButtons movementMask =
            MovementButtons.Jump |
            MovementButtons.Sprint |
            MovementButtons.CrouchOrRoll;
        var left = direct.Command;
        var right = accepted.Command;
        var directPressed = left.PressedButtons & movementMask;
        var acceptedPressed = right.PressedButtons & movementMask;
        var directReleased = left.ReleasedButtons & movementMask;
        var acceptedReleased = right.ReleasedButtons & movementMask;
        // Compaction may add older edges to the accepted command. It may also
        // defer the release half of a same-frame tap by one authority tick.
        // Direct-only presses, or direct-only non-tap releases, are not an
        // equivalent visual movement transition.
        var directPressesConfirmed = (directPressed & ~acceptedPressed) == 0;
        var allowedReleased = acceptedReleased | (directPressed & acceptedPressed);
        var directReleasesConfirmed = (directReleased & ~allowedReleased) == 0;
        return left.Sequence == right.Sequence &&
               left.ClientTick == right.ClientTick &&
               left.Movement == right.Movement &&
               left.ViewYawRadians.Equals(right.ViewYawRadians) &&
               left.ViewPitchRadians.Equals(right.ViewPitchRadians) &&
               (left.HeldButtons & movementMask) == (right.HeldButtons & movementMask) &&
               directPressesConfirmed &&
               directReleasesConfirmed &&
               direct.MovementProfileRevision == accepted.MovementProfileRevision &&
               direct.MovementCapabilityRevision == accepted.MovementCapabilityRevision;
    }
}
