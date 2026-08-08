using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayPostAuthGetDeviceInfoProbePolicyTests
{
    [Fact]
    public void SafetyDataFrameKeepsOuterGetDeviceInfoCommandAndEmptyPlaintext()
    {
        var cipher = new MiPlaySafetyDataSessionCipher(new byte[16], new byte[16]);
        var frameBytes = MiPlayPostAuthGetDeviceInfoProbe.ToSafetyDataCommandFrame(4, cipher);

        Assert.True(MiPlayCommandFrameCodec.TryDecode(frameBytes, out var frame, out var consumed));
        Assert.NotNull(frame);
        Assert.Equal(frameBytes.Length, consumed);
        Assert.Equal(MiPlayProtocolConstants.GetDeviceInfoCommand, frame.Command);
        Assert.Equal((ushort)4, frame.Sequence);
        Assert.NotEmpty(frame.Payload);
        Assert.True(cipher.TryDecryptVersion1(frame.Payload, out var decoded));
        Assert.NotNull(decoded);
        Assert.Empty(decoded.Plaintext);
    }

    [Fact]
    public void PreparedDecisionRequiresFreshAuthorizationBeforeSend()
    {
        var decision = MiPlayPostAuthGetDeviceInfoProbePolicy.EvaluateReadiness(
            CompletePrerequisites() with { FreshUserAuthorizationPresent = false });

        Assert.True(decision.CanPreparePlan);
        Assert.False(decision.CanSendNow);
        Assert.Equal(MiPlayProtocolConstants.GetDeviceInfoCommand, decision.Command);
        Assert.Equal(MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand, decision.ExpectedAcknowledgementCommand);
        Assert.Equal((ushort)4, decision.Sequence);
        Assert.Equal(0, decision.PlaintextPayloadLength);
        Assert.Equal(MiPlayPostAuthProbePolicy.MinimumDeviceInfoAcknowledgementPayloadLength, decision.MinimumAcknowledgementPayloadLength);
        Assert.Contains("fresh explicit user authorization", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("observe only for same-sequence 0x001f", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x0040", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x0058", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("RTSP", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthorizedDecisionAllowsOnlyOneReadOnlyGetDeviceInfoFrame()
    {
        var decision = MiPlayPostAuthGetDeviceInfoProbePolicy.EvaluateReadiness(CompletePrerequisites());

        Assert.True(decision.CanPreparePlan);
        Assert.True(decision.CanSendNow);
        Assert.Equal(MiPlayProtocolConstants.GetDeviceInfoCommand, decision.Command);
        Assert.Equal(MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand, decision.ExpectedAcknowledgementCommand);
        Assert.Equal((ushort)4, decision.Sequence);
        Assert.Equal(0, decision.PlaintextPayloadLength);
        Assert.Equal(MiPlayPostAuthProbePolicy.MinimumDeviceInfoAcknowledgementPayloadLength, decision.MinimumAcknowledgementPayloadLength);
        Assert.Contains("single authorized read-only network action", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("no retry", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("media", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadinessRejectsMissingCipherProfileOrStaticOrder()
    {
        var missingOutboundProfile = MiPlayPostAuthGetDeviceInfoProbePolicy.EvaluateReadiness(
            CompletePrerequisites() with { NativeNoResetOutboundProfileAvailable = false });
        var missingOrder = MiPlayPostAuthGetDeviceInfoProbePolicy.EvaluateReadiness(
            CompletePrerequisites() with { OfficialGetDeviceInfoOrderLocalized = false });

        Assert.False(missingOutboundProfile.CanPreparePlan);
        Assert.False(missingOutboundProfile.CanSendNow);
        Assert.Contains("native no-reset outbound", missingOutboundProfile.Reason, StringComparison.Ordinal);
        Assert.False(missingOrder.CanPreparePlan);
        Assert.False(missingOrder.CanSendNow);
        Assert.Contains("cmdSessionSuccess -> getDeviceInfo", missingOrder.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadinessRejectsAnyWiderBusinessOrMediaBoundary()
    {
        Assert.False(MiPlayPostAuthGetDeviceInfoProbePolicy.EvaluateReadiness(
            CompletePrerequisites() with { Forbid0040 = false }).CanSendNow);
        Assert.False(MiPlayPostAuthGetDeviceInfoProbePolicy.EvaluateReadiness(
            CompletePrerequisites() with { Forbid0058 = false }).CanSendNow);
        Assert.False(MiPlayPostAuthGetDeviceInfoProbePolicy.EvaluateReadiness(
            CompletePrerequisites() with { ForbidCmdOpen = false }).CanSendNow);
        Assert.False(MiPlayPostAuthGetDeviceInfoProbePolicy.EvaluateReadiness(
            CompletePrerequisites() with { ForbidAddMirror = false }).CanSendNow);
        Assert.False(MiPlayPostAuthGetDeviceInfoProbePolicy.EvaluateReadiness(
            CompletePrerequisites() with { ForbidRtsp = false }).CanSendNow);
        Assert.False(MiPlayPostAuthGetDeviceInfoProbePolicy.EvaluateReadiness(
            CompletePrerequisites() with { ForbidMediaPlaybackOrAudio = false }).CanSendNow);
    }

    [Fact]
    public void ReadinessRequiresSameSequenceAndMinimumPayloadGate()
    {
        var noSameSequence = MiPlayPostAuthGetDeviceInfoProbePolicy.EvaluateReadiness(
            CompletePrerequisites() with { RequireSameSequence001f = false });
        var noPayloadGate = MiPlayPostAuthGetDeviceInfoProbePolicy.EvaluateReadiness(
            CompletePrerequisites() with { RequireMinimumPayloadLength = false });

        Assert.False(noSameSequence.CanSendNow);
        Assert.Contains("sequence", noSameSequence.Reason, StringComparison.Ordinal);
        Assert.False(noPayloadGate.CanSendNow);
        Assert.Contains("minimum decrypted", noPayloadGate.Reason, StringComparison.Ordinal);
    }

    private static MiPlayPostAuthGetDeviceInfoProbePrerequisites CompletePrerequisites() =>
        new(
            MutualSafetyAuthVerified: true,
            SafetyDataSessionCandidateAvailable: true,
            NativeNoResetOutboundProfileAvailable: true,
            OfficialGetDeviceInfoOrderLocalized: true,
            CmdSourceGetDeviceInfoFrameShapeLocalized: true,
            Source001fAckListenerLocalized: true,
            ReceiverGetDeviceInfoAckSemanticsLocalized: true,
            FreshUserAuthorizationPresent: true,
            NextCommandSequence: 4,
            EmptyPayloadOnly: true,
            ObserveOnlyFor001f: true,
            RequireSameSequence001f: true,
            RequireMinimumPayloadLength: true,
            StopOnAnyUnexpectedFrameOrClose: true,
            ForbidRetry: true,
            Forbid0040: true,
            Forbid0058: true,
            ForbidCmdOpen: true,
            ForbidAddMirror: true,
            ForbidRtsp: true,
            ForbidMediaPlaybackOrAudio: true);
}
