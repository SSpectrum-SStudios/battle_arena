using BattleArena.Multiplayer.Prediction;
using BattleArena.Multiplayer.Connection;
using BattleArena.Multiplayer.Transport;
using BattleArena.Protocol.V1;
using Google.Protobuf;

namespace BattleArena.Multiplayer.Tests.Prediction;

public sealed class SteamPredictionRouteDescriptorCodecTests
{
    [Fact]
    public void RoundTripPreservesAuthenticatedSteamIdentityAndVirtualPort()
    {
        var descriptor = SteamPredictionRouteDescriptorCodec.Encode(
            new SteamPredictionEndpoint(76561198000000001, 1));

        Assert.True(SteamPredictionRouteDescriptorCodec.TryDecode(descriptor, out var endpoint));
        Assert.Equal(76561198000000001UL, endpoint.SteamId);
        Assert.Equal(1, endpoint.VirtualPort);
    }

    [Fact]
    public void RejectsWrongKindMalformedPayloadAndMissingIdentity()
    {
        var wrongKind = SteamPredictionRouteDescriptorCodec.Encode(
            new SteamPredictionEndpoint(76561198000000001, 1));
        wrongKind.TransportKind = PredictionTransportKind.Enet;
        var malformed = wrongKind.Clone();
        malformed.TransportKind = PredictionTransportKind.Steam;
        malformed.Payload = ByteString.CopyFrom([0xff]);
        var missingIdentity = wrongKind.Clone();
        missingIdentity.TransportKind = PredictionTransportKind.Steam;
        missingIdentity.Payload = new SteamPredictionRouteDescriptorPayload
        {
            VirtualPort = 1,
        }.ToByteString();

        Assert.False(SteamPredictionRouteDescriptorCodec.TryDecode(wrongKind, out _));
        Assert.False(SteamPredictionRouteDescriptorCodec.TryDecode(malformed, out _));
        Assert.False(SteamPredictionRouteDescriptorCodec.TryDecode(missingIdentity, out _));
    }

    [Fact]
    public void AdvertisementRequiresAuthorityAuthenticatedSteamIdentityAndLobbyMembership()
    {
        const ulong steamId = 76561198000000001;
        var connection = new TransportConnectionId(41);
        var player = new ConnectedPlayer(
            connection,
            new SessionPeerId(2),
            ConnectionGeneration.Initial,
            2,
            2,
            "Peer",
            []);
        var directory = new FakeSteamIdentities(connection, steamId);
        var descriptor = SteamPredictionRouteDescriptorCodec.Encode(
            new SteamPredictionEndpoint(steamId, 1));

        Assert.True(new SteamPredictionRouteAdvertisementVerifier(directory, _ => true)
            .IsAuthorized(player, descriptor));
        Assert.False(new SteamPredictionRouteAdvertisementVerifier(directory, _ => false)
            .IsAuthorized(player, descriptor));
        Assert.False(new SteamPredictionRouteAdvertisementVerifier(directory, _ => true)
            .IsAuthorized(
                player,
                SteamPredictionRouteDescriptorCodec.Encode(
                    new SteamPredictionEndpoint(steamId + 1, 1))));
    }

    private sealed class FakeSteamIdentities(
        TransportConnectionId connection,
        ulong steamId) : IAuthenticatedSteamIdentityDirectory
    {
        public bool TryGetRemoteSteamId(TransportConnectionId connectionId, out ulong found)
        {
            found = steamId;
            return connectionId == connection;
        }
    }
}
