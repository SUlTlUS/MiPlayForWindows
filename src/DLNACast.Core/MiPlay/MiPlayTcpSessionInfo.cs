using System.Net;
using System.Net.Sockets;

namespace DLNACast.Core.MiPlay;

/// <summary>
/// IPv4 endpoint material captured by the native TCP session before SafetyInfo.
/// Xiaomi Interconnectivity Services 18.0.0.3 stores the local endpoint first,
/// but constructs its type-1 SafetyKeyDeal input as peer endpoint then local endpoint.
/// </summary>
public sealed record MiPlayTcpSessionInfo
{
    public MiPlayTcpSessionInfo(
        IPAddress localAddress,
        ushort localPort,
        IPAddress peerAddress,
        ushort peerPort)
    {
        ValidateIpv4Endpoint(localAddress, localPort, nameof(localAddress), nameof(localPort));
        ValidateIpv4Endpoint(peerAddress, peerPort, nameof(peerAddress), nameof(peerPort));

        LocalAddress = localAddress;
        LocalPort = localPort;
        PeerAddress = peerAddress;
        PeerPort = peerPort;
    }

    public IPAddress LocalAddress { get; }
    public ushort LocalPort { get; }
    public IPAddress PeerAddress { get; }
    public ushort PeerPort { get; }

    /// <summary>
    /// Reproduces the endpoint ordering used by CmdSource::onSessionConnect:
    /// peer IPv4/port, followed by local IPv4/port.
    /// </summary>
    public string DeriveType1SafetyKey() => MiPlaySafetyKeyDerivation.DeriveType1(
        PeerAddress.ToString(),
        PeerPort,
        LocalAddress.ToString(),
        LocalPort);

    /// <summary>
    /// Reproduces the type-1 key that the connected peer derives when it is the
    /// CmdSource side of the same TCP session. This is needed by a bounded test
    /// receiver because the native source always hashes its peer endpoint before
    /// its local endpoint.
    /// </summary>
    public string DeriveType1SafetyKeyForPeerSourceRole() => MiPlaySafetyKeyDerivation.DeriveType1(
        LocalAddress.ToString(),
        LocalPort,
        PeerAddress.ToString(),
        PeerPort);

    private static void ValidateIpv4Endpoint(
        IPAddress address,
        ushort port,
        string addressParameterName,
        string portParameterName)
    {
        ArgumentNullException.ThrowIfNull(address, addressParameterName);
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ArgumentException("MiPlay's verified TCP SessionInfo path uses IPv4 sockaddr_in endpoints.", addressParameterName);
        }

        if (port == 0)
        {
            throw new ArgumentOutOfRangeException(portParameterName, "A TCP session endpoint port must be non-zero.");
        }
    }
}
