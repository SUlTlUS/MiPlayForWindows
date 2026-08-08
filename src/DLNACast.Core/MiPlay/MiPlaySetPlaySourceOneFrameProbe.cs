namespace DLNACast.Core.MiPlay;

/// <summary>
/// Builds the prepared official-JSON SetPlaySource one-frame payload. This
/// builder is offline/testable only; callers must still pass
/// <see cref="MiPlaySetPlaySourceOneFrameProbePlan"/> before any live use.
/// </summary>
public static class MiPlaySetPlaySourceOneFrameProbe
{
    public static byte[] BuildMinimalOfficialPayload() =>
        MiPlaySetPlaySourcePayloadCodec.EncodeOfficialStatsDefaults(
            MiPlaySetPlaySourceOneFrameProbePlan.MinimalRefChannel,
            MiPlaySetPlaySourceOneFrameProbePlan.MinimalRefFunction,
            MiPlaySetPlaySourceOneFrameProbePlan.MinimalRefContent);

    public static byte[] ToCommandFrame(ushort sequence) => MiPlayCommandFrameCodec.Encode(
        MiPlayProtocolConstants.SetPlaySourceCommand,
        sequence,
        BuildMinimalOfficialPayload());

    public static byte[] ToSafetyDataCommandFrame(ushort sequence, MiPlaySafetyDataSessionCipher cipher)
    {
        ArgumentNullException.ThrowIfNull(cipher);

        return MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.SetPlaySourceCommand,
            sequence,
            cipher.EncryptVersion1(BuildMinimalOfficialPayload()));
    }
}
