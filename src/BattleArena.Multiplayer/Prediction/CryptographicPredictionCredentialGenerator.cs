using System.Security.Cryptography;
using BattleArena.Multiplayer.Protocol;

namespace BattleArena.Multiplayer.Prediction;

public sealed class CryptographicPredictionCredentialGenerator : IPredictionRouteCredentialGenerator
{
    public byte[] CreateRouteCredential() => Create(ProtocolConstants.PredictionRouteCredentialBytes);
    public byte[] CreateHandshakeNonce() => Create(ProtocolConstants.PredictionHandshakeNonceBytes);

    private static byte[] Create(int length)
    {
        var result = new byte[length];
        RandomNumberGenerator.Fill(result);
        return result;
    }
}
