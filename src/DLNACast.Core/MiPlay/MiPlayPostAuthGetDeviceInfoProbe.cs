namespace DLNACast.Core.MiPlay;

/// <summary>
/// Builds the bounded post-auth Cmd_GetDeviceInfo read-only probe frame.
/// The plaintext payload is empty, matching CmdSource::getDeviceInfo static
/// evidence. The caller owns SafetyData cipher state and network authorization.
/// </summary>
public static class MiPlayPostAuthGetDeviceInfoProbe
{
    public static ReadOnlySpan<byte> EmptyPlaintextPayload => ReadOnlySpan<byte>.Empty;

    public static byte[] ToSafetyDataCommandFrame(ushort sequence, MiPlaySafetyDataSessionCipher cipher)
    {
        ArgumentNullException.ThrowIfNull(cipher);

        return MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.GetDeviceInfoCommand,
            sequence,
            cipher.EncryptVersion1(EmptyPlaintextPayload));
    }
}
