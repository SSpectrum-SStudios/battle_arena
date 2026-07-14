namespace BattleArena.Multiplayer.Connection;

public interface ISessionCredentialGenerator
{
    ulong CreateSessionId();

    byte[] CreateReconnectToken();

    byte[] CreateClientNonce();
}
