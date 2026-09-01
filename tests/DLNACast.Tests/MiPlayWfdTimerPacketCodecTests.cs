using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayWfdTimerPacketCodecTests
{
    [Fact]
    public void TabletResponseMatchesNativeTimestampSlotsByteForByte()
    {
        var request = Enumerable.Range(0, 40).Select(value => (byte)value).ToArray();
        var original = request.ToArray();
        var response = MiPlayWfdTimerPacketCodec.CreateTabletResponse(
            request, 0x0807060504030201UL, 0x1817161514131211UL);
        var expected = Convert.FromHexString(
            "000102030405060708090A0B0C0D0E0F" +
            "11121314151617180102030405060708" +
            "2021222324252627");

        Assert.Equal(expected, response);
        Assert.Equal(original, request);
        Assert.NotSame(request, response);
    }

    [Theory]
    [InlineData(0UL, 0UL)]
    [InlineData(1UL, 1UL)]
    [InlineData(1UL, 9_000_000_000UL)]
    [InlineData(ulong.MaxValue - 1, ulong.MaxValue)]
    public void TabletResponseKeepsFullWidthTimestamps(ulong receive, ulong send)
    {
        var response = MiPlayWfdTimerPacketCodec.CreateTabletResponse(new byte[40], receive, send);

        Assert.Equal(send, BinaryPrimitives.ReadUInt64LittleEndian(response.AsSpan(16, 8)));
        Assert.Equal(receive, BinaryPrimitives.ReadUInt64LittleEndian(response.AsSpan(24, 8)));
    }

    [Fact]
    public void TabletResponseRejectsOnlyAnEmptyDatagram()
    {
        Assert.Throws<FormatException>(() =>
            MiPlayWfdTimerPacketCodec.CreateTabletResponse([], 100, 200));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(15)]
    [InlineData(39)]
    [InlineData(40)]
    [InlineData(41)]
    [InlineData(512)]
    public void TabletResponseMatchesNativeTruncationAndZeroFill(int length)
    {
        var request = Enumerable.Range(1, length).Select(value => (byte)value).ToArray();
        var response = MiPlayWfdTimerPacketCodec.CreateTabletResponse(
            request, 0x0807060504030201UL, 0x1817161514131211UL);

        Assert.Equal(40, response.Length);
        var preservedPrefixLength = Math.Min(length, 16);
        Assert.Equal(request[..preservedPrefixLength], response[..preservedPrefixLength]);
        if (length < 16)
        {
            Assert.All(response[length..16], value => Assert.Equal(0, value));
        }
        Assert.Equal(0x1817161514131211UL, BinaryPrimitives.ReadUInt64LittleEndian(response.AsSpan(16, 8)));
        Assert.Equal(0x0807060504030201UL, BinaryPrimitives.ReadUInt64LittleEndian(response.AsSpan(24, 8)));
        var preservedSuffixLength = Math.Clamp(length - 32, 0, 8);
        if (preservedSuffixLength > 0)
        {
            Assert.Equal(request.AsSpan(32, preservedSuffixLength).ToArray(), response.AsSpan(32, preservedSuffixLength).ToArray());
        }
        Assert.All(response[(32 + preservedSuffixLength)..], value => Assert.Equal(0, value));
    }

    [Fact]
    public void LegacyResponseAndDecoderRemainUnchangedForSingleSpeaker()
    {
        var request = Enumerable.Range(0, 40).Select(value => (byte)value).ToArray();
        var response = MiPlayWfdTimerPacketCodec.CreateResponse(
            request, 0x0807060504030201UL, 0x1817161514131211UL);
        var expected = Convert.FromHexString(
            "000102030405060708090A0B0C0D0E0F" +
            "01020304050607081112131415161718" +
            "2021222324252627");

        Assert.Equal(expected, response);
        var decoded = MiPlayWfdTimerPacketCodec.Decode(response);
        Assert.Equal(0x0807060504030201UL, decoded.SourceReceiveTimestamp);
        Assert.Equal(0x1817161514131211UL, decoded.SourceSendTimestamp);
        Assert.Equal(response, MiPlayWfdTimerPacketCodec.Encode(decoded));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(12L)]
    [InlineData(13L)]
    [InlineData(50_000L)]
    public void NativeRoundTripSubtractsServerProcessingTime(long processing)
    {
        // Independent transcription of ARM64 TimerAlgorithm::unitRound:
        // clientReceive - clientSend - packet[16] + packet[24].
        const long clientSend = 1_000_000;
        const long serverReceive = 2_001_000;
        var clientReceive = clientSend + 2_000 + processing;
        var request = new byte[40];
        BinaryPrimitives.WriteInt64LittleEndian(request, clientSend);
        BinaryPrimitives.WriteInt64LittleEndian(request.AsSpan(8), clientReceive);

        var tablet = MiPlayWfdTimerPacketCodec.CreateTabletResponse(
            request, (ulong)serverReceive, (ulong)(serverReceive + processing));
        var legacy = MiPlayWfdTimerPacketCodec.CreateResponse(
            request, (ulong)serverReceive, (ulong)(serverReceive + processing));

        Assert.Equal(2_000, NativeRoundTrip(tablet));
        Assert.Equal(2_000 + 2 * processing, NativeRoundTrip(legacy));
        // Native unitOffset = (1-p)*(serverReceive-clientSend)
        //                   + p*(serverSend-clientReceive).
        // With symmetric paths (p=0.5), source processing must not shift it.
        Assert.Equal(1_000_000L, NativeOffset(tablet, 0.5));
        Assert.Equal(1_000_500L, NativeOffset(tablet, 0.25));
        Assert.Equal((long)(1_000_500 + processing * 0.5), NativeOffset(legacy, 0.25));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProductionUdpResponderUsesSelectedLayoutAndTheSameMonotonicClock(bool tabletOrder)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var source = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var endpoint = (IPEndPoint)source.Client.LocalEndPoint!;
        var run = typeof(MiPlayLegacySystemAudioSessionRunner).GetMethod(
            "RunTimerResponderAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
        var responder = (Task)run.Invoke(null, [source, IPAddress.Loopback, lifetime.Token, tabletOrder])!;
        try
        {
            for (uint sequence = 1; sequence <= 32; sequence++)
            {
                var request = Enumerable.Range(0, 40).Select(value => (byte)value).ToArray();
                BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(32), sequence);
                var before = MonotonicMicroseconds();
                await receiver.SendAsync(request, endpoint, lifetime.Token);
                var result = await receiver.ReceiveAsync(lifetime.Token);
                var after = MonotonicMicroseconds();
                var response = result.Buffer;
                Assert.Equal(endpoint, result.RemoteEndPoint);
                Assert.Equal(40, response.Length);
                Assert.Equal(request[..16], response[..16]);
                Assert.Equal(request[32..], response[32..]);
                var receive = BinaryPrimitives.ReadUInt64LittleEndian(response.AsSpan(tabletOrder ? 24 : 16, 8));
                var send = BinaryPrimitives.ReadUInt64LittleEndian(response.AsSpan(tabletOrder ? 16 : 24, 8));
                Assert.InRange(receive, before, after);
                Assert.InRange(send, receive, after);
            }
        }
        finally
        {
            lifetime.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => responder);
        }
    }

    private static long NativeRoundTrip(byte[] packet) =>
        ReadTimestamp(packet, 8) - ReadTimestamp(packet, 0) -
        ReadTimestamp(packet, 16) + ReadTimestamp(packet, 24);

    private static long NativeOffset(byte[] packet, double percent) => checked((long)(
        (1 - percent) * (ReadTimestamp(packet, 24) - ReadTimestamp(packet, 0)) +
        percent * (ReadTimestamp(packet, 16) - ReadTimestamp(packet, 8))));

    private static long ReadTimestamp(byte[] packet, int offset) =>
        BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(offset, 8));

    private static ulong MonotonicMicroseconds() =>
        MiPlayWfdMediaClock.ConvertStopwatchTicksToMicroseconds(Stopwatch.GetTimestamp(), Stopwatch.Frequency);
}
