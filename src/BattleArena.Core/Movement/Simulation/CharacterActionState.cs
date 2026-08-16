using BattleArena.Core.Common;

namespace BattleArena.Core.Movement.Simulation;

/// <summary>
/// Where an action is in its authored timeline. Phase, not outcome.
/// </summary>
public enum PredictedActionPhase : byte
{
    None = 0,
    Startup = 1,
    Committed = 2,
    Recovery = 3,
}

/// <summary>
/// Predicted action correlation and phase, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// This is the narrowest thing that still lets the owner predict the movement
/// consequences of its own attack. Accepted feel makes the active attack step
/// gate sprint acceleration, momentum preservation, acceleration and steering
/// multipliers, and additional deceleration — see
/// <see cref="MovementInfluence"/> — so replay must know which step was active
/// on the frame it is reproducing.
/// </para>
/// <para>
/// What is structurally absent is as important as what is present: no hit
/// candidate, no damage, no health, no target. The owner predicts that it swung
/// and how that swing moved it; whether the swing connected is authority's
/// answer alone, and keeping it out of this type means a client cannot assert it
/// by construction rather than by a rule someone has to enforce.
/// </para>
/// </remarks>
public readonly record struct CharacterActionState
{
    public CharacterActionState(
        PredictedActionId predictedActionId,
        AuthorityActionExecutionId authorityExecutionId,
        PredictedActionPhase phase,
        int stepIndex,
        SimulationInstant startedAt,
        bool continuationQueued,
        bool releasedDuringStep)
    {
        if (!Enum.IsDefined(phase))
        {
            throw new ArgumentOutOfRangeException(nameof(phase));
        }
        if (stepIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stepIndex));
        }

        PredictedActionId = predictedActionId;
        AuthorityExecutionId = authorityExecutionId;
        Phase = phase;
        StepIndex = stepIndex;
        StartedAt = startedAt;
        ContinuationQueued = continuationQueued;
        ReleasedDuringStep = releasedDuringStep;
    }

    /// <summary>The owner's correlation identity, valid while an action is predicted.</summary>
    public PredictedActionId PredictedActionId { get; }

    /// <summary>
    /// Authority's execution identity once it has accepted the action. Deliberately
    /// distinct from the client's correlation id so the two can never coincide by
    /// accident, which they previously did.
    /// </summary>
    public AuthorityActionExecutionId AuthorityExecutionId { get; }

    public PredictedActionPhase Phase { get; }

    /// <summary>
    /// Which authored step of a combo is active. This is what selects the
    /// movement influence curve, so it must survive restore.
    /// </summary>
    public int StepIndex { get; }

    public SimulationInstant StartedAt { get; }

    /// <summary>Whether the next combo step is buffered.</summary>
    public bool ContinuationQueued { get; }

    /// <summary>Whether attack was released during this step, for hold-sensitive policies.</summary>
    public bool ReleasedDuringStep { get; }

    public bool IsActive => Phase is not PredictedActionPhase.None;
    public bool IsValid => Enum.IsDefined(Phase) && StepIndex >= 0;
    public static CharacterActionState Idle => default;
}

/// <summary>
/// Counters a rule reads and which therefore must survive restore.
/// </summary>
/// <remarks>
/// Air jumps and air rolls are consumed on use and refunded on landing, so they
/// are state, not derivable from the current frame. Both authored maxima already
/// exist on <see cref="MovementCapabilitySnapshot"/>; today they are 1 and 0, so
/// nothing consumes these yet. They are stored now because adding a field to the
/// rewind unit later is a protocol change, and because a counter that is missing
/// from restore is exactly the silent divergence this type exists to prevent.
/// </remarks>
public readonly record struct DeterministicCounterState
{
    public DeterministicCounterState(int airJumpsUsed, int airRollsUsed)
    {
        if (airJumpsUsed < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(airJumpsUsed));
        }
        if (airRollsUsed < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(airRollsUsed));
        }

        AirJumpsUsed = airJumpsUsed;
        AirRollsUsed = airRollsUsed;
    }

    public int AirJumpsUsed { get; }
    public int AirRollsUsed { get; }
    public bool IsValid => AirJumpsUsed >= 0 && AirRollsUsed >= 0;
    public static DeterministicCounterState Empty => default;

    /// <summary>Refunds every air allowance, applied on landing.</summary>
    public DeterministicCounterState ResetOnGrounded() => default;
}

/// <summary>
/// The kind of one transition applying on a frame, decoupled from the
/// multiplayer journal type so <c>BattleArena.Core</c> keeps no dependency on
/// the networking assembly.
/// </summary>
public enum MovementTransitionKindTag : byte
{
    JumpPressed = 1,
    JumpReleased = 2,
    CrouchOrRollPressed = 3,
    CrouchOrRollReleased = 4,
    LedgeGrab = 5,
    LedgeClimb = 6,
    LedgeDrop = 7,
}

/// <summary>
/// Rebuilds tick-local pressed/released bits from the durable transition
/// references carried by one frame's input.
/// </summary>
/// <remarks>
/// <para>
/// The accepted rule simulators are written against edges —
/// <see cref="JumpFallSimulator"/> reads jump pressed and released,
/// <see cref="CrouchRollSimulator"/> reads crouch/roll pressed — but
/// <see cref="CharacterSimulationInput"/> deliberately carries only held state
/// plus durable transition references. That is not an oversight: P03-05 exists
/// precisely because a discrete edge must not depend on one transient bit
/// surviving three packets, so presses and releases travel as durable intents
/// that are resent until acknowledged.
/// </para>
/// <para>
/// This mapper is the compatibility layer the redesign anticipates, translating
/// those references back into the edge bits the existing rules expect. Deriving
/// edges this way is also strictly better for replay than diffing against the
/// previous frame's held state: an edge is present on exactly the frame its
/// transition applies, so a restored frame reproduces it without needing the
/// prior frame at all.
/// </para>
/// </remarks>
public static class OwnerInputEdgeMapper
{
    /// <summary>
    /// Produces the pressed and released button sets for one frame from its
    /// applied transition references.
    /// </summary>
    /// <param name="appliedTransitions">
    /// The transitions authority resolved as applying on this frame. On the
    /// owner's predicted path these are the transitions it originated for the
    /// frame; on replay they are the same set, which is what makes the edge
    /// reproducible.
    /// </param>
    public static (MovementButtons Pressed, MovementButtons Released) EdgesFor(
        ReadOnlySpan<MovementTransitionKindTag> appliedTransitions)
    {
        var pressed = MovementButtons.None;
        var released = MovementButtons.None;
        for (var index = 0; index < appliedTransitions.Length; index++)
        {
            switch (appliedTransitions[index])
            {
                case MovementTransitionKindTag.JumpPressed:
                    pressed |= MovementButtons.Jump;
                    break;
                case MovementTransitionKindTag.JumpReleased:
                    released |= MovementButtons.Jump;
                    break;
                case MovementTransitionKindTag.CrouchOrRollPressed:
                    pressed |= MovementButtons.CrouchOrRoll;
                    break;
                case MovementTransitionKindTag.CrouchOrRollReleased:
                    released |= MovementButtons.CrouchOrRoll;
                    break;
                default:
                    // Ledge transitions carry no button edge; they are consumed
                    // by traversal rules rather than by the button grammar.
                    break;
            }
        }

        return (pressed, released);
    }

    /// <summary>
    /// Builds the legacy <see cref="MovementCommand"/> the rule simulators
    /// consume, combining held state from the input with edges from transitions.
    /// </summary>
    public static MovementCommand ToMovementCommand(
        in CharacterSimulationInput input,
        ReadOnlySpan<MovementTransitionKindTag> appliedTransitions,
        SimulationInstant frame)
    {
        var (pressed, released) = EdgesFor(appliedTransitions);
        var held = HeldButtonsFrom(input);

        // A press implies the button is held on that frame even if the held mask
        // arrived without it, which happens when the press and its packet race.
        held |= pressed;
        return new MovementCommand(
            0UL,
            frame,
            new HorizontalVector(
                input.Movement.XQ15 / (double)MovementAxes.MaximumMagnitude,
                input.Movement.ZQ15 / (double)MovementAxes.MaximumMagnitude),
            input.View.YawRadians,
            input.View.PitchRadians,
            held,
            pressed,
            released);
    }

    /// <summary>Projects the canonical held state into the legacy button mask.</summary>
    public static MovementButtons HeldButtonsFrom(in CharacterSimulationInput input)
    {
        var held = MovementButtons.None;
        var movement = input.MovementHeld.Buttons;
        if ((movement & MovementHeldButtons.Jump) != 0)
        {
            held |= MovementButtons.Jump;
        }
        if ((movement & MovementHeldButtons.Sprint) != 0)
        {
            held |= MovementButtons.Sprint;
        }
        if ((movement & MovementHeldButtons.CrouchOrRoll) != 0)
        {
            held |= MovementButtons.CrouchOrRoll;
        }
        if ((input.CombatInput.HeldButtons & CombatHeldButtons.Attack) != 0)
        {
            held |= MovementButtons.Attack;
        }
        if ((input.CombatInput.HeldButtons & CombatHeldButtons.Block) != 0)
        {
            held |= MovementButtons.Block;
        }

        return held;
    }
}
