using Godot;

namespace BattleArena.Movement;

/// <summary>
/// Measures the explicit motor's real per-frame query cost in-engine.
/// </summary>
/// <remarks>
/// <para>
/// P01-11 measured a single query at about 10.8 microseconds, but the motor
/// issues several per frame — overlap recovery, the initial sweep, a re-sweep
/// per slide iteration, a ground probe, and up to three more when a step is
/// solved. The real per-frame budget is therefore a multiple that has never been
/// measured, and replay multiplies it again by history depth.
/// </para>
/// <para>
/// This is what turns the replay-depth cap from a guess into a number. Without
/// it, the cap is chosen by intuition and discovered to be wrong during
/// acceptance, with far more built on top.
/// </para>
/// </remarks>
public sealed partial class MovementMotorCostProbe : Node3D
{
    public override void _Ready() => throw new NotImplementedException();

    /// <summary>
    /// Measures queries and microseconds per frame at replay depths of 1, 8, and
    /// 32, over both typical and deliberately hostile geometry.
    /// </summary>
    private void Measure() => throw new NotImplementedException();

    /// <summary>
    /// Exits zero when every measured frame is inside the authored budget, one
    /// otherwise, naming the depth and geometry that exceeded it.
    /// </summary>
    /// <remarks>
    /// A hard failure rather than a warning: an unaffordable replay is a design
    /// problem, and the cap that bounds it has to be derived from this number.
    /// </remarks>
    private void EnforceBudget() => throw new NotImplementedException();
}
