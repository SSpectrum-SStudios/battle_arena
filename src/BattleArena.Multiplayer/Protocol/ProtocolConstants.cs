namespace BattleArena.Multiplayer.Protocol;

public static class ProtocolConstants
{
    public const uint CurrentVersion = 1;
    public const int MaxPacketBytes = 64 * 1024;
    public const int MaxInputFramesPerBatch = 8;
    public const int MaxCombatants = 256;
    public const int MaxActiveEffects = 4096;
    public const int MaxWorldObjects = 4096;
    public const int MaxEventsPerBatch = 256;
    public const int MaxDisplayNameCharacters = 32;
    public const int ClientNonceBytes = 16;
    public const int ReconnectTokenBytes = 32;
    public const uint EquipmentSlotCount = 6;
    public const uint KnownInputButtonMask = 0x3ff;
}
