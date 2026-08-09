using System.Text;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayPostAuthSafetyDataOutboundDryRunTests
{
    [Fact]
    public void OfficialSetPlaySourceDryRunComparesNativeNoResetWithOldProbeNegativeControl()
    {
        var comparison = CreateSyntheticComparison();

        Assert.Equal("native-no-reset-outbound-type2", comparison.NativeNoReset.ProfileLabel);
        Assert.Equal("native-selection-symmetric-type2", comparison.NativeNoReset.ExpectedVectorLabel);
        Assert.Equal("observed-inbound-promoted-outbound-type1", comparison.ObservedInboundPromotedNegativeControl.ProfileLabel);
        Assert.Equal("observed-s12-inbound-symmetric-type1", comparison.ObservedInboundPromotedNegativeControl.ExpectedVectorLabel);
        Assert.True(comparison.FramesDiffer);
        Assert.Contains("Dry-run only", comparison.Boundary, StringComparison.Ordinal);
        Assert.Contains("does not authorize", comparison.Boundary, StringComparison.Ordinal);
        Assert.False(comparison.NativeNoReset.SafeForNetworkUse);
        Assert.False(comparison.ObservedInboundPromotedNegativeControl.SafeForNetworkUse);
    }

    [Fact]
    public void SyntheticDryRunMatchesLockedGoldenVectorHashes()
    {
        var comparison = CreateSyntheticComparison();

        Assert.Equal((ushort)0x0040, comparison.NativeNoReset.Command);
        Assert.Equal((ushort)0x0004, comparison.NativeNoReset.Sequence);
        Assert.Equal(61, comparison.NativeNoReset.PlaintextPayloadLength);
        Assert.Equal(73, comparison.NativeNoReset.SafetyDataPayloadLength);
        Assert.Equal(82, comparison.NativeNoReset.CommandFrameLength);
        Assert.Equal("5c1d648c8cbd65c99b92bd96ef3e666aa648256b2d4ef82350513f6ae2eef21e", comparison.NativeNoReset.CommandFrameSha256);
        Assert.Equal("bd9f769e4a1f866ec3c467e34f5a88edb28ff890053ac896584863ad5ca57d6e", comparison.ObservedInboundPromotedNegativeControl.CommandFrameSha256);
    }

    [Fact]
    public void DryRunUsesProvidedPostAuthSequence()
    {
        var sequenceFour = CreateSyntheticComparison();
        var sequenceFive = MiPlayPostAuthSafetyDataOutboundDryRun.CompareOfficialSetPlaySourceProfiles(
            MiPlayPostAuthSafetyDataStateBoundaryEvidence.DeterministicVectorSyntheticAuthKey,
            Encoding.ASCII.GetBytes(MiPlayPostAuthSafetyDataStateBoundaryEvidence.DeterministicPreAuth1402Plaintext),
            Encoding.ASCII.GetBytes(MiPlayPostAuthSafetyDataStateBoundaryEvidence.DeterministicPreAuth1403Plaintext),
            sequence: 5);

        Assert.Equal((ushort)0x0005, sequenceFive.NativeNoReset.Sequence);
        Assert.Equal((ushort)0x0005, sequenceFive.ObservedInboundPromotedNegativeControl.Sequence);
        Assert.NotEqual(sequenceFour.NativeNoReset.CommandFrameSha256, sequenceFive.NativeNoReset.CommandFrameSha256);
        Assert.NotEqual(sequenceFour.ObservedInboundPromotedNegativeControl.CommandFrameSha256, sequenceFive.ObservedInboundPromotedNegativeControl.CommandFrameSha256);
    }

    [Fact]
    public void DryRunRequiresBothOutboundSafetyAuthPlaintexts()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            MiPlayPostAuthSafetyDataOutboundDryRun.CompareOfficialSetPlaySourceProfiles(
                MiPlayPostAuthSafetyDataStateBoundaryEvidence.DeterministicVectorSyntheticAuthKey,
                [],
                Encoding.ASCII.GetBytes(MiPlayPostAuthSafetyDataStateBoundaryEvidence.DeterministicPreAuth1403Plaintext),
                sequence: 4));

        Assert.Contains("local 0x1402", error.Message, StringComparison.Ordinal);
    }

    private static MiPlayPostAuthSafetyDataOutboundDryRunComparison CreateSyntheticComparison() =>
        MiPlayPostAuthSafetyDataOutboundDryRun.CompareOfficialSetPlaySourceProfiles(
            MiPlayPostAuthSafetyDataStateBoundaryEvidence.DeterministicVectorSyntheticAuthKey,
            Encoding.ASCII.GetBytes(MiPlayPostAuthSafetyDataStateBoundaryEvidence.DeterministicPreAuth1402Plaintext),
            Encoding.ASCII.GetBytes(MiPlayPostAuthSafetyDataStateBoundaryEvidence.DeterministicPreAuth1403Plaintext),
            MiPlayPostAuthSafetyDataStateBoundaryEvidence.DeterministicVectorSequence);
}