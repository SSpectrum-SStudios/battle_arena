using System.Buffers.Binary;
using System.Security.Cryptography;
using BattleArena.Multiplayer.Protocol;

namespace BattleArena.Multiplayer.Connection;

public sealed class CryptographicSessionCredentialGenerator : ISessionCredentialGenerator
{
    public ulong CreateSessionId()
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        ulong value;
        do
        {
            RandomNumberGenerator.Fill(bytes);
            value = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        }
        while (value == 0);

        return value;
    }

    public byte[] CreateReconnectToken() => CreateBytes(ProtocolConstants.ReconnectTokenBytes);

    public byte[] CreateClientNonce() => CreateBytes(ProtocolConstants.ClientNonceBytes);

    private static byte[] CreateBytes(int length)
    {
        var bytes = new byte[length];
        RandomNumberGenerator.Fill(bytes);
        return bytes;
    }
}
