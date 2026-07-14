namespace BattleArena.Multiplayer.Transport;

public readonly record struct NetworkPeerId
{
    public NetworkPeerId(ulong value)
    {
        if (value == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "A network peer ID must be positive.");
        }

        Value = value;
    }

    public ulong Value { get; }
}
