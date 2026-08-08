using System.Text;
using System.Text.Json;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlaySetMediaInfoPayloadCodecTests
{
    private const string OfficialJson =
        "{\"mArtist\":\"Vantage\",\"mAlbum\":\"Follow\",\"mTitle\":\"Follow\",\"mDuration\":234397," +
        "\"id\":\"\",\"mCoverUrl\":\"\",\"status\":0,\"volume\":24,\"mArt\":\"\"," +
        "\"mSourceName\":\"MI PAD 4\\/Plus\",\"mDeviceState\":3}";

    [Fact]
    public void DecodesTheRootedPhoneSetMediaInfoPayload()
    {
        var bytes = Encoding.UTF8.GetBytes(OfficialJson);

        var decoded = MiPlaySetMediaInfoPayloadCodec.TryDecode(bytes, out var payload);

        Assert.True(decoded);
        Assert.NotNull(payload);
        Assert.Equal(180, bytes.Length);
        Assert.Equal("Vantage", payload.Artist);
        Assert.Equal("Follow", payload.Album);
        Assert.Equal("Follow", payload.Title);
        Assert.Equal(234_397, payload.DurationMilliseconds);
        Assert.Equal(24, payload.Volume);
        Assert.Equal("MI PAD 4/Plus", payload.SourceName);
        Assert.Equal(3, payload.DeviceState);
    }

    [Fact]
    public void EncodesTheCapturedFieldOrderWithWindowsMetadata()
    {
        var source = MiPlaySetMediaInfoPayloadCodec.CreateWindowsSystemAudio(
            20_011,
            "DLNACast Windows");

        var encoded = MiPlaySetMediaInfoPayloadCodec.Encode(source);

        Assert.True(MiPlaySetMediaInfoPayloadCodec.TryDecode(encoded, out var decoded));
        Assert.Equal(source, decoded);
        using var document = JsonDocument.Parse(encoded);
        Assert.Equal(
            ["mArtist", "mAlbum", "mTitle", "mDuration", "id", "mCoverUrl", "status", "volume", "mArt", "mSourceName", "mDeviceState"],
            document.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal("System Audio", decoded!.Title);
        Assert.Equal(2, decoded.DeviceState);
    }

    [Fact]
    public void RejectsMissingOrOutOfRangeFields()
    {
        Assert.False(MiPlaySetMediaInfoPayloadCodec.TryDecode("{}"u8, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MiPlaySetMediaInfoPayloadCodec.Encode(
                MiPlaySetMediaInfoPayloadCodec.CreateWindowsSystemAudio(1_000, "Windows", volume: 101)));
    }
}
