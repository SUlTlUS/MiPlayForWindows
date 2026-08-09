namespace DLNACast.Core.MiPlay;

/// <summary>
/// Builds the least-invasive LX06 mpas Cmd_SetPlaySource probe frame.
/// Static receiver evidence shows 0x0040 sends 0x0041 before checking payload
/// length or parsing JSON, so the ACK-only probe deliberately uses an empty
/// plaintext payload to avoid mutating source identity fields.
/// </summary>
public static class MiPlaySetPlaySourceAckProbe
{
    public static ReadOnlySpan<byte> EmptyPlaintextPayload => [];

    public static byte[] ToCommandFrame(ushort sequence) => MiPlayCommandFrameCodec.Encode(
        MiPlayProtocolConstants.SetPlaySourceCommand,
        sequence,
        EmptyPlaintextPayload);

    public static byte[] ToSafetyDataCommandFrame(ushort sequence, MiPlaySafetyDataSessionCipher cipher)
    {
        ArgumentNullException.ThrowIfNull(cipher);

        return MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.SetPlaySourceCommand,
            sequence,
            cipher.EncryptVersion1(EmptyPlaintextPayload));
    }
}