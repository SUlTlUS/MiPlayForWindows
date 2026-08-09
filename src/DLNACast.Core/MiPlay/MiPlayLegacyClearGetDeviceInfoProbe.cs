namespace DLNACast.Core.MiPlay;

/// <summary>
/// Builds the least-invasive LX06 mpas legacy Cmd_GetDeviceInfo probe frame.
/// The frame deliberately uses the legacy clear-text '$' command envelope with
/// an empty plaintext payload so it can test only the 0x001e -> 0x001f dispatcher
/// boundary after legacy auth, without SafetyData, source identity, open, or media.
/// </summary>
public static class MiPlayLegacyClearGetDeviceInfoProbe
{
    public static ReadOnlySpan<byte> EmptyPlaintextPayload => [];

    public static byte[] ToCommandFrame(ushort sequence) => MiPlayCommandFrameCodec.Encode(
        MiPlayProtocolConstants.GetDeviceInfoCommand,
        sequence,
        EmptyPlaintextPayload);
}