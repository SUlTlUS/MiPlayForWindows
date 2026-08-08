using System.Security.Cryptography;

namespace DLNACast.Core.MiPlay;

public sealed record MiPlayCapturedSafetyDataDecryptResult(
    bool FirstBlockKnown,
    int KnownPlaintextOffset,
    byte PaddingLength,
    bool PaddingValid,
    byte[] KnownPaddedPlaintext,
    byte[] KnownPlaintext);

/// <summary>
/// Offline helper for decrypting a mid-session SafetyData capture when the
/// initial per-direction CBC IV is missing. After the first captured frame for a
/// direction, the previous frame's last ciphertext block is sufficient to
/// recover the next frame's first plaintext block.
/// </summary>
public sealed class MiPlayCapturedSafetyDataChainDecryptor
{
    private const int AesBlockLength = 16;
    private const int Version1HeaderLength = 9;
    private const byte Version1Flags = MiPlaySafetyDataHeaderCodec.EncryptionFlag |
        MiPlaySafetyDataHeaderCodec.PaddingLengthFieldFlag |
        MiPlaySafetyDataHeaderCodec.IntegrityFlag;

    private readonly byte[] aesKey;
    private readonly Dictionary<string, byte[]> lastCiphertextBlockByDirection = new(StringComparer.Ordinal);

    public MiPlayCapturedSafetyDataChainDecryptor(ReadOnlySpan<byte> aesKey)
    {
        if (aesKey.Length != AesBlockLength)
        {
            throw new ArgumentException("MiPlay SafetyData version 1 requires a 16-byte AES key.", nameof(aesKey));
        }

        this.aesKey = aesKey.ToArray();
    }

    public bool TryDecryptVersion1Continuation(
        string directionKey,
        ReadOnlySpan<byte> safetyData,
        out MiPlayCapturedSafetyDataDecryptResult? result)
    {
        ArgumentException.ThrowIfNullOrEmpty(directionKey);
        result = null;

        if (!MiPlaySafetyDataHeaderCodec.TryDecodeVersion1(safetyData, out var header) ||
            header is null ||
            header.HeaderLength != Version1HeaderLength ||
            header.Flags != Version1Flags ||
            header.PaddingLength is not >= 1 and <= AesBlockLength ||
            header.PayloadLength == 0 ||
            header.PayloadLength % AesBlockLength != 0)
        {
            return false;
        }

        var ciphertext = safetyData.Slice(header.PayloadOffset, header.PayloadLength);
        if (MiPlaySafetyDataCodec.ComputeNativeWireIntegrityValue(ciphertext) != header.IntegrityValue)
        {
            return false;
        }

        var firstBlockKnown = lastCiphertextBlockByDirection.TryGetValue(directionKey, out var previousCiphertextBlock);
        var knownPlaintextOffset = firstBlockKnown ? 0 : AesBlockLength;
        var knownPaddedPlaintextLength = Math.Max(0, ciphertext.Length - knownPlaintextOffset);
        var knownPaddedPlaintext = new byte[knownPaddedPlaintextLength];
        using var aes = Aes.Create();
        aes.Key = aesKey;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        using var transform = aes.CreateDecryptor();
        var decryptedBlock = new byte[AesBlockLength];

        for (var offset = 0; offset < ciphertext.Length; offset += AesBlockLength)
        {
            var currentCiphertextBlock = ciphertext.Slice(offset, AesBlockLength);
            var currentCiphertextBytes = currentCiphertextBlock.ToArray();
            if (transform.TransformBlock(
                    currentCiphertextBytes,
                    0,
                    AesBlockLength,
                    decryptedBlock,
                    0) != AesBlockLength)
            {
                throw new CryptographicException("MiPlay SafetyData AES block decryption failed.");
            }

            var xorBlock = offset == 0
                ? previousCiphertextBlock
                : ciphertext.Slice(offset - AesBlockLength, AesBlockLength).ToArray();

            if (xorBlock is not null)
            {
                var destinationOffset = offset - knownPlaintextOffset;
                for (var index = 0; index < AesBlockLength; index++)
                {
                    knownPaddedPlaintext[destinationOffset + index] =
                        (byte)(decryptedBlock[index] ^ xorBlock[index]);
                }
            }
        }

        lastCiphertextBlockByDirection[directionKey] =
            ciphertext.Slice(ciphertext.Length - AesBlockLength, AesBlockLength).ToArray();

        var paddingLength = header.PaddingLength.GetValueOrDefault();
        var paddingValid = knownPaddedPlaintext.Length >= paddingLength &&
            !ContainsNonZero(knownPaddedPlaintext.AsSpan(knownPaddedPlaintext.Length - paddingLength, paddingLength));
        var knownPlaintext = paddingValid
            ? knownPaddedPlaintext.AsSpan(0, knownPaddedPlaintext.Length - paddingLength).ToArray()
            : knownPaddedPlaintext;

        result = new MiPlayCapturedSafetyDataDecryptResult(
            firstBlockKnown,
            knownPlaintextOffset,
            paddingLength,
            paddingValid,
            knownPaddedPlaintext,
            knownPlaintext);
        return true;
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
}
