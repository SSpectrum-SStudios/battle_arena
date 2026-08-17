namespace BattleArena.Multiplayer.OwnerPrediction;

/// <summary>How a transport-overhead figure was arrived at.</summary>
/// <remarks>
/// Recorded rather than assumed because the two are not interchangeable. A
/// conservative estimate is safe to budget against and unsafe to quote as fact,
/// and the difference matters most right before a protocol freeze — which is
/// exactly when someone will want to spend the margin.
/// </remarks>
public enum PredictionTransportOverheadBasis
{
    MeasuredCapture,
    ConservativeEngineeringEstimate,
}

/// <summary>
/// Per-datagram overhead a transport adds around an encoded payload.
/// </summary>
/// <remarks>
/// <para>
/// Survives the deletion of `PredictionPacketBudgetProbe` in P06-A5 because it is
/// not part of the model that probe was. The protobuf codec tests use it to check
/// that a <em>really encoded</em> owner command still fits an MTU-safe datagram
/// once framing and IP/UDP headers are added — a measurement against real bytes,
/// which is the kind of evidence that revision kept.
/// </para>
/// <para>
/// The figures below are conservative engineering estimates, not captures. They
/// are deliberately labelled as such: they should be replaced with project packet
/// captures before the protocol is frozen, and
/// <see cref="PredictionTransportOverheadBasis"/> exists so nobody has to guess
/// which kind of number they are reading.
/// </para>
/// </remarks>
public sealed record PredictionTransportOverhead
{
    public static PredictionTransportOverhead EnetIpv6Unreliable { get; } = new(
        "ENet/IPv6 estimate",
        authenticationBytes: 0,
        transportFramingBytes: 16,
        ipUdpBytes: 48,
        PredictionTransportOverheadBasis.ConservativeEngineeringEstimate,
        "ENet 1.3.x framing reserve plus exact IPv6-without-extensions/UDP headers; " +
        "replace with project packet captures before protocol freeze.");

    public static PredictionTransportOverhead SteamIpv6Unreliable { get; } = new(
        "Steam Datagram Relay/IPv6 estimate",
        authenticationBytes: 16,
        transportFramingBytes: 48,
        ipUdpBytes: 48,
        PredictionTransportOverheadBasis.ConservativeEngineeringEstimate,
        "Steamworks SDK 1.62 encrypted-datagram engineering reserve plus exact " +
        "IPv6-without-extensions/UDP headers; opaque SDR framing requires project " +
        "packet-capture calibration before protocol freeze.");

    public PredictionTransportOverhead(
        string name,
        int authenticationBytes,
        int transportFramingBytes,
        int ipUdpBytes,
        PredictionTransportOverheadBasis basis,
        string provenance)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A transport-overhead profile needs a name.", nameof(name));
        }

        if (!Enum.IsDefined(basis))
        {
            throw new ArgumentOutOfRangeException(nameof(basis));
        }

        if (string.IsNullOrWhiteSpace(provenance))
        {
            throw new ArgumentException("Overhead provenance is required.", nameof(provenance));
        }

        AuthenticationBytes = NonNegative(authenticationBytes, nameof(authenticationBytes));
        TransportFramingBytes = NonNegative(transportFramingBytes, nameof(transportFramingBytes));
        IpUdpBytes = NonNegative(ipUdpBytes, nameof(ipUdpBytes));
        Name = name;
        Basis = basis;
        Provenance = provenance;
    }

    public string Name { get; }
    public int AuthenticationBytes { get; }
    public int TransportFramingBytes { get; }
    public int IpUdpBytes { get; }
    public PredictionTransportOverheadBasis Basis { get; }
    public string Provenance { get; }

    /// <summary>Total bytes added around a payload on the wire.</summary>
    public int TotalBytes => AuthenticationBytes + TransportFramingBytes + IpUdpBytes;

    private static int NonNegative(int value, string parameterName) => value < 0
        ? throw new ArgumentOutOfRangeException(parameterName)
        : value;
}
