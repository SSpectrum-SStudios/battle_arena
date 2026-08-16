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
        MovementCapabilityRevision capabilityRevision) => throw new NotImplementedException();

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
    public SimulationInstant Frame { get; }

    /// <summary>Position, velocity, facing, and support. The part the legacy state lacked.</summary>
    public CharacterKinematicState Kinematic { get; }

    public CollisionProfileState Profile { get; }

    /// <summary>Active external motion, replayable rather than applied once as an impulse.</summary>
    public MovementSourceBuffer MovementSources { get; }

    /// <summary>
    /// Predicted action correlation and phase — never an outcome.
    /// </summary>
    /// <remarks>
    /// Needed because accepted attack feel includes movement influence: the
    /// active attack step gates sprint acceleration, momentum preservation,
    /// acceleration and steering multipliers, and extra deceleration. Replay
    /// cannot reproduce those without knowing which step was active on the frame,
    /// and <see cref="ActionMode"/> alone is a three-state enum that cannot say.
    /// </remarks>
    public CharacterActionState Action { get; }

    /// <summary>
    /// Counters that must survive restore because a rule reads them.
    /// </summary>
    public DeterministicCounterState Counters { get; }

    public LocomotionMode LocomotionMode { get; }
    public PostureMode PostureMode { get; }
    public MovementActionMode ActionMode { get; }
    public SimulationInstant ModeStartedAt { get; }
    public JumpPhase JumpPhase { get; }
    public SimulationInstant LastGroundedAt { get; }
    public SimulationInstant? BufferedJumpUntil { get; }
    public bool JumpCutApplied { get; }
    public HorizontalVector RollDirection { get; }
    public double RollEntrySpeed { get; }
    public double RollBoostDistance { get; }
    public SimulationDuration RollDuration { get; }
    public SimulationInstant RollAvailableAt { get; }
    public bool LandingRollQueued { get; }

    /// <summary>
    /// Configuration the frame was simulated under. Carried so a replayed frame
    /// uses the revision that was in force then, not whatever is current — a
    /// mid-replay configuration change would otherwise silently alter history.
    /// </summary>
    public MovementConfigurationRevision MovementRevision { get; }
    public MovementCapabilityRevision CapabilityRevision { get; }

    public bool IsValid => throw new NotImplementedException();

    public static CharacterSimulationState CreateGrounded(
        WorldPosition position,
        double facingYawRadians,
        SimulationInstant now,
        MovementConfigurationRevision movementRevision,
        MovementCapabilityRevision capabilityRevision) => throw new NotImplementedException();

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
    public MovementRuntimeState ToRuntimeState() => throw new NotImplementedException();

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
    public CharacterSimulationState WithRuntimeState(MovementRuntimeState runtime) =>
        throw new NotImplementedException();

    public CharacterSimulationState WithFrame(SimulationInstant frame) =>
        throw new NotImplementedException();

    public CharacterSimulationState WithAction(CharacterActionState action) =>
        throw new NotImplementedException();

    public CharacterSimulationState WithCounters(DeterministicCounterState counters) =>
        throw new NotImplementedException();

    public CharacterSimulationState WithKinematic(CharacterKinematicState kinematic) =>
        throw new NotImplementedException();

    public CharacterSimulationState WithProfile(CollisionProfileState profile) =>
        throw new NotImplementedException();

    public CharacterSimulationState WithMovementSources(MovementSourceBuffer sources) =>
        throw new NotImplementedException();
}
