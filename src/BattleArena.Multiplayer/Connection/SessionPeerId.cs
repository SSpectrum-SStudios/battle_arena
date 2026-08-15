namespace BattleArena.Multiplayer.Connection;

/// <summary>
/// Authority-assigned identity for a participant within one match session.
/// Unlike a transport connection ID, it remains stable across route replacement.
/// </summary>
public readonly record struct SessionPeerId
{
    public SessionPeerId(ulong value)
    {
        if (value == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "A session peer ID must be positive.");
        }

        Value = value;
    }

    public ulong Value { get; }
}
