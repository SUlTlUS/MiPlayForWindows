using System.Security.Cryptography;

namespace DLNACast.Core.MiPlay;

/// <summary>
/// Produces bounded, payload-free fingerprints for comparing the application
/// transport with the validated Probe. No raw media or receiver metadata is
/// retained in these diagnostics.
/// </summary>
internal static class MiPlayRuntimeWireEvidence
{
    public static string DescribeSetMediaInfo(ReadOnlySpan<byte> frameBytes)
    {
        if (!MiPlayCommandFrameCodec.TryDecode(frameBytes, out var frame, out var consumed) ||
            frame is null || consumed != frameBytes.Length ||
            frame.Command != MiPlayProtocolConstants.SetMediaInfoCommand ||
            !MiPlaySetMediaInfoPayloadCodec.TryDecode(frame.Payload, out var mediaInfo) ||
            mediaInfo is null)
        {
            throw new InvalidDataException("SetMediaInfo evidence requires one valid 0x0012 frame.");
        }

        return
            $"setMediaInfo command=0x{frame.Command:X4}, sequence=0x{frame.Sequence:X4}, " +
            $"payloadLength={frame.Payload.Length}, payloadSha256={Hash(frame.Payload)}, " +
            $"frameSha256={Hash(frameBytes)}, durationMs={mediaInfo.DurationMilliseconds}, " +
            $"status={mediaInfo.Status}, deviceState={mediaInfo.DeviceState}, sourceName={mediaInfo.SourceName}";
    }

    public static string DescribeFirstMediaBatch(
        ReadOnlySpan<byte> accessUnit,
        ReadOnlySpan<byte> wireBytes,
        int rtpFrameCount)
    {
        if (accessUnit.IsEmpty || wireBytes.IsEmpty || rtpFrameCount is < 1 or > 2)
        {
            throw new ArgumentException("First-media evidence requires one non-empty AAC access unit and one or two RTP frames.");
        }

        return
            $"firstMedia accessUnitLength={accessUnit.Length}, accessUnitSha256={Hash(accessUnit)}, " +
            $"rtpFrameCount={rtpFrameCount}, wireLength={wireBytes.Length}, wireSha256={Hash(wireBytes)}";
    }

    public static string DescribePostOpenInbound(
        ReadOnlySpan<byte> frameBytes,
        MiPlayLegacyPostOpenPlaybackTransition transition,
        MiPlayLegacyPostOpenPlaybackSession session)
    {
        ArgumentNullException.ThrowIfNull(transition);
        ArgumentNullException.ThrowIfNull(session);
        if (!MiPlayCommandFrameCodec.TryDecode(frameBytes, out var frame, out var consumed) ||
            frame is null || consumed != frameBytes.Length)
        {
            throw new InvalidDataException("Post-Open evidence requires one valid command frame.");
        }

        var decoded = transition.Notify is { } notify
            ? notify.IntegerValue is { } integerValue
                ? $"label={notify.Label}, integerValue={integerValue}"
                : $"label={notify.Label}, objectFields={string.Join(',', notify.Fields.Select(field => field.Name))}"
            : "decodedNotify=0";
        return
            $"postOpenInbound command=0x{frame.Command:X4}, sequence=0x{frame.Sequence:X4}, " +
            $"payloadLength={frame.Payload.Length}, payloadSha256={Hash(frame.Payload)}, {decoded}, " +
            $"firstAudioPcm={(session.FirstAudioPcmObserved ? 1 : 0)}, " +
            $"receiverState={session.ReceiverState?.ToString() ?? "unset"}, " +
            $"unsupportedNotifications={session.UnsupportedNotificationCount}";
    }

    private static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));
}
