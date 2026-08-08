using DLNACast.Core.Abstractions;
using DLNACast.Core.Models;

namespace DLNACast.Core.Dlna;

public sealed class RendererDiscoveryService : IRendererDiscovery
{
    private static readonly string[] SearchTargets =
    [
        "urn:schemas-upnp-org:device:MediaRenderer:1",
        "urn:schemas-upnp-org:device:MediaRenderer:2",
        "urn:schemas-upnp-org:device:MediaRenderer:3",
    ];

    private readonly SsdpMulticastSearchClient _searchClient = new();
    private readonly UpnpSoapClient _soapClient = new();
    private readonly RendererController _controller;

    public RendererDiscoveryService() => _controller = new RendererController(_soapClient, ownsClient: false);

    public async Task<IReadOnlyList<RendererDevice>> SearchAsync(CancellationToken cancellationToken)
    {
        var descriptionLocations = await _searchClient
            .SearchAsync(SearchTargets, TimeSpan.FromSeconds(3), cancellationToken)
            .ConfigureAwait(false);

        var devices = new Dictionary<string, RendererDevice>(StringComparer.OrdinalIgnoreCase);
        foreach (var descriptionUrl in descriptionLocations)
        {
            try
            {
                var xml = await _soapClient.HttpClient.GetStringAsync(descriptionUrl, cancellationToken).ConfigureAwait(false);
                var renderer = RendererDescriptionParser.Parse(xml, descriptionUrl);
                var sink = await _controller.GetSinkProtocolInfoAsync(renderer, cancellationToken).ConfigureAwait(false);
                renderer = renderer with { SinkProtocolInfo = sink };
                devices[renderer.Udn] = renderer;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or FormatException or UpnpException)
            {
                // A malformed or disappearing device must not prevent other renderers from appearing.
            }
        }

        return devices.Values.OrderBy(device => device.FriendlyName, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public ValueTask DisposeAsync()
    {
        _controller.Dispose();
        _soapClient.Dispose();
        return ValueTask.CompletedTask;
    }
}
