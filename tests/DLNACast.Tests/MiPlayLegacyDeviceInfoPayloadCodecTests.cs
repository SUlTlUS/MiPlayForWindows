using System.Text;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayLegacyDeviceInfoPayloadCodecTests
{
    [Fact]
    public void DecodesLegacyGetDeviceInfoStringMapPayload()
    {
        var payload = CreateSanitizedDeviceInfoPayload();

        Assert.True(MiPlayLegacyDeviceInfoPayloadCodec.TryDecode(payload, out var deviceInfo, out var bytesConsumed));
        Assert.NotNull(deviceInfo);
        Assert.Equal(payload.Length, bytesConsumed);
        Assert.Equal(payload.Length - MiPlayLegacyDeviceInfoPayloadCodec.HeaderLength, deviceInfo.DeclaredBodyLength);
        Assert.Equal(20, deviceInfo.Fields.Count);
        Assert.All(deviceInfo.Fields, field => Assert.Equal(MiPlayLegacyDeviceInfoPayloadCodec.StringValueType, field.ValueType));
        Assert.Equal("LX06", deviceInfo.GetValue("model"));
        Assert.Equal("1.94.13", deviceInfo.GetValue("romVersion"));
        Assert.Equal("audio", deviceInfo.GetValue("support"));
        Assert.Equal("4", deviceInfo.GetValue("deviceType"));
        Assert.Equal("小爱音箱Pro", deviceInfo.GetValue("miName"));
        Assert.Equal("center", deviceInfo.GetValue("channel"));
    }

    [Fact]
    public void EncodeRoundTripsOrderedUtf8StringFields()
    {
        var fields = new[]
        {
            KeyValuePair.Create("deviceType", "4"),
            KeyValuePair.Create("miName", "DLNACast 真机捕获器"),
            KeyValuePair.Create("support", "audio"),
        };

        var payload = MiPlayLegacyDeviceInfoPayloadCodec.Encode(fields);

        Assert.True(MiPlayLegacyDeviceInfoPayloadCodec.TryDecode(payload, out var decoded, out var bytesConsumed));
        Assert.NotNull(decoded);
        Assert.Equal(payload.Length, bytesConsumed);
        Assert.Equal(fields.Select(field => field.Key), decoded.Fields.Select(field => field.Name));
        Assert.Equal(fields.Select(field => field.Value), decoded.Fields.Select(field => field.Value));
        Assert.Equal(payload.Length - MiPlayLegacyDeviceInfoPayloadCodec.HeaderLength, decoded.DeclaredBodyLength);
    }

    [Fact]
    public void EncodeRejectsEmptyDuplicateOrNonAsciiFieldNames()
    {
        Assert.Throws<ArgumentException>(() => MiPlayLegacyDeviceInfoPayloadCodec.Encode([]));
        Assert.Throws<ArgumentException>(() => MiPlayLegacyDeviceInfoPayloadCodec.Encode(
            [KeyValuePair.Create("model", "A"), KeyValuePair.Create("model", "B")]));
        Assert.Throws<ArgumentException>(() => MiPlayLegacyDeviceInfoPayloadCodec.Encode(
            [KeyValuePair.Create("型号", "LX06")]));
    }

    [Fact]
    public void RedactedDescriptionKeepsRoutingFieldsAndHidesPrivateIdentifiers()
    {
        Assert.True(MiPlayLegacyDeviceInfoPayloadCodec.TryDecode(CreateSanitizedDeviceInfoPayload(), out var deviceInfo, out _));
        Assert.NotNull(deviceInfo);

        var description = MiPlayLegacyDeviceInfoPayloadCodec.DescribeRedacted(deviceInfo);

        Assert.Contains("model=LX06", description, StringComparison.Ordinal);
        Assert.Contains("romVersion=1.94.13", description, StringComparison.Ordinal);
        Assert.Contains("support=audio", description, StringComparison.Ordinal);
        Assert.Contains("miName=小爱音箱Pro", description, StringComparison.Ordinal);
        Assert.Contains("accountId=<redacted:", description, StringComparison.Ordinal);
        Assert.Contains("bluetoothMac=<redacted:", description, StringComparison.Ordinal);
        Assert.Contains("deviceId=<redacted:", description, StringComparison.Ordinal);
        Assert.Contains("roomName=<redacted:", description, StringComparison.Ordinal);
        Assert.DoesNotContain("0000000001", description, StringComparison.Ordinal);
        Assert.DoesNotContain("00:00:00:00:00:00", description, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsTruncatedOrWrongTypePayloads()
    {
        var payload = CreateSanitizedDeviceInfoPayload();
        Assert.False(MiPlayLegacyDeviceInfoPayloadCodec.TryDecode(payload[..^1], out _, out _));

        var wrongType = payload.ToArray();
        var firstValueTypeOffset = MiPlayLegacyDeviceInfoPayloadCodec.HeaderLength + 1 + "accountId".Length;
        wrongType[firstValueTypeOffset] = 0x14;
        Assert.False(MiPlayLegacyDeviceInfoPayloadCodec.TryDecode(wrongType, out _, out _));
    }

    private static byte[] CreateSanitizedDeviceInfoPayload()
    {
        var fields = new (string Key, string Value)[]
        {
            ("accountId", "0000000001"),
            ("alonePlayCapacity", "1"),
            ("bluetoothMac", "00:00:00:00:00:00"),
            ("canAlonePlayCtrl", "1"),
            ("channel", "center"),
            ("deviceId", "00000000-0000-0000-0000-000000000000"),
            ("deviceType", "4"),
            ("groupId", string.Empty),
            ("groupName", string.Empty),
            ("house_Id", "000000000000"),
            ("isMaster", "0"),
            ("miName", "小爱音箱Pro"),
            ("miotDid", "000000000"),
            ("model", "LX06"),
            ("p2pSupport", "0"),
            ("romVersion", "1.94.13"),
            ("roomName", "房间"),
            ("room_Id", "000000000000"),
            ("sn", "00000/000000000"),
            ("support", "audio"),
        };

        using var body = new MemoryStream();
        foreach (var (key, value) in fields)
        {
            var keyBytes = Encoding.ASCII.GetBytes(key);
            var valueBytes = Encoding.UTF8.GetBytes(value);
            body.WriteByte(checked((byte)keyBytes.Length));
            body.Write(keyBytes);
            body.WriteByte(MiPlayLegacyDeviceInfoPayloadCodec.StringValueType);
            body.WriteByte((byte)(valueBytes.Length >> 8));
            body.WriteByte((byte)valueBytes.Length);
            body.Write(valueBytes);
        }

        var bodyBytes = body.ToArray();
        return
        [
            (byte)(bodyBytes.Length >> 16),
            (byte)(bodyBytes.Length >> 8),
            (byte)bodyBytes.Length,
            .. bodyBytes,
        ];
    }
}
