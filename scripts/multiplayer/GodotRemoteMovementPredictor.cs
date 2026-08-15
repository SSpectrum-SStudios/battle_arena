#nullable enable

using BattleArena.Core.Common;
using BattleArena.Core.Movement;
using BattleArena.Multiplayer.Replication;
using BattleArena.Protocol.V1;
using Godot;

namespace BattleArena.GodotNetworking;

/// <summary>
/// Replays authority-accepted input through the shared fixed-tick movement
/// rules and returns an inert render sample. This service never owns or mutates
/// a Godot body: fixed-physics orchestration commits canonical collision poses,
/// while render orchestration may apply this result only to visual nodes.
/// </summary>
public sealed class GodotRemoteMovementPredictor : IRemoteMovementPredictor
{
    private readonly SimulationRate _simulationRate;
    private readonly GroundLocomotionSimulator _ground = new();
    private readonly AirborneLocomotionSimulator _air = new();
    private readonly JumpFallSimulator _jump = new();
    private readonly CrouchRollSimulator _crouchRoll = new();

    public GodotRemoteMovementPredictor(SimulationRate simulationRate)
    {
        _simulationRate = simulationRate;
    }

    public RemoteMovementPrediction Predict(
        RemoteMovementFrame<AuthoritativeMovementState> baseline,
        IReadOnlyList<AcceptedMovementCommand> commands,
        double targetAuthorityTick,
        MovementAttributeSnapshot attributes)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(attributes);

        var source = baseline.State;
        var state = NetworkMovementStateMapper.FromMovementState(source);
        var position = ToGodot(source.Position);
        var viewYaw = source.ViewYawRadians;
        var viewPitch = source.ViewPitchRadians;
        var secondsPerTick = 1f / _simulationRate.TicksPerSecond;
        var commandsByTick = commands
            .Where(command => command.LifeId == baseline.LifeId)
            .GroupBy(command => command.AppliedAuthorityTick)
            .ToDictionary(group => group.Key, group => group.Last().Command);
        MovementCommand? lastCommand = null;
        ulong lastAcceptedAuthorityTick = 0;
        var lastWholeTick = baseline.AuthorityTick;
        var finalWholeTick = checked((ulong)Math.Floor(targetAuthorityTick));

        for (var tick = baseline.AuthorityTick + 1; tick <= finalWholeTick; tick++)
        {
            if (commandsByTick.TryGetValue(tick, out var accepted))
            {
                lastCommand = accepted;
                lastAcceptedAuthorityTick = tick;
            }
            else if (lastCommand is null)
            {
                // Do not guess an input direction before the first accepted
                // command. Constant-velocity motion is safer for this gap.
                position += VelocityOf(state) * secondsPerTick;
                lastWholeTick = tick;
                continue;
            }

            var command = lastCommand!.Value;
            if (!commandsByTick.ContainsKey(tick))
            {
                var elapsedTicks = tick - lastAcceptedAuthorityTick;
                command = WithoutEdgesAt(
                    command,
                    checked((ulong)command.ClientTick.Tick + elapsedTicks));
            }

            viewYaw = (float)command.ViewYawRadians;
            viewPitch = (float)command.ViewPitchRadians;
            var grounded = state.LocomotionMode == LocomotionMode.Grounded;
            state = _crouchRoll.Simulate(
                state,
                command,
                grounded,
                canStand: true,
                attributes.CrouchRoll,
                attributes.Jump,
                attributes.Ground,
                _simulationRate);

            if (state.LocomotionMode != LocomotionMode.Rolling)
            {
                state = _jump.Simulate(
                    state,
                    command,
                    grounded,
                    attributes.Jump,
                    _simulationRate);
                state = state.LocomotionMode == LocomotionMode.Grounded
                    ? _ground.Simulate(state, command, attributes.Ground, _simulationRate)
                    : _air.Simulate(state, command, attributes.Air, _simulationRate);
            }

            position += VelocityOf(state) * secondsPerTick;
            lastWholeTick = tick;
        }

        var fraction = (float)Math.Clamp(targetAuthorityTick - lastWholeTick, 0d, 1d);
        if (fraction > 0f)
        {
            position += VelocityOf(state) * (secondsPerTick * fraction);
        }

        return new RemoteMovementPrediction(
            position,
            VelocityOf(state),
            viewYaw,
            viewPitch,
            state);
    }

    private static MovementCommand WithoutEdgesAt(MovementCommand command, ulong clientTick) =>
        new(
            command.Sequence,
            new SimulationInstant(checked((long)clientTick)),
            command.Movement,
            command.ViewYawRadians,
            command.ViewPitchRadians,
            command.HeldButtons,
            MovementButtons.None,
            MovementButtons.None);

    private static Vector3 VelocityOf(MovementRuntimeState state) => new(
        (float)state.HorizontalVelocity.X,
        (float)state.VerticalVelocity,
        (float)state.HorizontalVelocity.Z);

    private static Vector3 ToGodot(Vector3Value? value) => value is null
        ? Vector3.Zero
        : new Vector3(value.X, value.Y, value.Z);
}
