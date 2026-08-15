#nullable enable

using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Protocol.V1;
using Godot;
using System.IO;

namespace BattleArena.GodotNetworking;

public static class NetworkMovementStateMapper
{
    public static void WriteTo(CombatantSnapshot snapshot, MovementRuntimeState state)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(state);

        snapshot.LocomotionMode = ToProtocol(state.LocomotionMode);
        snapshot.PostureMode = ToProtocol(state.PostureMode);
        snapshot.MovementActionMode = ToProtocol(state.ActionMode);
        snapshot.MovementModeStartedTick = checked((ulong)state.ModeStartedAt.Tick);
        snapshot.BodyFacingYawRadians = (float)state.FacingYawRadians;
        snapshot.JumpPhase = ToProtocol(state.JumpPhase);
        snapshot.LastGroundedTick = checked((ulong)state.LastGroundedAt.Tick);
        snapshot.JumpCutApplied = state.JumpCutApplied;
        snapshot.RollDirection = new Vector3Value
        {
            X = (float)state.RollDirection.X,
            Z = (float)state.RollDirection.Z,
        };
        snapshot.RollEntrySpeed = (float)state.RollEntrySpeed;
        snapshot.RollBoostDistance = (float)state.RollBoostDistance;
        snapshot.RollDurationTicks = checked((ulong)state.RollDuration.Ticks);
        snapshot.RollAvailableTick = checked((ulong)state.RollAvailableAt.Tick);
        snapshot.LandingRollQueued = state.LandingRollQueued;
        snapshot.MovementProfileRevision = snapshot.MovementProfileRevision == 0
            ? 1UL
            : snapshot.MovementProfileRevision;
        snapshot.MovementCapabilityRevision = snapshot.MovementCapabilityRevision == 0
            ? 1UL
            : snapshot.MovementCapabilityRevision;
        if (state.BufferedJumpUntil is { } buffered)
        {
            snapshot.BufferedJumpUntilTick = checked((ulong)buffered.Tick);
        }
        else
        {
            snapshot.ClearBufferedJumpUntilTick();
        }
    }

    public static MovementRuntimeState FromSnapshot(CombatantSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new MovementRuntimeState(
            new HorizontalVector(snapshot.Velocity?.X ?? 0f, snapshot.Velocity?.Z ?? 0f),
            snapshot.Velocity?.Y ?? 0f,
            snapshot.BodyFacingYawRadians,
            FromProtocol(snapshot.LocomotionMode),
            FromProtocol(snapshot.PostureMode),
            FromProtocol(snapshot.MovementActionMode),
            Instant(snapshot.MovementModeStartedTick),
            FromProtocol(snapshot.JumpPhase),
            Instant(snapshot.LastGroundedTick),
            snapshot.HasBufferedJumpUntilTick
                ? Instant(snapshot.BufferedJumpUntilTick)
                : null,
            snapshot.JumpCutApplied,
            new HorizontalVector(snapshot.RollDirection?.X ?? 0f, snapshot.RollDirection?.Z ?? 0f),
            snapshot.RollEntrySpeed,
            snapshot.RollBoostDistance,
            new SimulationDuration(checked((long)snapshot.RollDurationTicks)),
            Instant(snapshot.RollAvailableTick),
            snapshot.LandingRollQueued);
    }

    public static void WriteTo(
        AuthoritativeMovementState destination,
        MovementRuntimeState state)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(state);

        destination.LocomotionMode = ToProtocol(state.LocomotionMode);
        destination.PostureMode = ToProtocol(state.PostureMode);
        destination.MovementActionMode = ToProtocol(state.ActionMode);
        destination.MovementModeStartedTick = checked((ulong)state.ModeStartedAt.Tick);
        destination.BodyFacingYawRadians = (float)state.FacingYawRadians;
        destination.JumpPhase = ToProtocol(state.JumpPhase);
        destination.LastGroundedTick = checked((ulong)state.LastGroundedAt.Tick);
        destination.JumpCutApplied = state.JumpCutApplied;
        destination.RollDirection = new Vector3Value
        {
            X = (float)state.RollDirection.X,
            Z = (float)state.RollDirection.Z,
        };
        destination.RollEntrySpeed = (float)state.RollEntrySpeed;
        destination.RollBoostDistance = (float)state.RollBoostDistance;
        destination.RollDurationTicks = checked((ulong)state.RollDuration.Ticks);
        destination.RollAvailableTick = checked((ulong)state.RollAvailableAt.Tick);
        destination.LandingRollQueued = state.LandingRollQueued;
        destination.MovementProfileRevision = destination.MovementProfileRevision == 0
            ? 1UL
            : destination.MovementProfileRevision;
        destination.MovementCapabilityRevision = destination.MovementCapabilityRevision == 0
            ? 1UL
            : destination.MovementCapabilityRevision;
        if (state.BufferedJumpUntil is { } buffered)
        {
            destination.BufferedJumpUntilTick = checked((ulong)buffered.Tick);
        }
        else
        {
            destination.ClearBufferedJumpUntilTick();
        }
    }

    public static MovementRuntimeState FromMovementState(
        AuthoritativeMovementState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new MovementRuntimeState(
            new HorizontalVector(state.Velocity?.X ?? 0f, state.Velocity?.Z ?? 0f),
            state.Velocity?.Y ?? 0f,
            state.BodyFacingYawRadians,
            FromProtocol(state.LocomotionMode),
            FromProtocol(state.PostureMode),
            FromProtocol(state.MovementActionMode),
            Instant(state.MovementModeStartedTick),
            FromProtocol(state.JumpPhase),
            Instant(state.LastGroundedTick),
            state.HasBufferedJumpUntilTick
                ? Instant(state.BufferedJumpUntilTick)
                : null,
            state.JumpCutApplied,
            new HorizontalVector(state.RollDirection?.X ?? 0f, state.RollDirection?.Z ?? 0f),
            state.RollEntrySpeed,
            state.RollBoostDistance,
            new SimulationDuration(checked((long)state.RollDurationTicks)),
            Instant(state.RollAvailableTick),
            state.LandingRollQueued);
    }

    private static SimulationInstant Instant(ulong tick) =>
        new(checked((long)tick));

    private static ReplicatedLocomotionMode ToProtocol(LocomotionMode value) => value switch
    {
        LocomotionMode.Grounded => ReplicatedLocomotionMode.Grounded,
        LocomotionMode.Airborne => ReplicatedLocomotionMode.Airborne,
        LocomotionMode.Rolling => ReplicatedLocomotionMode.Rolling,
        LocomotionMode.Mantling => ReplicatedLocomotionMode.Mantling,
        LocomotionMode.Disabled => ReplicatedLocomotionMode.Disabled,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    private static LocomotionMode FromProtocol(ReplicatedLocomotionMode value) => value switch
    {
        ReplicatedLocomotionMode.Grounded => LocomotionMode.Grounded,
        ReplicatedLocomotionMode.Airborne => LocomotionMode.Airborne,
        ReplicatedLocomotionMode.Rolling => LocomotionMode.Rolling,
        ReplicatedLocomotionMode.Mantling => LocomotionMode.Mantling,
        ReplicatedLocomotionMode.Disabled => LocomotionMode.Disabled,
        _ => throw new InvalidDataException($"Unsupported replicated locomotion mode '{value}'."),
    };

    private static ReplicatedPostureMode ToProtocol(PostureMode value) => value switch
    {
        PostureMode.Standing => ReplicatedPostureMode.Standing,
        PostureMode.Crouched => ReplicatedPostureMode.Crouched,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    private static PostureMode FromProtocol(ReplicatedPostureMode value) => value switch
    {
        ReplicatedPostureMode.Standing => PostureMode.Standing,
        ReplicatedPostureMode.Crouched => PostureMode.Crouched,
        _ => throw new InvalidDataException($"Unsupported replicated posture mode '{value}'."),
    };

    private static ReplicatedMovementActionMode ToProtocol(MovementActionMode value) => value switch
    {
        MovementActionMode.Ready => ReplicatedMovementActionMode.Ready,
        MovementActionMode.Attacking => ReplicatedMovementActionMode.Attacking,
        MovementActionMode.Blocking => ReplicatedMovementActionMode.Blocking,
        MovementActionMode.UsingItem => ReplicatedMovementActionMode.UsingItem,
        MovementActionMode.Stunned => ReplicatedMovementActionMode.Stunned,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    private static MovementActionMode FromProtocol(ReplicatedMovementActionMode value) => value switch
    {
        ReplicatedMovementActionMode.Ready => MovementActionMode.Ready,
        ReplicatedMovementActionMode.Attacking => MovementActionMode.Attacking,
        ReplicatedMovementActionMode.Blocking => MovementActionMode.Blocking,
        ReplicatedMovementActionMode.UsingItem => MovementActionMode.UsingItem,
        ReplicatedMovementActionMode.Stunned => MovementActionMode.Stunned,
        _ => throw new InvalidDataException($"Unsupported replicated movement action mode '{value}'."),
    };

    private static ReplicatedJumpPhase ToProtocol(JumpPhase value) => value switch
    {
        JumpPhase.None => ReplicatedJumpPhase.None,
        JumpPhase.Rising => ReplicatedJumpPhase.Rising,
        JumpPhase.Apex => ReplicatedJumpPhase.Apex,
        JumpPhase.Falling => ReplicatedJumpPhase.Falling,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    private static JumpPhase FromProtocol(ReplicatedJumpPhase value) => value switch
    {
        ReplicatedJumpPhase.None => JumpPhase.None,
        ReplicatedJumpPhase.Rising => JumpPhase.Rising,
        ReplicatedJumpPhase.Apex => JumpPhase.Apex,
        ReplicatedJumpPhase.Falling => JumpPhase.Falling,
        _ => throw new InvalidDataException($"Unsupported replicated jump phase '{value}'."),
    };
}
