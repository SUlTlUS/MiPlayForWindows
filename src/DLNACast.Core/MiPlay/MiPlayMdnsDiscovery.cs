using System.Net;
using System.Net.Sockets;

namespace DLNACast.Core.MiPlay;

/// <summary>
/// Performs a standard mDNS PTR query only. It does not open a MiPlay command
/// channel or reserve a playback session on the discovered device.
/// </summary>
public sealed class MiPlayMdnsDiscovery
{
    private static readonly IPEndPoint MulticastEndpoint = new(IPAddress.Parse("224.0.0.251"), 5353);

    public async Task<IReadOnlyList<MiPlayMdnsDevice>> SearchAsync(
        IPAddress localAddress,
        TimeSpan duration,
        CancellationToken cancellationToken) =>
        await SearchAsync(localAddress, MiPlayMdnsQuery.ServiceName, duration, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<MiPlayMdnsDevice>> SearchAsync(
        IPAddress localAddress,
        string serviceName,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(localAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        if (localAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ArgumentException("MiPlay mDNS discovery currently supports IPv4 LAN addresses only.", nameof(localAddress));
        }
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        using var client = new UdpClient(new IPEndPoint(localAddress, 0));
        client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
        client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, localAddress.GetAddressBytes());

        serviceName = serviceName.TrimEnd('.');
        var query = MiPlayMdnsQuery.Create(serviceName, requestUnicastResponse: true);
        await client.SendAsync(query, MulticastEndpoint, cancellationToken).ConfigureAwait(false);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(duration);
        var devices = new Dictionary<string, MiPlayMdnsDevice>(StringComparer.OrdinalIgnoreCase);

        while (!timeout.IsCancellationRequested)
        {
            try
            {
                var response = await client.ReceiveAsync(timeout.Token).ConfigureAwait(false);
                foreach (var device in MiPlayMdnsMessageParser.Parse(response.Buffer, serviceName, response.RemoteEndPoint.Address))
                {
                    devices[device.InstanceName] = device;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (SocketException)
            {
                // A malformed or disappearing mDNS responder must not hide other devices.
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return devices.Values.OrderBy(device => device.FriendlyName, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
}
