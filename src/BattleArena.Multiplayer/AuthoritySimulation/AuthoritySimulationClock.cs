using BattleArena.Core.Common;
using BattleArena.Core.Movement.Simulation;

namespace BattleArena.Multiplayer.AuthoritySimulation;

/// <summary>What the authority should do with the wall time that just elapsed.</summary>
public enum AuthorityClockAdvanceDecision : byte
{
    /// <summary>Wall time and simulation time agree; run the planned steps.</summary>
    OnSchedule = 1,

    /// <summary>
    /// More frames are owed than one callback may run. The excess stays owed:
    /// the authority runs its cap now and remains temporarily behind wall time.
    /// </summary>
    CatchUpCapped = 2,

    /// <summary>
    /// Lag exceeded what the session can recover. The match must freeze and
    /// resume from an explicit new epoch rather than skip or compress frames.
    /// </summary>
    TimelineResetRequired = 3,

    /// <summary>Frozen after a reset was raised; no frames may run until resumed.</summary>
    Frozen = 4,

    /// <summary>Not enough wall time has accumulated for a whole frame yet.</summary>
    NoStepDue = 5,
}

/// <summary>
/// The authority's declaration that its timeline could not continue. It is the
/// heavy, reliable message the design reserves for genuine unrecoverable lag.
/// </summary>
public readonly record struct AuthorityTimelineReset
{
    internal AuthorityTimelineReset(
        MatchFrameEpochId previousEpoch,
        MatchFrameEpochId newEpoch,
        SimulationInstant? lastSimulatedFrame,
        SimulationInstant resumeFrame,
        double observedLagMilliseconds,
        AuthorityTimelineResetReason reason)
    {
        PreviousEpoch = previousEpoch;
        NewEpoch = newEpoch;
        LastSimulatedFrame = lastSimulatedFrame;
        ResumeFrame = resumeFrame;
        ObservedLagMilliseconds = observedLagMilliseconds;
        Reason = reason;
    }

    public MatchFrameEpochId PreviousEpoch { get; }
    public MatchFrameEpochId NewEpoch { get; }

    /// <summary>
    /// The last frame that really ran, or null when the timeline reset before
    /// simulating anything. Deriving it from the cursor would name a frame that
    /// was never simulated, which is precisely the claim this design forbids.
    /// </summary>
    public SimulationInstant? LastSimulatedFrame { get; }

    /// <summary>The first frame of the new epoch.</summary>
    public SimulationInstant ResumeFrame { get; }
    public double ObservedLagMilliseconds { get; }
    public AuthorityTimelineResetReason Reason { get; }
}

public enum AuthorityTimelineResetReason : byte
{
    /// <summary>Accumulated lag passed the recoverable bound.</summary>
    UnrecoverableLag = 1,

    /// <summary>Catch-up was capped for so long that the deficit never drained.</summary>
    SustainedCatchUpDeficit = 2,

    /// <summary>An external subsystem, such as history overrun, demanded a reset.</summary>
    ExternalDemand = 3,
}

/// <summary>Immutable pacing policy for one match.</summary>
public readonly record struct AuthoritySimulationClockPolicy
{
    public static AuthoritySimulationClockPolicy Default { get; } = new(
        maximumCatchUpStepsPerCallback: 4,
        maximumRecoverableLagMilliseconds: 2_000d,
        sustainedDeficitCallbackLimit: 600,
        retainedFrameBoundaries: 256,
        maximumElapsedMillisecondsPerCallback: 10_000d);

    public AuthoritySimulationClockPolicy(
        int maximumCatchUpStepsPerCallback,
        double maximumRecoverableLagMilliseconds,
        int sustainedDeficitCallbackLimit,
        int retainedFrameBoundaries,
        double maximumElapsedMillisecondsPerCallback)
    {
        if (maximumCatchUpStepsPerCallback is < 1 or > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCatchUpStepsPerCallback));
        }
        if (!double.IsFinite(maximumRecoverableLagMilliseconds) ||
            maximumRecoverableLagMilliseconds <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRecoverableLagMilliseconds));
        }
        if (sustainedDeficitCallbackLimit is < 1 or > 1_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(sustainedDeficitCallbackLimit));
        }
        if (retainedFrameBoundaries is < 1 or > 4_096)
        {
            throw new ArgumentOutOfRangeException(nameof(retainedFrameBoundaries));
        }
        if (!double.IsFinite(maximumElapsedMillisecondsPerCallback) ||
            maximumElapsedMillisecondsPerCallback <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumElapsedMillisecondsPerCallback));
        }

        // A bound below the recoverable lag would reset the timeline for
        // intervals the session is supposed to absorb.
        if (maximumElapsedMillisecondsPerCallback < maximumRecoverableLagMilliseconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumElapsedMillisecondsPerCallback),
                "The per-callback interval bound must be at least the maximum recoverable lag, " +
                "or ordinary recoverable hitches would reset the timeline.");
        }

        MaximumCatchUpStepsPerCallback = maximumCatchUpStepsPerCallback;
        MaximumRecoverableLagMilliseconds = maximumRecoverableLagMilliseconds;
        SustainedDeficitCallbackLimit = sustainedDeficitCallbackLimit;
        RetainedFrameBoundaries = retainedFrameBoundaries;
        MaximumElapsedMillisecondsPerCallback = maximumElapsedMillisecondsPerCallback;
    }

    public int MaximumCatchUpStepsPerCallback { get; }
    public double MaximumRecoverableLagMilliseconds { get; }
    public int SustainedDeficitCallbackLimit { get; }
    public int RetainedFrameBoundaries { get; }

    /// <summary>
    /// A single callback reporting more elapsed time than this is treated as a
    /// suspended process or a broken timer rather than real owed simulation, and
    /// resets the timeline. It must be at least
    /// <see cref="MaximumRecoverableLagMilliseconds"/>, so an interval that is
    /// merely a recoverable hitch is never mistaken for a suspended process.
    /// </summary>
    public double MaximumElapsedMillisecondsPerCallback { get; }

    public bool IsValid =>
        MaximumCatchUpStepsPerCallback >= 1 &&
        MaximumRecoverableLagMilliseconds > 0d &&
        SustainedDeficitCallbackLimit >= 1 &&
        RetainedFrameBoundaries >= 1 &&
        MaximumElapsedMillisecondsPerCallback >= MaximumRecoverableLagMilliseconds;
}

/// <summary>The plan for one engine callback.</summary>
public readonly record struct AuthorityClockAdvance
{
    internal AuthorityClockAdvance(
        AuthorityClockAdvanceDecision decision,
        int stepsToRun,
        SimulationInstant nextFrame,
        double owedLagMilliseconds,
        AuthorityTimelineReset? reset)
    {
        Decision = decision;
        StepsToRun = stepsToRun;
        NextFrame = nextFrame;
        OwedLagMilliseconds = owedLagMilliseconds;
        Reset = reset;
    }

    public AuthorityClockAdvanceDecision Decision { get; }

    /// <summary>Frames the caller must now simulate, each completed explicitly.</summary>
    public int StepsToRun { get; }
    public SimulationInstant NextFrame { get; }

    /// <summary>
    /// Wall time still owed after this callback's steps. It is retained, never
    /// discarded: discarding it would be silent time compression.
    /// </summary>
    public double OwedLagMilliseconds { get; }

    /// <summary>Set exactly once per unrecoverable event, never repeated.</summary>
    public AuthorityTimelineReset? Reset { get; }
}

/// <summary>One recorded physics-boundary instant.</summary>
public readonly record struct AuthorityFrameBoundary(
    MatchFrameEpochId Epoch,
    SimulationInstant Frame,
    long MonotonicTimestamp);

/// <summary>
/// Owns the authority's fixed-step pacing. It replaces the hard-coded
/// <c>NetworkArena.FixedDelta</c> and is the only place allowed to decide how
/// many simulation frames a wall-clock interval owes.
/// </summary>
/// <remarks>
/// <para>
/// Two rules are absolute here. Simulation time never advances without a frame
/// actually being simulated: the frame counter moves only in
/// <see cref="CompleteFrame"/>, which the caller invokes once per frame it really
/// ran. And elapsed wall time is never discarded to catch up: a callback runs at
/// most <see cref="AuthoritySimulationClockPolicy.MaximumCatchUpStepsPerCallback"/>
/// steps and stays behind, rather than compressing several frames into one.
/// </para>
/// <para>
/// When lag genuinely cannot be recovered the clock freezes and raises exactly
/// one <see cref="AuthorityTimelineReset"/>. The reset is latched rather than
/// re-raised every callback, because it is a heavy reliable message and
/// repeating it while the bad condition persists is its own amplification
/// problem.
/// </para>
/// <para>
/// Boundary timestamps are recorded per frame so clock synchronisation can reply
/// with a real physics boundary instead of sampling an integer tick at an
/// arbitrary callback time.
/// </para>
/// </remarks>
public sealed class AuthoritySimulationClock
{
    private readonly SimulationRate _rate;
    private readonly AuthoritySimulationClockPolicy _policy;
    private readonly double _stepMilliseconds;
    private readonly AuthorityFrameBoundary[] _boundaries;
    private readonly long[] _boundaryFrameTicks;

    private const long EmptyBoundary = -1;

    private MatchFrameEpochId _epoch;
    private long _nextFrameTick;
    private double _owedMilliseconds;
    private int _pendingSteps;
    private int _consecutiveCappedCallbacks;
    private bool _frozen;
    private bool _resetLatched;
    private int _latestBoundaryIndex = -1;
    private long _totalFramesSimulated;
    private long _framesSimulatedInEpoch;

    public AuthoritySimulationClock(
        MatchFrameEpochId epoch,
        SimulationRate rate,
        SimulationInstant firstFrame)
        : this(epoch, rate, firstFrame, AuthoritySimulationClockPolicy.Default)
    {
    }

    public AuthoritySimulationClock(
        MatchFrameEpochId epoch,
        SimulationRate rate,
        SimulationInstant firstFrame,
        AuthoritySimulationClockPolicy policy)
    {
        if (!epoch.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(epoch));
        }
        if (rate.TicksPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rate));
        }
        if (firstFrame.Tick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(firstFrame));
        }
        if (!policy.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(policy));
        }

        _epoch = epoch;
        _rate = rate;
        _policy = policy;
        _stepMilliseconds = 1_000d / rate.TicksPerSecond;
        _boundaries = new AuthorityFrameBoundary[policy.RetainedFrameBoundaries];
        _boundaryFrameTicks = new long[policy.RetainedFrameBoundaries];
        Array.Fill(_boundaryFrameTicks, EmptyBoundary);
        _nextFrameTick = firstFrame.Tick;
    }

    public MatchFrameEpochId Epoch => _epoch;
    public SimulationRate Rate => _rate;
    public AuthoritySimulationClockPolicy Policy => _policy;

    /// <summary>Constant for the match. It is never varied to absorb lag.</summary>
    public double FixedStepMilliseconds => _stepMilliseconds;

    /// <summary>The frame the next <see cref="CompleteFrame"/> will simulate.</summary>
    public SimulationInstant NextFrame => new(_nextFrameTick);

    public bool IsFrozen => _frozen;

    /// <summary>Frames planned by the current callback that have not been completed yet.</summary>
    public int PendingSteps => _pendingSteps;
    public double OwedLagMilliseconds => _owedMilliseconds;
    public long TotalFramesSimulated => _totalFramesSimulated;

    /// <summary>
    /// Frames simulated since the current epoch began. This, not the lifetime
    /// total, decides whether a reset may name a last simulated frame: a fresh
    /// epoch that freezes before running anything has none, however many frames
    /// earlier epochs ran.
    /// </summary>
    public long FramesSimulatedInEpoch => _framesSimulatedInEpoch;
    public int ConsecutiveCappedCallbacks => _consecutiveCappedCallbacks;

    /// <summary>
    /// Converts elapsed wall time into a bounded number of frames to run.
    /// </summary>
    public AuthorityClockAdvance Advance(double elapsedMilliseconds)
    {
        if (!double.IsFinite(elapsedMilliseconds) || elapsedMilliseconds < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsedMilliseconds));
        }

        if (_frozen)
        {
            return new AuthorityClockAdvance(
                AuthorityClockAdvanceDecision.Frozen,
                stepsToRun: 0,
                NextFrame,
                _owedMilliseconds,
                reset: null);
        }

        if (_pendingSteps > 0)
        {
            throw new InvalidOperationException(
                "The previous callback's planned frames were not all completed. " +
                "Every planned step must be simulated through CompleteFrame before advancing again.");
        }

        // A callback claiming an interval this large is a suspended process or a
        // broken timer, not real owed simulation. Truncating it and carrying on
        // would silently discard that time, so it resets the timeline instead:
        // the elapsed value is reported as observed rather than as clamped.
        if (elapsedMilliseconds > _policy.MaximumElapsedMillisecondsPerCallback)
        {
            _owedMilliseconds += elapsedMilliseconds;
            return Freeze(AuthorityTimelineResetReason.UnrecoverableLag);
        }

        _owedMilliseconds += elapsedMilliseconds;

        // A caller passing what it believes is exactly N frames of elapsed time
        // must get N steps. The step is 1000/rate, which is not representable
        // exactly, so N * step can land a fraction of a nanosecond below N
        // frames and floor to N-1. The tolerance is nine orders of magnitude
        // smaller than a step, so it can only ever absorb representation error,
        // never a real shortfall.
        var tolerance = _stepMilliseconds * 1e-9d;
        var owedSteps = (int)Math.Min(
            int.MaxValue,
            Math.Floor((_owedMilliseconds + tolerance) / _stepMilliseconds));
        if (owedSteps <= 0)
        {
            _consecutiveCappedCallbacks = 0;
            return new AuthorityClockAdvance(
                AuthorityClockAdvanceDecision.NoStepDue,
                stepsToRun: 0,
                NextFrame,
                _owedMilliseconds,
                reset: null);
        }

        var capped = owedSteps > _policy.MaximumCatchUpStepsPerCallback;
        var steps = capped ? _policy.MaximumCatchUpStepsPerCallback : owedSteps;

        if (capped)
        {
            _consecutiveCappedCallbacks++;
        }
        else
        {
            _consecutiveCappedCallbacks = 0;
        }

        // The reset test runs against the full unconsumed deficit, and freezing
        // consumes nothing. Subtracting the planned steps first would both
        // understate the lag reported to the match and silently discard time for
        // frames that are never going to run.
        var reason = ResetReason();
        if (reason is { } resetReason)
        {
            return Freeze(resetReason);
        }

        // The timeline can also exhaust; that is a reset, not a wrap.
        if (long.MaxValue - _nextFrameTick < steps)
        {
            return Freeze(AuthorityTimelineResetReason.ExternalDemand);
        }

        // Only the time actually converted into frames is consumed. The rest
        // stays owed so no frame is ever skipped. Clamping at zero keeps the
        // tolerance above from ever pushing the accumulator negative.
        _owedMilliseconds = Math.Max(0d, _owedMilliseconds - steps * _stepMilliseconds);
        _pendingSteps = steps;
        return new AuthorityClockAdvance(
            capped
                ? AuthorityClockAdvanceDecision.CatchUpCapped
                : AuthorityClockAdvanceDecision.OnSchedule,
            steps,
            NextFrame,
            _owedMilliseconds,
            reset: null);
    }

    /// <summary>
    /// Records that one planned frame really was simulated, and stamps its
    /// boundary. This is the only place the frame counter moves.
    /// </summary>
    public SimulationInstant CompleteFrame(long monotonicTimestamp)
    {
        if (_frozen)
        {
            throw new InvalidOperationException(
                "The authority timeline is frozen and cannot simulate frames until it resumes.");
        }
        if (_pendingSteps <= 0)
        {
            throw new InvalidOperationException(
                "No frame was planned for this callback. Simulation time cannot advance without a plan.");
        }

        var simulated = new SimulationInstant(_nextFrameTick);
        var index = (int)(_nextFrameTick % _boundaries.Length);
        _boundaries[index] = new AuthorityFrameBoundary(_epoch, simulated, monotonicTimestamp);
        _boundaryFrameTicks[index] = _nextFrameTick;
        _latestBoundaryIndex = index;
        _nextFrameTick++;
        _pendingSteps--;
        _totalFramesSimulated++;
        _framesSimulatedInEpoch++;
        return simulated;
    }

    /// <summary>
    /// Lets an external subsystem — history overrun, for example — demand the
    /// same explicit reset the clock raises for itself. Idempotent while frozen,
    /// so the heavy message is still emitted only once.
    /// </summary>
    public AuthorityClockAdvance DemandTimelineReset()
    {
        if (_frozen)
        {
            return new AuthorityClockAdvance(
                AuthorityClockAdvanceDecision.Frozen,
                stepsToRun: 0,
                NextFrame,
                _owedMilliseconds,
                reset: null);
        }

        _pendingSteps = 0;
        return Freeze(AuthorityTimelineResetReason.ExternalDemand);
    }

    /// <summary>
    /// Resumes into the epoch and frame the reset declared. The caller supplies
    /// them explicitly so the resume baseline is the one that was published.
    /// </summary>
    public void ResumeAfterReset(MatchFrameEpochId epoch, SimulationInstant resumeFrame)
    {
        if (!_frozen)
        {
            throw new InvalidOperationException("The authority timeline is not frozen.");
        }
        if (!epoch.IsValid || epoch <= _epoch)
        {
            throw new ArgumentOutOfRangeException(nameof(epoch));
        }
        if (resumeFrame.Tick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(resumeFrame));
        }

        _epoch = epoch;
        _nextFrameTick = resumeFrame.Tick;
        _owedMilliseconds = 0d;
        _pendingSteps = 0;
        _consecutiveCappedCallbacks = 0;
        _framesSimulatedInEpoch = 0;
        _frozen = false;
        _resetLatched = false;
    }

    /// <summary>
    /// The recorded boundary for a frame, while it remains in the bounded
    /// retention window. Clock synchronisation replies with one of these rather
    /// than a tick sampled at an arbitrary moment.
    /// </summary>
    /// <remarks>
    /// Occupancy is tracked by a parallel frame stamp rather than by testing the
    /// timestamp against zero. A monotonic clock may legitimately read zero, and
    /// treating that as an empty slot would make a real recorded boundary
    /// permanently unqueryable.
    /// </remarks>
    public bool TryGetFrameBoundary(
        SimulationInstant frame,
        out AuthorityFrameBoundary boundary)
    {
        var tick = frame.Tick;
        if (tick < 0)
        {
            boundary = default;
            return false;
        }

        var index = (int)(tick % _boundaries.Length);
        if (_boundaryFrameTicks[index] != tick || _boundaries[index].Epoch != _epoch)
        {
            boundary = default;
            return false;
        }

        boundary = _boundaries[index];
        return true;
    }

    public bool TryGetLatestFrameBoundary(out AuthorityFrameBoundary boundary)
    {
        if (_latestBoundaryIndex < 0 || _boundaries[_latestBoundaryIndex].Epoch != _epoch)
        {
            boundary = default;
            return false;
        }

        boundary = _boundaries[_latestBoundaryIndex];
        return true;
    }

    private AuthorityTimelineResetReason? ResetReason()
    {
        if (_owedMilliseconds > _policy.MaximumRecoverableLagMilliseconds)
        {
            return AuthorityTimelineResetReason.UnrecoverableLag;
        }

        if (_consecutiveCappedCallbacks >= _policy.SustainedDeficitCallbackLimit)
        {
            return AuthorityTimelineResetReason.SustainedCatchUpDeficit;
        }

        return null;
    }

    private AuthorityClockAdvance Freeze(AuthorityTimelineResetReason reason)
    {
        var lastSimulated = _framesSimulatedInEpoch > 0 && _nextFrameTick > 0
            ? new SimulationInstant(_nextFrameTick - 1)
            : (SimulationInstant?)null;
        var newEpoch = _epoch.Next();
        var reset = _resetLatched
            ? (AuthorityTimelineReset?)null
            : new AuthorityTimelineReset(
                _epoch,
                newEpoch,
                lastSimulated,
                NextFrame,
                _owedMilliseconds,
                reason);

        _frozen = true;
        _resetLatched = true;
        _pendingSteps = 0;

        return new AuthorityClockAdvance(
            AuthorityClockAdvanceDecision.TimelineResetRequired,
            stepsToRun: 0,
            NextFrame,
            _owedMilliseconds,
            reset);
    }
}
