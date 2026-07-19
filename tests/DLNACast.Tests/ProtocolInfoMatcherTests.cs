using DLNACast.Core.Dlna;
using DLNACast.Core.Models;

namespace DLNACast.Tests;

public sealed class ProtocolInfoMatcherTests
{
    [Fact]
    public void WildcardSinkPrefersWaveThenMp3Fallback()
    {
        var profiles = ProtocolInfoMatcher.SelectProfiles("http-get:*:*:*", allowMp3Fallback: true);

        Assert.Equal([StreamProfile.PcmWave, StreamProfile.Mp3Cbr320], profiles);
    }

    [Fact]
    public void SpecificMp3SinkSkipsUnsupportedWave()
    {
        var profiles = ProtocolInfoMatcher.SelectProfiles("http-get:*:audio/mpeg:DLNA.ORG_OP=00", allowMp3Fallback: true);

        Assert.Equal([StreamProfile.Mp3Cbr320], profiles);
    }

    [Fact]
    public void UnsupportedSinkIsRejectedClearly()
    {
        var error = Assert.Throws<NotSupportedException>(() =>
            ProtocolInfoMatcher.SelectProfiles("http-get:*:video/mp4:*", allowMp3Fallback: true));

        Assert.Contains("protocolInfo", error.Message);
    }
}
