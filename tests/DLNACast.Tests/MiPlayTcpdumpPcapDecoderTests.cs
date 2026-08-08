using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayTcpdumpPcapDecoderTests
{
    private const string RootTcpdumpHeartbeatPcapBase64 =
        "1MOyoQIABAAAAAAAAAAAAAAABAABAAAAQodlak3rCwBkAAAAZAAAACjRJ2sbwm6Mycy97wgARQAAVmEEQABABkQywKgKFMCoCgeu0iLDT+S7fy6gTYuAGACUlbQAAAEBCAqRww0J6frAeiQAGgAyAAAAGQAHAeAQhzTxJb7WYLs2sK40Ctkb6lav8fRCh2VqOv4LAGQAAABkAAAAbozJzL3vKNEnaxvCCABFAABWSUxAAEAGW+rAqAoHwKgKFCLDrtIuoE2LT+S7oYAYCM8ZBwAAAQEICun6xVyRww0JJAAbADIAAAAZAAcB4BBOzFkj6/nu1nS/3D/xUTpO0QTWbkKHZWpq/wsAQgAAAEIAAAAo0SdrG8JujMnMve8IAEUAADRhBUAAQAZEU8CoChTAqAoHrtIiw0/ku6EuoE2tgBAAlJWSAAABAQgKkcMNDun6xVxHh2VqtegLAGQAAABkAAAAKNEnaxvCbozJzL3vCABFAABWYQZAAEAGRDDAqAoUwKgKB67SIsNP5LuhLqBNrYAYAJSVtAAAAQEICpHDIJDp+sVcJAAaADMAAAAZAAcB4BDU/EUiFRi/0vYq5vuXkCNlPt7vBEeHZWqQBwwAZAAAAGQAAABujMnMve8o0SdrG8IIAEUAAFZJTUAAQAZb6cCoCgfAqAoUIsOu0i6gTa1P5LvDgBgIzwfSAAABAQgK6frKPpHDIJAkABsAMwAAABkABwHgEPDFUgwJPPtmilqkVyDL5+Hvzi+4R4dlangIDABCAAAAQgAAACjRJ2sbwm6Mycy97wgARQAANGEHQABABkRRwKgKFMCoCgeu0iLDT+S7wy6gTc+AEACUlZIAAAEBCAqRwyCY6frKPg==";

    [Fact]
    public void DecodeExtractsMiPlayFramesFromRootTcpdumpPcap()
    {
        var pcap = Convert.FromBase64String(RootTcpdumpHeartbeatPcapBase64);

        var result = MiPlayTcpdumpPcapDecoder.Decode(pcap);

        Assert.Empty(result.Issues);
        Assert.Equal(4, result.TcpPayloads.Count);
        Assert.Equal(4, result.CommandFrames.Count);
        Assert.Collection(
            result.CommandFrames,
            frame =>
            {
                Assert.Equal("192.168.10.20:44754", frame.SourceEndpoint);
                Assert.Equal("192.168.10.7:8899", frame.DestinationEndpoint);
                Assert.Equal(MiPlayProtocolConstants.HeartbeatCommand, frame.Frame.Command);
                Assert.Equal((ushort)0x0032, frame.Frame.Sequence);
                AssertSafetyDataPayload(frame.Frame);
            },
            frame =>
            {
                Assert.Equal("192.168.10.7:8899", frame.SourceEndpoint);
                Assert.Equal("192.168.10.20:44754", frame.DestinationEndpoint);
                Assert.Equal(MiPlayProtocolConstants.HeartbeatAcknowledgementCommand, frame.Frame.Command);
                Assert.Equal((ushort)0x0032, frame.Frame.Sequence);
                AssertSafetyDataPayload(frame.Frame);
            },
            frame =>
            {
                Assert.Equal(MiPlayProtocolConstants.HeartbeatCommand, frame.Frame.Command);
                Assert.Equal((ushort)0x0033, frame.Frame.Sequence);
                AssertSafetyDataPayload(frame.Frame);
            },
            frame =>
            {
                Assert.Equal(MiPlayProtocolConstants.HeartbeatAcknowledgementCommand, frame.Frame.Command);
                Assert.Equal((ushort)0x0033, frame.Frame.Sequence);
                AssertSafetyDataPayload(frame.Frame);
            });
    }

    [Fact]
    public void DecodeAcceptsEmptyClassicTcpdumpPcap()
    {
        var pcap = Convert.FromBase64String("1MOyoQIABAAAAAAAAAAAAAAABAABAAAA");

        var result = MiPlayTcpdumpPcapDecoder.Decode(pcap);

        Assert.Empty(result.Issues);
        Assert.Empty(result.TcpPayloads);
        Assert.Empty(result.CommandFrames);
    }

    private static void AssertSafetyDataPayload(MiPlayCapturedCommandFrameSummary frame)
    {
        Assert.Equal(25, frame.PayloadLength);
        Assert.NotNull(frame.SafetyDataHeader);
        Assert.Equal(9, frame.SafetyDataHeader.HeaderLength);
        Assert.Equal(0xE0, frame.SafetyDataHeader.Flags);
        Assert.Equal((byte)0x10, frame.SafetyDataHeader.PaddingLength);
        Assert.Equal(16, frame.SafetyDataHeader.PayloadLength);
        Assert.True(frame.SafetyDataHeader.IsEncrypted);
    }
}
