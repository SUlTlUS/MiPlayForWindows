namespace DLNACast.Core.MiPlay;

public sealed record MiPlayMilinkNativeCommandSessionSnapshot(
    string PackageName,
    string VersionName,
    int VersionCode,
    string ApkPath,
    string ApkSha256,
    string NativeLibraryPath,
    string NativeLibrarySha256,
    int CmdSourceSendCmdPayloadOffset,
    int CmdSourceSendCmdData2Offset,
    int CmdSourceDealSafetyInfoAckOffset,
    int CmdSourceDealSafetyDoneOffset,
    int SafetyDataDealConstructorOffset,
    int SafetyDataDealEncryptDataOffset,
    int SafetyDataDealDecryptDataOffset,
    int SafetyDataDealEncryptIntegrityWriteOffset,
    int SafetyDataDealDecryptIntegrityReadOffset,
    int SafetyDataDealDecryptIntegrityCompareOffset,
    int CmdSourceGetMirrorModeOffset,
    int CmdControlGetMirrorModeOffset,
    int JavaCmdSessionControlGetMirrorModeOffset,
    int CmdSourceOnRecvCmdOffset,
    int OnRecvLowCommandJumpTableOffset,
    int GetMirrorModeAckBranchOffset,
    bool SafetyDataWrapperInstalledByDealSafetyInfoAck,
    bool SendCmdPayloadUsesSafetyDataWrapperBeforeOuterFrame,
    bool SafetyDataDealInitializesSeparateEncryptAndDecryptAesCbcContexts,
    bool SafetyDataIntegrityFieldUsesBigEndianNativeValue,
    bool SafetyDataIntegrityNativeValueIsByteReversedLocalAccumulator,
    bool DealSafetyDoneOnlyMarksAuthDoneAndSchedulesTimers,
    bool GetMirrorModeSends0034WithNoPlainPayload,
    bool OnRecv0035ParsesBigEndianIntPayload);

/// <summary>
/// Offline static evidence from the current phone-side <c>com.milink.service:audio</c>
/// native command-session library. It identifies the official source process'
/// post-auth SafetyData insertion point and the previously unknown 0x0034/0x0035
/// readiness command pair without sending or replaying any captured frame.
/// </summary>
public static class MiPlayMilinkNativeCommandSessionEvidence
{
    public const string PackageName = "com.milink.service";
    public const string VersionName = "17.2.4.1.2606161948";
    public const int VersionCode = 170020401;

    public const string ApkPath =
        "artifacts/phone_live/2210132C_OS3.0.313.0/packages/com.milink.service_17.2.4.1.2606161948/base.apk";

    public const string ApkSha256 =
        "ABE48100CD90EF872ABD40C8B5CAFA34F3561E8A7871865BF60CA93D2DFB1C4E";

    public const string NativeLibraryPath =
        "artifacts/phone_live/2210132C_OS3.0.313.0/packages/com.milink.service_17.2.4.1.2606161948/extracted/lib/arm64-v8a/libaudiomirror-jni.so";

    public const string NativeLibrarySha256 =
        "DADB024547BAE1B210BD99E4C4AC00AD9595A8924E438EBD1730AAD7DF200DDF";

    public const int CmdSourceSendCmdPayloadOffset = 0x17B858;
    public const int CmdSourceSendCmdData2Offset = 0x17B998;
    public const int CmdSourceDealSafetyInfoAckOffset = 0x17C5F0;
    public const int CmdSourceDealSafetyDoneOffset = 0x17BE70;

    public const int SafetyDataDealConstructorOffset = 0x269D34;
    public const int SafetyDataDealEncryptDataOffset = 0x26A084;
    public const int SafetyDataDealDecryptDataOffset = 0x26A350;
    public const int SafetyDataDealEncryptIntegrityWriteOffset = 0x26A270;
    public const int SafetyDataDealDecryptIntegrityReadOffset = 0x26A468;
    public const int SafetyDataDealDecryptIntegrityCompareOffset = 0x26A548;

    public const int JavaCmdSessionControlGetMirrorModeOffset = 0x16E270;
    public const int CmdControlGetMirrorModeOffset = 0x1775C8;
    public const int CmdSourceGetMirrorModeOffset = 0x177648;
    public const int CmdSourceOnRecvCmdOffset = 0x1802BC;
    public const int OnRecvLowCommandJumpTableOffset = 0x10E67A;
    public const int GetMirrorModeAckBranchOffset = 0x180E08;

    public const string SendCmdPayloadSafetyDataWrapperEvidence =
        "CmdSource::sendCmdPayload checks this+0x3c0 and calls its virtual transform before getCmdData builds the clear '$'/cmd/seq/len frame.";

    public const string SafetyDataDealLifecycleEvidence =
        "CmdSource::dealSafetyInfoAck derives auth/aes key material, constructs SafetyDataDeal(true, integrityType, aesKey, aesIv), and stores it at CmdSource+0x3c0.";

    public const string SafetyDataDealStateEvidence =
        "SafetyDataDeal copies the first 16 bytes of key/iv into AES_init_ctx_iv twice: this+0x40 for encryptData and this+0x100 for decryptData.";

    public const string SafetyDataIntegrityEndianEvidence =
        "SafetyDataDeal::encryptData writes the integrity value as crc>>24, crc>>16, crc>>8, crc; decryptData loads the four header bytes and applies rev before comparison, so the header field is the native value in big-endian order.";

    public const string SafetyDataIntegrityAccumulatorEvidence =
        "The observed S12 SafetyData bytes store 00 EC AE 89 as the native big-endian integrity value, while the local CRC-32/MPEG-2 accumulator returns 89 AE EC 00 for the same ciphertext; the codec therefore byte-reverses the local accumulator before reading or writing the header value.";

    public const string GetMirrorModeEvidence =
        "Java CmdSessionControl_getMirrorMode -> CmdControl::getMirrorMode -> CmdSource::getMirrorMode sends command 0x0034 with null payload and incremented CmdSource sequence.";

    public const string GetMirrorModeAckEvidence =
        "CmdSource::onRecvCmd low-command jump table maps 0x0035 to 0x180e08, where value-type 0 parses a big-endian uint32 mirror-mode value before dispatching the callback.";

    public static MiPlayMilinkNativeCommandSessionSnapshot CreateCurrentSnapshot() =>
        new(
            PackageName,
            VersionName,
            VersionCode,
            ApkPath,
            ApkSha256,
            NativeLibraryPath,
            NativeLibrarySha256,
            CmdSourceSendCmdPayloadOffset,
            CmdSourceSendCmdData2Offset,
            CmdSourceDealSafetyInfoAckOffset,
            CmdSourceDealSafetyDoneOffset,
            SafetyDataDealConstructorOffset,
            SafetyDataDealEncryptDataOffset,
            SafetyDataDealDecryptDataOffset,
            SafetyDataDealEncryptIntegrityWriteOffset,
            SafetyDataDealDecryptIntegrityReadOffset,
            SafetyDataDealDecryptIntegrityCompareOffset,
            CmdSourceGetMirrorModeOffset,
            CmdControlGetMirrorModeOffset,
            JavaCmdSessionControlGetMirrorModeOffset,
            CmdSourceOnRecvCmdOffset,
            OnRecvLowCommandJumpTableOffset,
            GetMirrorModeAckBranchOffset,
            SafetyDataWrapperInstalledByDealSafetyInfoAck: true,
            SendCmdPayloadUsesSafetyDataWrapperBeforeOuterFrame: true,
            SafetyDataDealInitializesSeparateEncryptAndDecryptAesCbcContexts: true,
            SafetyDataIntegrityFieldUsesBigEndianNativeValue: true,
            SafetyDataIntegrityNativeValueIsByteReversedLocalAccumulator: true,
            DealSafetyDoneOnlyMarksAuthDoneAndSchedulesTimers: true,
            GetMirrorModeSends0034WithNoPlainPayload: true,
            OnRecv0035ParsesBigEndianIntPayload: true);

    public static int ComputeOnRecvLowCommandBranchOffset(ushort command)
    {
        if (command is 0 or > 0x006d)
        {
            throw new ArgumentOutOfRangeException(nameof(command), "The recovered low-command jump table covers commands 0x0001 through 0x006d.");
        }

        return command switch
        {
            MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand => 0x180AA4,
            MiPlayProtocolConstants.SetPlaySourceAcknowledgementCommand => 0x180C54,
            MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand => 0x180BC4,
            MiPlayProtocolConstants.GetMirrorModeAcknowledgementCommand => GetMirrorModeAckBranchOffset,
            _ => throw new ArgumentOutOfRangeException(nameof(command), "This evidence currently records only ACK branches needed by the official post-auth sequence."),
        };
    }

    public static MiPlayIdmStateDecision Evaluate(MiPlayMilinkNativeCommandSessionSnapshot snapshot)
    {
        if (snapshot.PackageName != PackageName ||
            snapshot.VersionName != VersionName ||
            snapshot.ApkSha256 != ApkSha256 ||
            snapshot.NativeLibrarySha256 != NativeLibrarySha256)
        {
            return new MiPlayIdmStateDecision(false, "The APK/native-library identity does not match the rooted phone sender artifact.");
        }

        if (!snapshot.SafetyDataWrapperInstalledByDealSafetyInfoAck ||
            !snapshot.SendCmdPayloadUsesSafetyDataWrapperBeforeOuterFrame ||
            !snapshot.SafetyDataDealInitializesSeparateEncryptAndDecryptAesCbcContexts ||
            !snapshot.SafetyDataIntegrityFieldUsesBigEndianNativeValue ||
            !snapshot.SafetyDataIntegrityNativeValueIsByteReversedLocalAccumulator)
        {
            return new MiPlayIdmStateDecision(false, "The post-auth SafetyData lifecycle is incomplete; do not infer a sendable command state.");
        }

        if (!snapshot.GetMirrorModeSends0034WithNoPlainPayload ||
            !snapshot.OnRecv0035ParsesBigEndianIntPayload)
        {
            return new MiPlayIdmStateDecision(false, "The 0x0034/0x0035 readiness pair is not proven to be GetMirrorMode/GetMirrorMode_Ack.");
        }

        return new MiPlayIdmStateDecision(
            true,
            "The current com.milink.service:audio native library installs SafetyDataDeal after SafetyInfo_Ack, applies it inside sendCmdPayload before the outer command frame, keeps separate AES-CBC encrypt/decrypt contexts, stores SafetyData integrity as a native big-endian value, and identifies the captured 0x0034/0x0035 pair as GetMirrorMode/GetMirrorMode_Ack. This is offline structure evidence only, not authorization to replay any command.");
    }
}
