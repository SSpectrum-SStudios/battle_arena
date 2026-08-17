using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Core.Movement.Simulation;
using Godot;

namespace BattleArena.Movement;

/// <summary>
/// Runs one scripted input trace through both motors and reports where they
/// diverge.
/// </summary>
/// <remarks>
/// <para>
/// P05-16 supplies the motor switch but nothing compares the two, so "does the
/// explicit motor still feel right" has stayed an opinion. Both motors read the
/// same authored <c>movement.json</c>, so any divergence is the motor's doing
/// and not a difference in tuning.
/// </para>
/// <para>
/// This must run before reconciliation is trusted. Once corrections are live, a
/// motor that drifts from accepted feel stops looking like a motor bug and
/// starts looking like a network correction, which is a far more expensive thing
/// to debug.
/// </para>
/// <para>
/// Divergence is reported with its frame and field rather than averaged. A mean
/// error hides exactly the case that matters — one frame that went badly wrong —
/// inside hundreds of frames that went fine.
/// </para>
/// <para>
/// Scope: the trace contains no attack, because
/// <c>CharacterMovementSimulator.InfluenceFor</c> returns null until P05-19
/// authors the influence table. The result therefore means "the motors match
/// with no attack step active", and must not be read as "the motors match".
/// </para>
/// </remarks>
public sealed partial class MovementMotorParityProbe : Node3D
{
    public override void _Ready() => throw new NotImplementedException();

    /// <summary>
    /// The scripted trace: flat running, sprint, a wall slide, a stair climb, a
    /// jump arc, a crouch passage, and a roll.
    /// </summary>
    /// <remarks>
    /// A pure function of frame index, so both motors receive byte-identical
    /// input and the comparison is about the motors alone.
    /// </remarks>
    private static MovementCommand ScriptedInput(int frameIndex, SimulationInstant frame) =>
        throw new NotImplementedException();

    /// <summary>Runs the trace under the legacy driver.</summary>
    private void RunLegacy() => throw new NotImplementedException();

    /// <summary>Runs the same trace under the explicit motor.</summary>
    private void RunExplicit() => throw new NotImplementedException();

    /// <summary>
    /// Compares the two traces frame by frame and exits zero when every frame is
    /// inside the authored tolerance, one otherwise, naming the worst excursion
    /// by frame and field.
    /// </summary>
    private void ReportDivergence() => throw new NotImplementedException();
}
