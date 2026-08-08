using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayPassiveSenderBootstrapCaptureEvidenceTests
{
    [Fact]
    public void CapturedFramesDecodeToOfficialPhoneBootstrapSequence()
    {
        var snapshot = MiPlayPassiveSenderBootstrapCaptureEvidence.CreateCurrentSnapshot();

        Assert.Equal("passive-sender-20260726-111422.stdout.log", snapshot.ArtifactName);
        Assert.Equal("192.168.10.20:49432", snapshot.PhoneEndpoint);
        Assert.Equal("192.168.10.9:8899", snapshot.CaptureEndpoint);
        Assert.True(snapshot.SentOnlyLegacyChallenge);
        Assert.Equal(MiPlayProtocolConstants.LegacySafetyChallengeCommand, snapshot.OutboundChallengeCommand);
        Assert.Equal((ushort)0, snapshot.OutboundChallengeSequence);
        Assert.Equal("123456789", snapshot.OutboundChallengeText);

        Assert.Equal(MiPlayProtocolConstants.NativeSourceVersionCommand, snapshot.NativeSourceVersionCommand);
        Assert.Equal((ushort)0, snapshot.NativeSourceVersionSequence);
        Assert.Equal("3.1.6030516", snapshot.NativeSourceVersion);

        Assert.Equal(MiPlayProtocolConstants.LegacySafetyAcknowledgementCommand, snapshot.LegacyAcknowledgementCommand);
        Assert.Equal((ushort)0, snapshot.LegacyAcknowledgementSequence);
        Assert.True(snapshot.LegacyAcknowledgementValid);

        Assert.Equal(MiPlayProtocolConstants.SafetyInfoCommand, snapshot.SafetyInfoCommand);
        Assert.Equal((ushort)1, snapshot.SafetyInfoSequence);
        Assert.Equal((uint)1, snapshot.SafetyInfoOffer.AuthKeyTypes);
        Assert.Equal((uint)7, snapshot.SafetyInfoOffer.AuthAlgorithmTypes);
        Assert.Equal((uint)1, snapshot.SafetyInfoOffer.IntegrityTypes);
        Assert.Equal((uint)3, snapshot.SafetyInfoOffer.AesKeyTypes);
        Assert.Equal((uint)3, snapshot.SafetyInfoOffer.AesIvTypes);
        Assert.True(snapshot.PhoneClosedAfterNoSafetyInfoAcknowledgement);
    }

    [Fact]
    public void DecisionTreatsBootstrapAsPreAuthSenderOracleOnly()
    {
        var decision = MiPlayPassiveSenderBootstrapCaptureEvidence.EvaluateCaptureBoundary(
            MiPlayPassiveSenderBootstrapCaptureEvidence.CreateCurrentSnapshot());

        Assert.True(decision.CanProceed);
        Assert.Contains("voluntarily connected", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("sent no 0x1401/0x1402/business/media frames", decision.Reason, StringComparison.Ordinal);
    }
}
