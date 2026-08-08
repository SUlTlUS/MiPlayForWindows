using System.Security.Cryptography;

namespace DLNACast.Core.MiPlay;

public sealed record MiPlayFreshLegacyPostDeviceInfoProgressionSnapshot(
    string DexArtifactPath,
    string DexArtifactSha256,
    string NativeLibraryPath,
    string NativeLibrarySha256,
    string LooperLibraryPath,
    string LooperLibrarySha256,
    bool DeviceInfoAckDispatchesOnDeviceInfo,
    bool CmdSessionStateOneRequired,
    bool VerifySameAccountCalledBeforeHandleDevice,
    bool CapturedValueZeroBranchCallsSetLocalDeviceInfo,
    bool HandleDeviceCallsGetMirrorMode,
    bool SetLocalDeviceInfoAndGetMirrorModeShareSequenceCounter,
    bool BothCommandsUseSameCmdSourceSendPath,
    bool SendPayloadTargetsSameCmdSourceHandler,
    bool SendPayloadPostsAsynchronously,
    bool ZeroDelayLooperPostsAppendFifoUnderMutex,
    bool MessageCodeThreeDispatchesOnSendCmd,
    bool OnSendCmdWritesQueuedBufferToSameCommandSession,
    bool SetDeviceInfoAckMapsTo210028,
    bool Java210028CaseReturnsWithoutCallback);

public sealed record MiPlayFreshLegacyPostDeviceInfoProgressionPlan(
    ushort ObservedSetLocalDeviceInfoSequence,
    ushort PredictedGetMirrorModeSequence,
    byte[] PredictedGetMirrorModeFrame,
    string PredictedGetMirrorModeFrameSha256,
    bool SafeForNetworkUse);

public sealed record MiPlayFreshLegacyPostDeviceInfoProgressionDecision(
    bool CanPredictNextQueuedCommand,
    bool GetMirrorModeWasQueuedWithoutWaitingFor0059,
    bool PredictedCommandOrderIsFifoPreserved,
    bool CanUseNetwork,
    string Reason,
    string RemainingBoundary,
    MiPlayFreshLegacyPostDeviceInfoProgressionPlan Plan);

/// <summary>
/// Offline reconstruction of the official source's synchronous Java and native
/// command order after it receives a valid legacy-clear 0x001f. The evidence
/// predicts a queued source command; it neither sends nor authorizes a frame.
/// </summary>
public static class MiPlayFreshLegacyPostDeviceInfoProgressionEvidence
{
    public const string DexArtifactPath =
        "artifacts/phone_firmware/mi13p_os3_0_313/apk_extract/MiLinkOS3Cn/classes3.dex";
    public const string DexArtifactSha256 =
        "2A0860847789A746AC112859DDCD2372BB8864FF0AB08B8AEB56C01D7DD1E3C0";

    public const string NativeLibraryPath = MiPlayMilinkNativeCommandSessionEvidence.NativeLibraryPath;
    public const string NativeLibrarySha256 = MiPlayMilinkNativeCommandSessionEvidence.NativeLibrarySha256;
    public const string LooperLibraryPath =
        "artifacts/phone_live/2210132C_OS3.0.313.0/packages/com.milink.service_17.2.4.1.2606161948/extracted/lib/arm64-v8a/libmirror-jni.so";
    public const string LooperLibrarySha256 =
        "35778B2DA7D95D02FFD37AE1AD645D4B23D7E0A1718604F7B40D4EB9E04810DE";

    public const int OnCmdSessionDeviceInfoAckOffset = 0x2962DC;
    public const int CmdSessionDevicesInfoOffset = 0x2B1EEC;
    public const int CmdSessionStateCheckOffset = 0x2B1F46;
    public const int VerifySameAccountCallOffset = 0x2B1FD4;
    public const int HandleDeviceCallOffset = 0x2B1FDA;
    public const int HandleDeviceOffset = 0x2B227C;
    public const int HandleDeviceSetDeviceCallOffset = 0x2B251E;
    public const int HandleDeviceGetMirrorModeCallOffset = 0x2B2528;
    public const int VerifySameAccountOffset = 0x2B2BB0;
    public const int VerifyCanReceiveControlCheckOffset = 0x2B2C2E;
    public const int SetLocalDeviceInfoSameAccountOneCallOffset = 0x2B2CDC;
    public const int SetLocalDeviceInfoSameAccountZeroCallOffset = 0x2B2D8A;
    public const int OnCmdSessionInfoOffset = 0x29649C;
    public const int SetDeviceInfoAckNoOpReturnOffset = 0x297072;
    public const int SetDeviceInfoAckEvent = 210028;

    public const int CmdSourceSetLocalDeviceInfoOffset = 0x1771E8;
    public const int SetLocalDeviceInfoSequenceLoadOffset = 0x177238;
    public const int SetLocalDeviceInfoCommandLoadOffset = 0x17723C;
    public const int SetLocalDeviceInfoSequenceStoreOffset = 0x177250;
    public const int SetLocalDeviceInfoSendCallOffset = 0x177254;
    public const int CmdSourceGetMirrorModeOffset = 0x177648;
    public const int GetMirrorModeSequenceLoadOffset = 0x17768C;
    public const int GetMirrorModeCommandLoadOffset = 0x177690;
    public const int GetMirrorModeSequenceStoreOffset = 0x1776A4;
    public const int GetMirrorModeSendCallOffset = 0x1776A8;
    public const int SendPayloadGetHandlerCallOffset = 0x17B618;
    public const int SendPayloadMessageWhatThreeOffset = 0x17B650;
    public const int SendPayloadMessageConstructorCallOffset = 0x17B65C;
    public const int SendPayloadAsyncPostOffset = 0x17B708;
    public const int CmdSourceOnMessageReceivedOffset = 0x17E758;
    public const int OnMessageJumpTableOffset = 0x10E66F;
    public const int OnMessageWhatThreeBranchOffset = 0x17E9C0;
    public const int OnMessageOnSendCmdCallOffset = 0x17E9D8;
    public const int CmdSourceOnSendCmdOffset = 0x1800FC;
    public const int OnSendCmdBufferLookupCallOffset = 0x18014C;
    public const int OnSendCmdSessionWriteCallOffset = 0x1801AC;
    public const int SetLocalDeviceInfoAcknowledgementBranchOffset = 0x180BC4;

    public const int AMessagePostOffset = 0x262550;
    public const int AMessageToLooperPostCallOffset = 0x262600;
    public const int ALooperPostOffset = 0x25EDF8;
    public const int ALooperZeroDelayBranchOffset = 0x25EE1C;
    public const int ALooperZeroDelayAppendPathOffset = 0x25EE80;
    public const int ALooperZeroDelayMutexLockOffset = 0x25EE88;
    public const int ALooperZeroDelayTailStoreOffset = 0x25EEDC;
    public const int ALooperZeroDelayMutexUnlockOffset = 0x25EEE8;

    public const ushort ObservedSetLocalDeviceInfoSequence =
        MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.AdvancedSetLocalDeviceInfoSequence;
    public const ushort PredictedGetMirrorModeSequence = ObservedSetLocalDeviceInfoSequence + 1;
    public const string PredictedGetMirrorModeFrameSha256 =
        "DDDAFA73414A3B71D7DF04B90FDC20BDDDAE735F852C1125E9BB576223032FD4";

    public static MiPlayFreshLegacyPostDeviceInfoProgressionSnapshot CreateCurrentSnapshot() =>
        new(
            DexArtifactPath,
            DexArtifactSha256,
            NativeLibraryPath,
            NativeLibrarySha256,
            LooperLibraryPath,
            LooperLibrarySha256,
            DeviceInfoAckDispatchesOnDeviceInfo: true,
            CmdSessionStateOneRequired: true,
            VerifySameAccountCalledBeforeHandleDevice: true,
            CapturedValueZeroBranchCallsSetLocalDeviceInfo: true,
            HandleDeviceCallsGetMirrorMode: true,
            SetLocalDeviceInfoAndGetMirrorModeShareSequenceCounter: true,
            BothCommandsUseSameCmdSourceSendPath: true,
            SendPayloadTargetsSameCmdSourceHandler: true,
            SendPayloadPostsAsynchronously: true,
            ZeroDelayLooperPostsAppendFifoUnderMutex: true,
            MessageCodeThreeDispatchesOnSendCmd: true,
            OnSendCmdWritesQueuedBufferToSameCommandSession: true,
            SetDeviceInfoAckMapsTo210028: true,
            Java210028CaseReturnsWithoutCallback: true);

    public static MiPlayFreshLegacyPostDeviceInfoProgressionPlan CreateOfflinePlan()
    {
        var frame = MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.GetMirrorModeCommand,
            PredictedGetMirrorModeSequence,
            []);

        return new MiPlayFreshLegacyPostDeviceInfoProgressionPlan(
            ObservedSetLocalDeviceInfoSequence,
            PredictedGetMirrorModeSequence,
            frame,
            Convert.ToHexString(SHA256.HashData(frame)),
            SafeForNetworkUse: false);
    }

    public static MiPlayFreshLegacyPostDeviceInfoProgressionDecision Evaluate(
        MiPlayFreshLegacyPostDeviceInfoProgressionSnapshot snapshot)
    {
        var plan = CreateOfflinePlan();
        var exactArtifacts =
            string.Equals(snapshot.DexArtifactPath, DexArtifactPath, StringComparison.Ordinal) &&
            string.Equals(snapshot.DexArtifactSha256, DexArtifactSha256, StringComparison.Ordinal) &&
            string.Equals(snapshot.NativeLibraryPath, NativeLibraryPath, StringComparison.Ordinal) &&
            string.Equals(snapshot.NativeLibrarySha256, NativeLibrarySha256, StringComparison.Ordinal) &&
            string.Equals(snapshot.LooperLibraryPath, LooperLibraryPath, StringComparison.Ordinal) &&
            string.Equals(snapshot.LooperLibrarySha256, LooperLibrarySha256, StringComparison.Ordinal);
        var exactFrame =
            plan.ObservedSetLocalDeviceInfoSequence == ObservedSetLocalDeviceInfoSequence &&
            plan.PredictedGetMirrorModeSequence == PredictedGetMirrorModeSequence &&
            string.Equals(
                plan.PredictedGetMirrorModeFrameSha256,
                PredictedGetMirrorModeFrameSha256,
                StringComparison.Ordinal) &&
            MiPlayCommandFrameCodec.TryDecode(
                plan.PredictedGetMirrorModeFrame,
                out var frame,
                out var bytesConsumed) &&
            frame is not null &&
            bytesConsumed == plan.PredictedGetMirrorModeFrame.Length &&
            frame.Command == MiPlayProtocolConstants.GetMirrorModeCommand &&
            frame.Sequence == PredictedGetMirrorModeSequence &&
            frame.Payload.Length == 0;
        var exactOrder =
            snapshot.DeviceInfoAckDispatchesOnDeviceInfo &&
            snapshot.CmdSessionStateOneRequired &&
            snapshot.VerifySameAccountCalledBeforeHandleDevice &&
            snapshot.CapturedValueZeroBranchCallsSetLocalDeviceInfo &&
            snapshot.HandleDeviceCallsGetMirrorMode &&
            snapshot.SetLocalDeviceInfoAndGetMirrorModeShareSequenceCounter &&
            snapshot.BothCommandsUseSameCmdSourceSendPath &&
            snapshot.SendPayloadTargetsSameCmdSourceHandler &&
            snapshot.SendPayloadPostsAsynchronously &&
            snapshot.ZeroDelayLooperPostsAppendFifoUnderMutex &&
            snapshot.MessageCodeThreeDispatchesOnSendCmd &&
            snapshot.OnSendCmdWritesQueuedBufferToSameCommandSession &&
            snapshot.SetDeviceInfoAckMapsTo210028 &&
            snapshot.Java210028CaseReturnsWithoutCallback;
        var canPredict = exactArtifacts && exactFrame && exactOrder;

        return new MiPlayFreshLegacyPostDeviceInfoProgressionDecision(
            canPredict,
            GetMirrorModeWasQueuedWithoutWaitingFor0059: canPredict,
            PredictedCommandOrderIsFifoPreserved: canPredict,
            CanUseNetwork: false,
            canPredict
                ? "After the accepted 0x001f, Java synchronously calls verifySameAccount before handleDevice. The observed value-zero branch queues 0x0058 sequence 0x0003, then handleDevice calls getMirrorMode. Both native methods use the same CmdSource sequence and send path, target the same handler with zero-delay messages, and the recovered ALooper path appends those messages FIFO under one mutex before onSendCmd writes each queued buffer to the same command session. The next source command is therefore empty 0x0034 sequence 0x0004 without waiting for 0x0059."
                : "The exact DEX/native/looper identities, Java call order, shared native sequence state, same-handler FIFO enqueue, onSendCmd route, ACK event route, or deterministic 0x0034 encoding is incomplete.",
            "The previous live probe stopped as soon as it observed 0x0058 and therefore could not observe the already-queued 0x0034. A future check should only keep the socket open and observe; it must not send 0x0059, 0x0035, Open, AddMirror, RTSP, playback, media, or audio without fresh explicit authorization.",
            plan);
    }
}
