using BattleArena.Core.Common;
using BattleArena.Multiplayer.OwnerPrediction;
using BattleArena.Multiplayer.Timing;

namespace BattleArena.Multiplayer.AuthoritySimulation;

/// <summary>
/// Why the controller moved, held, or refused to move the owner's lead.
/// </summary>
public enum PredictionLeadAdjustmentReason : byte
{
    /// <summary>The current target is already within the deadband of the desired lead.</summary>
    WithinDeadband = 1,

    /// <summary>A change is wanted but the minimum interval between changes has not elapsed.</summary>
    ChangeIntervalNotElapsed = 2,

    /// <summary>Path or occupancy evidence says the owner must predict further ahead.</summary>
    Increased = 3,

    /// <summary>Conditions improved; the owner can predict less far ahead and cut authority-side delay.</summary>
    Decreased = 4,

    /// <summary>
    /// The authority ran out of owner input. Starvation outranks every smoothing
    /// rule: an owner whose frames are being filled by fallback is visibly wrong
    /// right now, so the response is immediate rather than gradual.
    /// </summary>
    StarvationRecovery = 5,

    /// <summary>Clock evidence is not trustworthy enough to move an absolute policy.</summary>
    ClockConfidenceTooLow = 6,

    /// <summary>
    /// The required lead exceeds what policy supports, or clock confidence stayed
    /// low too long. The design forbids absorbing that with a large implicit
    /// adjustment, so an explicit timeline rebase is demanded instead.
    /// </summary>
    RebaseRequired = 7,

    /// <summary>No effective frame far enough ahead of already scheduled commands could be chosen.</summary>
    NoSafeEffectiveFrame = 8,

    /// <summary>
    /// A path sample contained a non-finite or negative value. Garbage
    /// instrumentation must not move an absolute policy, so the lead is held.
    /// </summary>
    PathEvidenceUnusable = 9,
}

public enum PredictionLeadControllerDecision : byte
{
    Unchanged = 1,
    Updated = 2,
    RebaseRequired = 3,
}

/// <summary>
/// Immutable tuning for <see cref="PredictionLeadController"/>.
/// </summary>
/// <remarks>
/// Growing the lead costs the owner nothing it can feel, while shrinking too
/// eagerly re-enters starvation, so the controller grows readily and shrinks
/// reluctantly. That reluctance is expressed as time and step size, never as a
/// wider shrink deadband, because a magnitude asymmetry latches a stale target.
/// </remarks>
public readonly record struct PredictionLeadControllerPolicy
{
    public static PredictionLeadControllerPolicy Default { get; } = new(
        authorityBufferTargetFrames: 2,
        jitterSafetyFactor: 1.0d,
        maximumJitterSafetyMilliseconds: 120d,
        deadbandFrames: 1,
        minimumFramesBetweenIncreases: 30,
        minimumFramesBetweenDecreases: 150,
        minimumFramesBetweenStarvationChanges: 6,
        maximumIncreaseStepFrames: 6,
        maximumDecreaseStepFrames: 6,
        minimumClockConfidence: 0.5d,
        lowConfidenceRebaseFrames: 600,
        occupancySmoothingFactor: 0.1d,
        maximumOccupancyReductionFrames: 2);

    public PredictionLeadControllerPolicy(
        int authorityBufferTargetFrames,
        double jitterSafetyFactor,
        double maximumJitterSafetyMilliseconds,
        int deadbandFrames,
        int minimumFramesBetweenIncreases,
        int minimumFramesBetweenDecreases,
        int minimumFramesBetweenStarvationChanges,
        int maximumIncreaseStepFrames,
        int maximumDecreaseStepFrames,
        double minimumClockConfidence,
        int lowConfidenceRebaseFrames,
        double occupancySmoothingFactor,
        int maximumOccupancyReductionFrames)
    {
        RequireRange(authorityBufferTargetFrames is >= 0 and <= 16, nameof(authorityBufferTargetFrames));
        RequireRange(double.IsFinite(jitterSafetyFactor) && jitterSafetyFactor is >= 0d and <= 8d, nameof(jitterSafetyFactor));
        RequireRange(
            double.IsFinite(maximumJitterSafetyMilliseconds) && maximumJitterSafetyMilliseconds is >= 0d and <= 2_000d,
            nameof(maximumJitterSafetyMilliseconds));
        RequireRange(deadbandFrames is >= 0 and <= 32, nameof(deadbandFrames));
        RequireRange(minimumFramesBetweenIncreases is >= 0 and <= 100_000, nameof(minimumFramesBetweenIncreases));
        RequireRange(minimumFramesBetweenDecreases is >= 0 and <= 100_000, nameof(minimumFramesBetweenDecreases));
        RequireRange(
            minimumFramesBetweenStarvationChanges is >= 1 and <= 100_000,
            nameof(minimumFramesBetweenStarvationChanges));
        RequireRange(maximumIncreaseStepFrames is >= 1 and <= 64, nameof(maximumIncreaseStepFrames));
        RequireRange(maximumDecreaseStepFrames is >= 1 and <= 64, nameof(maximumDecreaseStepFrames));
        RequireRange(
            double.IsFinite(minimumClockConfidence) && minimumClockConfidence is >= 0d and <= 1d,
            nameof(minimumClockConfidence));
        RequireRange(lowConfidenceRebaseFrames is >= 1 and <= 1_000_000, nameof(lowConfidenceRebaseFrames));
        RequireRange(
            double.IsFinite(occupancySmoothingFactor) && occupancySmoothingFactor is > 0d and <= 1d,
            nameof(occupancySmoothingFactor));
        RequireRange(maximumOccupancyReductionFrames is >= 0 and <= 16, nameof(maximumOccupancyReductionFrames));

        AuthorityBufferTargetFrames = authorityBufferTargetFrames;
        JitterSafetyFactor = jitterSafetyFactor;
        MaximumJitterSafetyMilliseconds = maximumJitterSafetyMilliseconds;
        DeadbandFrames = deadbandFrames;
        MinimumFramesBetweenIncreases = minimumFramesBetweenIncreases;
        MinimumFramesBetweenDecreases = minimumFramesBetweenDecreases;
        MinimumFramesBetweenStarvationChanges = minimumFramesBetweenStarvationChanges;
        MaximumIncreaseStepFrames = maximumIncreaseStepFrames;
        MaximumDecreaseStepFrames = maximumDecreaseStepFrames;
        MinimumClockConfidence = minimumClockConfidence;
        LowConfidenceRebaseFrames = lowConfidenceRebaseFrames;
        OccupancySmoothingFactor = occupancySmoothingFactor;
        MaximumOccupancyReductionFrames = maximumOccupancyReductionFrames;
    }

    /// <summary>
    /// The design's supported lead ceiling is a duration, not a frame count: 24
    /// frames at 60 Hz and 48 at 120 Hz are both 400 ms. Deriving it from the
    /// negotiated rate keeps that meaning instead of letting a flat frame count
    /// silently double the ceiling at the lower rate.
    /// </summary>
    public const int MaximumLeadMilliseconds = 400;

    public static int MaximumLeadFramesForRate(SimulationRate rate)
    {
        if (rate.TicksPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rate));
        }

        var frames = (int)Math.Round(
            rate.TicksPerSecond * (MaximumLeadMilliseconds / 1_000d),
            MidpointRounding.AwayFromZero);
        return Math.Clamp(
            frames,
            PredictionLeadLimits.DefaultMinimumFrames,
            PredictionLeadLimits.MaximumSupportedFrames);
    }

    /// <summary>Lead policy whose ceiling matches the negotiated simulation rate.</summary>
    public static PredictionLeadUpdatePolicy LeadPolicyForRate(SimulationRate rate) => new(
        PredictionLeadLimits.DefaultMinimumFrames,
        MaximumLeadFramesForRate(rate),
        PredictionLeadLimits.DefaultMinimumNoticeFrames);

    /// <summary>Frames of owner input the authority wants waiting ahead of its cursor.</summary>
    public int AuthorityBufferTargetFrames { get; }
    public double JitterSafetyFactor { get; }
    public double MaximumJitterSafetyMilliseconds { get; }

    /// <summary>
    /// Symmetric. An asymmetric magnitude deadband latches: a target that rises
    /// past the narrow grow threshold can never fall back through the wider
    /// shrink threshold, leaving the lead permanently high. Reluctance to shrink
    /// is expressed in time and step size instead, which cannot latch.
    /// </summary>
    public int DeadbandFrames { get; }
    public int MinimumFramesBetweenIncreases { get; }

    /// <summary>
    /// Five times the increase interval, so shrinking stays clearly reluctant
    /// relative to growing, while still completing a worst-case walk back from
    /// the ceiling in tens of seconds rather than over a minute.
    /// </summary>
    public int MinimumFramesBetweenDecreases { get; }

    /// <summary>
    /// Starvation reacts faster than an ordinary change but is still rate
    /// limited. An unlimited bypass lets one fallback-filled frame per
    /// evaluation emit an update every frame, which is update-traffic
    /// amplification triggerable by ordinary loss.
    /// </summary>
    public int MinimumFramesBetweenStarvationChanges { get; }
    public int MaximumIncreaseStepFrames { get; }

    /// <summary>
    /// The ceiling on one shrink, not the size of every shrink. The actual step
    /// scales with how far the lead is from target, so the controller is gentle
    /// near target where chattering lives and decisive after a large excursion.
    /// A flat one-frame step would take over a minute to walk back from the
    /// maximum lead, leaving the owner paying inflated authority-side latency
    /// long after conditions recovered.
    /// </summary>
    public int MaximumDecreaseStepFrames { get; }

    /// <summary>Gap, in frames, that buys one extra frame of decrease step.</summary>
    public const int FramesPerDecreaseStepIncrement = 3;

    public double MinimumClockConfidence { get; }
    public int LowConfidenceRebaseFrames { get; }
    public double OccupancySmoothingFactor { get; }

    /// <summary>
    /// Occupancy correction is one-sided: it may only shorten the lead. A buffer
    /// deeper than target proves the travel estimate is overshooting. A shallow
    /// buffer proves nothing on its own, and letting it lengthen the lead would
    /// let an ordinary transient dip ratchet the lead up permanently. Real
    /// shortfall shows up as starvation, which is handled separately.
    /// </summary>
    public int MaximumOccupancyReductionFrames { get; }

    public bool IsValid =>
        AuthorityBufferTargetFrames >= 0 &&
        MaximumIncreaseStepFrames >= 1 &&
        MaximumDecreaseStepFrames >= 1 &&
        MinimumFramesBetweenStarvationChanges >= 1 &&
        OccupancySmoothingFactor > 0d;

    private static void RequireRange(bool valid, string parameterName)
    {
        if (!valid)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

/// <summary>
/// Everything the controller is allowed to look at for one evaluation.
/// </summary>
/// <remarks>
/// <see cref="BufferedFrameCount"/> and <see cref="StarvedFramesSinceLastEvaluation"/>
/// are measured from the scheduler, not derived from RTT. The design explicitly
/// requires the real occupancy: RTT alone cannot tell whether the buffer is
/// actually starving or comfortably full.
/// </remarks>
public readonly record struct PredictionLeadObservation
{
    public PredictionLeadObservation(
        SimulationInstant authorityFrame,
        NetworkPathEstimate path,
        double clockConfidence,
        int bufferedFrameCount,
        int starvedFramesSinceLastEvaluation,
        SimulationInstant? lastScheduledCommandFrame)
    {
        if (authorityFrame.Tick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(authorityFrame));
        }
        if (!double.IsFinite(clockConfidence) || clockConfidence is < 0d or > 1d)
        {
            throw new ArgumentOutOfRangeException(nameof(clockConfidence));
        }
        if (bufferedFrameCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bufferedFrameCount));
        }
        if (starvedFramesSinceLastEvaluation < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(starvedFramesSinceLastEvaluation));
        }

        AuthorityFrame = authorityFrame;
        Path = path;
        ClockConfidence = clockConfidence;
        BufferedFrameCount = bufferedFrameCount;
        StarvedFramesSinceLastEvaluation = starvedFramesSinceLastEvaluation;
        LastScheduledCommandFrame = lastScheduledCommandFrame;
    }

    public SimulationInstant AuthorityFrame { get; }
    public NetworkPathEstimate Path { get; }
    public double ClockConfidence { get; }

    /// <summary>Measured owner commands waiting ahead of the authority cursor.</summary>
    public int BufferedFrameCount { get; }

    /// <summary>Frames the scheduler had to fill with fallback since the last evaluation.</summary>
    public int StarvedFramesSinceLastEvaluation { get; }
    public SimulationInstant? LastScheduledCommandFrame { get; }
}

public readonly record struct PredictionLeadEvaluation
{
    internal PredictionLeadEvaluation(
        PredictionLeadControllerDecision decision,
        PredictionLeadAdjustmentReason reason,
        int currentLeadFrames,
        int desiredLeadFrames,
        double smoothedBufferOccupancy,
        PredictionLeadUpdate? update)
    {
        Decision = decision;
        Reason = reason;
        CurrentLeadFrames = currentLeadFrames;
        DesiredLeadFrames = desiredLeadFrames;
        SmoothedBufferOccupancy = smoothedBufferOccupancy;
        Update = update;
    }

    public PredictionLeadControllerDecision Decision { get; }
    public PredictionLeadAdjustmentReason Reason { get; }

    /// <summary>The lead in force after this evaluation.</summary>
    public int CurrentLeadFrames { get; }

    /// <summary>What the evidence asked for, before deadband and step limiting.</summary>
    public int DesiredLeadFrames { get; }
    public double SmoothedBufferOccupancy { get; }

    /// <summary>Present only when the absolute policy actually changed.</summary>
    public PredictionLeadUpdate? Update { get; }
}

/// <summary>
/// Turns clock, path, and measured authority buffer evidence into an absolute,
/// revisioned prediction lead for one owner.
/// </summary>
/// <remarks>
/// <para>
/// The lead is expressed only as a whole number of fixed simulation frames. This
/// type has no access to and no concept of the physical step duration, so it
/// structurally cannot implement the rejected "vary the movement delta" approach:
/// the step is always exactly one over the negotiated rate, and a lead change is
/// absorbed by an occasional zero-step or two-step scheduling decision instead.
/// </para>
/// <para>
/// Output is absolute and revisioned rather than incremental, so a lost or
/// duplicated update cannot leave the two sides disagreeing about the lead: a
/// duplicate is idempotent and an older revision is ignored.
/// </para>
/// <para>
/// Oscillation is prevented by three independent mechanisms rather than one
/// tuning constant: a deadband that is wider on the shrink side than the grow
/// side, a minimum interval between changes, and a per-change step cap that is
/// larger for growth than for shrink. Starvation bypasses the interval, because
/// an owner whose frames are being filled by fallback is wrong now, not later.
/// </para>
/// </remarks>
public sealed class PredictionLeadController
{
    private readonly OwnerIntentScope _scope;
    private readonly PredictionLeadUpdatePolicy _leadPolicy;
    private readonly PredictionLeadControllerPolicy _policy;
    private readonly SimulationRate _rate;
    private readonly double _stepMilliseconds;

    private int _currentLead;
    private PredictionLeadPolicyRevision _revision;
    private double _smoothedOccupancy;
    private bool _hasOccupancySample;
    private long _lastIncreaseFrame;
    private long _lastDecreaseFrame;
    private bool _hasIncreased;
    private bool _hasDecreased;
    private long _lowConfidenceSinceFrame;
    private bool _inLowConfidence;

    public PredictionLeadController(
        OwnerIntentScope scope,
        SimulationRate rate,
        int initialLeadFrames,
        PredictionLeadPolicyRevision initialRevision)
        : this(
            scope,
            rate,
            initialLeadFrames,
            initialRevision,
            PredictionLeadControllerPolicy.LeadPolicyForRate(rate),
            PredictionLeadControllerPolicy.Default)
    {
    }

    public PredictionLeadController(
        OwnerIntentScope scope,
        SimulationRate rate,
        int initialLeadFrames,
        PredictionLeadPolicyRevision initialRevision,
        PredictionLeadUpdatePolicy leadPolicy,
        PredictionLeadControllerPolicy policy)
    {
        if (!scope.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(scope));
        }
        if (rate.TicksPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rate));
        }
        if (!leadPolicy.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(leadPolicy));
        }
        if (!policy.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(policy));
        }
        if (!initialRevision.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(initialRevision));
        }
        if (!leadPolicy.Contains(new PredictionLeadFrameCount(
                Math.Clamp(initialLeadFrames, 1, PredictionLeadLimits.MaximumSupportedFrames))) ||
            initialLeadFrames < leadPolicy.MinimumLeadFrames ||
            initialLeadFrames > leadPolicy.MaximumLeadFrames)
        {
            throw new ArgumentOutOfRangeException(nameof(initialLeadFrames));
        }

        _scope = scope;
        _rate = rate;
        _stepMilliseconds = 1_000d / rate.TicksPerSecond;
        _leadPolicy = leadPolicy;
        _policy = policy;
        _currentLead = initialLeadFrames;
        _revision = initialRevision;
    }

    public OwnerIntentScope Scope => _scope;
    public SimulationRate Rate => _rate;
    public PredictionLeadUpdatePolicy LeadPolicy => _leadPolicy;
    public PredictionLeadControllerPolicy Policy => _policy;
    public int CurrentLeadFrames => _currentLead;
    public PredictionLeadPolicyRevision CurrentRevision => _revision;
    public double SmoothedBufferOccupancy => _smoothedOccupancy;

    /// <summary>The physical step never changes; it is exposed only for diagnostics.</summary>
    public double FixedStepMilliseconds => _stepMilliseconds;

    public PredictionLeadEvaluation Evaluate(PredictionLeadObservation observation)
    {
        // Garbage telemetry must not be able to move an absolute policy. A
        // non-finite RTT would otherwise reach the frame arithmetic, where a NaN
        // cast to int becomes int.MinValue and jumps straight past every
        // deadband and step limit.
        if (!IsUsablePathEvidence(observation.Path))
        {
            return Result(
                PredictionLeadControllerDecision.Unchanged,
                PredictionLeadAdjustmentReason.PathEvidenceUnusable,
                _currentLead,
                update: null);
        }

        UpdateSmoothedOccupancy(observation.BufferedFrameCount);

        var starving = observation.StarvedFramesSinceLastEvaluation > 0;
        var confident = observation.ClockConfidence >= _policy.MinimumClockConfidence;
        TrackConfidence(observation.AuthorityFrame.Tick, confident);

        var desired = DesiredLead(observation, starving, confident);

        // Sustained loss of clock confidence is not something to absorb quietly.
        if (_inLowConfidence &&
            observation.AuthorityFrame.Tick - _lowConfidenceSinceFrame >= _policy.LowConfidenceRebaseFrames)
        {
            return Result(
                PredictionLeadControllerDecision.RebaseRequired,
                PredictionLeadAdjustmentReason.RebaseRequired,
                desired,
                update: null);
        }

        // Evidence demanding more lead than policy supports is also a rebase, not
        // a silent clamp that would leave the owner permanently starved.
        if (desired > _leadPolicy.MaximumLeadFrames && starving &&
            _currentLead >= _leadPolicy.MaximumLeadFrames)
        {
            return Result(
                PredictionLeadControllerDecision.RebaseRequired,
                PredictionLeadAdjustmentReason.RebaseRequired,
                desired,
                update: null);
        }

        desired = Math.Clamp(desired, _leadPolicy.MinimumLeadFrames, _leadPolicy.MaximumLeadFrames);

        if (!confident && !starving)
        {
            // Without trustworthy clock evidence the safest move is none: an
            // absolute policy built on a bad offset is worse than a stale one.
            return Result(
                PredictionLeadControllerDecision.Unchanged,
                PredictionLeadAdjustmentReason.ClockConfidenceTooLow,
                desired,
                update: null);
        }

        var delta = desired - _currentLead;
        if (delta == 0 ||
            (Math.Abs(delta) <= _policy.DeadbandFrames && !starving))
        {
            return Result(
                PredictionLeadControllerDecision.Unchanged,
                PredictionLeadAdjustmentReason.WithinDeadband,
                desired,
                update: null);
        }

        // Starvation reacts on a shorter clock than an ordinary change, but it is
        // still rate limited: an unlimited bypass turns one fallback frame per
        // evaluation into one wire update per evaluation.
        if (!HasIntervalElapsed(observation.AuthorityFrame.Tick, delta > 0, starving))
        {
            return Result(
                PredictionLeadControllerDecision.Unchanged,
                PredictionLeadAdjustmentReason.ChangeIntervalNotElapsed,
                desired,
                update: null);
        }

        var step = delta > 0
            ? Math.Min(delta, _policy.MaximumIncreaseStepFrames)
            : -Math.Min(-delta, DecreaseStepFor(-delta));
        var next = Math.Clamp(
            _currentLead + step,
            _leadPolicy.MinimumLeadFrames,
            _leadPolicy.MaximumLeadFrames);
        if (next == _currentLead)
        {
            return Result(
                PredictionLeadControllerDecision.Unchanged,
                PredictionLeadAdjustmentReason.WithinDeadband,
                desired,
                update: null);
        }

        if (!TryChooseEffectiveFrame(observation, out var effectiveFrame))
        {
            return Result(
                PredictionLeadControllerDecision.Unchanged,
                PredictionLeadAdjustmentReason.NoSafeEffectiveFrame,
                desired,
                update: null);
        }

        var revision = _revision.Next();
        var update = new PredictionLeadUpdate(
            _scope,
            new PredictionLeadFrameCount(next),
            revision,
            effectiveFrame);

        _revision = revision;
        _currentLead = next;
        if (step > 0)
        {
            _lastIncreaseFrame = observation.AuthorityFrame.Tick;
            _hasIncreased = true;
        }
        else
        {
            _lastDecreaseFrame = observation.AuthorityFrame.Tick;
            _hasDecreased = true;
        }

        var reason = starving && step > 0
            ? PredictionLeadAdjustmentReason.StarvationRecovery
            : step > 0
                ? PredictionLeadAdjustmentReason.Increased
                : PredictionLeadAdjustmentReason.Decreased;

        return Result(PredictionLeadControllerDecision.Updated, reason, desired, update);
    }

    /// <summary>
    /// target lead = travel frames + jitter safety frames + authority buffer
    /// target, corrected by how far measured occupancy sits from that target.
    /// </summary>
    private int DesiredLead(
        PredictionLeadObservation observation,
        bool starving,
        bool confident)
    {
        var oneWayMilliseconds = Math.Max(0d, observation.Path.SmoothedRttMilliseconds) / 2d;
        var jitterMilliseconds = Math.Min(
            Math.Max(0d, observation.Path.EffectiveJitterMilliseconds) * _policy.JitterSafetyFactor,
            _policy.MaximumJitterSafetyMilliseconds);

        var desired =
            CeilingFrames(oneWayMilliseconds) +
            CeilingFrames(jitterMilliseconds) +
            _policy.AuthorityBufferTargetFrames;

        // Measured occupancy corrects the estimate in one direction only. A
        // buffer persistently deeper than target proves the travel estimate is
        // overshooting, so the owner is paying authority-side latency for
        // nothing. A shallow buffer proves nothing by itself; letting it
        // lengthen the lead would let an ordinary transient dip ratchet the lead
        // up and never come back down. Genuine shortfall arrives as starvation.
        if (confident && !starving && _hasOccupancySample)
        {
            var surplus = _smoothedOccupancy - _policy.AuthorityBufferTargetFrames;
            if (surplus > 0d)
            {
                var reduction = (int)Math.Floor(
                    Math.Min(surplus, _policy.MaximumOccupancyReductionFrames));
                desired -= reduction;
            }
        }

        if (starving)
        {
            // A starving owner's desired lead is never at or below where it
            // already is, whatever the smoothed estimates say. The per-change
            // step cap, not this value, bounds how fast it actually moves.
            desired = Math.Max(desired, _currentLead + observation.StarvedFramesSinceLastEvaluation);
        }

        return desired;
    }

    /// <summary>
    /// One frame of shrink per <see cref="PredictionLeadControllerPolicy.FramesPerDecreaseStepIncrement"/>
    /// frames of gap, capped by policy. Near target this is exactly one frame,
    /// preserving the gentleness that stops chattering; after a large excursion
    /// it recovers in a reasonable time instead of over a minute.
    /// </summary>
    private int DecreaseStepFor(int gap) => Math.Clamp(
        gap / PredictionLeadControllerPolicy.FramesPerDecreaseStepIncrement,
        1,
        _policy.MaximumDecreaseStepFrames);

    private bool HasIntervalElapsed(long frameTick, bool increasing, bool starving)
    {
        if (starving && increasing)
        {
            return !_hasIncreased ||
                frameTick - _lastIncreaseFrame >= _policy.MinimumFramesBetweenStarvationChanges;
        }

        return increasing
            ? !_hasIncreased ||
                frameTick - _lastIncreaseFrame >= _policy.MinimumFramesBetweenIncreases
            : !_hasDecreased ||
                frameTick - _lastDecreaseFrame >= _policy.MinimumFramesBetweenDecreases;
    }

    private static bool IsUsablePathEvidence(NetworkPathEstimate path) =>
        double.IsFinite(path.SmoothedRttMilliseconds) &&
        double.IsFinite(path.RttJitterMilliseconds) &&
        double.IsFinite(path.ArrivalJitterMilliseconds) &&
        path.SmoothedRttMilliseconds >= 0d &&
        path.RttJitterMilliseconds >= 0d &&
        path.ArrivalJitterMilliseconds >= 0d;

    private int CeilingFrames(double milliseconds) =>
        milliseconds <= 0d ? 0 : (int)Math.Ceiling(milliseconds / _stepMilliseconds);

    private void UpdateSmoothedOccupancy(int bufferedFrameCount)
    {
        if (!_hasOccupancySample)
        {
            _smoothedOccupancy = bufferedFrameCount;
            _hasOccupancySample = true;
            return;
        }

        _smoothedOccupancy +=
            _policy.OccupancySmoothingFactor * (bufferedFrameCount - _smoothedOccupancy);
    }

    private void TrackConfidence(long frameTick, bool confident)
    {
        if (confident)
        {
            _inLowConfidence = false;
            return;
        }

        if (!_inLowConfidence)
        {
            _inLowConfidence = true;
            _lowConfidenceSinceFrame = frameTick;
        }
    }

    /// <summary>
    /// The effective frame must be beyond both the required notice and any
    /// command already scheduled under the previous lead, so an update never
    /// relabels a command the owner has already built.
    /// </summary>
    private bool TryChooseEffectiveFrame(
        PredictionLeadObservation observation,
        out SimulationInstant effectiveFrame)
    {
        effectiveFrame = default;
        var context = new PredictionLeadSafetyContext(
            observation.AuthorityFrame,
            observation.LastScheduledCommandFrame);

        if (observation.AuthorityFrame.Tick > long.MaxValue - _leadPolicy.MinimumNoticeFrames)
        {
            return false;
        }

        var candidate = observation.AuthorityFrame.Tick + _leadPolicy.MinimumNoticeFrames;
        if (observation.LastScheduledCommandFrame is { } scheduled)
        {
            if (scheduled.Tick == long.MaxValue)
            {
                return false;
            }

            candidate = Math.Max(candidate, scheduled.Tick + 1);
        }

        var chosen = new SimulationInstant(candidate);
        if (!context.IsSafe(chosen, _leadPolicy))
        {
            return false;
        }

        effectiveFrame = chosen;
        return true;
    }

    private PredictionLeadEvaluation Result(
        PredictionLeadControllerDecision decision,
        PredictionLeadAdjustmentReason reason,
        int desired,
        PredictionLeadUpdate? update) =>
        new(decision, reason, _currentLead, desired, _smoothedOccupancy, update);
}
