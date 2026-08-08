using System.Text;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayOfficialPostAuthSequenceProbePlanTests
{
    [Fact]
    public void CreateStepsBuildsRecoveredOfficialOrderAndPayloads()
    {
        var steps = MiPlayOfficialPostAuthSequenceProbePlan.CreateSteps(firstCommandSequence: 0x0004);

        Assert.Collection(
            steps,
            step =>
            {
                Assert.Equal(MiPlayOfficialPostAuthSequenceStepKind.SendSourceName, step.Kind);
                Assert.Equal(MiPlayProtocolConstants.SetLocalDeviceInfoCommand, step.Command);
                Assert.Equal((ushort)0x0004, step.Sequence);
                Assert.Equal(MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand, step.ExpectedAcknowledgementCommand);
                Assert.False(step.AcknowledgementRequiredBeforeSetPlaySource);
                Assert.Equal(
                    MiPlayRealPhonePostAuthPlaintextEvidence.FirstSetLocalDeviceInfoJson,
                    MiPlayOfficialPostAuthSequenceProbePlan.DecodePlaintextUtf8(step));
                Assert.Equal(80, step.PlaintextPayload.Length);
            },
            step =>
            {
                Assert.Equal(MiPlayOfficialPostAuthSequenceStepKind.SendGetDeviceInfo, step.Kind);
                Assert.Equal(MiPlayProtocolConstants.GetDeviceInfoCommand, step.Command);
                Assert.Equal((ushort)0x0005, step.Sequence);
                Assert.Equal(MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand, step.ExpectedAcknowledgementCommand);
                Assert.Equal(step.Sequence, step.ExpectedAcknowledgementSequence);
                Assert.True(step.AcknowledgementRequiredBeforeSetPlaySource);
                Assert.Empty(step.PlaintextPayload);
            },
            step =>
            {
                Assert.Equal(MiPlayOfficialPostAuthSequenceStepKind.SendCanAlonePlayCtrl, step.Kind);
                Assert.Equal(MiPlayProtocolConstants.SetLocalDeviceInfoCommand, step.Command);
                Assert.Equal((ushort)0x0006, step.Sequence);
                Assert.Equal("{\"canAlonePlayCtrl\":\"1\"}", MiPlayOfficialPostAuthSequenceProbePlan.DecodePlaintextUtf8(step));
            },
            step =>
            {
                Assert.Equal(MiPlayOfficialPostAuthSequenceStepKind.SendAlonePlayCapacity, step.Kind);
                Assert.Equal(MiPlayProtocolConstants.SetLocalDeviceInfoCommand, step.Command);
                Assert.Equal((ushort)0x0007, step.Sequence);
                Assert.Equal("{\"alonePlayCapacity\":\"1\"}", MiPlayOfficialPostAuthSequenceProbePlan.DecodePlaintextUtf8(step));
            },
            step =>
            {
                Assert.Equal(MiPlayOfficialPostAuthSequenceStepKind.SendGetMirrorMode, step.Kind);
                Assert.Equal(MiPlayProtocolConstants.GetMirrorModeCommand, step.Command);
                Assert.Equal((ushort)0x0008, step.Sequence);
                Assert.Equal(MiPlayProtocolConstants.GetMirrorModeAcknowledgementCommand, step.ExpectedAcknowledgementCommand);
                Assert.Equal(step.Sequence, step.ExpectedAcknowledgementSequence);
                Assert.True(step.AcknowledgementRequiredBeforeSetPlaySource);
                Assert.Empty(step.PlaintextPayload);
            },
            step =>
            {
                Assert.Equal(MiPlayOfficialPostAuthSequenceStepKind.SendSetPlaySource, step.Kind);
                Assert.Equal(MiPlayProtocolConstants.SetPlaySourceCommand, step.Command);
                Assert.Equal((ushort)0x0009, step.Sequence);
                Assert.Equal(MiPlayProtocolConstants.SetPlaySourceAcknowledgementCommand, step.ExpectedAcknowledgementCommand);
                Assert.False(step.AcknowledgementRequiredBeforeSetPlaySource);
                Assert.Equal(
                    MiPlayRealPhonePostAuthPlaintextEvidence.OfficialSetPlaySourceJson,
                    MiPlayOfficialPostAuthSequenceProbePlan.DecodePlaintextUtf8(step));
            });
    }

    [Fact]
    public void EvaluatePreparesButDoesNotAuthorizeNetworkSendWithoutFreshAuthorization()
    {
        var decision = MiPlayOfficialPostAuthSequenceProbePlan.Evaluate(CreateSatisfiedPrerequisites(
            freshUserAuthorizationPresent: false));

        Assert.True(decision.CanPreparePlan);
        Assert.False(decision.CanSendNow);
        Assert.False(decision.SafeForNetworkUse);
        Assert.Contains("fresh explicit authorization", decision.Reason, StringComparison.Ordinal);
        Assert.Equal(6, decision.Steps.Count);
    }

    [Fact]
    public void EvaluateAllowsSendOnlyInsideFreshAuthorizationBoundary()
    {
        var decision = MiPlayOfficialPostAuthSequenceProbePlan.Evaluate(CreateSatisfiedPrerequisites(
            freshUserAuthorizationPresent: true));

        Assert.True(decision.CanPreparePlan);
        Assert.True(decision.CanSendNow);
        Assert.True(decision.SafeForNetworkUse);
        Assert.Contains("require 0x001f and 0x0035 before 0x0040", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("without Open/AddMirror/RTSP/media/playback/audio", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateBlocksMidSessionPcapOrderAsFreshDealSafetyDoneSequence()
    {
        var decision = MiPlayOfficialPostAuthSequenceProbePlan.Evaluate(
            CreateSatisfiedPrerequisites(
                freshUserAuthorizationPresent: true,
                freshSessionCommandOrderCaptured: false));

        Assert.True(decision.CanPreparePlan);
        Assert.False(decision.CanSendNow);
        Assert.False(decision.SafeForNetworkUse);
        Assert.Contains("starts mid-session at sequence 0x013a", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("does not prove that 0x0058 is the first command", decision.Reason, StringComparison.Ordinal);
        Assert.Equal(6, decision.Steps.Count);
    }

    [Fact]
    public void EvaluateRefusesFreshSendWhenSourceIdentityDiffersFromRecoveredOfficialFirstFrame()
    {
        var decision = MiPlayOfficialPostAuthSequenceProbePlan.Evaluate(
            CreateSatisfiedPrerequisites(freshUserAuthorizationPresent: true),
            sourceName: "DLNACast Windows",
            bluetoothMacHash: null);

        Assert.True(decision.CanPreparePlan);
        Assert.False(decision.CanSendNow);
        Assert.False(decision.SafeForNetworkUse);
        Assert.Contains("first 0x0058 source identity does not match", decision.Reason, StringComparison.Ordinal);
        Assert.Equal(
            "{\"sourceName\":\"DLNACast Windows\",\"mSourceBtMac\":\"\"}",
            MiPlayOfficialPostAuthSequenceProbePlan.DecodePlaintextUtf8(decision.Steps[0]));
    }

    [Fact]
    public void SafetyDataFrameDryRunRoundTripsPreparedStepsWithContinuousOutboundCbc()
    {
        var key = Encoding.ASCII.GetBytes("0123456789abcdef");
        var iv = Encoding.ASCII.GetBytes("fedcba9876543210");
        var steps = MiPlayOfficialPostAuthSequenceProbePlan.CreateSteps(firstCommandSequence: 0x0004);
        var sender = new MiPlaySafetyDataSessionCipher(key, iv);
        var receiver = new MiPlaySafetyDataSessionCipher(key, iv);

        var frames = MiPlayOfficialPostAuthSequenceProbePlan.CreateSafetyDataCommandFrames(steps, sender);

        Assert.Equal(steps.Count, frames.Count);
        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];

            Assert.True(MiPlayCommandFrameCodec.TryDecode(frames[index], out var commandFrame, out var bytesConsumed));
            Assert.NotNull(commandFrame);
            Assert.Equal(frames[index].Length, bytesConsumed);
            Assert.Equal(step.Command, commandFrame.Command);
            Assert.Equal(step.Sequence, commandFrame.Sequence);
            Assert.NotEqual(step.PlaintextPayload, commandFrame.Payload);
            Assert.True(receiver.TryDecryptVersion1(commandFrame.Payload, out var decrypted));
            Assert.NotNull(decrypted);
            Assert.Equal(step.PlaintextPayload, decrypted.Plaintext);
        }
    }

    [Fact]
    public void PayloadCodecsExposeRecoveredOfficialSingleFieldAndSetPlaySourceJson()
    {
        Assert.Equal(
            "{\"canAlonePlayCtrl\":\"1\"}",
            Encoding.UTF8.GetString(MiPlayLocalDeviceInfoPayloadCodec.EncodeCanAlonePlayCtrl()));
        Assert.Equal(
            "{\"alonePlayCapacity\":\"1\"}",
            Encoding.UTF8.GetString(MiPlayLocalDeviceInfoPayloadCodec.EncodeAlonePlayCapacity()));
        Assert.Equal(
            MiPlayRealPhonePostAuthPlaintextEvidence.OfficialSetPlaySourceJson,
            Encoding.UTF8.GetString(MiPlaySetPlaySourcePayloadCodec.EncodeRecoveredOfficialRuntimePayload()));
        Assert.Equal(
            MiPlayRealPhonePostAuthPlaintextEvidence.FirstSetLocalDeviceInfoJson,
            Encoding.UTF8.GetString(MiPlayLocalDeviceInfoPayloadCodec.EncodeRecoveredOfficialSourceIdentity()));
        Assert.Equal(
            MiPlayRealPhonePostAuthPlaintextEvidence.RecoveredOfficialSourceBluetoothMacHash,
            MiPlayLocalDeviceInfoPayloadCodec.NormalizeBluetoothMacHash(
                MiPlayRealPhonePostAuthPlaintextEvidence.RecoveredOfficialSourceBluetoothMacHash.ToLowerInvariant()));
    }

    private static MiPlayOfficialPostAuthSequencePrerequisites CreateSatisfiedPrerequisites(
        bool freshUserAuthorizationPresent,
        bool freshSessionCommandOrderCaptured = true) =>
        new(
            MutualSafetyAuthVerified: true,
            NativeNoResetOutboundProfileAvailable: true,
            OfficialPlaintextRecoveredFromRootPcap: true,
            FreshSessionCommandOrderCaptured: freshSessionCommandOrderCaptured,
            SafetyDataIntegrityEndianAlignedWithNative: true,
            LocalDeviceInfoPayloadsAvailable: true,
            GetDeviceInfoAcknowledgementParserAvailable: true,
            GetMirrorModePairLocalized: true,
            StopOnUnexpectedFrameOrClose: true,
            ForbidCmdOpen: true,
            ForbidAddMirror: true,
            ForbidRtsp: true,
            ForbidMediaPlaybackOrAudio: true,
            FreshUserAuthorizationPresent: freshUserAuthorizationPresent,
            FirstCommandSequence: 0x0004);
}
