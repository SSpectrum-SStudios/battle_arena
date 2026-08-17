using BattleArena.Core.Common;

namespace BattleArena.Core.Movement.Simulation;

/// <summary>
/// The complete unit of owner prediction: everything needed to restore a
/// character to a past frame and resimulate forward to an identical result.
/// </summary>
/// <remarks>
/// <para>
/// Completeness is the whole contract. Any prediction-relevant value left out
/// becomes a silent divergence: replay reproduces every field this type holds
/// and none that it does not, so an omitted field means the same inputs produce
/// a different outcome on the second run. That failure is invisible until it
/// shows up as a correction the player feels.
/// </para>
/// <para>
/// Authority-only state is structurally excluded — health, hit registration,
/// damage, and score are never stored here. The owner must not predict them, and
/// keeping them out of the type means a client cannot claim them by
/// construction rather than by a validation rule someone has to remember. The
/// action fields present are correlation and phase only: enough to predict
/// presentation and movement influence, never an outcome.
/// </para>
/// <para>
/// Authority discontinuity and owner-control epoch are deliberately absent.
/// They live in <c>BattleArena.Multiplayer</c>, and Phase 4's
/// <c>CombatantPredictionEpochGate</c> already owns that scoping; duplicating
/// them here would create a second place for them to disagree.
/// </para>
/// <para>
/// A value struct, copied per retained frame. The Phase 1 replay probe measured
/// history memory and per-frame cost against exactly this shape.
/// </para>
/// </remarks>
public readonly record struct CharacterSimulationState
{
    public CharacterSimulationState(
        SimulationInstant frame,
        CharacterKinematicState kinematic,
        CollisionProfileState profile,
        FrameContactBuffer contacts,
        MovementSourceBuffer movementSources,
        CharacterActionState action,
        DeterministicCounterState counters,
        LocomotionMode locomotionMode,
        PostureMode postureMode,
        MovementActionMode actionMode,
        SimulationInstant modeStartedAt,
        JumpPhase jumpPhase,
        SimulationInstant lastGroundedAt,
        SimulationInstant? bufferedJumpUntil,
        bool jumpCutApplied,
        HorizontalVector rollDirection,
        double rollEntrySpeed,
        double rollBoostDistance,
        SimulationDuration rollDuration,
        SimulationInstant rollAvailableAt,
        bool landingRollQueued,
        MovementConfigurationRevision movementRevision,
        MovementCapabilityRevision capabilityRevision)
    {
        if (!kinematic.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(kinematic));
        }
        if (!profile.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(profile));
        }
        if (!action.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(action));
        }
        if (!counters.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(counters));
        }
        if (!rollDirection.IsFinite ||
            !double.IsFinite(rollEntrySpeed) || rollEntrySpeed < 0d ||
            !double.IsFinite(rollBoostDistance) || rollBoostDistance < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(rollDirection));
        }
        if (!movementRevision.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(movementRevision));
        }
        if (!capabilityRevision.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(capabilityRevision));
        }

        Frame = frame;
        Kinematic = kinematic;
        Profile = profile;
        Contacts = contacts;
        MovementSources = movementSources;
        Action = action;
        Counters = counters;
        LocomotionMode = locomotionMode;
        PostureMode = postureMode;
        ActionMode = actionMode;
        ModeStartedAt = modeStartedAt;
        JumpPhase = jumpPhase;
        LastGroundedAt = lastGroundedAt;
        BufferedJumpUntil = bufferedJumpUntil;
        JumpCutApplied = jumpCutApplied;
        RollDirection = rollDirection;
        RollEntrySpeed = rollEntrySpeed;
        RollBoostDistance = rollBoostDistance;
        RollDuration = rollDuration;
        RollAvailableAt = rollAvailableAt;
        LandingRollQueued = landingRollQueued;
        MovementRevision = movementRevision;
        CapabilityRevision = capabilityRevision;
    }

    /// <summary>
    /// The frame this state is the result of.
    /// </summary>
    /// <remarks>
    /// Carried so a restored state can identify itself. Without it, restoring the
    /// wrong frame is undetectable — the replay simply produces a wrong answer
    /// confidently — and invariants that relate other fields to time, such as
    /// <see cref="ModeStartedAt"/> never exceeding the current frame, cannot be
    /// checked at all.
    /// </remarks>
    public SimulationInstant Frame { get; init; }

    /// <summary>Position, velocity, facing, and support. The part the legacy state lacked.</summary>
    public CharacterKinematicState Kinematic { get; init; }

    public CollisionProfileState Profile { get; init; }

    /// <summary>
    /// The contacts this frame resolved against.
    /// </summary>
    /// <remarks>
    /// Derivable from position, profile, and the world, so storing them is not
    /// what makes replay correct — it is what makes a correction explainable. The
    /// comparer can say the two simulations disagreed about which surface the
    /// character was on rather than only that they disagreed about position, and
    /// the policy can classify that as contact divergence rather than numeric
    /// drift, which is the difference between a correction that may be smoothed
    /// and one that must not be.
    /// </remarks>
    public FrameContactBuffer Contacts { get; init; }

    /// <summary>Active external motion, replayable rather than applied once as an impulse.</summary>
    public MovementSourceBuffer MovementSources { get; init; }

    /// <summary>
    /// Predicted action correlation and phase — never an outcome.
    /// </summary>
    public CharacterActionState Action { get; init; }

    /// <summary>Counters that must survive restore because a rule reads them.</summary>
    public DeterministicCounterState Counters { get; init; }

    public LocomotionMode LocomotionMode { get; init; }
    public PostureMode PostureMode { get; init; }
    public MovementActionMode ActionMode { get; init; }
    public SimulationInstant ModeStartedAt { get; init; }
    public JumpPhase JumpPhase { get; init; }
    public SimulationInstant LastGroundedAt { get; init; }
    public SimulationInstant? BufferedJumpUntil { get; init; }
    public bool JumpCutApplied { get; init; }
    public HorizontalVector RollDirection { get; init; }
    public double RollEntrySpeed { get; init; }
    public double RollBoostDistance { get; init; }
    public SimulationDuration RollDuration { get; init; }
    public SimulationInstant RollAvailableAt { get; init; }
    public bool LandingRollQueued { get; init; }

    /// <summary>
    /// Configuration the frame was simulated under. Carried so a replayed frame
    /// uses the revision that was in force then, not whatever is current — a
    /// mid-replay configuration change would otherwise silently alter history.
    /// </summary>
    public MovementConfigurationRevision MovementRevision { get; init; }
    public MovementCapabilityRevision CapabilityRevision { get; init; }

    /// <summary>
    /// Every invariant this type promises.
    /// </summary>
    /// <remarks>
    /// The properties carry <c>init</c> setters so the narrow <c>With*</c>
    /// helpers can use <c>with</c> expressions, which means a caller could in
    /// principle bypass the validating constructor. This check is therefore the
    /// standing guard rather than a redundant one, and inserting a state into
    /// prediction history must assert it.
    /// </remarks>
    public bool IsValid =>
        Kinematic.IsValid &&
        Profile.IsValid &&
        Action.IsValid &&
        Counters.IsValid &&
        MovementRevision.IsValid &&
        CapabilityRevision.IsValid &&
        ModeStartedAt.Tick <= Frame.Tick &&
        LastGroundedAt.Tick <= Frame.Tick;

    public static CharacterSimulationState CreateGrounded(
        WorldPosition position,
        double facingYawRadians,
        SimulationInstant now,
        MovementConfigurationRevision movementRevision,
        MovementCapabilityRevision capabilityRevision) => new(
            now,
            CharacterKinematicState.AtRest(position, facingYawRadians),
            CollisionProfileState.Standing,
            default,
            default,
            CharacterActionState.Idle,
            DeterministicCounterState.Empty,
            LocomotionMode.Grounded,
            PostureMode.Standing,
            MovementActionMode.Ready,
            now,
            JumpPhase.None,
            now,
            null,
            false,
            HorizontalVector.Zero,
            0d,
            0d,
            SimulationDuration.Zero,
            SimulationInstant.Zero,
            false,
            movementRevision,
            capabilityRevision);

    /// <summary>
    /// Projects this state into a <see cref="MovementRuntimeState"/> so the
    /// existing accepted rule simulators can be driven unchanged.
    /// </summary>
    /// <remarks>
    /// Reads horizontal velocity, vertical velocity, and facing out of
    /// <see cref="Kinematic"/>, and the mode/jump/roll fields from this state
    /// directly. Those three velocity and facing values live only in
    /// <see cref="Kinematic"/> — there is no second copy — so this projection is
    /// the only way the rules see them.
    /// </remarks>
    public MovementRuntimeState ToRuntimeState() => new(
        Kinematic.HorizontalVelocity,
        Kinematic.VerticalVelocity,
        Kinematic.FacingYawRadians,
        LocomotionMode,
        PostureMode,
        ActionMode,
        ModeStartedAt,
        JumpPhase,
        LastGroundedAt,
        BufferedJumpUntil,
        JumpCutApplied,
        RollDirection,
        RollEntrySpeed,
        RollBoostDistance,
        RollDuration,
        RollAvailableAt,
        LandingRollQueued);

    /// <summary>
    /// Folds a rule simulator's result back in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Writes velocity and facing <em>into</em> <see cref="Kinematic"/>, because
    /// that is the entire output of the rule simulators — if this left
    /// <see cref="Kinematic"/> alone the character would never move at all — and
    /// copies the mode, jump, and roll fields across.
    /// </para>
    /// <para>
    /// It deliberately does not touch position, grounded, ground normal, or
    /// support: those are the motor's to decide, from the world, after the rules
    /// have said how fast the character wants to go.
    /// </para>
    /// </remarks>
    public CharacterSimulationState WithRuntimeState(MovementRuntimeState runtime) => new(
        Frame,
        Kinematic
            .WithVelocity(runtime.HorizontalVelocity, runtime.VerticalVelocity)
            .WithFacing(runtime.FacingYawRadians),
        Profile,
        Contacts,
        MovementSources,
        Action,
        Counters,
        runtime.LocomotionMode,
        runtime.PostureMode,
        runtime.ActionMode,
        runtime.ModeStartedAt,
        runtime.JumpPhase,
        runtime.LastGroundedAt,
        runtime.BufferedJumpUntil,
        runtime.JumpCutApplied,
        runtime.RollDirection,
        runtime.RollEntrySpeed,
        runtime.RollBoostDistance,
        runtime.RollDuration,
        runtime.RollAvailableAt,
        runtime.LandingRollQueued,
        MovementRevision,
        CapabilityRevision);

    public CharacterSimulationState WithFrame(SimulationInstant frame) =>
        this with { Frame = frame };

    public CharacterSimulationState WithKinematic(CharacterKinematicState kinematic) =>
        this with { Kinematic = kinematic };

    public CharacterSimulationState WithProfile(CollisionProfileState profile) =>
        this with { Profile = profile };

    public CharacterSimulationState WithContacts(FrameContactBuffer contacts) =>
        this with { Contacts = contacts };

    public CharacterSimulationState WithMovementSources(MovementSourceBuffer sources) =>
        this with { MovementSources = sources };

    public CharacterSimulationState WithAction(CharacterActionState action) =>
        this with { Action = action };

    public CharacterSimulationState WithCounters(DeterministicCounterState counters) =>
        this with { Counters = counters };

    public CharacterSimulationState WithRevisions(
        MovementConfigurationRevision movementRevision,
        MovementCapabilityRevision capabilityRevision) => this with
        {
            MovementRevision = movementRevision,
            CapabilityRevision = capabilityRevision,
        };
}
