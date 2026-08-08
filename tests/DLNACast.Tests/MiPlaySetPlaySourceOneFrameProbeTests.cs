using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlaySetPlaySourceOneFrameProbeTests
{
    [Fact]
    public void CommandFrameCarriesOfficialMinimalJsonPayload()
    {
        var frameBytes = MiPlaySetPlaySourceOneFrameProbe.ToCommandFrame(4);

        Assert.True(MiPlayCommandFrameCodec.TryDecode(frameBytes, out var frame, out var consumed));
        Assert.NotNull(frame);
        Assert.Equal(frameBytes.Length, consumed);
        Assert.Equal(MiPlayProtocolConstants.SetPlaySourceCommand, frame.Command);
        Assert.Equal((ushort)4, frame.Sequence);
        Assert.Equal(
            "{\"ref_channel\":\"playpage\",\"ref_function\":\"\",\"ref_content\":\"\"}",
            MiPlaySetPlaySourcePayloadCodec.DecodeUtf8(frame.Payload));
    }

    [Fact]
    public void SafetyDataCommandFrameDecryptsBackToOfficialMinimalJsonPayload()
    {
        var cipher = new MiPlaySafetyDataSessionCipher(new byte[16], new byte[16]);
        var frameBytes = MiPlaySetPlaySourceOneFrameProbe.ToSafetyDataCommandFrame(4, cipher);

        Assert.True(MiPlayCommandFrameCodec.TryDecode(frameBytes, out var frame, out var consumed));
        Assert.NotNull(frame);
        Assert.Equal(frameBytes.Length, consumed);
        Assert.Equal(MiPlayProtocolConstants.SetPlaySourceCommand, frame.Command);
        Assert.Equal((ushort)4, frame.Sequence);
        Assert.NotEmpty(frame.Payload);
        Assert.True(cipher.TryDecryptVersion1(frame.Payload, out var decoded));
        Assert.NotNull(decoded);
        Assert.Equal(
            "{\"ref_channel\":\"playpage\",\"ref_function\":\"\",\"ref_content\":\"\"}",
            MiPlaySetPlaySourcePayloadCodec.DecodeUtf8(decoded.Plaintext));
    }
}
