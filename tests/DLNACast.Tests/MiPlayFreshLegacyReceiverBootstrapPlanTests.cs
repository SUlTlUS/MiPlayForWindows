using System.Security.Cryptography;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayFreshLegacyReceiverBootstrapPlanTests
{
    [Fact]
    public void DefaultProfileMatchesDistinctCaptureIdentityAndFullDeviceInfoSchema()
    {
        var profile = MiPlayFreshLegacyReceiverBootstrapPlanner.CreateDefaultDeviceInfoProfile();
        var fields = profile.ToOrderedFields();

        Assert.Equal(20, fields.Count);
        Assert.Equal(MiPlayPassiveSenderCaptureProfile.DefaultDeviceId.ToString("D"), profile.DeviceId);
        Assert.Equal(MiPlayPassiveSenderCaptureProfile.DefaultFriendlyName, profile.MiName);
        Assert.Equal("4", profile.DeviceType);
        Assert.Equal("audio", profile.Support);
        Assert.Equal("DLNACast.LegacyReceiver", profile.Model);
        Assert.Matches("^[0-9A-F]{2}(:[0-9A-F]{2}){5}$", profile.BluetoothMac);
        Assert.True((Convert.ToByte(profile.BluetoothMac[..2], 16) & 0x03) == 0x02);
        Assert.Equal(
            [
                "accountId", "alonePlayCapacity", "bluetoothMac", "canAlonePlayCtrl", "channel",
                "deviceId", "deviceType", "groupId", "groupName", "house_Id", "isMaster", "miName",
                "miotDid", "model", "p2pSupport", "romVersion", "roomName", "room_Id", "sn", "support",
            ],
            fields.Select(field => field.Key));
    }

    [Fact]
    public void OfflinePlanBuildsOnlySameSequenceClearGetDeviceInfoAcknowledgement()
    {
        var plan = MiPlayFreshLegacyReceiverBootstrapPlanner.CreateOfflinePlan(0x0001);

        Assert.False(plan.NativeVersionAcknowledgementRequiredBeforeSourceCommands);
        Assert.False(plan.BuildsSetLocalDeviceInfoAcknowledgement);
        Assert.False(plan.BuildsHeartbeatAcknowledgement);
        Assert.False(plan.SafeForNetworkUse);
        Assert.Equal(377, plan.DeviceInfoPayload.Length);
        Assert.Equal(386, plan.GetDeviceInfoAcknowledgementFrame.Length);
        Assert.Equal(
            "C344E8224C2ED699EE4F0EFDBE407821223C34C23D4027F8FAEA131517DD9FB3",
            plan.GetDeviceInfoAcknowledgementFrameSha256);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(plan.GetDeviceInfoAcknowledgementFrame)),
            plan.GetDeviceInfoAcknowledgementFrameSha256);

        Assert.True(MiPlayCommandFrameCodec.TryDecode(
            plan.GetDeviceInfoAcknowledgementFrame,
            out var frame,
            out var bytesConsumed));
        Assert.NotNull(frame);
        Assert.Equal(plan.GetDeviceInfoAcknowledgementFrame.Length, bytesConsumed);
        Assert.Equal(MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand, frame.Command);
        Assert.Equal((ushort)0x0001, frame.Sequence);
        Assert.Equal(plan.DeviceInfoPayload, frame.Payload);

        Assert.True(MiPlayLegacyDeviceInfoPayloadCodec.TryDecode(
            frame.Payload,
            out var deviceInfo,
            out var payloadBytesConsumed));
        Assert.NotNull(deviceInfo);
        Assert.Equal(frame.Payload.Length, payloadBytesConsumed);
        Assert.Equal("4", deviceInfo.GetValue("deviceType"));
        Assert.Equal("audio", deviceInfo.GetValue("support"));
        Assert.Equal(MiPlayPassiveSenderCaptureProfile.DefaultFriendlyName, deviceInfo.GetValue("miName"));
    }

    [Fact]
    public void CurrentEvidenceIsReadyOfflineButStillForbidsNetworkUse()
    {
        var decision = MiPlayFreshLegacyReceiverBootstrapPlanner.EvaluateCurrentEvidence();

        Assert.EndsWith(
            "fresh-legacy-20260807-014741.milink-logcat.txt",
            MiPlayFreshLegacyReceiverBootstrapPlanner.SenderLogArtifact,
            StringComparison.Ordinal);
        Assert.Contains("cmd_sessionsuccess/onSuccess", MiPlayFreshLegacyReceiverBootstrapPlanner.SenderCausality, StringComparison.Ordinal);
        Assert.Contains("0x001f later triggers onDeviceInfo", MiPlayFreshLegacyReceiverBootstrapPlanner.SenderCausality, StringComparison.Ordinal);
        Assert.True(decision.CanBuildDeterministicGetDeviceInfoAcknowledgement);
        Assert.True(decision.SourceProgressesWithoutNativeVersionAcknowledgement);
        Assert.False(decision.CanSendNow);
        Assert.False(decision.Plan.SafeForNetworkUse);
        Assert.Contains("0x0037 is not a prerequisite", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("same-sequence 0x001f", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("advanced from 0x0058 sequence 0x0002 to 0x0003", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("identity/bootstrap gate is now closed", decision.RemainingBoundary, StringComparison.Ordinal);
        Assert.Contains("do not infer permission for 0x0059", decision.RemainingBoundary, StringComparison.Ordinal);
    }
}
