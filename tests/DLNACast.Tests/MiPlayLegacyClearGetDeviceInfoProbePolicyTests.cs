using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayLegacyClearGetDeviceInfoProbePolicyTests
{
    [Fact]
    public void ReadyDecisionAllowsOnlyOneEmptyClearGetDeviceInfoAfterReadyNotify()
    {
        var decision = MiPlayLegacyClearGetDeviceInfoProbePolicy.EvaluateReadiness(CompletePrerequisites());

        Assert.True(decision.CanSend);
        Assert.Equal(MiPlayProtocolConstants.GetDeviceInfoCommand, decision.Command);
        Assert.Equal(MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand, decision.ExpectedAcknowledgementCommand);
        Assert.Equal((ushort)2, decision.Sequence);
        Assert.Equal(0, decision.PlaintextPayloadLength);
        Assert.Contains("clear-text", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0x001e", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0x001f", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("state=3", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no 0x1400", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SafetyData", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("Cmd_SetPlaySource", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("Cmd_Open", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x0058", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RTSP", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("audio", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RefusesMissingStaticLegacyAuthOrReadyNotifyPrerequisites()
    {
        var missingLegacyAuth = MiPlayLegacyClearGetDeviceInfoProbePolicy.EvaluateReadiness(
            CompletePrerequisites() with { LegacyChallengeAcknowledged = false });
        Assert.False(missingLegacyAuth.CanSend);
        Assert.Contains("0x0028", missingLegacyAuth.Reason, StringComparison.OrdinalIgnoreCase);

        var missingDispatch = MiPlayLegacyClearGetDeviceInfoProbePolicy.EvaluateReadiness(
            CompletePrerequisites() with { MpasGetDeviceInfoDispatchObserved = false });
        Assert.False(missingDispatch.CanSend);
        Assert.Contains("0x001e", missingDispatch.Reason, StringComparison.OrdinalIgnoreCase);

        var missingAck = MiPlayLegacyClearGetDeviceInfoProbePolicy.EvaluateReadiness(
            CompletePrerequisites() with { MpasGetDeviceInfoAcknowledgementObserved = false });
        Assert.False(missingAck.CanSend);
        Assert.Contains("0x001f", missingAck.Reason, StringComparison.OrdinalIgnoreCase);

        var missingAsyncBoundary = MiPlayLegacyClearGetDeviceInfoProbePolicy.EvaluateReadiness(
            CompletePrerequisites() with { MpasGetDeviceInfoAsyncPreparePathObserved = false });
        Assert.False(missingAsyncBoundary.CanSend);
        Assert.Contains("async", missingAsyncBoundary.Reason, StringComparison.OrdinalIgnoreCase);

        var missingReadyNotify = MiPlayLegacyClearGetDeviceInfoProbePolicy.EvaluateReadiness(
            CompletePrerequisites() with { ReadyStateNotifyObservedBeforeSend = false });
        Assert.False(missingReadyNotify.CanSend);
        Assert.Contains("state", missingReadyNotify.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RefusesModernSafetySetPlaySourceOpenOrMediaBoundaryExpansion()
    {
        Assert.False(MiPlayLegacyClearGetDeviceInfoProbePolicy.EvaluateReadiness(
            CompletePrerequisites() with { NoModernSafetyInfoOrSafetyAuth = false }).CanSend);
        Assert.False(MiPlayLegacyClearGetDeviceInfoProbePolicy.EvaluateReadiness(
            CompletePrerequisites() with { NoSafetyDataEncryption = false }).CanSend);
        Assert.False(MiPlayLegacyClearGetDeviceInfoProbePolicy.EvaluateReadiness(
            CompletePrerequisites() with { EmptyPayloadOnly = false }).CanSend);
        Assert.False(MiPlayLegacyClearGetDeviceInfoProbePolicy.EvaluateReadiness(
            CompletePrerequisites() with { NoSetPlaySource = false }).CanSend);
        Assert.False(MiPlayLegacyClearGetDeviceInfoProbePolicy.EvaluateReadiness(
            CompletePrerequisites() with { ForbidCmdOpen = false }).CanSend);
        Assert.False(MiPlayLegacyClearGetDeviceInfoProbePolicy.EvaluateReadiness(
            CompletePrerequisites() with { Forbid0058 = false }).CanSend);
        Assert.False(MiPlayLegacyClearGetDeviceInfoProbePolicy.EvaluateReadiness(
            CompletePrerequisites() with { ForbidAddMirror = false }).CanSend);
        Assert.False(MiPlayLegacyClearGetDeviceInfoProbePolicy.EvaluateReadiness(
            CompletePrerequisites() with { ForbidRtsp = false }).CanSend);
        Assert.False(MiPlayLegacyClearGetDeviceInfoProbePolicy.EvaluateReadiness(
            CompletePrerequisites() with { ForbidPlaybackOrAudio = false }).CanSend);
    }

    [Fact]
    public void ClearFrameKeepsOuterGetDeviceInfoCommandAndEmptyPayload()
    {
        var frameBytes = MiPlayLegacyClearGetDeviceInfoProbe.ToCommandFrame(sequence: 2);

        Assert.True(MiPlayCommandFrameCodec.TryDecode(frameBytes, out var frame, out var bytesConsumed));
        Assert.NotNull(frame);
        Assert.Equal(frameBytes.Length, bytesConsumed);
        Assert.Equal(MiPlayProtocolConstants.GetDeviceInfoCommand, frame.Command);
        Assert.Equal((ushort)2, frame.Sequence);
        Assert.Empty(frame.Payload);
    }

    private static MiPlayLegacyClearGetDeviceInfoPrerequisites CompletePrerequisites() =>
        new(
            LegacyChallengeAcknowledged: true,
            NativeVersionBootstrapSent: true,
            MpasGetDeviceInfoDispatchObserved: true,
            MpasGetDeviceInfoAcknowledgementObserved: true,
            MpasGetDeviceInfoAsyncPreparePathObserved: true,
            ReadyStateNotifyObservedBeforeSend: true,
            NextCommandSequence: 2,
            EmptyPayloadOnly: true,
            NoModernSafetyInfoOrSafetyAuth: true,
            NoSafetyDataEncryption: true,
            NoSetPlaySource: true,
            NoMediaBoundary: true,
            ForbidCmdOpen: true,
            Forbid0058: true,
            ForbidAddMirror: true,
            ForbidRtsp: true,
            ForbidPlaybackOrAudio: true);
}