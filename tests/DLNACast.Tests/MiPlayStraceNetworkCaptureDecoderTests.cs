using System.Text;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayStraceNetworkCaptureDecoderTests
{
    [Fact]
    public void ReconstructsUnfinishedCallsAndCoalescedFramesWithoutReturningRawPayloads()
    {
        var challenge = MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.LegacySafetyChallengeCommand,
            0x00be,
            Encoding.ASCII.GetBytes("1234567890123456"));
        var version = MiPlayNativeVersionCodec.EncodeSourceVersion(
            0,
            MiPlayProtocolConstants.NativeSourceVersion12_4_8_13);
        var acknowledgement = MiPlayLegacySafetyChallengeCodec.EncodeAcknowledgement(
            MiPlayLegacySafetyChallengeCodec.CreateAcknowledgement(
                0x00be,
                Encoding.ASCII.GetBytes("1234567890123456")));
        var outbound = version.Concat(acknowledgement).ToArray();

        var trace = string.Join(
            '\n',
            $"10  13:13:25.000001 recvfrom(95<TCP:[192.168.10.58:60912->192.168.10.3:8899]>,  <unfinished ...>",
            $"10  13:13:25.000002 <... recvfrom resumed> \"{Escape(challenge)}\", 20480, 0, NULL, NULL) = {challenge.Length}",
            $"10  13:13:25.000003 sendto(95<TCP:[192.168.10.58:60912->192.168.10.3:8899]>, \"{Escape(outbound)}\", {outbound.Length}, MSG_NOSIGNAL, NULL, 0 <unfinished ...>",
            $"10  13:13:25.000004 <... sendto resumed> ) = {outbound.Length}",
            "11  13:13:25.000005 sendto(7<TCP:[192.168.10.58:1->192.168.10.8:7777]>, \"\\x24\", 1, 0, NULL, 0) = 1");

        var result = MiPlayStraceNetworkCaptureDecoder.Decode(trace);

        Assert.False(result.ContainsRawPayloads);
        Assert.Equal(8899, result.ControlPort);
        Assert.Empty(result.Issues);
        Assert.Equal(2, result.Chunks.Count);
        Assert.Equal([25, 70], result.Chunks.Select(chunk => chunk.ByteLength));
        Assert.Equal(
            [
                MiPlayProtocolConstants.LegacySafetyChallengeCommand,
                MiPlayProtocolConstants.NativeSourceVersionCommand,
                MiPlayProtocolConstants.LegacySafetyAcknowledgementCommand,
            ],
            result.Frames.Select(frame => frame.Command));
        Assert.Equal(
            [
                MiPlayStraceNetworkDirection.Inbound,
                MiPlayStraceNetworkDirection.Outbound,
                MiPlayStraceNetworkDirection.Outbound,
            ],
            result.Frames.Select(frame => frame.Direction));
        Assert.All(result.Frames, frame => Assert.Empty(frame.PayloadHexPrefix));
        Assert.All(result.Frames, frame => Assert.Equal(64, frame.FrameSha256Hex.Length));
    }

    [Fact]
    public void TruncatedSuccessfulSyscallBecomesIssueInsteadOfPartialFrame()
    {
        const string trace =
            "20  13:13:25.000001 recvfrom(9<TCP:[192.168.10.58:2->192.168.10.4:8899]>, \"\\x24\", 20480, 0, NULL, NULL) = 9";

        var result = MiPlayStraceNetworkCaptureDecoder.Decode(trace);

        Assert.Empty(result.Chunks);
        Assert.Empty(result.Frames);
        Assert.Contains(result.Issues, issue => issue.Reason.Contains("exposed only 1", StringComparison.Ordinal));
    }

    [Fact]
    public void PayloadPrefixMustBeExplicitlyRequested()
    {
        var frame = MiPlayCommandFrameCodec.Encode(0x001a, 5, [0xab, 0xcd]);
        var trace =
            $"30  13:13:25.000001 sendto(9<TCP:[192.168.10.58:2->192.168.10.4:8899]>, \"{Escape(frame)}\", {frame.Length}, 0, NULL, 0) = {frame.Length}";

        var result = MiPlayStraceNetworkCaptureDecoder.Decode(trace, payloadHexPrefixBytes: 1);

        var decoded = Assert.Single(result.Frames);
        Assert.Equal("AB", decoded.PayloadHexPrefix);
    }

    [Fact]
    public void DecodesStrictCommandFramesFromPreconnectedFileDescriptors()
    {
        var setPlaySource = MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.SetPlaySourceCommand,
            0x006d,
            Encoding.UTF8.GetBytes("{\"ref_channel\":\"control\"}"));
        var open = MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.OpenDeviceCommand,
            0x006e,
            Encoding.ASCII.GetBytes("wfd://192.168.10.58:7262"));
        var trace = string.Join(
            '\n',
            $"40  14:06:45.000001 sendto(165, \"{Escape(setPlaySource)}\", {setPlaySource.Length}, MSG_NOSIGNAL, NULL, 0) = {setPlaySource.Length}",
            $"40  14:06:45.000002 sendto(165, \"{Escape(open)}\", {open.Length}, MSG_NOSIGNAL, NULL, 0) = {open.Length}");

        var result = MiPlayStraceNetworkCaptureDecoder.Decode(trace);

        Assert.Empty(result.Issues);
        Assert.Equal(2, result.Chunks.Count);
        Assert.Equal(
            [MiPlayProtocolConstants.SetPlaySourceCommand, MiPlayProtocolConstants.OpenDeviceCommand],
            result.Frames.Select(frame => frame.Command));
        Assert.All(result.Frames, frame =>
        {
            Assert.False(frame.Endpoint.IsMapped);
            Assert.Equal(165, frame.Endpoint.FileDescriptor);
            Assert.Contains("preconnected endpoint unmapped", frame.Endpoint.ToString(), StringComparison.Ordinal);
            Assert.Empty(frame.PayloadHexPrefix);
        });
    }

    [Fact]
    public void RejectsMediaAndRtspPayloadsOnPreconnectedFileDescriptors()
    {
        var mediaLike = new byte[]
        {
            0x24, 0x00, 0x02, 0xfc, 0x80, 0xa1, 0x31, 0x55, 0x01,
        };
        var rtsp = Encoding.ASCII.GetBytes("RTSP/1.0 200 OK\r\n\r\n");
        var trace = string.Join(
            '\n',
            $"50  14:06:45.000001 sendto(189, \"{Escape(mediaLike)}\", {mediaLike.Length}, MSG_NOSIGNAL, NULL, 0) = {mediaLike.Length}",
            $"51  14:06:45.000002 sendto(187, \"{Escape(rtsp)}\", {rtsp.Length}, MSG_NOSIGNAL, NULL, 0) = {rtsp.Length}");

        var result = MiPlayStraceNetworkCaptureDecoder.Decode(trace);

        Assert.Empty(result.Chunks);
        Assert.Empty(result.Frames);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void IgnoresTruncatedUnknownMediaButReportsTruncatedUnknownCommandCandidate()
    {
        var mediaPrefix = new byte[]
        {
            0x24, 0x00, 0x05, 0x30, 0x80, 0xa1, 0x00, 0x00, 0x00,
        };
        var command = MiPlayCommandFrameCodec.Encode(0x0040, 7, new byte[92]);
        var commandPrefix = command[..16];
        var trace = string.Join(
            '\n',
            $"60  14:06:45.000001 sendto(200, \"{Escape(mediaPrefix)}\", 1332, MSG_NOSIGNAL, NULL, 0) = 1332",
            $"61  14:06:45.000002 sendto(201, \"{Escape(commandPrefix)}\", {command.Length}, MSG_NOSIGNAL, NULL, 0) = {command.Length}");

        var result = MiPlayStraceNetworkCaptureDecoder.Decode(trace);

        Assert.Empty(result.Chunks);
        Assert.Empty(result.Frames);
        var issue = Assert.Single(result.Issues);
        Assert.Contains("exposed only 16", issue.Reason, StringComparison.Ordinal);
    }

    private static string Escape(IEnumerable<byte> bytes) =>
        string.Concat(bytes.Select(value => $"\\x{value:x2}"));
}
