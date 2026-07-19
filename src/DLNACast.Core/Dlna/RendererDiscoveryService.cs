using DLNACast.Core.Abstractions;
using DLNACast.Core.Models;
using Rssdp;

namespace DLNACast.Core.Dlna;

public sealed class RendererDiscoveryService : IRendererDiscovery
{
    private readonly SsdpDeviceLocator _locator = new();
    private readonly UpnpSoapClient _soapClient = new();
    private readonly RendererController _controller;

    public RendererDiscoveryService() => _controller = new RendererController(_soapClient, ownsClient: false);

    public async Task<IReadOnlyList<RendererDevice>> SearchAsync(CancellationToken cancellationToken)
    {
        var discovered = new List<DiscoveredSsdpDevice>();
        for (var version = 1; version <= 3; version++)
        {
            var searchTarget = $"urn:schemas-upnp-org:device:MediaRenderer:{version}";
            var result = await _locator.SearchAsync(searchTarget, TimeSpan.FromSeconds(2), cancellationToken)
                .ConfigureAwait(false);
            discovered.AddRange(result);
        }

        var devices = new Dictionary<string, RendererDevice>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in discovered
                     .Where(candidate => candidate.DescriptionLocation is not null)
                     .GroupBy(candidate => candidate.DescriptionLocation)
                     .Select(group => group.First()))
        {
            try
            {
                var descriptionUrl = candidate.DescriptionLocation!;
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
        _locator.Dispose();
        _controller.Dispose();
        _soapClient.Dispose();
        return ValueTask.CompletedTask;
    }
}

