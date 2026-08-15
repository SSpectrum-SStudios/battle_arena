using System.Runtime.CompilerServices;
using BattleArena.Core.Common;

namespace BattleArena.Core.Movement.Simulation;

/// <summary>Single source of truth for bounded owner-command hot-path limits.</summary>
public static class OwnerSimulationLimits
{
    public const int MaximumTransitionReferences = 16;
    public const int MaximumActionReferences = 8;
}

/// <summary>
/// Canonical Q15 movement input. Quantization happens once at the adapter
/// boundary; current simulation, replay, and network projections consume the
/// same integers.
/// </summary>
public readonly record struct MovementAxes
{
    public const short MaximumMagnitude = short.MaxValue;

    public MovementAxes(short xQ15, short zQ15)
    {
        var lengthSquared = ((long)xQ15 * xQ15) + ((long)zQ15 * zQ15);
        if (lengthSquared > (long)MaximumMagnitude * MaximumMagnitude)
        {
            throw new ArgumentOutOfRangeException(
                nameof(xQ15),
                "Canonical movement axes must lie inside the Q15 unit circle.");
        }

        XQ15 = xQ15;
        ZQ15 = zQ15;
    }

    public short XQ15 { get; }
    public short ZQ15 { get; }

    public static MovementAxes FromUnitVector(HorizontalVector input)
    {
        if (!input.IsFinite)
        {
            throw new ArgumentOutOfRangeException(nameof(input));
        }

        var maximumComponent = Math.Max(Math.Abs(input.X), Math.Abs(input.Z));
        if (maximumComponent == 0d)
        {
            return default;
        }

        double x;
        double z;
        if (maximumComponent <= 1d && input.LengthSquared <= 1d)
        {
            x = input.X;
            z = input.Z;
        }
        else
        {
            // Scale before measuring length so even finite values near
            // Double.MaxValue preserve direction instead of overflowing.
            var scaledX = input.X / maximumComponent;
            var scaledZ = input.Z / maximumComponent;
            var scaledLength = Math.Sqrt((scaledX * scaledX) + (scaledZ * scaledZ));
            x = scaledX / scaledLength;
            z = scaledZ / scaledLength;
        }

        var quantizedX = Quantize(x);
        var quantizedZ = Quantize(z);
        var quantizedLengthSquared =
            ((long)quantizedX * quantizedX) + ((long)quantizedZ * quantizedZ);
        if (quantizedLengthSquared > (long)MaximumMagnitude * MaximumMagnitude)
        {
            var correction = MaximumMagnitude / Math.Sqrt(quantizedLengthSquared);
            quantizedX = checked((short)Math.Truncate(quantizedX * correction));
            quantizedZ = checked((short)Math.Truncate(quantizedZ * correction));
        }

        return new MovementAxes(quantizedX, quantizedZ);
    }

    public HorizontalVector ToUnitVector() => new(
        XQ15 / (double)MaximumMagnitude,
        ZQ15 / (double)MaximumMagnitude);

    private static short Quantize(double value) => checked((short)Math.Round(
        value * MaximumMagnitude,
        MidpointRounding.AwayFromZero));
}

/// <summary>Canonical camera orientation used by simulation-facing input.</summary>
public readonly record struct ViewOrientation
{
    public const double MinimumPitchRadians = -Math.PI / 2d;
    public const double MaximumPitchRadians = Math.PI / 2d;
    private const double YawScale = ushort.MaxValue + 1d;
    private const double PitchScale = short.MaxValue;

    public ViewOrientation(ushort yawU16, short pitchI16)
    {
        if (pitchI16 == short.MinValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pitchI16),
                "The symmetric pitch encoding reserves Int16.MinValue.");
        }

        YawU16 = yawU16;
        PitchI16 = pitchI16;
    }

    public ushort YawU16 { get; }
    public short PitchI16 { get; }

    public static ViewOrientation FromRadians(double yawRadians, double pitchRadians)
    {
        if (!double.IsFinite(yawRadians))
        {
            throw new ArgumentOutOfRangeException(nameof(yawRadians));
        }

        if (!double.IsFinite(pitchRadians) ||
            pitchRadians < MinimumPitchRadians ||
            pitchRadians > MaximumPitchRadians)
        {
            throw new ArgumentOutOfRangeException(nameof(pitchRadians));
        }

        var normalizedYaw = yawRadians % Math.Tau;
        if (normalizedYaw < 0d)
        {
            normalizedYaw += Math.Tau;
        }

        var yaw = (uint)Math.Round(
            normalizedYaw / Math.Tau * YawScale,
            MidpointRounding.AwayFromZero) & ushort.MaxValue;
        var pitch = checked((short)Math.Round(
            pitchRadians / MaximumPitchRadians * PitchScale,
            MidpointRounding.AwayFromZero));
        return new ViewOrientation((ushort)yaw, pitch);
    }

    public double YawRadians => YawU16 / YawScale * Math.Tau;
    public double PitchRadians => PitchI16 / PitchScale * MaximumPitchRadians;
}

[Flags]
public enum MovementHeldButtons : byte
{
    None = 0,
    Jump = 1 << 0,
    Sprint = 1 << 1,
    CrouchOrRoll = 1 << 2,
}

public readonly record struct MovementHeldState
{
    private const MovementHeldButtons KnownButtons =
        MovementHeldButtons.Jump |
        MovementHeldButtons.Sprint |
        MovementHeldButtons.CrouchOrRoll;

    public MovementHeldState(MovementHeldButtons buttons)
    {
        if ((buttons & ~KnownButtons) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(buttons));
        }

        Buttons = buttons;
    }

    public MovementHeldButtons Buttons { get; }
    public bool IsHeld(MovementHeldButtons button) =>
        button != MovementHeldButtons.None &&
        (button & ~KnownButtons) == 0 &&
        (Buttons & button) == button;
}

[Flags]
public enum CombatHeldButtons : ushort
{
    None = 0,
    Attack = 1 << 0,
    Block = 1 << 1,
    ActivateWeapon = 1 << 2,
    ActivateHelmet = 1 << 3,
    ActivateChestArmor = 1 << 4,
    ActivateGloves = 1 << 5,
    ActivateBoots = 1 << 6,
    ActivateAmulet = 1 << 7,
    ActivateSelectedFlexibleItem = 1 << 8,
}

/// <summary>
/// Complete held gameplay-input state. Durable action intents describe edges;
/// these bits preserve hold/release grammar for attacks and authored abilities.
/// </summary>
public readonly record struct CombatInputState
{
    private const CombatHeldButtons KnownButtons =
        CombatHeldButtons.Attack |
        CombatHeldButtons.Block |
        CombatHeldButtons.ActivateWeapon |
        CombatHeldButtons.ActivateHelmet |
        CombatHeldButtons.ActivateChestArmor |
        CombatHeldButtons.ActivateGloves |
        CombatHeldButtons.ActivateBoots |
        CombatHeldButtons.ActivateAmulet |
        CombatHeldButtons.ActivateSelectedFlexibleItem;

    public CombatInputState(CombatHeldButtons heldButtons)
    {
        if ((heldButtons & ~KnownButtons) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(heldButtons));
        }

        HeldButtons = heldButtons;
    }

    public CombatHeldButtons HeldButtons { get; }
    public bool IsHeld(CombatHeldButtons button) =>
        button != CombatHeldButtons.None &&
        (button & ~KnownButtons) == 0 &&
        (HeldButtons & button) == button;
}

/// <summary>A positive movement-configuration timeline revision.</summary>
public readonly record struct MovementConfigurationRevision
{
    public MovementConfigurationRevision(ulong value)
    {
        if (value == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        Value = value;
    }

    public ulong Value { get; }
    public bool IsValid => Value != 0;
}

/// <summary>A positive movement-capability timeline revision.</summary>
public readonly record struct MovementCapabilityRevision
{
    public MovementCapabilityRevision(ulong value)
    {
        if (value == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        Value = value;
    }

    public ulong Value { get; }
    public bool IsValid => Value != 0;
}

/// <summary>
/// Fixed-capacity, canonical movement-transition references. Values must be
/// strictly increasing so two equivalent inputs have one representation.
/// </summary>
public readonly struct TransitionReferenceBuffer :
    IEquatable<TransitionReferenceBuffer>
{
    public const int Capacity = OwnerSimulationLimits.MaximumTransitionReferences;
    private readonly TransitionReferenceStorage _storage;

    public TransitionReferenceBuffer(ReadOnlySpan<MovementTransitionId> references)
    {
        if (references.Length > Capacity)
        {
            throw new ArgumentOutOfRangeException(nameof(references));
        }

        TransitionReferenceStorage storage = default;
        ulong previous = 0;
        for (var index = 0; index < references.Length; index++)
        {
            var current = references[index];
            if (!current.IsValid || current.Value <= previous)
            {
                throw new ArgumentException(
                    "Transition references must be valid, unique, and strictly increasing.",
                    nameof(references));
            }

            storage[index] = current.Value;
            previous = current.Value;
        }

        _storage = storage;
        Count = (byte)references.Length;
    }

    public int Count { get; }
    public MovementTransitionId this[int index] => index >= 0 && index < Count
        ? new MovementTransitionId(_storage[index])
        : throw new ArgumentOutOfRangeException(nameof(index));

    public void CopyTo(Span<MovementTransitionId> destination)
    {
        if (destination.Length < Count)
        {
            throw new ArgumentException("The destination is too small.", nameof(destination));
        }

        for (var index = 0; index < Count; index++)
        {
            destination[index] = this[index];
        }
    }

    public bool Equals(TransitionReferenceBuffer other)
    {
        if (Count != other.Count)
        {
            return false;
        }
        for (var index = 0; index < Count; index++)
        {
            if (_storage[index] != other._storage[index])
            {
                return false;
            }
        }
        return true;
    }

    public override bool Equals(object? obj) =>
        obj is TransitionReferenceBuffer other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Count);
        for (var index = 0; index < Count; index++)
        {
            hash.Add(_storage[index]);
        }
        return hash.ToHashCode();
    }

    public static bool operator ==(
        TransitionReferenceBuffer left,
        TransitionReferenceBuffer right) => left.Equals(right);

    public static bool operator !=(
        TransitionReferenceBuffer left,
        TransitionReferenceBuffer right) => !left.Equals(right);

    [InlineArray(Capacity)]
    private struct TransitionReferenceStorage
    {
        private ulong _element0;
    }
}

/// <summary>
/// Fixed-capacity, canonical predicted-action references. The action journal,
/// not this input, owns the referenced action payload.
/// </summary>
public readonly struct ActionReferenceBuffer : IEquatable<ActionReferenceBuffer>
{
    public const int Capacity = OwnerSimulationLimits.MaximumActionReferences;
    private readonly ActionReferenceStorage _storage;

    public ActionReferenceBuffer(ReadOnlySpan<PredictedActionId> references)
    {
        if (references.Length > Capacity)
        {
            throw new ArgumentOutOfRangeException(nameof(references));
        }

        ActionReferenceStorage storage = default;
        ulong previous = 0;
        for (var index = 0; index < references.Length; index++)
        {
            var current = references[index];
            if (!current.IsValid || current.Value <= previous)
            {
                throw new ArgumentException(
                    "Action references must be valid, unique, and strictly increasing.",
                    nameof(references));
            }

            storage[index] = current.Value;
            previous = current.Value;
        }

        _storage = storage;
        Count = (byte)references.Length;
    }

    public int Count { get; }
    public PredictedActionId this[int index] => index >= 0 && index < Count
        ? new PredictedActionId(_storage[index])
        : throw new ArgumentOutOfRangeException(nameof(index));

    public void CopyTo(Span<PredictedActionId> destination)
    {
        if (destination.Length < Count)
        {
            throw new ArgumentException("The destination is too small.", nameof(destination));
        }

        for (var index = 0; index < Count; index++)
        {
            destination[index] = this[index];
        }
    }

    public bool Equals(ActionReferenceBuffer other)
    {
        if (Count != other.Count)
        {
            return false;
        }
        for (var index = 0; index < Count; index++)
        {
            if (_storage[index] != other._storage[index])
            {
                return false;
            }
        }
        return true;
    }

    public override bool Equals(object? obj) =>
        obj is ActionReferenceBuffer other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Count);
        for (var index = 0; index < Count; index++)
        {
            hash.Add(_storage[index]);
        }
        return hash.ToHashCode();
    }

    public static bool operator ==(
        ActionReferenceBuffer left,
        ActionReferenceBuffer right) => left.Equals(right);

    public static bool operator !=(
        ActionReferenceBuffer left,
        ActionReferenceBuffer right) => !left.Equals(right);

    [InlineArray(Capacity)]
    private struct ActionReferenceStorage
    {
        private ulong _element0;
    }
}

/// <summary>
/// Core-owned, immutable per-frame simulation intent. It has no delivery,
/// connection, authority-discontinuity, wall-clock, or arrival-time metadata.
/// </summary>
public readonly record struct CharacterSimulationInput
{
    public CharacterSimulationInput(
        MovementAxes movement,
        ViewOrientation view,
        MovementHeldState movementHeld,
        TransitionReferenceBuffer transitionReferences,
        CombatInputState combatInput,
        ActionReferenceBuffer actionReferences,
        MovementConfigurationRevision movementRevision,
        MovementCapabilityRevision capabilityRevision)
    {
        if (!movementRevision.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(movementRevision));
        }
        if (!capabilityRevision.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(capabilityRevision));
        }

        Movement = movement;
        View = view;
        MovementHeld = movementHeld;
        TransitionReferences = transitionReferences;
        CombatInput = combatInput;
        ActionReferences = actionReferences;
        MovementRevision = movementRevision;
        CapabilityRevision = capabilityRevision;
    }

    public MovementAxes Movement { get; }
    public ViewOrientation View { get; }
    public MovementHeldState MovementHeld { get; }
    public TransitionReferenceBuffer TransitionReferences { get; }
    public CombatInputState CombatInput { get; }
    public ActionReferenceBuffer ActionReferences { get; }
    public MovementConfigurationRevision MovementRevision { get; }
    public MovementCapabilityRevision CapabilityRevision { get; }
    public bool IsValid => MovementRevision.IsValid && CapabilityRevision.IsValid;
}
