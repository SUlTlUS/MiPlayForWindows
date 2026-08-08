using System.Text;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlaySetPlaySourcePayloadCodecTests
{
    [Fact]
    public void EncodeUsesOfficialStatsUtilsKeyOrderAndUtf8Bytes()
    {
        var payload = MiPlaySetPlaySourcePayloadCodec.Encode(
            "playpage",
            "single_room",
            "music_wangyiyun");

        var json = Encoding.UTF8.GetString(payload);

        Assert.Equal(
            "{\"ref_channel\":\"playpage\",\"ref_function\":\"single_room\",\"ref_content\":\"music_wangyiyun\"}",
            json);
    }

    [Fact]
    public void EncodeOfficialStatsDefaultsIncludesEmptyFunctionAndContent()
    {
        var payload = MiPlaySetPlaySourcePayloadCodec.EncodeOfficialStatsDefaults("playpage");

        Assert.Equal(
            "{\"ref_channel\":\"playpage\",\"ref_function\":\"\",\"ref_content\":\"\"}",
            Encoding.UTF8.GetString(payload));
    }

    [Fact]
    public void EncodeModelsJsonObjectPutOptByOmittingNullValues()
    {
        var payload = MiPlaySetPlaySourcePayloadCodec.Encode(null, "", null);

        Assert.Equal("{\"ref_function\":\"\"}", Encoding.UTF8.GetString(payload));
    }

    [Fact]
    public void DecodeRoundTripsRecoveredOfficialPayloadShape()
    {
        var payload = MiPlaySetPlaySourcePayloadCodec.Encode(
            "首页",
            "multi_room",
            "music_qq");

        var decoded = MiPlaySetPlaySourcePayloadCodec.Decode(payload);

        Assert.Equal("首页", decoded.RefChannel);
        Assert.Equal("multi_room", decoded.RefFunction);
        Assert.Equal("music_qq", decoded.RefContent);
    }

    [Fact]
    public void DecodeRejectsNonStringKnownFields()
    {
        var ex = Assert.Throws<FormatException>(() =>
            MiPlaySetPlaySourcePayloadCodec.Decode(Encoding.UTF8.GetBytes("{\"ref_channel\":1}")));

        Assert.Contains("ref_channel", ex.Message, StringComparison.Ordinal);
    }
}