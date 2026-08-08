using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace DLNACast.Core.Dlna;

public sealed class SsdpMulticastSearchClient
{
    private static readonly IPEndPoint MulticastEndpoint = new(IPAddress.Parse("239.255.255.250"), 1900);

    public async Task<IReadOnlyList<Uri>> SearchAsync(
        IEnumerable<string> searchTargets,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(searchTargets);
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));

        var targets = searchTargets
            .Where(target => !string.IsNullOrWhiteSpace(target))
            .Select(target => target.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (targets.Length == 0) return [];

        var localAddresses = GetActiveMulticastIPv4Addresses();
        if (localAddresses.Count == 0) return [];

        var results = await Task.WhenAll(localAddresses.Select(address =>
            SearchFromAddressAsync(address, targets, duration, cancellationToken))).ConfigureAwait(false);

        return results
            .SelectMany(locations => locations)
            .Distinct()
            .OrderBy(location => location.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<IPAddress> GetActiveMulticastIPv4Addresses()
    {
        var addresses = new HashSet<IPAddress>();
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                !networkInterface.SupportsMulticast ||
                networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            try
            {
                foreach (var unicast in networkInterface.GetIPProperties().UnicastAddresses)
                {
                    var address = unicast.Address;
                    if (address.AddressFamily != AddressFamily.InterNetwork ||
                        IPAddress.IsLoopback(address) ||
                        address.GetAddressBytes() is [169, 254, _, _])
                    {
                        continue;
                    }

                    addresses.Add(address);
                }
            }
            catch (NetworkInformationException)
            {
                // An adapter can disappear while its properties are being enumerated.
            }
        }

        return addresses.OrderBy(address => address.ToString(), StringComparer.Ordinal).ToArray();
    }

    private static async Task<IReadOnlyList<Uri>> SearchFromAddressAsync(
        IPAddress localAddress,
        IReadOnlyList<string> searchTargets,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var locations = new HashSet<Uri>();
        try
        {
            using var client = new UdpClient(new IPEndPoint(localAddress, 0));
            client.Client.SetSocketOption(
                SocketOptionLevel.IP,
                SocketOptionName.MulticastInterface,
                localAddress.GetAddressBytes());

            foreach (var searchTarget in searchTargets)
            {
                var request = CreateSearchRequest(searchTarget);
                await client.SendAsync(request, MulticastEndpoint, cancellationToken).ConfigureAwait(false);
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(duration);
            while (!timeout.IsCancellationRequested)
            {
                try
                {
                    var response = await client.ReceiveAsync(timeout.Token).ConfigureAwait(false);
                    if (SsdpResponseParser.TryGetDescriptionLocation(response.Buffer, out var location))
                    {
                        locations.Add(location);
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
        catch (SocketException)
        {
            // One broken or disconnected adapter must not suppress results from healthy adapters.
        }

        cancellationToken.ThrowIfCancellationRequested();
        return locations.ToArray();
    }

    private static byte[] CreateSearchRequest(string searchTarget)
    {
        if (searchTarget.Contains('\r') || searchTarget.Contains('\n'))
        {
            throw new ArgumentException("An SSDP search target cannot contain line breaks.", nameof(searchTarget));
        }

        return Encoding.ASCII.GetBytes(
            "M-SEARCH * HTTP/1.1\r\n" +
            "HOST: 239.255.255.250:1900\r\n" +
            "MAN: \"ssdp:discover\"\r\n" +
            "MX: 2\r\n" +
            $"ST: {searchTarget}\r\n\r\n");
    }
}

public static class SsdpResponseParser
{
    public static bool TryGetDescriptionLocation(ReadOnlySpan<byte> response, out Uri location)
    {
        var text = Encoding.UTF8.GetString(response);
        foreach (var line in text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0 ||
                !line.AsSpan(0, separator).Trim().Equals("LOCATION", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = line[(separator + 1)..].Trim();
            if (Uri.TryCreate(value, UriKind.Absolute, out var parsed) &&
                parsed.Scheme is "http" or "https")
            {
                location = parsed;
                return true;
            }
        }

        location = null!;
        return false;
    }
}
