using BattleArena.Multiplayer.Protocol;
using BattleArena.Protocol.V1;
using Google.Protobuf;

namespace BattleArena.Multiplayer.Tests.Protocol;

public sealed class ProtobufDiagnosticFormatterTests
{
    [Fact]
    public void ReconnectCredentialIsRedactedFromDiagnosticJson()
    {
        var envelope = new PacketEnvelope
        {
            ProtocolVersion = ProtocolConstants.CurrentVersion,
            SessionId = 10,
            Sequence = 1,
            ReconnectRequest = new ReconnectRequest
            {
                SessionId = 10,
                PlayerId = 20,
                ReconnectToken = ByteString.CopyFromUtf8("never-log-this-token"),
            },
        };

        var json = new ProtobufDiagnosticFormatter().Format(envelope);

        Assert.DoesNotContain("never-log-this-token", json, StringComparison.Ordinal);
        Assert.DoesNotContain("reconnectToken", json, StringComparison.Ordinal);
        Assert.Equal("never-log-this-token", envelope.ReconnectRequest.ReconnectToken.ToStringUtf8());
    }
}
