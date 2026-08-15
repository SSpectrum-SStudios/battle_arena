using BattleArena.Multiplayer.Prediction;
using BattleArena.Protocol.V1;
using Google.Protobuf;

namespace BattleArena.Multiplayer.Tests.Prediction;

public sealed class EnetPredictionRouteDescriptorCodecTests
{
    [Fact]
    public void RoundTripNormalizesAddressAndPreservesPort()
    {
        var descriptor = EnetPredictionRouteDescriptorCodec.Encode(
            new EnetPredictionEndpoint("127.0.0.1", 7782));

        Assert.True(EnetPredictionRouteDescriptorCodec.TryDecode(descriptor, out var endpoint));
        Assert.Equal("127.0.0.1", endpoint.Address);
        Assert.Equal((ushort)7782, endpoint.Port);
        Assert.True(EnetPredictionRouteDescriptorCodec.EndpointMatches(
            endpoint, "::ffff:127.0.0.1", 7782));
    }

    [Fact]
    public void RejectsWrongKindMalformedPayloadAndInvalidPort()
    {
        var wrongKind = EnetPredictionRouteDescriptorCodec.Encode(
            new EnetPredictionEndpoint("127.0.0.1", 7782));
        wrongKind.TransportKind = PredictionTransportKind.Steam;
        var malformed = wrongKind.Clone();
        malformed.TransportKind = PredictionTransportKind.Enet;
        malformed.Payload = ByteString.CopyFrom([0xff]);
        var invalidPort = wrongKind.Clone();
        invalidPort.TransportKind = PredictionTransportKind.Enet;
        invalidPort.Payload = new EnetPredictionRouteDescriptorPayload
        {
            Address = "127.0.0.1",
            Port = 0,
        }.ToByteString();

        Assert.False(EnetPredictionRouteDescriptorCodec.TryDecode(wrongKind, out _));
        Assert.False(EnetPredictionRouteDescriptorCodec.TryDecode(malformed, out _));
        Assert.False(EnetPredictionRouteDescriptorCodec.TryDecode(invalidPort, out _));
    }
}
