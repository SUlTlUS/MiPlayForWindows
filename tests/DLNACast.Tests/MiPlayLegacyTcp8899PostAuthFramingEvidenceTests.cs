using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayLegacyTcp8899PostAuthFramingEvidenceTests
{
    [Fact]
    public void SourceSendCmdPayloadWrapsOriginalOuterCommandWithEncryptedPayload()
    {
        var snapshot = MiPlayLegacyTcp8899PostAuthFramingEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.CmdSourceSendCmdPayloadObserved);
        Assert.True(snapshot.CmdSourceSafetyDataDealPointerGateObserved);
        Assert.True(snapshot.CmdSourceSafetyDataEncryptCallObserved);
        Assert.True(snapshot.CmdSourceWrapsOriginalOuterCommandObserved);
        Assert.True(snapshot.CmdSourceGetDeviceInfoEmptyPayloadObserved);
        Assert.Equal("libaudiomirror-jni.so", MiPlayLegacyTcp8899PostAuthFramingEvidence.SourceApkNativeLibrary);
        Assert.Equal(0x17b858, MiPlayLegacyTcp8899PostAuthFramingEvidence.CmdSourceSendCmdPayloadAddress);
        Assert.Equal(0x03c0, MiPlayLegacyTcp8899PostAuthFramingEvidence.CmdSourceSafetyDataDealPointerOffset);
        Assert.Equal(0x10, MiPlayLegacyTcp8899PostAuthFramingEvidence.SafetyDataDealEncryptVtableOffset);
        Assert.Equal(0x1779a4, MiPlayLegacyTcp8899PostAuthFramingEvidence.CmdSourceGetDeviceInfoAddress);
        Assert.Equal(0, MiPlayLegacyTcp8899PostAuthFramingEvidence.CmdSourceGetDeviceInfoPayloadLength);
        Assert.Equal(
            MiPlayProtocolConstants.GetDeviceInfoCommand,
            MiPlayLegacyTcp8899PostAuthFramingEvidence.CmdSourceGetDeviceInfoCommandId);

        var decision = MiPlayLegacyTcp8899PostAuthFramingEvidence.EvaluateSourceGetDeviceInfoFrameShape(snapshot);

        Assert.True(decision.CanProceed);
        Assert.Contains("0x17b858", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("CmdSource+0x3c0", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("original outer command", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x001e", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("empty plaintext payload", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceSafetyDataUsesDirectionalCbcContexts()
    {
        var snapshot = MiPlayLegacyTcp8899PostAuthFramingEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.SafetyDataDirectionalCbcContextsObserved);
        Assert.Equal(0x40, MiPlayLegacyTcp8899PostAuthFramingEvidence.SafetyDataDealEncryptContextOffset);
        Assert.Equal(0x100, MiPlayLegacyTcp8899PostAuthFramingEvidence.SafetyDataDealDecryptContextOffset);

        var decision = MiPlayLegacyTcp8899PostAuthFramingEvidence.EvaluateSafetyDataDirectionalCbcState(snapshot);

        Assert.True(decision.CanProceed);
        Assert.Contains("+0x40", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("+0x100", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("outbound IV state", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("successful decrypt", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceAckJumpTableDefinesObservationGatesButDoesNotAuthorize0058()
    {
        var snapshot = MiPlayLegacyTcp8899PostAuthFramingEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.SourceAckJumpTableObserved);
        Assert.True(snapshot.CmdSourceSetLocalDeviceInfo0058Observed);
        Assert.Equal(0x1802bc, MiPlayLegacyTcp8899PostAuthFramingEvidence.CmdSourceOnRecvCmdAddress);
        Assert.Equal(0x180aa4, MiPlayLegacyTcp8899PostAuthFramingEvidence.CmdSourceOnRecvGetDeviceInfoAckBranchAddress);
        Assert.Equal(0x180bc4, MiPlayLegacyTcp8899PostAuthFramingEvidence.CmdSourceOnRecvSetLocalDeviceInfoAckBranchAddress);
        Assert.Equal(0x180c44, MiPlayLegacyTcp8899PostAuthFramingEvidence.CmdSourceOnRecvNotifyBranchAddress);
        Assert.Equal(0x28, MiPlayLegacyTcp8899PostAuthFramingEvidence.CmdSourceDeviceInfoAckListenerVtableOffset);
        Assert.Equal(0x0003346c, MiPlayLegacyTcp8899PostAuthFramingEvidence.CmdSourceSetDeviceInfoAckEventCode);
        Assert.Equal(
            MiPlayProtocolConstants.SetLocalDeviceInfoCommand,
            MiPlayLegacyTcp8899PostAuthFramingEvidence.CmdSourceSetLocalDeviceInfoCommandId);

        var decision = MiPlayLegacyTcp8899PostAuthFramingEvidence.EvaluateSourceAckObservationBoundary(snapshot);

        Assert.True(decision.CanProceed);
        Assert.Contains("0x001f", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x0059", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x0022", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("fresh legacy-clear phone capture", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("does not prove the receiver payloads", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("authorize sending 0x001f/0x0059", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ReceiverCtrlProtocolParsesClearFrameHeaderBeforeCtrlClientDispatch()
    {
        var snapshot = MiPlayLx06MpasReceiverEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.MpasCtrlProtocolFrameParserObserved);
        Assert.True(snapshot.MpasCtrlProtocolHeaderBeforeCallbackObserved);
        Assert.True(snapshot.MpasCtrlProtocolPayloadLengthObserved);
        Assert.True(snapshot.MpasCtrlProtocolCallbackObserved);
        Assert.Equal(0x36a68, MiPlayLx06MpasReceiverEvidence.MpasCtrlProtocolFrameParserEntryAddress);
        Assert.Equal(9, MiPlayLx06MpasReceiverEvidence.MpasCtrlProtocolMinimumFrameLength);
        Assert.Equal(0x36c50, MiPlayLx06MpasReceiverEvidence.MpasCtrlProtocolHeaderCopyAddress);
        Assert.Equal(4, MiPlayLx06MpasReceiverEvidence.MpasCtrlProtocolBufferDataOffset);
        Assert.Equal(1, MiPlayLx06MpasReceiverEvidence.MpasCtrlProtocolWireCommandOffset);
        Assert.Equal(3, MiPlayLx06MpasReceiverEvidence.MpasCtrlProtocolWireSequenceOffset);
        Assert.Equal(5, MiPlayLx06MpasReceiverEvidence.MpasCtrlProtocolWirePayloadLengthOffset);
        Assert.Equal(0x36c60, MiPlayLx06MpasReceiverEvidence.MpasCtrlProtocolCommandFieldReadAddress);
        Assert.Equal(0x36c64, MiPlayLx06MpasReceiverEvidence.MpasCtrlProtocolSequenceFieldReadAddress);
        Assert.Equal(0x36c68, MiPlayLx06MpasReceiverEvidence.MpasCtrlProtocolPayloadLengthReadAddress);
        Assert.Equal(0x36c70, MiPlayLx06MpasReceiverEvidence.MpasCtrlProtocolCommandEndianSwapAddress);
        Assert.Equal(0x36c74, MiPlayLx06MpasReceiverEvidence.MpasCtrlProtocolSequenceEndianSwapAddress);
        Assert.Equal(0x36c7c, MiPlayLx06MpasReceiverEvidence.MpasCtrlProtocolPayloadLengthEndianSwapAddress);
        Assert.Equal(0x38, MiPlayLx06MpasReceiverEvidence.MpasCtrlProtocolCallbackPointerOffset);
        Assert.Equal(0x36ce8, MiPlayLx06MpasReceiverEvidence.MpasCtrlProtocolCallbackCallAddress);

        var decision = MiPlayLx06MpasReceiverEvidence.EvaluateCtrlProtocolFrameParserHandoff(snapshot);

        Assert.True(decision.CanProceed);
        Assert.Contains("0x36a68", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("9-byte '$' frame header", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("wire offsets 1/3/5", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("+0x38", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("ServerApp::doMpasCommand", decision.Reason, StringComparison.Ordinal);
    }
}
