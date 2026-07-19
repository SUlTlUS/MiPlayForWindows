using System.Xml.Linq;
using DLNACast.Core.Abstractions;
using DLNACast.Core.Models;

namespace DLNACast.Core.Dlna;

public sealed class RendererController : IRendererController, IDisposable
{
    private readonly UpnpSoapClient _soapClient;
    private readonly bool _ownsClient;

    public RendererController() : this(new UpnpSoapClient(), ownsClient: true) { }

    internal RendererController(UpnpSoapClient soapClient, bool ownsClient)
    {
        _soapClient = soapClient;
        _ownsClient = ownsClient;
    }

    public async Task<string> GetSinkProtocolInfoAsync(RendererDevice device, CancellationToken cancellationToken)
    {
        var response = await _soapClient.InvokeAsync(
            device.ConnectionManager,
            "GetProtocolInfo",
            EmptyArguments,
            cancellationToken).ConfigureAwait(false);
        return Value(response, "Sink");
    }

    public Task SetTransportUriAsync(
        RendererDevice device,
        Uri streamUri,
        StreamProfile profile,
        CancellationToken cancellationToken)
    {
        var metadata = CreateDidlMetadata(streamUri, profile);
        return InvokeNoResultAsync(device.AvTransport, "SetAVTransportURI", new Dictionary<string, string?>
        {
            ["InstanceID"] = "0",
            ["CurrentURI"] = streamUri.AbsoluteUri,
            ["CurrentURIMetaData"] = metadata
        }, cancellationToken);
    }

    public Task PlayAsync(RendererDevice device, CancellationToken cancellationToken) =>
        InvokeNoResultAsync(device.AvTransport, "Play", new Dictionary<string, string?>
        {
            ["InstanceID"] = "0",
            ["Speed"] = "1"
        }, cancellationToken);

    public Task StopAsync(RendererDevice device, CancellationToken cancellationToken) =>
        InvokeNoResultAsync(device.AvTransport, "Stop", new Dictionary<string, string?>
        {
            ["InstanceID"] = "0"
        }, cancellationToken);

    public async Task<TransportStatus> GetTransportStatusAsync(RendererDevice device, CancellationToken cancellationToken)
    {
        var response = await _soapClient.InvokeAsync(
            device.AvTransport,
            "GetTransportInfo",
            new Dictionary<string, string?> { ["InstanceID"] = "0" },
            cancellationToken).ConfigureAwait(false);
        return new TransportStatus(Value(response, "CurrentTransportState"), Value(response, "CurrentTransportStatus"));
    }

    public async Task<int?> GetVolumeAsync(RendererDevice device, CancellationToken cancellationToken)
    {
        if (device.RenderingControl is null)
        {
            return null;
        }

        var response = await _soapClient.InvokeAsync(
            device.RenderingControl,
            "GetVolume",
            new Dictionary<string, string?>
            {
                ["InstanceID"] = "0",
                ["Channel"] = "Master"
            },
            cancellationToken).ConfigureAwait(false);
        return int.TryParse(Value(response, "CurrentVolume"), out var volume) ? Math.Clamp(volume, 0, 100) : null;
    }

    public Task SetVolumeAsync(RendererDevice device, int volume, CancellationToken cancellationToken)
    {
        if (device.RenderingControl is null)
        {
            return Task.CompletedTask;
        }

        return InvokeNoResultAsync(device.RenderingControl, "SetVolume", new Dictionary<string, string?>
        {
            ["InstanceID"] = "0",
            ["Channel"] = "Master",
            ["DesiredVolume"] = Math.Clamp(volume, 0, 100).ToString()
        }, cancellationToken);
    }

    private async Task InvokeNoResultAsync(
        UpnpServiceEndpoint service,
        string action,
        IReadOnlyDictionary<string, string?> arguments,
        CancellationToken cancellationToken) =>
        _ = await _soapClient.InvokeAsync(service, action, arguments, cancellationToken).ConfigureAwait(false);

    public static string CreateDidlMetadata(Uri streamUri, StreamProfile profile)
    {
        XNamespace didl = "urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/";
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        XNamespace upnp = "urn:schemas-upnp-org:metadata-1-0/upnp/";
        var mime = profile == StreamProfile.PcmWave ? "audio/wav" : "audio/mpeg";
        var protocolInfo = $"http-get:*:{mime}:DLNA.ORG_OP=00;DLNA.ORG_CI=0";
        var document = new XElement(didl + "DIDL-Lite",
            new XAttribute(XNamespace.Xmlns + "dc", dc.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "upnp", upnp.NamespaceName),
            new XElement(didl + "item",
                new XAttribute("id", "windows-audio"),
                new XAttribute("parentID", "0"),
                new XAttribute("restricted", "1"),
                new XElement(dc + "title", "Windows Audio"),
                new XElement(upnp + "class", "object.item.audioItem.musicTrack"),
                new XElement(didl + "res", new XAttribute("protocolInfo", protocolInfo), streamUri.AbsoluteUri)));
        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static string Value(XDocument document, string localName) =>
        document.Descendants().FirstOrDefault(node => node.Name.LocalName == localName)?.Value.Trim() ?? string.Empty;

    private static readonly IReadOnlyDictionary<string, string?> EmptyArguments = new Dictionary<string, string?>();

    public void Dispose()
    {
        if (_ownsClient)
        {
            _soapClient.Dispose();
        }
    }
}
