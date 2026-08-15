using BattleArena.Core.Common;
using BattleArena.Core.Movement;

namespace BattleArena.Multiplayer.Presentation;

public enum CharacterPresentationPass
{
    Unspecified = 0,
    CommittedFrame = 1,
    HistoricalReplay = 2,
}

public enum CharacterPresentationUpdateKind
{
    Unspecified = 0,
    HoldEliminated = 1,
    HoldActiveAction = 2,
    HoldRollContinuation = 3,
    Locomotion = 4,
    CrouchIdle = 5,
    CrouchMove = 6,
    RollStart = 7,
}

public enum CharacterPresentationDecision
{
    Unspecified = 0,
    SuppressedHistoricalReplay = 1,
    Published = 2,
}

/// <summary>
/// Immutable, presentation-relevant projection of one final simulation state.
/// It contains no simulation service or mutable body reference.
/// </summary>
public readonly record struct CharacterPresentationSample
{
    private readonly bool _initialized;

    public CharacterPresentationSample(
        SimulationInstant simulationFrame,
        LocomotionMode locomotionMode,
        PostureMode postureMode,
        float normalizedLocalRightVelocity,
        float normalizedLocalForwardVelocity,
        float horizontalSpeed,
        SimulationDuration rollDuration,
        bool eliminated,
        bool activeAction)
    {
        if (!Enum.IsDefined(locomotionMode))
        {
            throw new ArgumentOutOfRangeException(nameof(locomotionMode));
        }

        if (!Enum.IsDefined(postureMode))
        {
            throw new ArgumentOutOfRangeException(nameof(postureMode));
        }

        RequireFinite(normalizedLocalRightVelocity, nameof(normalizedLocalRightVelocity));
        RequireFinite(normalizedLocalForwardVelocity, nameof(normalizedLocalForwardVelocity));
        RequireFinite(horizontalSpeed, nameof(horizontalSpeed));
        if (horizontalSpeed < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(horizontalSpeed));
        }

        if (locomotionMode == LocomotionMode.Rolling && rollDuration == SimulationDuration.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rollDuration),
                "A rolling presentation sample requires a positive authored duration.");
        }

        SimulationFrame = simulationFrame;
        LocomotionMode = locomotionMode;
        PostureMode = postureMode;
        NormalizedLocalRightVelocity = normalizedLocalRightVelocity;
        NormalizedLocalForwardVelocity = normalizedLocalForwardVelocity;
        HorizontalSpeed = horizontalSpeed;
        RollDuration = rollDuration;
        Eliminated = eliminated;
        ActiveAction = activeAction;
        _initialized = true;
    }

    public SimulationInstant SimulationFrame { get; }
    public LocomotionMode LocomotionMode { get; }
    public PostureMode PostureMode { get; }
    public float NormalizedLocalRightVelocity { get; }
    public float NormalizedLocalForwardVelocity { get; }
    public float HorizontalSpeed { get; }
    public SimulationDuration RollDuration { get; }
    public bool Eliminated { get; }
    public bool ActiveAction { get; }
    public bool IsValid =>
        _initialized &&
        Enum.IsDefined(LocomotionMode) &&
        Enum.IsDefined(PostureMode) &&
        float.IsFinite(NormalizedLocalRightVelocity) &&
        float.IsFinite(NormalizedLocalForwardVelocity) &&
        float.IsFinite(HorizontalSpeed) &&
        HorizontalSpeed >= 0f &&
        (LocomotionMode != LocomotionMode.Rolling ||
         RollDuration != SimulationDuration.Zero);

    private static void RequireFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

/// <summary>
/// One semantic operation published to the Godot-facing presentation adapter.
/// Default and unspecified updates are never published.
/// </summary>
public readonly record struct CharacterPresentationUpdate
{
    private CharacterPresentationUpdate(
        CharacterPresentationUpdateKind kind,
        float normalizedLocalRightVelocity,
        float normalizedLocalForwardVelocity,
        bool airborne,
        double deltaSeconds,
        double rollDurationSeconds)
    {
        Kind = kind;
        NormalizedLocalRightVelocity = normalizedLocalRightVelocity;
        NormalizedLocalForwardVelocity = normalizedLocalForwardVelocity;
        Airborne = airborne;
        DeltaSeconds = deltaSeconds;
        RollDurationSeconds = rollDurationSeconds;
    }

    public CharacterPresentationUpdateKind Kind { get; }
    public float NormalizedLocalRightVelocity { get; }
    public float NormalizedLocalForwardVelocity { get; }
    public bool Airborne { get; }
    public double DeltaSeconds { get; }
    public double RollDurationSeconds { get; }
    public bool IsValid => Kind switch
    {
        CharacterPresentationUpdateKind.HoldEliminated or
        CharacterPresentationUpdateKind.HoldActiveAction or
        CharacterPresentationUpdateKind.HoldRollContinuation or
        CharacterPresentationUpdateKind.CrouchIdle or
        CharacterPresentationUpdateKind.CrouchMove => true,
        CharacterPresentationUpdateKind.Locomotion =>
            float.IsFinite(NormalizedLocalRightVelocity) &&
            float.IsFinite(NormalizedLocalForwardVelocity) &&
            double.IsFinite(DeltaSeconds) && DeltaSeconds > 0d,
        CharacterPresentationUpdateKind.RollStart =>
            double.IsFinite(RollDurationSeconds) && RollDurationSeconds > 0d,
        _ => false,
    };

    public static CharacterPresentationUpdate Hold(CharacterPresentationUpdateKind kind)
    {
        if (kind is not (
                CharacterPresentationUpdateKind.HoldEliminated or
                CharacterPresentationUpdateKind.HoldActiveAction or
                CharacterPresentationUpdateKind.HoldRollContinuation))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        return new CharacterPresentationUpdate(kind, 0f, 0f, false, 0d, 0d);
    }

    public static CharacterPresentationUpdate Locomotion(
        float normalizedLocalRightVelocity,
        float normalizedLocalForwardVelocity,
        bool airborne,
        double deltaSeconds) => new(
            CharacterPresentationUpdateKind.Locomotion,
            normalizedLocalRightVelocity,
            normalizedLocalForwardVelocity,
            airborne,
            deltaSeconds,
            0d);

    public static CharacterPresentationUpdate Crouch(bool moving) => new(
        moving
            ? CharacterPresentationUpdateKind.CrouchMove
            : CharacterPresentationUpdateKind.CrouchIdle,
        0f,
        0f,
        false,
        0d,
        0d);

    public static CharacterPresentationUpdate RollStart(double durationSeconds) => new(
        CharacterPresentationUpdateKind.RollStart,
        0f,
        0f,
        false,
        0d,
        durationSeconds);
}

/// <summary>
/// Port implemented by the visual rig adapter. One committed sample results in
/// exactly one call; historical replay results in none.
/// </summary>
public interface ICharacterPresentationView
{
    void Apply(in CharacterPresentationUpdate update);
}

/// <summary>
/// Converts final simulation samples into semantic visual updates. It never owns
/// or mutates simulation state and historical replay is a strict no-op.
/// </summary>
public sealed class CharacterPresentationController
{
    private readonly ICharacterPresentationView _view;
    private readonly SimulationRate _simulationRate;
    private readonly float _crouchMovingThreshold;
    private bool _wasRolling;

    public CharacterPresentationController(
        ICharacterPresentationView view,
        SimulationRate simulationRate,
        float crouchMovingThreshold = 0.1f)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        if (simulationRate.TicksPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(simulationRate));
        }

        if (!float.IsFinite(crouchMovingThreshold) || crouchMovingThreshold < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(crouchMovingThreshold));
        }

        _simulationRate = simulationRate;
        _crouchMovingThreshold = crouchMovingThreshold;
    }

    public CharacterPresentationDecision Present(
        in CharacterPresentationSample sample,
        CharacterPresentationPass pass,
        double deltaSeconds)
    {
        if (pass == CharacterPresentationPass.HistoricalReplay)
        {
            return CharacterPresentationDecision.SuppressedHistoricalReplay;
        }

        if (pass != CharacterPresentationPass.CommittedFrame || !Enum.IsDefined(pass))
        {
            throw new ArgumentOutOfRangeException(nameof(pass));
        }

        if (!double.IsFinite(deltaSeconds) || deltaSeconds <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        }

        if (!sample.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(sample));
        }

        var update = SelectUpdate(sample, deltaSeconds);
        if (!update.IsValid)
        {
            throw new InvalidOperationException("Presentation controller produced an invalid update.");
        }

        _view.Apply(in update);
        return CharacterPresentationDecision.Published;
    }

    public void Reset() => _wasRolling = false;

    private CharacterPresentationUpdate SelectUpdate(
        in CharacterPresentationSample sample,
        double deltaSeconds)
    {
        if (sample.Eliminated)
        {
            _wasRolling = false;
            return CharacterPresentationUpdate.Hold(
                CharacterPresentationUpdateKind.HoldEliminated);
        }

        if (sample.ActiveAction)
        {
            return CharacterPresentationUpdate.Hold(
                CharacterPresentationUpdateKind.HoldActiveAction);
        }

        if (sample.LocomotionMode == LocomotionMode.Rolling)
        {
            if (_wasRolling)
            {
                return CharacterPresentationUpdate.Hold(
                    CharacterPresentationUpdateKind.HoldRollContinuation);
            }

            _wasRolling = true;
            var durationSeconds = (double)_simulationRate.SecondsFromDuration(
                sample.RollDuration);
            return CharacterPresentationUpdate.RollStart(durationSeconds);
        }

        _wasRolling = false;
        if (sample.PostureMode == PostureMode.Crouched)
        {
            return CharacterPresentationUpdate.Crouch(
                sample.HorizontalSpeed > _crouchMovingThreshold);
        }

        return CharacterPresentationUpdate.Locomotion(
            sample.NormalizedLocalRightVelocity,
            sample.NormalizedLocalForwardVelocity,
            sample.LocomotionMode == LocomotionMode.Airborne,
            deltaSeconds);
    }
}
