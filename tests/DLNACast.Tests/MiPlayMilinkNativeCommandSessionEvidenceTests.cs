using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayMilinkNativeCommandSessionEvidenceTests
{
    [Fact]
    public void SnapshotCapturesCurrentMilinkServicePackageAndNativeLibrary()
    {
        var snapshot = MiPlayMilinkNativeCommandSessionEvidence.CreateCurrentSnapshot();

        Assert.Equal("com.milink.service", snapshot.PackageName);
        Assert.Equal("17.2.4.1.2606161948", snapshot.VersionName);
        Assert.Equal(170020401, snapshot.VersionCode);
        Assert.EndsWith("com.milink.service_17.2.4.1.2606161948/base.apk", snapshot.ApkPath, StringComparison.Ordinal);
        Assert.Equal("ABE48100CD90EF872ABD40C8B5CAFA34F3561E8A7871865BF60CA93D2DFB1C4E", snapshot.ApkSha256);
        Assert.EndsWith("lib/arm64-v8a/libaudiomirror-jni.so", snapshot.NativeLibraryPath, StringComparison.Ordinal);
        Assert.Equal("DADB024547BAE1B210BD99E4C4AC00AD9595A8924E438EBD1730AAD7DF200DDF", snapshot.NativeLibrarySha256);
    }

    [Fact]
    public void SnapshotCapturesSafetyDataLifecycleOffsets()
    {
        var snapshot = MiPlayMilinkNativeCommandSessionEvidence.CreateCurrentSnapshot();

        Assert.Equal(0x17B858, snapshot.CmdSourceSendCmdPayloadOffset);
        Assert.Equal(0x17B998, snapshot.CmdSourceSendCmdData2Offset);
        Assert.Equal(0x17C5F0, snapshot.CmdSourceDealSafetyInfoAckOffset);
        Assert.Equal(0x17BE70, snapshot.CmdSourceDealSafetyDoneOffset);
        Assert.Equal(0x269D34, snapshot.SafetyDataDealConstructorOffset);
        Assert.Equal(0x26A084, snapshot.SafetyDataDealEncryptDataOffset);
        Assert.Equal(0x26A350, snapshot.SafetyDataDealDecryptDataOffset);
        Assert.Equal(0x26A270, snapshot.SafetyDataDealEncryptIntegrityWriteOffset);
        Assert.Equal(0x26A468, snapshot.SafetyDataDealDecryptIntegrityReadOffset);
        Assert.Equal(0x26A548, snapshot.SafetyDataDealDecryptIntegrityCompareOffset);

        Assert.True(snapshot.SafetyDataWrapperInstalledByDealSafetyInfoAck);
        Assert.True(snapshot.SendCmdPayloadUsesSafetyDataWrapperBeforeOuterFrame);
        Assert.True(snapshot.SafetyDataDealInitializesSeparateEncryptAndDecryptAesCbcContexts);
        Assert.True(snapshot.SafetyDataIntegrityFieldUsesBigEndianNativeValue);
        Assert.True(snapshot.SafetyDataIntegrityNativeValueIsByteReversedLocalAccumulator);
        Assert.True(snapshot.DealSafetyDoneOnlyMarksAuthDoneAndSchedulesTimers);
    }

    [Fact]
    public void SnapshotIdentifies0034And0035AsGetMirrorModePair()
    {
        var snapshot = MiPlayMilinkNativeCommandSessionEvidence.CreateCurrentSnapshot();

        Assert.Equal(MiPlayProtocolConstants.GetMirrorModeCommand, (ushort)0x0034);
        Assert.Equal(MiPlayProtocolConstants.GetMirrorModeAcknowledgementCommand, (ushort)0x0035);
        Assert.Equal(0x16E270, snapshot.JavaCmdSessionControlGetMirrorModeOffset);
        Assert.Equal(0x1775C8, snapshot.CmdControlGetMirrorModeOffset);
        Assert.Equal(0x177648, snapshot.CmdSourceGetMirrorModeOffset);
        Assert.Equal(0x1802BC, snapshot.CmdSourceOnRecvCmdOffset);
        Assert.Equal(0x10E67A, snapshot.OnRecvLowCommandJumpTableOffset);
        Assert.Equal(0x180E08, snapshot.GetMirrorModeAckBranchOffset);
        Assert.True(snapshot.GetMirrorModeSends0034WithNoPlainPayload);
        Assert.True(snapshot.OnRecv0035ParsesBigEndianIntPayload);
    }

    [Fact]
    public void JumpTableEvidenceMapsCapturedAckCommands()
    {
        Assert.Equal(0x180AA4, MiPlayMilinkNativeCommandSessionEvidence.ComputeOnRecvLowCommandBranchOffset(MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand));
        Assert.Equal(0x180E08, MiPlayMilinkNativeCommandSessionEvidence.ComputeOnRecvLowCommandBranchOffset(MiPlayProtocolConstants.GetMirrorModeAcknowledgementCommand));
        Assert.Equal(0x180C54, MiPlayMilinkNativeCommandSessionEvidence.ComputeOnRecvLowCommandBranchOffset(MiPlayProtocolConstants.SetPlaySourceAcknowledgementCommand));
        Assert.Equal(0x180BC4, MiPlayMilinkNativeCommandSessionEvidence.ComputeOnRecvLowCommandBranchOffset(MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand));
    }

    [Fact]
    public void DecisionSeparatesStaticStructureFromReplayPermission()
    {
        var decision = MiPlayMilinkNativeCommandSessionEvidence.Evaluate(
            MiPlayMilinkNativeCommandSessionEvidence.CreateCurrentSnapshot());

        Assert.True(decision.CanProceed);
        Assert.Contains("SafetyDataDeal after SafetyInfo_Ack", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("native big-endian", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("GetMirrorMode/GetMirrorMode_Ack", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("not authorization to replay", decision.Reason, StringComparison.Ordinal);
    }
}
