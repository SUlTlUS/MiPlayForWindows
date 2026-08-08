using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayLegacyClearGetDeviceInfoLiveValidationEvidenceTests
{
    [Fact]
    public void CapturesReadOnlyLegacyClearGetDeviceInfoSuccess()
    {
        var snapshot = MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.CreateCurrentSnapshot();

        Assert.Equal("192.168.10.4", MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.DeviceAddress);
        Assert.Equal(8_899, MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.DevicePort);
        Assert.Equal(MiPlayProtocolConstants.NativeSourceVersionCommand, MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.NativeVersionCommand);
        Assert.Equal((ushort)0x0001, MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.NativeVersionSequence);
        Assert.Equal("3.1.6030516", MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.NativeVersionPayload);
        Assert.Equal("2.1.5091615", MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.ControlSessionVersionAcknowledgement);
        Assert.Equal(MiPlayProtocolConstants.LegacySafetyChallengeCommand, MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.LegacyAuthChallengeCommand);
        Assert.Equal(MiPlayProtocolConstants.LegacySafetyAcknowledgementCommand, MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.LegacyAuthAcknowledgementCommand);
        Assert.Equal((ushort)0x01bf, MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.LegacyAuthSequence);
        Assert.Equal(16, MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.LegacyAuthChallengePayloadLength);
        Assert.Equal(20, MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.LegacyAuthAcknowledgementPayloadLength);
        Assert.Equal((ushort)0x01c2, MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.ReadyStateNotifySequence);
        Assert.Equal("state", MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.ReadyStateNotifyLabel);
        Assert.Equal(3, MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.ReadyStateNotifyIntegerValue);

        Assert.True(snapshot.SentNativeVersionBootstrap);
        Assert.True(snapshot.LegacyAuthChallengeObserved);
        Assert.True(snapshot.LegacyAuthAcknowledgementSent);
        Assert.True(snapshot.LegacyReadyStateNotifyObservedBeforeSend);
        Assert.True(snapshot.SentExactlyOneEmptyClearGetDeviceInfo);
        Assert.True(snapshot.LegacyGetDeviceInfoAcknowledgementObserved);
        Assert.True(snapshot.DeviceClosedAfterReadOnlyValidation);
    }

    [Fact]
    public void CapturesDeviceInfoAckPayloadShapeWithoutPersistingRawIdentifiers()
    {
        Assert.Equal(MiPlayProtocolConstants.GetDeviceInfoCommand, MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.ClearGetDeviceInfoCommand);
        Assert.Equal((ushort)0x0002, MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.ClearGetDeviceInfoSequence);
        Assert.Equal(0, MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.ClearGetDeviceInfoPlaintextPayloadLength);
        Assert.Equal(MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand, MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.ClearGetDeviceInfoAcknowledgementCommand);
        Assert.Equal((ushort)0x0002, MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.ClearGetDeviceInfoAcknowledgementSequence);
        Assert.Equal(415, MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.ClearGetDeviceInfoAcknowledgementPayloadLength);
        Assert.Equal(
            "BF693DD245AFA365D04BB246032A2A86BF9E28FC3765D3D9C36DB1F3F1E8155F",
            MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.ClearGetDeviceInfoAcknowledgementPayloadSha256);
        Assert.Equal("LX06", MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.DeviceInfoModel);
        Assert.Equal("1.94.13", MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.DeviceInfoRomVersion);
        Assert.Equal("audio", MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.DeviceInfoSupport);
        Assert.Equal("4", MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.DeviceInfoDeviceType);
        Assert.Equal("小爱音箱Pro", MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.DeviceInfoMiName);
        Assert.Contains("accountId", MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.RedactedSensitiveFields, StringComparison.Ordinal);
        Assert.DoesNotContain("2329301359", MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.RedactedSensitiveFields, StringComparison.Ordinal);
    }

    [Fact]
    public void DecisionAllowsOfflineNextStepButKeepsBusinessAndMediaGated()
    {
        var decision = MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.EvaluateLiveResult(
            MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.CreateCurrentSnapshot());

        Assert.True(decision.CanProceed);
        Assert.Contains("0x001e", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0x001f", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("model=LX06", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("romVersion=1.94.13", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("source-identity", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not authorize", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0x0058", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Cmd_Open", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("RTSP", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("audio", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BoundaryExpansionInvalidatesLiveReadOnlyEvidence()
    {
        Assert.False(MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.EvaluateLiveResult(
            MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.CreateCurrentSnapshot() with { NoSetPlaySourceSent = false }).CanProceed);
        Assert.False(MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.EvaluateLiveResult(
            MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.CreateCurrentSnapshot() with { NoSetLocalDeviceInfoSent = false }).CanProceed);
        Assert.False(MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.EvaluateLiveResult(
            MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.CreateCurrentSnapshot() with { NoCmdOpenSent = false }).CanProceed);
        Assert.False(MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.EvaluateLiveResult(
            MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.CreateCurrentSnapshot() with { NoRtspMediaPlaybackOrAudioSent = false }).CanProceed);
    }
}