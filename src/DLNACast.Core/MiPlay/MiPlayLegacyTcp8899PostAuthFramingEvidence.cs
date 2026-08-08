namespace DLNACast.Core.MiPlay;

public sealed record MiPlayLegacyTcp8899PostAuthFramingSnapshot(
    bool CmdSourceSendCmdPayloadObserved,
    bool CmdSourceSafetyDataDealPointerGateObserved,
    bool CmdSourceSafetyDataEncryptCallObserved,
    bool CmdSourceWrapsOriginalOuterCommandObserved,
    bool CmdSourceGetDeviceInfoEmptyPayloadObserved,
    bool CmdSourceSetLocalDeviceInfo0058Observed,
    bool SourceAckJumpTableObserved,
    bool SafetyDataDirectionalCbcContextsObserved);

/// <summary>
/// Offline-only static evidence for the source-side legacy TCP 8899 post-auth
/// command framing recovered from Xiaomi Interconnectivity Services 18.0.0.3.
/// This class deliberately models proof boundaries; it does not send frames.
/// </summary>
public static class MiPlayLegacyTcp8899PostAuthFramingEvidence
{
    public const string SourceApkNativeLibrary = "libaudiomirror-jni.so";
    public const string SourceApkEvidenceVersion = "Xiaomi Interconnectivity Services 18.0.0.3";

    public const int CmdSourceOnSessionConnectAddress = 0x1828d0;
    public const int CmdSourceSequenceOffset = 0x02c0;
    public const int CmdSourceSafetyDataDealPointerOffset = 0x03c0;

    public const int CmdSourceDealSafetyInfoAckAddress = 0x17c5f0;
    public const int CmdSourceSafetyDataDealInstallAddress = 0x17cfcc;
    public const int CmdSourceDealSafetyDoneAddress = 0x17be70;
    public const int CmdSourceSafetyDoneFlagOffset = 0x03a8;
    public const int CmdSourceSafetyDoneListenerEventCode = 0x00030d41;
    public const int CmdSourceSafetyDoneListenerVtableOffset = 0x50;

    public const int CmdSourceSendCmdPayloadAddress = 0x17b858;
    public const int SafetyDataDealEncryptVtableOffset = 0x10;
    public const int SafetyDataDealEncryptContextOffset = 0x40;
    public const int SafetyDataDealDecryptContextOffset = 0x100;

    public const int CmdSourceGetDeviceInfoAddress = 0x1779a4;
    public const int CmdSourceGetDeviceInfoCommandId = 0x001e;
    public const int CmdSourceGetDeviceInfoPayloadLength = 0;

    public const int CmdSourceSetLocalDeviceInfoAddress = 0x1771e8;
    public const int CmdSourceSetLocalDeviceInfoCommandId = 0x0058;

    public const int CmdSourceOnRecvCmdAddress = 0x1802bc;
    public const int CmdSourceOnRecvGetDeviceInfoAckBranchAddress = 0x180aa4;
    public const int CmdSourceOnRecvSetLocalDeviceInfoAckBranchAddress = 0x180bc4;
    public const int CmdSourceOnRecvNotifyBranchAddress = 0x180c44;
    public const int CmdSourceDeviceInfoAckListenerVtableOffset = 0x28;
    public const int CmdSourceSetDeviceInfoAckEventCode = 0x0003346c;

    public static MiPlayLegacyTcp8899PostAuthFramingSnapshot CreateCurrentSnapshot() =>
        new(
            CmdSourceSendCmdPayloadObserved: true,
            CmdSourceSafetyDataDealPointerGateObserved: true,
            CmdSourceSafetyDataEncryptCallObserved: true,
            CmdSourceWrapsOriginalOuterCommandObserved: true,
            CmdSourceGetDeviceInfoEmptyPayloadObserved: true,
            CmdSourceSetLocalDeviceInfo0058Observed: true,
            SourceAckJumpTableObserved: true,
            SafetyDataDirectionalCbcContextsObserved: true);

    public static MiPlayIdmStateDecision EvaluateSourceGetDeviceInfoFrameShape(
        MiPlayLegacyTcp8899PostAuthFramingSnapshot snapshot)
    {
        if (!snapshot.CmdSourceSendCmdPayloadObserved)
        {
            return new MiPlayIdmStateDecision(false, "CmdSource::sendCmdPayload has not been statically located.");
        }

        if (!snapshot.CmdSourceSafetyDataDealPointerGateObserved)
        {
            return new MiPlayIdmStateDecision(false, "The CmdSource SafetyDataDeal pointer gate at +0x3c0 is not proven.");
        }

        if (!snapshot.CmdSourceSafetyDataEncryptCallObserved)
        {
            return new MiPlayIdmStateDecision(false, "The post-auth SafetyData encryption call is not proven.");
        }

        if (!snapshot.CmdSourceWrapsOriginalOuterCommandObserved)
        {
            return new MiPlayIdmStateDecision(false, "The original wire command header after SafetyData wrapping is not proven.");
        }

        if (!snapshot.CmdSourceGetDeviceInfoEmptyPayloadObserved)
        {
            return new MiPlayIdmStateDecision(false, "CmdSource::getDeviceInfo has not been tied to an empty 0x001e payload.");
        }


        return new MiPlayIdmStateDecision(
            true,
            "CmdSource::sendCmdPayload at 0x17b858 checks CmdSource+0x3c0, encrypts the payload through SafetyDataDeal vtable +0x10 when present, then wraps the original outer command; CmdSource::getDeviceInfo at 0x1779a4 sends command 0x001e with an empty plaintext payload.");
    }

    public static MiPlayIdmStateDecision EvaluateSafetyDataDirectionalCbcState(
        MiPlayLegacyTcp8899PostAuthFramingSnapshot snapshot)
    {
        if (!snapshot.SafetyDataDirectionalCbcContextsObserved)
        {
            return new MiPlayIdmStateDecision(false, "The native SafetyDataDeal directional CBC contexts are not proven.");
        }

        return new MiPlayIdmStateDecision(
            true,
            "SafetyDataDeal uses separate AES-CBC contexts at +0x40 for encrypt and +0x100 for decrypt, so post-auth sends must continue the outbound IV state and inbound validation must advance only after successful decrypt.");
    }

    public static MiPlayIdmStateDecision EvaluateSourceAckObservationBoundary(
        MiPlayLegacyTcp8899PostAuthFramingSnapshot snapshot)
    {
        if (!snapshot.SourceAckJumpTableObserved)
        {
            return new MiPlayIdmStateDecision(false, "The source onRecvCmd ACK/notify jump-table evidence is missing.");
        }

        if (!snapshot.CmdSourceSetLocalDeviceInfo0058Observed)
        {
            return new MiPlayIdmStateDecision(false, "The source-side setLocalDeviceInfo 0x0058 path is not proven.");
        }


        return new MiPlayIdmStateDecision(
            true,
            "Source onRecvCmd routes 0x001f to the device-info ACK listener at vtable +0x28, 0x0059 to the set-local-device-info ACK event 0x0003346c, and 0x0022 to notify. A fresh legacy-clear phone capture now proves the source may send empty 0x001e and 0x0058 sourceName before receiving either ACK; the jump table still does not prove the receiver payloads or authorize sending 0x001f/0x0059.");
    }
}
