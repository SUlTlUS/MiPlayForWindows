namespace DLNACast.Core.MiPlay;

/// <summary>
/// Maintains the native SafetyDataDeal-style AES-CBC state for one MiPlay safety session.
/// Native code constructs separate encrypt and decrypt AES contexts from the same key/IV;
/// each AES_CBC_* call advances only that direction's IV state.
/// </summary>
public sealed class MiPlaySafetyDataSessionCipher
{
    private const int AesBlockLength = 16;

    private readonly byte[] aesKey;
    private readonly byte[] encryptIvState;
    private readonly byte[] decryptIvState;

    public MiPlaySafetyDataSessionCipher(ReadOnlySpan<byte> aesKey, ReadOnlySpan<byte> aesIv)
    {
        if (aesKey.Length != AesBlockLength)
        {
            throw new ArgumentException("MiPlay SafetyData version 1 requires a 16-byte AES key.", nameof(aesKey));
        }

        if (aesIv.Length != AesBlockLength)
        {
            throw new ArgumentException("MiPlay SafetyData version 1 requires a 16-byte AES IV.", nameof(aesIv));
        }

        this.aesKey = aesKey.ToArray();
        encryptIvState = aesIv.ToArray();
        decryptIvState = aesIv.ToArray();
    }

    public byte[] EncryptVersion1(ReadOnlySpan<byte> plaintext) =>
        MiPlaySafetyDataCodec.EncryptVersion1WithState(plaintext, aesKey, encryptIvState);

    public bool TryDecryptVersion1(
        ReadOnlySpan<byte> data,
        out MiPlaySafetyDataDecodeResult? result) =>
        MiPlaySafetyDataCodec.TryDecryptVersion1WithState(data, aesKey, decryptIvState, out result);
}