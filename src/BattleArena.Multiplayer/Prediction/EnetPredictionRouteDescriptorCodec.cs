using System.Net;
using BattleArena.Protocol.V1;
using Google.Protobuf;

namespace BattleArena.Multiplayer.Prediction;

public static class EnetPredictionRouteDescriptorCodec
{
    public const uint CurrentDescriptorVersion = 1;

    public static PredictionRouteDescriptor Encode(EnetPredictionEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var normalized = NormalizeAddress(endpoint.Address);
        if (endpoint.Port == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(endpoint));
        }

        return new PredictionRouteDescriptor
        {
            DescriptorVersion = CurrentDescriptorVersion,
            TransportKind = PredictionTransportKind.Enet,
            Payload = new EnetPredictionRouteDescriptorPayload
            {
                Address = normalized,
                Port = endpoint.Port,
            }.ToByteString(),
        };
    }

    public static bool TryDecode(
        PredictionRouteDescriptor? descriptor,
        out EnetPredictionEndpoint endpoint)
    {
        endpoint = null!;
        if (descriptor is null ||
            descriptor.DescriptorVersion != CurrentDescriptorVersion ||
            descriptor.TransportKind != PredictionTransportKind.Enet ||
            descriptor.Payload.IsEmpty)
        {
            return false;
        }

        try
        {
            var payload = EnetPredictionRouteDescriptorPayload.Parser.ParseFrom(descriptor.Payload);
            if (payload.Port is 0 or > ushort.MaxValue)
            {
                return false;
            }

            endpoint = new EnetPredictionEndpoint(
                NormalizeAddress(payload.Address),
                checked((ushort)payload.Port));
            return true;
        }
        catch (InvalidProtocolBufferException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static bool EndpointMatches(
        EnetPredictionEndpoint expected,
        string actualAddress,
        int actualPort)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (actualPort != expected.Port)
        {
            return false;
        }

        try
        {
            return IPAddress.Parse(expected.Address).MapToIPv6().Equals(
                IPAddress.Parse(actualAddress).MapToIPv6());
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string NormalizeAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address) ||
            !IPAddress.TryParse(address.Trim(), out var parsed))
        {
            throw new ArgumentException(
                "An ENet prediction route requires a numeric IP address.",
                nameof(address));
        }

        return parsed.ToString();
    }
}
