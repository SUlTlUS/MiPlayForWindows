using System.Net;
using System.Xml.Linq;
using DLNACast.Core.Models;

namespace DLNACast.Core.Dlna;

public static class RendererDescriptionParser
{
    public static RendererDevice Parse(string xml, Uri descriptionUrl, string sinkProtocolInfo = "")
    {
        var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        var device = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "device")
                     ?? throw new FormatException("设备描述缺少 device 节点。");

        var deviceType = ChildValue(device, "deviceType");
        if (!deviceType.Contains("MediaRenderer", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("发现的 UPnP 设备不是 MediaRenderer。");
        }

        var avTransport = ParseService(device, descriptionUrl, "AVTransport")
                          ?? throw new FormatException("MediaRenderer 缺少 AVTransport 服务。");
        var connectionManager = ParseService(device, descriptionUrl, "ConnectionManager")
                                ?? throw new FormatException("MediaRenderer 缺少 ConnectionManager 服务。");
        var renderingControl = ParseService(device, descriptionUrl, "RenderingControl");

        if (!IPAddress.TryParse(descriptionUrl.Host, out var address))
        {
            address = Dns.GetHostAddresses(descriptionUrl.Host)
                .First(candidate => candidate.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
        }

        return new RendererDevice(
            ChildValue(device, "UDN"),
            ChildValue(device, "friendlyName"),
            ChildValue(device, "manufacturer"),
            ChildValue(device, "modelName"),
            address,
            descriptionUrl,
            avTransport,
            connectionManager,
            renderingControl,
            sinkProtocolInfo);
    }

    private static UpnpServiceEndpoint? ParseService(XElement device, Uri descriptionUrl, string serviceName)
    {
        var service = device.Descendants().FirstOrDefault(element =>
            element.Name.LocalName == "service" &&
            ChildValue(element, "serviceType").Contains(serviceName, StringComparison.OrdinalIgnoreCase));
        if (service is null)
        {
            return null;
        }

        var serviceType = ChildValue(service, "serviceType");
        var controlUrl = new Uri(descriptionUrl, ChildValue(service, "controlURL"));
        var eventPath = ChildValue(service, "eventSubURL");
        var descriptionPath = ChildValue(service, "SCPDURL");
        return new UpnpServiceEndpoint(
            serviceType,
            controlUrl,
            string.IsNullOrWhiteSpace(eventPath) ? controlUrl : new Uri(descriptionUrl, eventPath),
            string.IsNullOrWhiteSpace(descriptionPath) ? descriptionUrl : new Uri(descriptionUrl, descriptionPath));
    }

    private static string ChildValue(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(element => element.Name.LocalName == localName)?.Value.Trim() ?? string.Empty;
}

