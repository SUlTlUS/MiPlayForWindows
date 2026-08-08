using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayFreshLegacyPostDeviceInfoProgressionEvidenceTests
{
    [Fact]
    public void SnapshotPinsExactDexAndCurrentRootedNativeArtifacts()
    {
        var snapshot = MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.CreateCurrentSnapshot();

        Assert.EndsWith("MiLinkOS3Cn/classes3.dex", snapshot.DexArtifactPath, StringComparison.Ordinal);
        Assert.Equal("2A0860847789A746AC112859DDCD2372BB8864FF0AB08B8AEB56C01D7DD1E3C0", snapshot.DexArtifactSha256);
        Assert.Equal(MiPlayMilinkNativeCommandSessionEvidence.NativeLibraryPath, snapshot.NativeLibraryPath);
        Assert.Equal("DADB024547BAE1B210BD99E4C4AC00AD9595A8924E438EBD1730AAD7DF200DDF", snapshot.NativeLibrarySha256);
        Assert.EndsWith("lib/arm64-v8a/libmirror-jni.so", snapshot.LooperLibraryPath, StringComparison.Ordinal);
        Assert.Equal("35778B2DA7D95D02FFD37AE1AD645D4B23D7E0A1718604F7B40D4EB9E04810DE", snapshot.LooperLibrarySha256);

        Assert.Equal(0x2962DC, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.OnCmdSessionDeviceInfoAckOffset);
        Assert.Equal(0x2B1EEC, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.CmdSessionDevicesInfoOffset);
        Assert.Equal(0x2B1F46, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.CmdSessionStateCheckOffset);
        Assert.Equal(0x2B1FD4, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.VerifySameAccountCallOffset);
        Assert.Equal(0x2B1FDA, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.HandleDeviceCallOffset);
        Assert.Equal(0x2B2528, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.HandleDeviceGetMirrorModeCallOffset);
        Assert.Equal(0x2B2BB0, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.VerifySameAccountOffset);
        Assert.Equal(0x2B2D8A, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.SetLocalDeviceInfoSameAccountZeroCallOffset);
        Assert.Equal(0x29649C, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.OnCmdSessionInfoOffset);
        Assert.Equal(0x297072, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.SetDeviceInfoAckNoOpReturnOffset);
        Assert.Equal(210028, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.SetDeviceInfoAckEvent);
    }

    [Fact]
    public void SnapshotPinsSharedSequenceAndAsynchronousEnqueueOffsets()
    {
        Assert.Equal(0x1771E8, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.CmdSourceSetLocalDeviceInfoOffset);
        Assert.Equal(0x177238, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.SetLocalDeviceInfoSequenceLoadOffset);
        Assert.Equal(0x17723C, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.SetLocalDeviceInfoCommandLoadOffset);
        Assert.Equal(0x177250, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.SetLocalDeviceInfoSequenceStoreOffset);
        Assert.Equal(0x177254, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.SetLocalDeviceInfoSendCallOffset);
        Assert.Equal(0x177648, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.CmdSourceGetMirrorModeOffset);
        Assert.Equal(0x17768C, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.GetMirrorModeSequenceLoadOffset);
        Assert.Equal(0x177690, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.GetMirrorModeCommandLoadOffset);
        Assert.Equal(0x1776A4, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.GetMirrorModeSequenceStoreOffset);
        Assert.Equal(0x1776A8, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.GetMirrorModeSendCallOffset);
        Assert.Equal(0x17B618, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.SendPayloadGetHandlerCallOffset);
        Assert.Equal(0x17B650, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.SendPayloadMessageWhatThreeOffset);
        Assert.Equal(0x17B65C, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.SendPayloadMessageConstructorCallOffset);
        Assert.Equal(0x17B708, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.SendPayloadAsyncPostOffset);
        Assert.Equal(0x17E758, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.CmdSourceOnMessageReceivedOffset);
        Assert.Equal(0x10E66F, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.OnMessageJumpTableOffset);
        Assert.Equal(0x17E9C0, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.OnMessageWhatThreeBranchOffset);
        Assert.Equal(0x17E9D8, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.OnMessageOnSendCmdCallOffset);
        Assert.Equal(0x1800FC, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.CmdSourceOnSendCmdOffset);
        Assert.Equal(0x18014C, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.OnSendCmdBufferLookupCallOffset);
        Assert.Equal(0x1801AC, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.OnSendCmdSessionWriteCallOffset);
        Assert.Equal(0x180BC4, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.SetLocalDeviceInfoAcknowledgementBranchOffset);

        Assert.Equal(0x262550, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.AMessagePostOffset);
        Assert.Equal(0x262600, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.AMessageToLooperPostCallOffset);
        Assert.Equal(0x25EDF8, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.ALooperPostOffset);
        Assert.Equal(0x25EE1C, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.ALooperZeroDelayBranchOffset);
        Assert.Equal(0x25EE80, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.ALooperZeroDelayAppendPathOffset);
        Assert.Equal(0x25EE88, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.ALooperZeroDelayMutexLockOffset);
        Assert.Equal(0x25EEDC, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.ALooperZeroDelayTailStoreOffset);
        Assert.Equal(0x25EEE8, MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.ALooperZeroDelayMutexUnlockOffset);
    }

    [Fact]
    public void BuildsExactEmpty0034SequenceFourPredictionOffline()
    {
        var plan = MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.CreateOfflinePlan();

        Assert.Equal((ushort)0x0003, plan.ObservedSetLocalDeviceInfoSequence);
        Assert.Equal((ushort)0x0004, plan.PredictedGetMirrorModeSequence);
        Assert.Equal(new byte[] { 0x24, 0x00, 0x34, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00 }, plan.PredictedGetMirrorModeFrame);
        Assert.Equal("DDDAFA73414A3B71D7DF04B90FDC20BDDDAE735F852C1125E9BB576223032FD4", plan.PredictedGetMirrorModeFrameSha256);
        Assert.False(plan.SafeForNetworkUse);

        Assert.True(MiPlayCommandFrameCodec.TryDecode(plan.PredictedGetMirrorModeFrame, out var frame, out var bytesConsumed));
        Assert.NotNull(frame);
        Assert.Equal(plan.PredictedGetMirrorModeFrame.Length, bytesConsumed);
        Assert.Equal(MiPlayProtocolConstants.GetMirrorModeCommand, frame.Command);
        Assert.Equal((ushort)0x0004, frame.Sequence);
        Assert.Empty(frame.Payload);
    }

    [Fact]
    public void CurrentEvidenceProvesQueueingWithout0059ButNeverAuthorizesNetworkUse()
    {
        var decision = MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.Evaluate(
            MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.CreateCurrentSnapshot());

        Assert.True(decision.CanPredictNextQueuedCommand);
        Assert.True(decision.GetMirrorModeWasQueuedWithoutWaitingFor0059);
        Assert.True(decision.PredictedCommandOrderIsFifoPreserved);
        Assert.False(decision.CanUseNetwork);
        Assert.Contains("without waiting for 0x0059", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("FIFO under one mutex", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("only keep the socket open and observe", decision.RemainingBoundary, StringComparison.Ordinal);
        Assert.Contains("must not send 0x0059", decision.RemainingBoundary, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingOrderingSequenceAsyncOrAckEvidenceInvalidatesPrediction()
    {
        var snapshot = MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.CreateCurrentSnapshot();

        Assert.False(MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.Evaluate(
            snapshot with { VerifySameAccountCalledBeforeHandleDevice = false }).CanPredictNextQueuedCommand);
        Assert.False(MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.Evaluate(
            snapshot with { SetLocalDeviceInfoAndGetMirrorModeShareSequenceCounter = false }).CanPredictNextQueuedCommand);
        Assert.False(MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.Evaluate(
            snapshot with { SendPayloadPostsAsynchronously = false }).CanPredictNextQueuedCommand);
        Assert.False(MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.Evaluate(
            snapshot with { SendPayloadTargetsSameCmdSourceHandler = false }).CanPredictNextQueuedCommand);
        Assert.False(MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.Evaluate(
            snapshot with { ZeroDelayLooperPostsAppendFifoUnderMutex = false }).CanPredictNextQueuedCommand);
        Assert.False(MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.Evaluate(
            snapshot with { MessageCodeThreeDispatchesOnSendCmd = false }).CanPredictNextQueuedCommand);
        Assert.False(MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.Evaluate(
            snapshot with { OnSendCmdWritesQueuedBufferToSameCommandSession = false }).CanPredictNextQueuedCommand);
        Assert.False(MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.Evaluate(
            snapshot with { SetDeviceInfoAckMapsTo210028 = false }).CanPredictNextQueuedCommand);
        Assert.False(MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.Evaluate(
            snapshot with { Java210028CaseReturnsWithoutCallback = false }).CanPredictNextQueuedCommand);
    }
}
