using BattleArena.Core.Common;

namespace BattleArena.Core.Movement.Simulation;

/// <summary>
/// Applies active external movement sources in the canonical frame order.
/// </summary>
/// <remarks>
/// <para>
/// Replaces the current practice of adding an impulse to node velocity at the
/// instant an effect fires. That is unreplayable in both directions: rewinding
/// past the impulse loses it, and rewinding after it re-applies it. Evaluating
/// stored sources per frame makes the contribution a pure function of the frame
/// number.
/// </para>
/// <para>
/// Order within the frame is fixed — sources are added, then expired, then
/// aggregated — so a source that starts and ends on the same frame behaves
/// identically on first run and replay.
/// </para>
/// </remarks>
public sealed class MovementSourceSimulator
{
    /// <summary>
    /// Advances the source buffer for one frame and returns the velocity
    /// contribution to fold into locomotion.
    /// </summary>
    /// <remarks>
    /// Expiry runs before aggregation so a source ending on this frame does not
    /// contribute to it, which is what makes a lunge's final frame consistent
    /// whether it is simulated once or replayed ten times.
    /// </remarks>
    /// <remarks>
    /// The returned contribution is applied to the frame's DISPLACEMENT only and
    /// is never written back into the persisted velocity. Persisting it would
    /// double-count: the next frame re-evaluates the same source's curve and
    /// would add it on top of the copy already folded into velocity, so a lunge
    /// would accelerate every frame instead of following its authored falloff.
    /// Only the motor's collision-resolved velocity is persisted.
    /// </remarks>
    public (MovementSourceBuffer Sources, HorizontalVector Horizontal, double Vertical) Advance(
        in MovementSourceBuffer sources,
        SimulationInstant frame) => throw new NotImplementedException();

    /// <summary>
    /// Starts a source on an exact frame, replacing any existing source with the
    /// same identity so a repeated authority message is idempotent.
    /// </summary>
    /// <remarks>
    /// The start frame is explicit rather than "now" because an accepted attack
    /// lunge begins on the action's accepted frame, which may be earlier than the
    /// frame this is called on, and a knockback begins on the frame the hit
    /// resolved. Deriving it from the call site would put the impulse on the
    /// wrong frame during replay.
    /// </remarks>
    public MovementSourceBuffer Start(
        in MovementSourceBuffer sources,
        in MovementSourceState source) => throw new NotImplementedException();
}

/// <summary>
/// Drives the accepted locomotion, jump, and roll rules through the explicit
/// capsule motor.
/// </summary>
/// <remarks>
/// <para>
/// This is a composition, not a rewrite. <see cref="GroundLocomotionSimulator"/>,
/// <see cref="AirborneLocomotionSimulator"/>, <see cref="JumpFallSimulator"/>,
/// and <see cref="CrouchRollSimulator"/> already encode the accepted feel and are
/// used unchanged; what changes is that their resulting velocity is integrated
/// against the world by <see cref="CapsuleMovementSimulator"/> from explicit
/// state, instead of being handed to a live node's <c>MoveAndSlide</c>.
/// </para>
/// <para>
/// The canonical per-frame order is fixed and is itself part of the contract:
/// </para>
/// <list type="number">
/// <item>probe standing clearance, producing canStand;</item>
/// <item>run the crouch/roll rules, whose output IS the profile intent;</item>
/// <item>commit the profile, including any pending expansion clearance now
/// permits;</item>
/// <item>run the jump/fall and ground/air rules to get desired velocity;</item>
/// <item>evaluate external movement sources and add their contribution;</item>
/// <item>move the capsule against the world;</item>
/// <item>settle grounding and mode from what the motor actually found.</item>
/// </list>
/// <para>
/// The profile steps sit after the crouch/roll rules and before the move for a
/// specific reason: the rules decide the intent, and the move must sweep the
/// capsule it is going to commit. Resolving the profile before the rules would
/// sweep the standing shape on the frame a roll begins and then commit the
/// rolling one. Sources are applied before the move so a knockback collides
/// rather than passing through a wall, and ground snap is suppressed while
/// rising so a jump is not pulled straight back down.
/// </para>
/// </remarks>
public sealed class CharacterMovementSimulator
{
    public CharacterMovementSimulator(
        CapsuleMovementSimulator capsule,
        MovementSourceSimulator sources) => throw new NotImplementedException();

    /// <summary>
    /// Simulates one complete frame for one character.
    /// </summary>
    /// <remarks>
    /// Pure: the same state and input on the same frame always produce the same
    /// result, which is what replay depends on. Nothing is read from a node and
    /// nothing is written to one.
    /// </remarks>
    /// <param name="appliedTransitions">
    /// Durable transitions applying on this frame, mapped into tick-local edge
    /// bits by <see cref="OwnerInputEdgeMapper"/> for the accepted rules.
    /// </param>
    /// <param name="attributes">
    /// Authored tuning for the revision this frame is simulated under. Passed per
    /// frame rather than held, because a replayed frame must use the revision
    /// that was in force then; the capsule dimensions and motor policy travel
    /// with it for the same reason.
    /// </param>
    public CharacterFrameResult Simulate(
        in CharacterSimulationState state,
        in CharacterSimulationInput input,
        ReadOnlySpan<MovementTransitionKindTag> appliedTransitions,
        in SimulationStepContext context,
        MovementAttributeSnapshot attributes,
        MovementCapabilitySnapshot capabilities) => throw new NotImplementedException();

    /// <summary>
    /// Probes whether the standing capsule fits at the current position.
    /// </summary>
    /// <returns>
    /// Whether the standing capsule fits at the current position. The crouch/roll
    /// rules need this — a character under a low ceiling may not stand — so it is
    /// probed before them and passed in, rather than being discovered after the
    /// decision has already been made.
    /// </returns>
    internal bool ProbeStandingClearance(in CharacterSimulationState state) =>
        throw new NotImplementedException();

    /// <summary>
    /// Commits the profile the crouch/roll rules asked for, applying a pending
    /// expansion when clearance now permits it.
    /// </summary>
    /// <remarks>
    /// Runs after the rules and before the move, so the capsule that is swept is
    /// the capsule that is committed.
    /// </remarks>
    internal CharacterSimulationState CommitProfile(
        in CharacterSimulationState state,
        bool canStand,
        CollisionProfileTable profiles) => throw new NotImplementedException();

    /// <summary>
    /// Runs the accepted ground or airborne rule set for this frame, producing
    /// desired velocity without touching the world.
    /// </summary>
    /// <remarks>
    /// Also applies the accepted crouch input adjustments that currently live in
    /// the Godot driver and have no Core home: while crouched the movement vector
    /// is scaled to the crouch speed ratio and sprint is stripped, and while
    /// unable to stand the jump edge is stripped. Those are accepted feel, so
    /// they move here rather than being lost with the driver.
    /// </remarks>
    internal MovementRuntimeState ApplyLocomotionRules(
        in CharacterSimulationState state,
        in MovementCommand command,
        bool canStand,
        in SimulationStepContext context,
        MovementAttributeSnapshot attributes,
        MovementCapabilitySnapshot capabilities,
        MovementInfluence? influence) => throw new NotImplementedException();

    /// <summary>
    /// Selects the movement influence the active attack step imposes this frame.
    /// </summary>
    /// <remarks>
    /// Derived from the action step index and the authored attack policy, so a
    /// replayed frame reproduces the same gating of sprint acceleration, momentum
    /// preservation, acceleration and steering multipliers, and additional
    /// deceleration that the original frame had.
    /// </remarks>
    internal MovementInfluence? InfluenceFor(
        in CharacterActionState action,
        MovementAttributeSnapshot attributes) => throw new NotImplementedException();

    /// <summary>
    /// Reconciles locomotion mode with what the motor actually found underfoot.
    /// </summary>
    /// <remarks>
    /// The rule simulators decide mode from the state they were given; the motor
    /// then discovers whether the character is really supported. Landing and
    /// leaving a ledge are both detected here, which is why coyote time and
    /// buffered jumps read <see cref="CharacterKinematicState.IsGrounded"/> after
    /// this rather than before.
    /// </remarks>
    /// <remarks>
    /// Folds back the motor's collision-resolved velocity. The movement-source
    /// contribution is deliberately not re-captured here; see
    /// <see cref="MovementSourceSimulator.Advance"/>.
    /// </remarks>
    internal CharacterSimulationState SettleGrounding(
        in CharacterSimulationState state,
        in CapsuleMotionResult motion,
        SimulationInstant frame) => throw new NotImplementedException();
}

/// <summary>
/// One simulated frame: the new state, plus what the motor did to produce it.
/// </summary>
/// <remarks>
/// The diagnostics are not optional colour. The golden suite asserts on step
/// rejection, slide iteration counts, and ceiling blocking; the dual-motor test
/// arena reports the active motor; and Phase 6 reconciliation needs the per-frame
/// outcome to explain a correction. Returning only the state would leave all of
/// them with nothing to assert on but position.
/// </remarks>
public readonly record struct CharacterFrameResult
{
    internal CharacterFrameResult(
        CharacterSimulationState state,
        CapsuleMotionOutcome outcome,
        int slideIterations,
        bool ceilingBlocked,
        bool profileExpansionBlocked) => throw new NotImplementedException();

    public CharacterSimulationState State { get; }
    public CapsuleMotionOutcome Outcome { get; }
    public int SlideIterations { get; }
    public bool CeilingBlocked { get; }

    /// <summary>Whether a pending stand was refused by clearance this frame.</summary>
    public bool ProfileExpansionBlocked { get; }

    /// <summary>
    /// Whether this frame ended in a state the caller must repair rather than
    /// continue from.
    /// </summary>
    public bool RequiresRepair => throw new NotImplementedException();
}
