namespace BattleArena.Multiplayer.Connection;

/// <summary>
/// Rejects packets and routes belonging to an earlier connection for the same
/// stable session peer.
/// </summary>
public readonly record struct ConnectionGeneration
{
    public static ConnectionGeneration Initial { get; } = new(1);

    public ConnectionGeneration(uint value)
    {
        if (value == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "A connection generation must be positive.");
        }

        Value = value;
    }

    public uint Value { get; }
}
