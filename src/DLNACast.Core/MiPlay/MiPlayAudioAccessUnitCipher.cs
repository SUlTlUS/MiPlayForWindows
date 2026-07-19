using System.Security.Cryptography;

namespace DLNACast.Core.MiPlay;

public sealed record MiPlayEncryptedAccessUnit(byte[] StartingIv, byte[] Payload);

/// <summary>
/// Offline AES-CBC helper for the MiPlay audio access-unit encryption/decryption shape.
/// Complete 16-byte blocks are transformed, any tail shorter than one block remains
/// clear, and no SafetyData header, RTP, MPEG-TS, socket, or pacing behaviour is
/// performed here. Create one instance per CBC stream direction.
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

    public byte[] Decrypt(ReadOnlySpan<byte> encryptedAccessUnit)
    {
        var decrypted = encryptedAccessUnit.ToArray();
        var decryptableLength = encryptedAccessUnit.Length - encryptedAccessUnit.Length % AesBlockLength;
        if (decryptableLength == 0)
        {
            return decrypted;
        }

        var nextDecryptIv = decrypted.AsSpan(decryptableLength - AesBlockLength, AesBlockLength).ToArray();

        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        aes.Key = aesKey;
        aes.IV = nextIv;

        using var decryptor = aes.CreateDecryptor();
        var written = decryptor.TransformBlock(decrypted, 0, decryptableLength, decrypted, 0);
        if (written != decryptableLength)
        {
            throw new CryptographicException("Unexpected AES-CBC output length while decrypting MiPlay audio.");
        }

        nextDecryptIv.CopyTo(nextIv);
        return decrypted;
    }
}
