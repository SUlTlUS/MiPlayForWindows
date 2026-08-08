namespace DLNACast.Core.MiPlay;

public sealed record MiPlayLegacyProAudibleSystemAudioLiveValidationSnapshot(
    string ValidationDate,
    string ReceiverAddress,
    string ReceiverFriendlyName,
    string SourceAddress,
    string ReceiverFirmwareVersion,
    ushort ReceiverChallengeSequence,
    IReadOnlyList<int> ReceiverReverseTcpSourcePorts,
    int ReceiverTimerSourcePort,
    string Encoder,
    int AacBitRate,
    int SampleRate,
    int Channels,
    long TimeOffsetMicroseconds,
    long InitialPcr90Khz,
    int MediaAccessUnitCount,
    int MediaRtpFrameCount,
    int FragmentedExtraRtpFrameCount,
    long MediaWireBytes,
    double MediaDurationMilliseconds,
    long CaptureOverruns,
    long CaptureUnderruns,
    double NonZeroSamplePercentage,
    double PeakAmplitude,
    double RmsAmplitude,
    double RmsDbfs,
    bool ReceiverLightBarActivated,
    bool UserConfirmedAudibleAtReceiver,
    bool MediaWriteCompleted,
    bool AddMirrorSent,
    bool PauseOrResumeSent,
    bool RetryOrFallbackUsed,
    bool AlternateTargetUsed,
    bool UnsupportedReadOnlyNotificationObservedAfterMedia,
    bool ProvesUsableBoundedSystemAudio,
    bool ProvesMainApplicationIntegration,
    bool ProvesIndefiniteStreaming);

/// <summary>
/// Immutable facts from the explicitly authorized 2026-08-07 bounded Windows
/// system-audio validation against the bedroom XiaoAI Speaker Pro. Creating
/// the snapshot is pure and performs no capture or network operation.
/// </summary>
public static class MiPlayLegacyProAudibleSystemAudioLiveValidationEvidence
{
    public static MiPlayLegacyProAudibleSystemAudioLiveValidationSnapshot CreateCurrentSnapshot() =>
        new(
            ValidationDate: "2026-08-07",
            ReceiverAddress: "192.168.10.3",
            ReceiverFriendlyName: "次卧的小爱音箱 Pro",
            SourceAddress: "192.168.10.9",
            ReceiverFirmwareVersion: "1.94.13",
            ReceiverChallengeSequence: 0x0296,
            ReceiverReverseTcpSourcePorts: [39122, 39126, 39128],
            ReceiverTimerSourcePort: 33822,
            Encoder: "aac_mf",
            AacBitRate: 256_000,
            SampleRate: 48_000,
            Channels: 2,
            TimeOffsetMicroseconds: 1_415_153_637_704,
            InitialPcr90Khz: 7_104_653_105,
            MediaAccessUnitCount: 938,
            MediaRtpFrameCount: 964,
            FragmentedExtraRtpFrameCount: 26,
            MediaWireBytes: 848_640,
            MediaDurationMilliseconds: 20_010.7,
            CaptureOverruns: 1,
            CaptureUnderruns: 0,
            NonZeroSamplePercentage: 80.077,
            PeakAmplitude: 0.906921,
            RmsAmplitude: 0.109914,
            RmsDbfs: -19.18,
            ReceiverLightBarActivated: true,
            UserConfirmedAudibleAtReceiver: true,
            MediaWriteCompleted: true,
            AddMirrorSent: false,
            PauseOrResumeSent: false,
            RetryOrFallbackUsed: false,
            AlternateTargetUsed: false,
            UnsupportedReadOnlyNotificationObservedAfterMedia: true,
            ProvesUsableBoundedSystemAudio: true,
            ProvesMainApplicationIntegration: false,
            ProvesIndefiniteStreaming: false);

    public static bool IsSuccessfulAudibleBoundedValidation(
        MiPlayLegacyProAudibleSystemAudioLiveValidationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.MediaWriteCompleted &&
               snapshot.MediaAccessUnitCount == 938 &&
               snapshot.MediaRtpFrameCount == 964 &&
               snapshot.FragmentedExtraRtpFrameCount == 26 &&
               snapshot.CaptureUnderruns == 0 &&
               snapshot.ReceiverLightBarActivated &&
               snapshot.UserConfirmedAudibleAtReceiver &&
               !snapshot.AddMirrorSent &&
               !snapshot.PauseOrResumeSent &&
               !snapshot.RetryOrFallbackUsed &&
               !snapshot.AlternateTargetUsed &&
               snapshot.ProvesUsableBoundedSystemAudio;
    }
}
