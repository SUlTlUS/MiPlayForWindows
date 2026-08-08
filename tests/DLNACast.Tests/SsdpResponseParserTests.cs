using System.Text;
using DLNACast.Core.Dlna;

namespace DLNACast.Tests;

public sealed class SsdpResponseParserTests
{
    [Fact]
    public void TryGetDescriptionLocation_ReadsCaseInsensitiveLocationHeader()
    {
        var response = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\n" +
            "ST: urn:schemas-upnp-org:device:MediaRenderer:1\r\n" +
            "location: http://192.168.10.4:9999/device.xml\r\n\r\n");

        Assert.True(SsdpResponseParser.TryGetDescriptionLocation(response, out var location));
        Assert.Equal("http://192.168.10.4:9999/device.xml", location.AbsoluteUri);
    }

    [Theory]
    [InlineData("HTTP/1.1 200 OK\r\nST: ssdp:all\r\n\r\n")]
    [InlineData("HTTP/1.1 200 OK\r\nLOCATION: not-a-url\r\n\r\n")]
    [InlineData("HTTP/1.1 200 OK\r\nLOCATION: file:///tmp/device.xml\r\n\r\n")]
    public void TryGetDescriptionLocation_RejectsMissingOrUnsafeLocations(string response)
    {
        Assert.False(SsdpResponseParser.TryGetDescriptionLocation(
            Encoding.ASCII.GetBytes(response),
            out _));
    }
}
