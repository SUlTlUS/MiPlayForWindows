using System.Buffers.Binary;

namespace DLNACast.Core.MiPlay;

/// <summary>
/// Structural decoder for the version-1 SafetyDataDeal container observed in the
/// Xiaomi Interconnectivity Services 18.0.0.3 native implementation.
/// This intentionally does not validate the proprietary CRC or decrypt payloads.
/// </summary>
public static class MiPlaySafetyDataHeaderCodec
{
    public const byte EncryptionFlag = 0x80;
    public const byte PaddingLengthFieldFlag = 0x40;
    public const byte IntegrityFlag = 0x20;

    /// <summary>
    /// Decodes only the static-confirmed version-1 header. It neither derives keys
    /// nor transforms the payload, so it is safe to use for offline diagnostics.
    /// </summary>
    public static bool TryDecodeVersion1(
        ReadOnlySpan<byte> data,
        out MiPlaySafetyDataHeader? header)
    {
        header = null;

        if (data.Length < 4)
        {
            return false;
        }

        var headerLength = BinaryPrimitives.ReadUInt16BigEndian(data) + 2;
        if (headerLength > data.Length || data[2] != 1)
        {
            return false;
        }

        var flags = data[3];
        var cursor = 4;
        byte? paddingLength = null;
        uint? integrityValue = null;

        if ((flags & PaddingLengthFieldFlag) != 0)
        {
            if (cursor >= headerLength)
            {
                return false;
            }

            paddingLength = data[cursor++];
        }

        if ((flags & IntegrityFlag) != 0)
        {
            if (headerLength - cursor < sizeof(uint))
            {
                return false;
            }

            integrityValue = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(cursor, sizeof(uint)));
            cursor += sizeof(uint);
        }

        // The recovered native version-1 layout has no unparsed extension bytes.
        if (cursor != headerLength)
        {
            return false;
        }

        var payloadLength = data.Length - headerLength;
        if (paddingLength.HasValue && paddingLength.Value > payloadLength)
        {
            return false;
        }

        header = new MiPlaySafetyDataHeader(
            HeaderLength: headerLength,
            Flags: flags,
            PaddingLength: paddingLength,
            IntegrityValue: integrityValue,
            PayloadOffset: headerLength,
            PayloadLength: payloadLength);
        return true;
    }
}

/// <summary>
/// Metadata from a SafetyDataDeal version-1 header. The payload remains untouched.
/// </summary>
public sealed record MiPlaySafetyDataHeader(
    int HeaderLength,
    byte Flags,
    byte? PaddingLength,
    uint? IntegrityValue,
    int PayloadOffset,
    int PayloadLength)
{
    public bool IsEncrypted => (Flags & MiPlaySafetyDataHeaderCodec.EncryptionFlag) != 0;
    public bool HasPaddingLengthField => (Flags & MiPlaySafetyDataHeaderCodec.PaddingLengthFieldFlag) != 0;
    public bool HasIntegrityValue => (Flags & MiPlaySafetyDataHeaderCodec.IntegrityFlag) != 0;
}
