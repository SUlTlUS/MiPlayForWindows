using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlaySetPlaySourceOneFrameProbePlanTests
{
    [Fact]
    public void PreparedPlanBuildsOfficialMinimalPayloadButDoesNotSendWithoutFreshAuthorization()
    {
        var decision = MiPlaySetPlaySourceOneFrameProbePlan.Evaluate(CompletePrerequisites());

        Assert.True(decision.CanPreparePlan);
        Assert.False(decision.CanSendNow);
        Assert.Equal(MiPlayProtocolConstants.SetPlaySourceCommand, decision.Command);
        Assert.Equal((ushort)4, decision.Sequence);
        Assert.Equal(
            "{\"ref_channel\":\"playpage\",\"ref_function\":\"\",\"ref_content\":\"\"}",
            decision.PayloadText);
        Assert.NotNull(decision.PayloadText);
        Assert.Equal(decision.PayloadText.Length, decision.PlaintextPayloadLength);
        Assert.Contains("fresh explicit user authorization", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0x0040", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0x0041", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no retry", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0x0058", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Cmd_Open", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("AddMirror", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("RTSP", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("audio", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AuthorizedDecisionAllowsOnlyOneOfficialJsonSetPlaySourceFrame()
    {
        var decision = MiPlaySetPlaySourceOneFrameProbePlan.Evaluate(
            CompletePrerequisites() with { FreshUserAuthorizationPresent = true });

        Assert.True(decision.CanPreparePlan);
        Assert.True(decision.CanSendNow);
        Assert.Equal(MiPlayProtocolConstants.SetPlaySourceCommand, decision.Command);
        Assert.Equal((ushort)4, decision.Sequence);
        Assert.Equal(
            "{\"ref_channel\":\"playpage\",\"ref_function\":\"\",\"ref_content\":\"\"}",
            decision.PayloadText);
        Assert.Contains("single authorized network action", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stop on close", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no retry", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0x0058", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("media", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsNonMinimalPayloadOrMissingEmptyNegativeEvidence()
    {
        var nonMinimalChannel = MiPlaySetPlaySourceOneFrameProbePlan.Evaluate(
            CompletePrerequisites() with { RefChannel = "controlcenter" });

        Assert.False(nonMinimalChannel.CanPreparePlan);
        Assert.Contains("minimal payload", nonMinimalChannel.Reason, StringComparison.OrdinalIgnoreCase);

        var nonEmptyFunction = MiPlaySetPlaySourceOneFrameProbePlan.Evaluate(
            CompletePrerequisites() with { RefFunction = "single_room" });

        Assert.False(nonEmptyFunction.CanPreparePlan);
        Assert.Contains("minimal payload", nonEmptyFunction.Reason, StringComparison.OrdinalIgnoreCase);

        var missingPriorNegative = MiPlaySetPlaySourceOneFrameProbePlan.Evaluate(
            CompletePrerequisites() with { PriorEmptyAckRoutesClosedWithoutAcknowledgement = false });

        Assert.False(missingPriorNegative.CanPreparePlan);
        Assert.Contains("empty-payload", missingPriorNegative.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsAnyWiderBusinessMediaRetryOrObservationBoundary()
    {
        Assert.False(MiPlaySetPlaySourceOneFrameProbePlan.Evaluate(
            CompletePrerequisites() with { ObserveOnlyFor0041 = false }).CanPreparePlan);
        Assert.False(MiPlaySetPlaySourceOneFrameProbePlan.Evaluate(
            CompletePrerequisites() with { StopOnAnyUnexpectedFrameOrClose = false }).CanPreparePlan);
        Assert.False(MiPlaySetPlaySourceOneFrameProbePlan.Evaluate(
            CompletePrerequisites() with { ForbidRetry = false }).CanPreparePlan);
        Assert.False(MiPlaySetPlaySourceOneFrameProbePlan.Evaluate(
            CompletePrerequisites() with { Forbid0058 = false }).CanPreparePlan);
        Assert.False(MiPlaySetPlaySourceOneFrameProbePlan.Evaluate(
            CompletePrerequisites() with { ForbidCmdOpen = false }).CanPreparePlan);
        Assert.False(MiPlaySetPlaySourceOneFrameProbePlan.Evaluate(
            CompletePrerequisites() with { ForbidAddMirror = false }).CanPreparePlan);
        Assert.False(MiPlaySetPlaySourceOneFrameProbePlan.Evaluate(
            CompletePrerequisites() with { ForbidRtsp = false }).CanPreparePlan);
        Assert.False(MiPlaySetPlaySourceOneFrameProbePlan.Evaluate(
            CompletePrerequisites() with { ForbidMediaPlaybackOrAudio = false }).CanPreparePlan);
    }

    private static MiPlaySetPlaySourceOneFramePrerequisites CompletePrerequisites() =>
        new(
            MutualSafetyAuthVerified: true,
            SafetyDataSessionCandidateAvailable: true,
            OfficialSenderPayloadBuilderLocalized: true,
            NativeSetPlaySourceCommandId0040Confirmed: true,
            NativeConnectCmdSession2OnlyCarriesLyraKeyMaterial: true,
            PriorEmptyAckRoutesClosedWithoutAcknowledgement: true,
            FreshUserAuthorizationPresent: false,
            NextCommandSequence: 4,
            RefChannel: MiPlaySetPlaySourceOneFrameProbePlan.MinimalRefChannel,
            RefFunction: MiPlaySetPlaySourceOneFrameProbePlan.MinimalRefFunction,
            RefContent: MiPlaySetPlaySourceOneFrameProbePlan.MinimalRefContent,
            ObserveOnlyFor0041: true,
            StopOnAnyUnexpectedFrameOrClose: true,
            ForbidRetry: true,
            Forbid0058: true,
            ForbidCmdOpen: true,
            ForbidAddMirror: true,
            ForbidRtsp: true,
            ForbidMediaPlaybackOrAudio: true);
}
