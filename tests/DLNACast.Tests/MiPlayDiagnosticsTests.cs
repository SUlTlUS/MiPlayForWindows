using System.Buffers.Binary;
using System.Net;
using System.Text;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayDiagnosticsTests
{
    [Fact]
    public void MdnsQueryRequestsMiConnectPtrWithUnicastResponse()
    {
        var query = MiPlayMdnsQuery.Create();

        Assert.Equal(1, BinaryPrimitives.ReadUInt16BigEndian(query.AsSpan(4, 2)));
        Assert.Contains(Encoding.ASCII.GetBytes("_mi-connect"), query);
        Assert.Equal(12, BinaryPrimitives.ReadUInt16BigEndian(query.AsSpan(query.Length - 4, 2)));
        Assert.Equal(0x8001, BinaryPrimitives.ReadUInt16BigEndian(query.AsSpan(query.Length - 2, 2)));
    }

    [Fact]
    public void ParsesMiConnectPtrSrvTxtAndAddressRecords()
    {
        var packet = BuildMdnsResponse();

        var device = Assert.Single(MiPlayMdnsMessageParser.Parse(packet));

        Assert.Equal("小爱音箱-7503", device.FriendlyName);
        Assert.Equal("speaker._mi-connect._udp.local", device.InstanceName);
        Assert.Equal("s12.local", device.HostName);
        Assert.Equal(IPAddress.Parse("192.168.31.42"), device.Address);
        Assert.Equal(56_666, device.Port);
        Assert.Equal("[5]", device.TxtRecords["apps"]);
        Assert.Equal("9d6105d2-6f96-4cab-8360-83f01ca951aa", device.TxtRecords["appsData"]);
    }

    [Fact]
    public void ExtractsMicoDeviceIdFromBinaryPrefixedAppsData()
    {
        const string value = "gQBmBIMiw4xTw70lmQAAAAAAAAAAAAAAAAAAAAAAewoJIm1pY28iOiB7CgkJImRldmljZV9pZCI6ICI3NTljMDYxMy01MDUyLTRhODEtYTE4OS1jYTc2ZDM0MzI0MzgiIAoJfSAKfSAK";

        var parsed = MiPlayMicoAppData.TryParse(value, out var appData);

        Assert.True(parsed);
        Assert.NotNull(appData);
        Assert.Equal(30, appData.BinaryPrefix.Length);
        Assert.Equal("759c0613-5052-4a81-a189-ca76d3432438", appData.DeviceId);
    }

    [Fact]
    public void ParsesObservedS12MiPlayAudioCapabilities()
    {
        const string appData = "gQBmBIMiw4xTw70lmQAAAAAAAAAAAAAAAAAAAAAAewoJIm1pY28iOiB7CgkJImRldmljZV9pZCI6ICI3NTljMDYxMy01MDUyLTRhODEtYTE4OS1jYTc2ZDM0MzI0MzgiIAoJfSAKfSAK";
        var txt = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["version"] = "65542",
            ["apps"] = "[5]",
            ["flags"] = "Ag==",
            ["idHash"] = "MDBl",
            ["dev"] = "4",
            ["sec"] = "2",
            ["appsData"] = appData,
        };

        var capabilities = MiPlayMdnsCapabilities.Parse(txt);

        Assert.Equal(1, capabilities.VersionMajor);
        Assert.Equal(6, capabilities.VersionMinor);
        Assert.Equal([5], capabilities.ApplicationIds);
        Assert.Equal([0x02], capabilities.Flags);
        Assert.Equal("00e", Encoding.ASCII.GetString(capabilities.IdHash));
        Assert.Equal(4, capabilities.DeviceType);
        Assert.Equal(MiConnectSecurityMode.Transport, capabilities.SecurityMode);
        Assert.True(capabilities.SupportsMiPlayAudio);
        Assert.True(capabilities.RequiresTransportSecurity);
        Assert.Equal("759c0613-5052-4a81-a189-ca76d3432438", capabilities.MicoAppData?.DeviceId);
        Assert.Equal(27, capabilities.MicoAppData?.BinaryPrefix.Length);
        Assert.Equal(102, capabilities.ApplicationData[5].Length);
        Assert.Equal(1_155, capabilities.MiPlayAudioAppData?.ControlPort);
        Assert.True(capabilities.MiPlayAudioAppData?.HasAdvertisedControlPort);
        Assert.False(capabilities.MiPlayAudioAppData?.SupportsLyra);
        Assert.Equal(
            "urn:aiot-spec-v3:com.mi.idm:service:miplay-audio:00017803:1.0",
            MiPlayMdnsCapabilities.MiPlayAudioServiceType);
    }

    [Fact]
    public void ExtractsAppFivePayloadFromObservedMdnsContainer()
    {
        const string value = "gQBmBIMiwyjRJ2sbwgAAAAAAAAAAAAAAAAAAAAAAewoJIm1pY28iOiB7CgkJImRldmljZV9pZCI6ICJkYjFlYTA2Mi03NTYzLTQ2MDQtODM0OS1kYWM2MDUzMDNhNWUiIAoJfSAKfSAK";

        var parsed = MiPlayMdnsAppData.TryParse(value, [5], out var applications);

        Assert.True(parsed);
        var appData = Assert.Single(applications);
        Assert.Equal(5, appData.Key);
        Assert.Equal(102, appData.Value.Length);
        Assert.Equal(new byte[] { 0x04, 0x83 }, appData.Value[..2]);
        Assert.Equal(1_155, MiPlayLegacyAppData.Parse(appData.Value).ControlPort);
        Assert.True(MiPlayMicoAppData.TryParse(appData.Value, out var mico));
        Assert.Equal("db1ea062-7563-4604-8349-dac605303a5e", mico?.DeviceId);
    }

    [Fact]
    public void RejectsTruncatedMdnsAppDataContainer()
    {
        var truncated = Convert.ToBase64String(new byte[] { 0x01, 0x04, 0xaa });

        Assert.False(MiPlayMdnsAppData.TryParse(truncated, [5], out var applications));
        Assert.Empty(applications);
    }

    [Fact]
    public void InvalidOptionalCapabilityFieldsDoNotBreakDiscovery()
    {
        var capabilities = MiPlayMdnsCapabilities.Parse(new Dictionary<string, string>
        {
            ["version"] = "invalid",
            ["apps"] = "[5, invalid, 7]",
            ["flags"] = "not-base64",
            ["sec"] = "99",
        });

        Assert.Equal([5, 7], capabilities.ApplicationIds);
        Assert.Empty(capabilities.Flags);
        Assert.Null(capabilities.SecurityMode);
        Assert.True(capabilities.SupportsMiPlayAudio);
    }

    [Fact]
    public void TruncatedMdnsPacketIsIgnored()
    {
        var packet = BuildMdnsResponse();

        Assert.Empty(MiPlayMdnsMessageParser.Parse(packet.AsSpan(0, packet.Length - 3)));
    }

    [Fact]
    public void DecodesSetupRequestAndMptTransportWithoutConsumingFollowingBytes()
    {
        const string message = "SETUP wfd://192.168.31.8:7236 RTSP/1.0\r\n" +
                               "CSeq: 3\r\n" +
                               "Transport: RTP/AVP/MPT;unicast;client_port=7236;userid=9\r\n" +
                               "Content-Length: 0\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(message).Concat(new byte[] { 0xaa, 0xbb }).ToArray();

        var decoded = MiPlayRtspRequestCodec.TryDecode(bytes, out var request, out var consumed);

        Assert.True(decoded);
        Assert.NotNull(request);
        Assert.Equal("SETUP", request.Method);
        Assert.Equal(new Version(1, 0), request.Version);
        Assert.Equal(message.Length, consumed);
        Assert.NotNull(request.Transport);
        Assert.Equal(MiPlayTransportMode.MptKcp, request.Transport.Mode);
        Assert.Equal(7_236, request.Transport.ClientRtpPort);
        Assert.Equal(9, request.Transport.UserId);
    }

    [Fact]
    public void EncodesRtspOptionsOkWithObservedPublicHeaderAndZeroBody()
    {
        var response = MiPlayRtspResponseCodec.EncodeOk(
            cseq: 1,
            [new MiPlayRtspHeader("Public", "org.wfa.wfd1.0, GET_PARAMETER, SET_PARAMETER")]);

        Assert.Equal(
            "RTSP/1.0 200 OK\r\n" +
            "CSeq: 1\r\n" +
            "Public: org.wfa.wfd1.0, GET_PARAMETER, SET_PARAMETER\r\n" +
            "Content-Length: 0\r\n\r\n",
            Encoding.ASCII.GetString(response));
    }

    [Fact]
    public void EncodesRtspSetupOkWithMptTransportAndExactBodyLength()
    {
        var body = "wfd_audio_codecs: AAC 00000001 00\r\n"u8.ToArray();
        var response = MiPlayRtspResponseCodec.EncodeOk(
            cseq: 3,
            body,
            [new MiPlayRtspHeader("Transport", "RTP/AVP/MPT;unicast;client_port=7236;server_port=7236;userid=9")]);

        Assert.Equal(
            "RTSP/1.0 200 OK\r\n" +
            "CSeq: 3\r\n" +
            "Transport: RTP/AVP/MPT;unicast;client_port=7236;server_port=7236;userid=9\r\n" +
            "Content-Length: 35\r\n\r\n" +
            "wfd_audio_codecs: AAC 00000001 00\r\n",
            Encoding.ASCII.GetString(response));
    }

    [Fact]
    public void IncompleteRtspBodyIsNotConsumed()
    {
        var bytes = "SET_PARAMETER * RTSP/1.0\r\nContent-Length: 4\r\n\r\nab"u8.ToArray();

        Assert.False(MiPlayRtspRequestCodec.TryDecode(bytes, out var request, out var consumed));
        Assert.Null(request);
        Assert.Equal(0, consumed);
    }

    private static byte[] BuildMdnsResponse()
    {
        using var stream = new MemoryStream();
        WriteUInt16(stream, 0);
        WriteUInt16(stream, 0x8400);
        WriteUInt16(stream, 0);
        WriteUInt16(stream, 1);
        WriteUInt16(stream, 0);
        WriteUInt16(stream, 3);

        WriteRecord(stream, "_mi-connect._udp.local", 12, record =>
            WriteName(record, "speaker._mi-connect._udp.local"));
        WriteRecord(stream, "speaker._mi-connect._udp.local", 33, record =>
        {
            WriteUInt16(record, 0);
            WriteUInt16(record, 0);
            WriteUInt16(record, 56_666);
            WriteName(record, "s12.local");
        });
        WriteRecord(stream, "speaker._mi-connect._udp.local", 16, record =>
        {
            WriteTxt(record, "name=小爱音箱-7503");
            WriteTxt(record, "version=1");
            WriteTxt(record, "apps=[5]");
            WriteTxt(record, "appsData=9d6105d2-6f96-4cab-8360-83f01ca951aa");
        });
        WriteRecord(stream, "s12.local", 1, record =>
            record.Write(IPAddress.Parse("192.168.31.42").GetAddressBytes()));
        return stream.ToArray();
    }

    private static void WriteRecord(Stream stream, string owner, ushort type, Action<MemoryStream> writeData)
    {
        WriteName(stream, owner);
        WriteUInt16(stream, type);
        WriteUInt16(stream, 0x8001);
        WriteUInt32(stream, 120);
        using var data = new MemoryStream();
        writeData(data);
        WriteUInt16(stream, checked((ushort)data.Length));
        data.Position = 0;
        data.CopyTo(stream);
    }

    private static void WriteName(Stream stream, string name)
    {
        foreach (var label in name.Split('.'))
        {
            var bytes = Encoding.UTF8.GetBytes(label);
            stream.WriteByte(checked((byte)bytes.Length));
            stream.Write(bytes);
        }
        stream.WriteByte(0);
    }

    private static void WriteTxt(Stream stream, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        stream.WriteByte(checked((byte)bytes.Length));
        stream.Write(bytes);
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }
}
