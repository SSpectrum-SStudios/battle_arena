#nullable enable

using BattleArena.Core.Movement;
using Godot;

namespace BattleArena.GodotNetworking;

public sealed record RemoteMovementPrediction(
    Vector3 Position,
    Vector3 Velocity,
    float ViewYawRadians,
    float ViewPitchRadians,
    MovementRuntimeState State);
