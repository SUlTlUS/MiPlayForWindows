using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayLegacyProAudibleSystemAudioLiveValidationEvidenceTests
{
    [Fact]
    public void PinsTheUserConfirmedAudibleProValidation()
    {
        var snapshot = MiPlayLegacyProAudibleSystemAudioLiveValidationEvidence.CreateCurrentSnapshot();

        Assert.Equal("2026-08-07", snapshot.ValidationDate);
        Assert.Equal("192.168.10.3", snapshot.ReceiverAddress);
        Assert.Equal("次卧的小爱音箱 Pro", snapshot.ReceiverFriendlyName);
        Assert.Equal("192.168.10.9", snapshot.SourceAddress);
        Assert.Equal("1.94.13", snapshot.ReceiverFirmwareVersion);
        Assert.Equal((ushort)0x0296, snapshot.ReceiverChallengeSequence);
        Assert.Equal([39122, 39126, 39128], snapshot.ReceiverReverseTcpSourcePorts);
        Assert.Equal(33822, snapshot.ReceiverTimerSourcePort);
        Assert.Equal("aac_mf", snapshot.Encoder);
        Assert.Equal(256_000, snapshot.AacBitRate);
        Assert.Equal(48_000, snapshot.SampleRate);
        Assert.Equal(2, snapshot.Channels);
        Assert.Equal(1_415_153_637_704, snapshot.TimeOffsetMicroseconds);
        Assert.Equal(7_104_653_105, snapshot.InitialPcr90Khz);
        Assert.Equal(938, snapshot.MediaAccessUnitCount);
        Assert.Equal(964, snapshot.MediaRtpFrameCount);
        Assert.Equal(26, snapshot.FragmentedExtraRtpFrameCount);
        Assert.Equal(848_640, snapshot.MediaWireBytes);
        Assert.Equal(20_010.7, snapshot.MediaDurationMilliseconds);
        Assert.Equal(1, snapshot.CaptureOverruns);
        Assert.Equal(0, snapshot.CaptureUnderruns);
        Assert.Equal(80.077, snapshot.NonZeroSamplePercentage);
        Assert.Equal(0.906921, snapshot.PeakAmplitude);
        Assert.Equal(0.109914, snapshot.RmsAmplitude);
        Assert.Equal(-19.18, snapshot.RmsDbfs);
        Assert.True(snapshot.ReceiverLightBarActivated);
        Assert.True(snapshot.UserConfirmedAudibleAtReceiver);
        Assert.True(snapshot.UnsupportedReadOnlyNotificationObservedAfterMedia);
        Assert.True(MiPlayLegacyProAudibleSystemAudioLiveValidationEvidence
            .IsSuccessfulAudibleBoundedValidation(snapshot));
    }

    [Fact]
    public void KeepsBoundedProbeProofSeparateFromApplicationAndLongRunProof()
    {
        var snapshot = MiPlayLegacyProAudibleSystemAudioLiveValidationEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.ProvesUsableBoundedSystemAudio);
        Assert.False(snapshot.ProvesMainApplicationIntegration);
        Assert.False(snapshot.ProvesIndefiniteStreaming);
        Assert.False(snapshot.AddMirrorSent);
        Assert.False(snapshot.PauseOrResumeSent);
        Assert.False(snapshot.RetryOrFallbackUsed);
        Assert.False(snapshot.AlternateTargetUsed);
    }

    [Fact]
    public void RejectsMissingAudibilityOrExpandedSendLedger()
    {
        var snapshot = MiPlayLegacyProAudibleSystemAudioLiveValidationEvidence.CreateCurrentSnapshot();

        Assert.False(MiPlayLegacyProAudibleSystemAudioLiveValidationEvidence
            .IsSuccessfulAudibleBoundedValidation(snapshot with { UserConfirmedAudibleAtReceiver = false }));
        Assert.False(MiPlayLegacyProAudibleSystemAudioLiveValidationEvidence
            .IsSuccessfulAudibleBoundedValidation(snapshot with { AddMirrorSent = true }));
        Assert.False(MiPlayLegacyProAudibleSystemAudioLiveValidationEvidence
            .IsSuccessfulAudibleBoundedValidation(snapshot with { MediaRtpFrameCount = 938 }));
    }
}
