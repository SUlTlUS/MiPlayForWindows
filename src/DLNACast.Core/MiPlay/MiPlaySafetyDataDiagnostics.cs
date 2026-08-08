using System.Buffers.Binary;

namespace DLNACast.Core.MiPlay;

/// <summary>
/// Read-only SafetyData diagnostics used by constrained probes. It never derives
/// keys, decrypts payloads, or mutates session CBC state.
/// </summary>
public static class MiPlaySafetyDataDiagnostics
{
    private const int Version1HeaderLength = 9;
    private const int AesBlockLength = 16;
    private const byte Version1Flags = MiPlaySafetyDataHeaderCodec.EncryptionFlag |
        MiPlaySafetyDataHeaderCodec.PaddingLengthFieldFlag |
        MiPlaySafetyDataHeaderCodec.IntegrityFlag;

    public static string DescribeVersion1DecodeFailure(ReadOnlySpan<byte> data)
    {
        if (!MiPlaySafetyDataHeaderCodec.TryDecodeVersion1(data, out var header) ||
            header is null)
        {
            return $"header=invalid,length={data.Length}";
        }

        var prefix =
            $"header=ok,headerLength={header.HeaderLength},flags=0x{header.Flags:X2},paddingLength={FormatNullable(header.PaddingLength)},payloadLength={header.PayloadLength}";

        if (header.HeaderLength != Version1HeaderLength)
        {
            return $"{prefix},failure=unexpected-header-length";
        }

        if (header.Flags != Version1Flags)
        {
            return $"{prefix},failure=unexpected-flags";
        }

        if (header.PaddingLength is not >= 1 and <= AesBlockLength)
        {
            return $"{prefix},failure=invalid-padding-length";
        }

        if (header.PayloadLength == 0)
        {
            return $"{prefix},failure=empty-ciphertext";
        }

        if (header.PayloadLength % AesBlockLength != 0)
        {
            return $"{prefix},failure=unaligned-ciphertext";
        }

        var ciphertext = data.Slice(header.PayloadOffset, header.PayloadLength);
        var storedCrc = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(5, sizeof(uint)));
        var computedCrc = MiPlaySafetyDataCodec.ComputeNativeWireIntegrityValue(ciphertext);
        var crcPrefix = $"{prefix},storedCrc=0x{storedCrc:X8},computedCrc=0x{computedCrc:X8}";
        return storedCrc == computedCrc
            ? $"{crcPrefix},failure=decrypt-or-padding"
            : $"{crcPrefix},failure=crc-mismatch";
    }

    private static string FormatNullable(byte? value) =>
        value.HasValue ? value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "none";
}
