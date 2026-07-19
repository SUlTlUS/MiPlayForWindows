using DLNACast.Core.Dlna;

namespace DLNACast.Tests;

public sealed class RendererDescriptionParserTests
{
    private const string XiaomiDescription = """
        <?xml version="1.0" encoding="utf-8"?>
        <root xmlns="urn:schemas-upnp-org:device-1-0">
          <device>
            <deviceType>urn:schemas-upnp-org:device:MediaRenderer:1</deviceType>
            <friendlyName>小爱音箱-7503</friendlyName>
            <manufacturer>Mi, Inc.</manufacturer>
            <modelName>S12</modelName>
            <UDN>uuid:759c0613</UDN>
            <serviceList>
              <service><serviceType>urn:schemas-upnp-org:service:AVTransport:1</serviceType><SCPDURL>AVTransport1.xml</SCPDURL><controlURL>/AVTransport/control</controlURL><eventSubURL>/AVTransport/event</eventSubURL></service>
              <service><serviceType>urn:schemas-upnp-org:service:ConnectionManager:1</serviceType><SCPDURL>ConnectionManager1.xml</SCPDURL><controlURL>/ConnectionManager/control</controlURL><eventSubURL>/ConnectionManager/event</eventSubURL></service>
              <service><serviceType>urn:schemas-upnp-org:service:RenderingControl:1</serviceType><SCPDURL>RenderingControl1.xml</SCPDURL><controlURL>/RenderingControl/control</controlURL><eventSubURL>/RenderingControl/event</eventSubURL></service>
            </serviceList>
          </device>
        </root>
        """;

    [Fact]
    public void ParsesNamespacedRendererAndRelativeServiceUrls()
    {
        var renderer = RendererDescriptionParser.Parse(
            XiaomiDescription,
            new Uri("http://192.168.10.4:9999/device.xml"),
            "http-get:*:*:*");

        Assert.Equal("小爱音箱-7503", renderer.FriendlyName);
        Assert.Equal("S12", renderer.ModelName);
        Assert.Equal("192.168.10.4", renderer.Address.ToString());
        Assert.Equal("http://192.168.10.4:9999/AVTransport/control", renderer.AvTransport.ControlUrl.AbsoluteUri);
        Assert.Equal("http-get:*:*:*", renderer.SinkProtocolInfo);
    }
}

