namespace BattleArena.Multiplayer.Transport;

public enum TransportChannel : byte
{
    Input = 0,
    Snapshot = 1,
    StructuralEvent = 2,
    PresentationEvent = 3,
    Connection = 4,
}
