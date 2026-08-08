namespace DLNACast.Core.MiPlay;

public sealed record MiPlayLegacySystemAudioLiveValidationSnapshot(
    string ValidationDate,
    string ReceiverAddress,
    string SourceAddress,
    string ReceiverFirmwareVersion,
    ushort ReceiverChallengeSequence,
    string WindowsOutputEndpoint,
    int CaptureBufferMillisecondsAtStart,
    long CaptureOverruns,
    long CaptureUnderruns,
    int AacBitRate,
    int MediaAccessUnitCount,
    long MediaWireBytes,
    double MediaDurationMilliseconds,
    IReadOnlyList<int> ReceiverReverseTcpSourcePorts,
    int ReceiverTimerSourcePort,
    bool LocalTestToneInjected,
    bool FullControlSequenceAccepted,
    bool ReverseRtspReachedReady,
    bool SystemLoopbackCaptureStarted,
    bool MediaWriteCompleted,
    bool AddMirrorSent,
    bool PauseOrResumeSent,
    bool RetryOrFallbackUsed,
    bool ProvesSystemAudioTransportAccepted,
    bool UserConfirmedAudibleAtReceiver);

/// <summary>
/// Redacted facts from the explicitly authorized 2026-08-07 Windows default
/// output -> WASAPI loopback -> FFmpeg -> MiPlay/WFD live validation. Creating
/// the snapshot is pure and performs no capture or network operation.
/// </summary>
public static class MiPlayLegacySystemAudioLiveValidationEvidence
{
    public const string ValidationDate = "2026-08-07";
    public const string ReceiverAddress = "192.168.10.4";
    public const string SourceAddress = "192.168.10.9";
    public const string ReceiverFirmwareVersion = "1.94.13";
    public const ushort ReceiverChallengeSequence = 0x03ea;
    public const string WindowsOutputEndpoint = "扬声器 (Realtek(R) Audio)";
    public const int CaptureBufferMillisecondsAtStart = 40;
    public const long CaptureOverruns = 3;
    public const long CaptureUnderruns = 0;
    public const int AacBitRate = 192_000;
    public const int MediaAccessUnitCount = 240;
    public const long MediaWireBytes = 178_868;
    public const double MediaDurationMilliseconds = 5_120;
    public const int ReceiverRtspSourcePort = 50306;
    public const int ReceiverSecondTcpSourcePort = 50310;
    public const int ReceiverAudioSourcePort = 50312;
    public const int ReceiverTimerSourcePort = 50639;

    public static MiPlayLegacySystemAudioLiveValidationSnapshot CreateCurrentSnapshot() =>
        new(
            ValidationDate,
            ReceiverAddress,
            SourceAddress,
            ReceiverFirmwareVersion,
            ReceiverChallengeSequence,
            WindowsOutputEndpoint,
            CaptureBufferMillisecondsAtStart,
            CaptureOverruns,
            CaptureUnderruns,
            AacBitRate,
            MediaAccessUnitCount,
            MediaWireBytes,
            MediaDurationMilliseconds,
            [ReceiverRtspSourcePort, ReceiverSecondTcpSourcePort, ReceiverAudioSourcePort],
            ReceiverTimerSourcePort,
            LocalTestToneInjected: true,
            FullControlSequenceAccepted: true,
            ReverseRtspReachedReady: true,
            SystemLoopbackCaptureStarted: true,
            MediaWriteCompleted: true,
            AddMirrorSent: false,
            PauseOrResumeSent: false,
            RetryOrFallbackUsed: false,
            ProvesSystemAudioTransportAccepted: true,
            UserConfirmedAudibleAtReceiver: false);

    public static bool IsSuccessfulBoundedTransport(
        MiPlayLegacySystemAudioLiveValidationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.FullControlSequenceAccepted &&
               snapshot.ReverseRtspReachedReady &&
               snapshot.SystemLoopbackCaptureStarted &&
               snapshot.MediaWriteCompleted &&
               snapshot.MediaAccessUnitCount == MediaAccessUnitCount &&
               snapshot.MediaDurationMilliseconds == MediaDurationMilliseconds &&
               snapshot.CaptureUnderruns == 0 &&
               !snapshot.AddMirrorSent &&
               !snapshot.PauseOrResumeSent &&
               !snapshot.RetryOrFallbackUsed &&
               snapshot.ProvesSystemAudioTransportAccepted;
    }
}
