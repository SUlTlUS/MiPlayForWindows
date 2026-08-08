using System.Net;
using System.Security.Cryptography;

namespace DLNACast.Core.MiPlay;

public sealed record MiPlayLegacySilencePlaybackMediaSummary(
    int AccessUnitCount,
    int ProgramTablePacketCount,
    int SteadyPacketCount,
    long WireBytes,
    double DurationMilliseconds,
    IReadOnlyList<int> ProgramTableAccessUnitIndexes);

public sealed record MiPlayLegacySilencePlaybackLiveValidationSnapshot(
    string ValidationDate,
    string ReceiverAddress,
    string SourceAddress,
    string ReceiverFirmwareVersion,
    ushort ReceiverChallengeSequence,
    int BootstrapFrameCount,
    int PlaybackContinuationFrameCount,
    int DeviceInfoPayloadLength,
    byte[] SetPlaySourceFrame,
    byte[] OpenFrame,
    int SourceListenerPort,
    int TimerServerPort,
    IReadOnlyList<int> ReceiverReverseTcpSourcePorts,
    int ReceiverTimerSourcePort,
    MiPlayLegacySilencePlaybackMediaSummary Media,
    bool BootstrapAccepted,
    bool PlaybackContinuationAccepted,
    bool ReverseRtspReachedReady,
    bool TimerExchangeObserved,
    bool MediaWriteCompleted,
    bool AddMirrorSent,
    bool PauseOrResumeSent,
    bool UserAudioSent,
    bool RetryOrFallbackUsed,
    bool ProvesWindowsSourceTransportAccepted,
    bool ProvesAudibleUserAudio);

/// <summary>
/// Immutable, non-secret evidence from the explicitly authorized 2026-08-07
/// Windows-to-LX06 silence-only live validation. The snapshot contains no
/// receiver device-info payload and performs no network operation.
/// </summary>
public static class MiPlayLegacySilencePlaybackLiveValidationEvidence
{
    public const string ValidationDate = "2026-08-07";
    public const string ReceiverAddress = "192.168.10.4";
    public const string SourceAddress = "192.168.10.9";
    public const string ReceiverFirmwareVersion = "1.94.13";
    public const ushort ReceiverChallengeSequence = 0x03bc;

    public const int BootstrapFrameCount = 9;
    public const int PlaybackContinuationFrameCount = 7;
    public const int DeviceInfoPayloadLength = 415;
    public const ushort SetPlaySourceSequence = 13;
    public const ushort OpenSequence = 14;

    public const int SourceListenerPort = 7274;
    public const int TimerServerPort = 36524;
    public const int ReceiverRtspSourcePort = 50256;
    public const int ReceiverSecondTcpSourcePort = 50260;
    public const int ReceiverAudioSourcePort = 50262;
    public const int ReceiverTimerSourcePort = 34994;

    public const int MediaAccessUnitCount = 48;
    public const int ProgramTablePacketCount = 9;
    public const int SteadyPacketCount = 39;
    public const long MediaWireBytes = 14_868;
    public const double MediaDurationMilliseconds = 1_024;

    public const string SetPlaySourceFrameSha256Hex =
        "5450DE56ADCD4946052E35F9897A5F2258FA5A943F61DFA008A2666F09275F93";
    public const string OpenFrameSha256Hex =
        "5B89F6951449BC45CEE745D669050CF30FB290C1583E1EA42061578299A3B851";

    public static MiPlayLegacySilencePlaybackLiveValidationSnapshot CreateCurrentSnapshot()
    {
        var setPlaySourceFrame = MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.SetPlaySourceCommand,
            SetPlaySourceSequence,
            MiPlaySetPlaySourcePayloadCodec.Encode(
                MiPlayRealLegacyPlaybackSessionEvidence.SetPlaySourceRefChannel,
                MiPlayRealLegacyPlaybackSessionEvidence.SetPlaySourceRefFunction,
                MiPlayRealLegacyPlaybackSessionEvidence.SetPlaySourceRefContent));
        var openFrame = new MiPlayOpenDeviceRequest(
                IPAddress.Parse(SourceAddress),
                SourceListenerPort)
            .ToCommandFrame(OpenSequence);

        return new MiPlayLegacySilencePlaybackLiveValidationSnapshot(
            ValidationDate,
            ReceiverAddress,
            SourceAddress,
            ReceiverFirmwareVersion,
            ReceiverChallengeSequence,
            BootstrapFrameCount,
            PlaybackContinuationFrameCount,
            DeviceInfoPayloadLength,
            setPlaySourceFrame,
            openFrame,
            SourceListenerPort,
            TimerServerPort,
            [ReceiverRtspSourcePort, ReceiverSecondTcpSourcePort, ReceiverAudioSourcePort],
            ReceiverTimerSourcePort,
            ReconstructMediaSummary(),
            BootstrapAccepted: true,
            PlaybackContinuationAccepted: true,
            ReverseRtspReachedReady: true,
            TimerExchangeObserved: true,
            MediaWriteCompleted: true,
            AddMirrorSent: false,
            PauseOrResumeSent: false,
            UserAudioSent: false,
            RetryOrFallbackUsed: false,
            ProvesWindowsSourceTransportAccepted: true,
            ProvesAudibleUserAudio: false);
    }

    public static MiPlayLegacySilencePlaybackMediaSummary ReconstructMediaSummary()
    {
        // This reconstructs the historical Windows silence run, which used
        // the then-observed 0,10,15,... table cadence. The current clean-phone
        // default is deliberately kept separate at 0,13,18,... .
        var packetizer = new MiPlayWfdAudioPacketizer(
            firstPeriodicTableAccessUnitIndex: 10,
            periodicTableAccessUnitInterval: 5);
        var silence = MiPlayAacSilenceAccessUnit.Create();
        var packets = Enumerable.Range(0, MediaAccessUnitCount)
            .Select(_ => packetizer.Packetize(silence))
            .ToArray();
        var tableIndexes = packets
            .Select((packet, index) => (packet, index))
            .Where(item => item.packet.ContainsProgramTables)
            .Select(item => item.index)
            .ToArray();

        return new MiPlayLegacySilencePlaybackMediaSummary(
            packets.Length,
            tableIndexes.Length,
            packets.Length - tableIndexes.Length,
            packets.Sum(packet => (long)packet.WireFrame.Length),
            packets.Length * 1024d / 48_000d * 1000d,
            tableIndexes);
    }

    public static bool MatchesPinnedControlFrameHashes(
        MiPlayLegacySilencePlaybackLiveValidationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return Hash(snapshot.SetPlaySourceFrame) == SetPlaySourceFrameSha256Hex &&
               Hash(snapshot.OpenFrame) == OpenFrameSha256Hex;
    }

    private static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));
}
