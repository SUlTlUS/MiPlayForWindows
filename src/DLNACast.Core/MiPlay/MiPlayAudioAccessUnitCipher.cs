using System.Security.Cryptography;

namespace DLNACast.Core.MiPlay;

public sealed record MiPlayEncryptedAccessUnit(byte[] StartingIv, byte[] Payload);

/// <summary>
/// Offline AES-CBC helper for the MiPlay audio access-unit encryption shape.
/// Complete 16-byte blocks are encrypted, any tail shorter than one block remains
/// clear, and no SafetyData header, RTP, MPEG-TS, socket, or pacing behaviour is
/// performed here.
/// </summary>
public sealed class MiPlayAudioAccessUnitCipher
{
    private const int AesBlockLength = 16;

    private readonly byte[] aesKey;
    private readonly byte[] nextIv;

    public MiPlayAudioAccessUnitCipher(ReadOnlySpan<byte> streamKey, ReadOnlySpan<byte> streamIv)
    {
        if (streamKey.Length != AesBlockLength)
        {
            throw new ArgumentException("MiPlay audio access-unit encryption requires a 16-byte AES key.", nameof(streamKey));
        }
        if (streamIv.Length != AesBlockLength)
        {
            throw new ArgumentException("MiPlay audio access-unit encryption requires a 16-byte AES IV.", nameof(streamIv));
        }

        aesKey = streamKey.ToArray();
        nextIv = streamIv.ToArray();
    }

    public MiPlayEncryptedAccessUnit Encrypt(ReadOnlySpan<byte> accessUnit)
    {
        var startingIv = nextIv.ToArray();
        var encrypted = accessUnit.ToArray();
        var encryptableLength = accessUnit.Length - accessUnit.Length % AesBlockLength;
        if (encryptableLength == 0)
        {
            return new MiPlayEncryptedAccessUnit(startingIv, encrypted);
        }

        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        aes.Key = aesKey;
        aes.IV = nextIv;

        using var encryptor = aes.CreateEncryptor();
        var written = encryptor.TransformBlock(encrypted, 0, encryptableLength, encrypted, 0);
        if (written != encryptableLength)
        {
            throw new CryptographicException("Unexpected AES-CBC output length while encrypting MiPlay audio.");
        }

        encrypted.AsSpan(encryptableLength - AesBlockLength, AesBlockLength).CopyTo(nextIv);
        return new MiPlayEncryptedAccessUnit(startingIv, encrypted);
    }
}
