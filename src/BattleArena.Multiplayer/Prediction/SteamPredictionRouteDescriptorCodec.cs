using BattleArena.Protocol.V1;
using Google.Protobuf;

namespace BattleArena.Multiplayer.Prediction;

public static class SteamPredictionRouteDescriptorCodec
{
    public const uint CurrentDescriptorVersion = 1;
    public const int MaximumVirtualPort = 65_535;

    public static PredictionRouteDescriptor Encode(SteamPredictionEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (endpoint.SteamId == 0 || endpoint.VirtualPort is < 0 or > MaximumVirtualPort)
        {
            throw new ArgumentOutOfRangeException(nameof(endpoint));
        }

        return new PredictionRouteDescriptor
        {
            DescriptorVersion = CurrentDescriptorVersion,
            TransportKind = PredictionTransportKind.Steam,
            Payload = new SteamPredictionRouteDescriptorPayload
            {
                SteamId = endpoint.SteamId,
                VirtualPort = checked((uint)endpoint.VirtualPort),
            }.ToByteString(),
        };
    }

    public static bool TryDecode(
        PredictionRouteDescriptor? descriptor,
        out SteamPredictionEndpoint endpoint)
    {
        endpoint = null!;
        if (descriptor is null ||
            descriptor.DescriptorVersion != CurrentDescriptorVersion ||
            descriptor.TransportKind != PredictionTransportKind.Steam ||
            descriptor.Payload.IsEmpty)
        {
            return false;
        }

        try
        {
            var payload = SteamPredictionRouteDescriptorPayload.Parser.ParseFrom(descriptor.Payload);
            if (payload.SteamId == 0 || payload.VirtualPort > MaximumVirtualPort)
            {
                return false;
            }

            endpoint = new SteamPredictionEndpoint(
                payload.SteamId,
                checked((int)payload.VirtualPort));
            return true;
        }
        catch (InvalidProtocolBufferException)
        {
            return false;
        }
    }
}
