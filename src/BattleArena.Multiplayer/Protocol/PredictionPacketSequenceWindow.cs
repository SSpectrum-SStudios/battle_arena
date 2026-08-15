namespace BattleArena.Multiplayer.Protocol;

// Prediction packets are replaceable hints. A packet older than the newest
// observed packet is stale even when the transport delivers it later.
public sealed class PredictionPacketSequenceWindow
{
    private ulong _highestAcceptedSequence;

    public ulong HighestAcceptedSequence => _highestAcceptedSequence;

    public bool TryAccept(ulong packetSequence)
    {
        if (packetSequence == 0 || packetSequence <= _highestAcceptedSequence)
        {
            return false;
        }

        _highestAcceptedSequence = packetSequence;
        return true;
    }

    public void Reset() => _highestAcceptedSequence = 0;
}
