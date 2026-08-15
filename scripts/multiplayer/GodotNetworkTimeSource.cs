#nullable enable

using BattleArena.Multiplayer.Timing;
using Godot;

namespace BattleArena.GodotNetworking;

public sealed class GodotNetworkTimeSource : INetworkTimeSource
{
    public ulong GetTimestampMicroseconds() => Time.GetTicksUsec();
}
