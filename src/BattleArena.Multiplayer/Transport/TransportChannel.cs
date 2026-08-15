namespace BattleArena.Multiplayer.Transport;

public enum TransportChannel : byte
{
    Input = 0,
    Snapshot = 1,
    StructuralEvent = 2,
    PresentationEvent = 3,
    Connection = 4,
    Action = 5,
    Movement = 6,
    Timing = 7,
}

/// <summary>
/// Defines transport-wide channel metadata once. Adapters consume this catalog
/// rather than duplicating assumptions about the current highest channel.
/// </summary>
public static class TransportChannels
{
    public static int RequiredChannelCount { get; } =
        Enum.GetValues<TransportChannel>()
            .Select(channel => (int)channel)
            .Max() + 1;

    public static bool IsDefined(TransportChannel channel) =>
        Enum.IsDefined(channel);
}
