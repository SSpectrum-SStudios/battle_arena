using BattleArena.Core.Common;

namespace BattleArena.Core.Movement.Simulation;

/// <summary>
/// Identifies one fixed simulation step on the shared match-frame clock.
/// Movement, actions, and effects consume this same frame and negotiated rate;
/// callers may not substitute delivery sequence numbers or render delta time.
/// </summary>
public readonly record struct SimulationStepContext
{
    private static readonly SimulationDuration OneTick = new(1);

    public SimulationStepContext(
        SimulationInstant frame,
        SimulationRate rate,
        SimulationPassKind pass)
    {
        if (rate.TicksPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rate),
                "A simulation step requires a valid positive fixed rate.");
        }

        if (!Enum.IsDefined(pass) || pass == SimulationPassKind.Unspecified)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pass),
                "A simulation step requires an explicit current or replay pass.");
        }

        Frame = frame;
        Rate = rate;
        Pass = pass;
    }

    public SimulationInstant Frame { get; }

    public SimulationRate Rate { get; }

    public SimulationPassKind Pass { get; }

    public bool IsValid =>
        Rate.TicksPerSecond > 0 &&
        Pass is SimulationPassKind.Current or SimulationPassKind.HistoricalReplay;

    public SimulationDuration StepDuration
    {
        get
        {
            RequireValid();
            return OneTick;
        }
    }

    public decimal StepSeconds
    {
        get
        {
            RequireValid();
            return Rate.SecondsFromDuration(OneTick);
        }
    }

    public static SimulationStepContext Current(
        SimulationInstant frame,
        SimulationRate rate) =>
        new(frame, rate, SimulationPassKind.Current);

    public static SimulationStepContext Replay(
        SimulationInstant frame,
        SimulationRate rate) =>
        new(frame, rate, SimulationPassKind.HistoricalReplay);

    public SimulationDuration ElapsedSince(SimulationInstant earlierFrame)
    {
        RequireValid();
        return Frame - earlierFrame;
    }

    public decimal SecondsFor(SimulationDuration duration)
    {
        RequireValid();
        return Rate.SecondsFromDuration(duration);
    }

    public SimulationStepContext NextFrame()
    {
        RequireValid();
        return new SimulationStepContext(Frame + OneTick, Rate, Pass);
    }

    private void RequireValid()
    {
        if (!IsValid)
        {
            throw new InvalidOperationException(
                "A default or invalid simulation-step context cannot be used.");
        }
    }
}

public enum SimulationPassKind
{
    Unspecified = 0,
    Current = 1,
    HistoricalReplay = 2,
}
