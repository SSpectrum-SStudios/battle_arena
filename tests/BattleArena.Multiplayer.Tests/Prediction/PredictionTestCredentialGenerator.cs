using BattleArena.Multiplayer.Prediction;

namespace BattleArena.Multiplayer.Tests.Prediction;

internal sealed class PredictionTestCredentialGenerator : IPredictionRouteCredentialGenerator
{
    private byte _next = 1;

    public byte[] CreateRouteCredential() => Create();
    public byte[] CreateHandshakeNonce() => Create();

    private byte[] Create()
    {
        var result = Enumerable.Repeat(_next, 32).ToArray();
        _next++;
        return result;
    }
}
