using System.Buffers.Binary;
using System.Security.Cryptography;

namespace DLNACast.Core.MiPlay;

/// <summary>
/// A decrypted version-1 SafetyDataDeal payload. This model contains only data
/// already present in the supplied container and performs no network activity.
/// </summary>
public sealed record MiPlaySafetyDataDecodeResult(
    MiPlaySafetyDataHeader Header,
    byte[] Plaintext);

/// <summary>
/// Encodes and validates the version-1 SafetyDataDeal container recovered from
/// Xiaomi Interconnectivity Services 18.0.0.3. It uses AES-128-CBC with manual
/// zero padding and CRC-32/MPEG-2 over the ciphertext.
/// </summary>
public static class MiPlaySafetyDataCodec
{
    private const int AesBlockLength = 16;
    private const int Version1HeaderLength = 9;
    private const ushort Version1HeaderLengthMinusTwo = Version1HeaderLength - 2;
    private const byte Version1 = 1;
    private const byte Version1Flags = MiPlaySafetyDataHeaderCodec.EncryptionFlag |
        MiPlaySafetyDataHeaderCodec.PaddingLengthFieldFlag |
        MiPlaySafetyDataHeaderCodec.IntegrityFlag;
    private const uint Crc32Mpeg2Polynomial = 0x04C11DB7;

    /// <summary>
    /// Builds a version-1 SafetyDataDeal container. The native format always adds
    /// between one and sixteen zero bytes, including a whole block for aligned input.
    /// </summary>
    public static byte[] EncryptVersion1(
        ReadOnlySpan<byte> plaintext,
        ReadOnlySpan<byte> aesKey,
        ReadOnlySpan<byte> aesIv)
    {
        ValidateAesMaterial(aesKey, aesIv);
        var aesIvState = aesIv.ToArray();

        return EncryptVersion1WithState(plaintext, aesKey, aesIvState);
    }

    internal static byte[] EncryptVersion1WithState(
        ReadOnlySpan<byte> plaintext,
        ReadOnlySpan<byte> aesKey,
        Span<byte> aesIvState)
    {
        ValidateAesMaterial(aesKey, aesIvState);

        var paddingLength = AesBlockLength - plaintext.Length % AesBlockLength;
        var paddedPlaintext = new byte[checked(plaintext.Length + paddingLength)];
        plaintext.CopyTo(paddedPlaintext);
        var ciphertext = TransformCbcNoPadding(paddedPlaintext, aesKey, aesIvState, encrypt: true);
        var data = new byte[Version1HeaderLength + ciphertext.Length];

        BinaryPrimitives.WriteUInt16BigEndian(data, Version1HeaderLengthMinusTwo);
        data[2] = Version1;
        data[3] = Version1Flags;
        data[4] = checked((byte)paddingLength);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(5, sizeof(uint)), ComputeCrc32Mpeg2(ciphertext));
        ciphertext.CopyTo(data.AsSpan(Version1HeaderLength));
        return data;
    }

    /// <summary>
    /// Strictly validates and decrypts a version-1 SafetyDataDeal container.
    /// Invalid wire data returns false; invalid AES key or IV input throws.
    /// </summary>
    public static bool TryDecryptVersion1(
        ReadOnlySpan<byte> data,
        ReadOnlySpan<byte> aesKey,
        ReadOnlySpan<byte> aesIv,
        out MiPlaySafetyDataDecodeResult? result)
    {
        ValidateAesMaterial(aesKey, aesIv);
        var aesIvState = aesIv.ToArray();

        return TryDecryptVersion1WithState(data, aesKey, aesIvState, out result);
    }

    internal static bool TryDecryptVersion1WithState(
        ReadOnlySpan<byte> data,
        ReadOnlySpan<byte> aesKey,
        Span<byte> aesIvState,
        out MiPlaySafetyDataDecodeResult? result)
    {
        ValidateAesMaterial(aesKey, aesIvState);
        result = null;

        if (!MiPlaySafetyDataHeaderCodec.TryDecodeVersion1(data, out var header) ||
            header is null ||
            header.HeaderLength != Version1HeaderLength ||
            header.Flags != Version1Flags ||
            header.PaddingLength is not >= 1 and <= AesBlockLength ||
            header.PayloadLength == 0 ||
            header.PayloadLength % AesBlockLength != 0)
        {
            return false;
        }

        var ciphertext = data.Slice(header.PayloadOffset, header.PayloadLength);
        var expectedCrc = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(5, sizeof(uint)));
        if (ComputeCrc32Mpeg2(ciphertext) != expectedCrc)
        {
            return false;
        }

        try
        {
            var candidateAesIvState = aesIvState.ToArray();
            var paddedPlaintext = TransformCbcNoPadding(ciphertext, aesKey, candidateAesIvState, encrypt: false);
            var paddingLength = header.PaddingLength.GetValueOrDefault();
            if (paddingLength > paddedPlaintext.Length ||
                ContainsNonZero(paddedPlaintext.AsSpan(paddedPlaintext.Length - paddingLength, paddingLength)))
            {
                return false;
            }

            result = new MiPlaySafetyDataDecodeResult(
                header,
                paddedPlaintext.AsSpan(0, paddedPlaintext.Length - paddingLength).ToArray());
            candidateAesIvState.CopyTo(aesIvState);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>
    /// Computes CRC-32/MPEG-2 (poly 0x04C11DB7, init 0xffffffff, no final xor).
    /// The native container writes this value in little-endian byte order.
    /// </summary>
    public static uint ComputeCrc32Mpeg2(ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        foreach (var value in data)
        {
            crc ^= (uint)value << 24;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x80000000) != 0
                    ? (crc << 1) ^ Crc32Mpeg2Polynomial
                    : crc << 1;
            }
        }

        return crc;
    }

    private static byte[] TransformCbcNoPadding(
        ReadOnlySpan<byte> input,
        ReadOnlySpan<byte> aesKey,
        Span<byte> aesIvState,
        bool encrypt)
    {
        if (input.Length % AesBlockLength != 0)
        {
            throw new CryptographicException("MiPlay SafetyData CBC input must be block aligned.");
        }

        using var aes = Aes.Create();
        aes.Key = aesKey.ToArray();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        using var transform = encrypt ? aes.CreateEncryptor() : aes.CreateDecryptor();
        var inputBytes = input.ToArray();
        var output = new byte[inputBytes.Length];
        var scratch = new byte[AesBlockLength];

        for (var offset = 0; offset < inputBytes.Length; offset += AesBlockLength)
        {
            if (encrypt)
            {
                for (var index = 0; index < AesBlockLength; index++)
                {
                    scratch[index] = (byte)(inputBytes[offset + index] ^ aesIvState[index]);
                }

                if (transform.TransformBlock(scratch, 0, AesBlockLength, output, offset) != AesBlockLength)
                {
                    throw new CryptographicException("MiPlay SafetyData AES block encryption failed.");
                }

                output.AsSpan(offset, AesBlockLength).CopyTo(aesIvState);
            }
            else
            {
                if (transform.TransformBlock(inputBytes, offset, AesBlockLength, scratch, 0) != AesBlockLength)
                {
                    throw new CryptographicException("MiPlay SafetyData AES block decryption failed.");
                }

                for (var index = 0; index < AesBlockLength; index++)
                {
                    output[offset + index] = (byte)(scratch[index] ^ aesIvState[index]);
                }

                inputBytes.AsSpan(offset, AesBlockLength).CopyTo(aesIvState);
            }
        }

        return output;
    }

    private static bool ContainsNonZero(ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            if (value != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static void ValidateAesMaterial(ReadOnlySpan<byte> aesKey, ReadOnlySpan<byte> aesIv)
    {
        if (aesKey.Length != AesBlockLength)
        {
            throw new ArgumentException("MiPlay SafetyData version 1 requires a 16-byte AES key.", nameof(aesKey));
        }

        if (aesIv.Length != AesBlockLength)
        {
            throw new ArgumentException("MiPlay SafetyData version 1 requires a 16-byte AES IV.", nameof(aesIv));
        }
    }
}