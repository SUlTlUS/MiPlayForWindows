using System.Xml.Linq;
using DLNACast.Core.Dlna;
using DLNACast.Core.Models;

namespace DLNACast.Tests;

public sealed class RendererControllerTests
{
    [Theory]
    [InlineData(StreamProfile.PcmWave, "audio/wav")]
    [InlineData(StreamProfile.Mp3Cbr320, "audio/mpeg")]
    public void CreatesEscapedDidlMetadata(StreamProfile profile, string expectedMime)
    {
        var metadata = RendererController.CreateDidlMetadata(
            new Uri("http://192.168.10.9:49555/stream/a%20b.wav?x=1&y=2"),
            profile);

        var document = XDocument.Parse(metadata);
        var resource = document.Descendants().Single(element => element.Name.LocalName == "res");
        Assert.Contains(expectedMime, resource.Attribute("protocolInfo")!.Value);
        Assert.Contains("x=1&y=2", resource.Value);
    }
}

