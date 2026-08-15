namespace BattleArena.Multiplayer.Transport;

/// <summary>
/// Identifies one currently open adapter connection. This value is ephemeral,
/// transport-specific, and must never be used as a player or session identity.
/// </summary>
public readonly record struct TransportConnectionId
{
    public TransportConnectionId(ulong value)
    {
        if (value == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "A transport connection ID must be positive.");
        }

        Value = value;
    }

    public ulong Value { get; }
}
