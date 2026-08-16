using BattleArena.Core.Common;

namespace BattleArena.Core.Movement;

/// <remarks>
/// A value struct rather than a record class. The rule simulators build results
/// with `with` expressions, which on a record class heap-allocate once or twice
/// per call; owner replay runs those simulators for every retained frame of
/// every combatant, so that allocation is multiplied by history depth and
/// combatant count. The `with` expressions compile unchanged over a struct, so
/// the accepted rules are preserved verbatim.
/// </remarks>
public readonly record struct MovementRuntimeState
{
    public MovementRuntimeState(
        HorizontalVector horizontalVelocity,
        double verticalVelocity,
        double facingYawRadians,
        LocomotionMode locomotionMode,
        PostureMode postureMode,
        MovementActionMode actionMode,
        SimulationInstant modeStartedAt,
        JumpPhase jumpPhase = JumpPhase.None,
        SimulationInstant? lastGroundedAt = null,
        SimulationInstant? bufferedJumpUntil = null,
        bool jumpCutApplied = false,
        HorizontalVector? rollDirection = null,
        double rollEntrySpeed = 0d,
        double rollBoostDistance = 0d,
        SimulationDuration? rollDuration = null,
        SimulationInstant? rollAvailableAt = null,
        bool landingRollQueued = false)
    {
        if (!horizontalVelocity.IsFinite ||
            !double.IsFinite(verticalVelocity) ||
            !double.IsFinite(facingYawRadians) ||
            !double.IsFinite(rollEntrySpeed) ||
            !double.IsFinite(rollBoostDistance) ||
            rollEntrySpeed < 0d ||
            rollBoostDistance < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(horizontalVelocity), "Movement state values must be finite.");
        }

        HorizontalVelocity = horizontalVelocity;
        VerticalVelocity = verticalVelocity;
        FacingYawRadians = MovementMath.WrapAngle(facingYawRadians);
        LocomotionMode = locomotionMode;
        PostureMode = postureMode;
        ActionMode = actionMode;
        ModeStartedAt = modeStartedAt;
        JumpPhase = jumpPhase;
        LastGroundedAt = lastGroundedAt ?? modeStartedAt;
        BufferedJumpUntil = bufferedJumpUntil;
        JumpCutApplied = jumpCutApplied;
        RollDirection = rollDirection ?? HorizontalVector.Zero;
        RollEntrySpeed = rollEntrySpeed;
        RollBoostDistance = rollBoostDistance;
        RollDuration = rollDuration ?? SimulationDuration.Zero;
        RollAvailableAt = rollAvailableAt ?? SimulationInstant.Zero;
        LandingRollQueued = landingRollQueued;
    }

    public HorizontalVector HorizontalVelocity { get; init; }

    public double VerticalVelocity { get; init; }

    public double FacingYawRadians { get; init; }

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

    public static MovementRuntimeState CreateGrounded(SimulationInstant now, double facingYawRadians = 0d) =>
        new(
            HorizontalVector.Zero,
            0d,
            facingYawRadians,
            LocomotionMode.Grounded,
            PostureMode.Standing,
            MovementActionMode.Ready,
            now);
}
