using System.Security.Cryptography;
using System.Text;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayPostAuthSafetyDataCipherProfileTests
{
    [Fact]
    public void NativeNoResetOutboundProfileSeparatesPostAuthSendStateFromInboundWorkaround()
    {
        var profile = MiPlayPostAuthSafetyDataCipherProfile.CreateNativeNoResetOutboundProfile();

        Assert.Equal("native-no-reset-outbound-type2", profile.Label);
        Assert.Equal("native-selection-symmetric-type2", profile.ExpectedSendOnlyVectorLabel);
        Assert.Equal(MiPlayPostAuthSafetyDataStateBoundaryEvidence.NativeAesKeyMaterialForType1, profile.AesKeyMaterial);
        Assert.Equal(MiPlayPostAuthSafetyDataStateBoundaryEvidence.NativeIvMaterialForType2, profile.InitialEncryptIvMaterial);
        Assert.Equal(MiPlayPostAuthSafetyDataStateBoundaryEvidence.ObservedS12InboundIvMaterial, profile.InitialDecryptIvMaterial);
        Assert.Equal(2, profile.RequiredOutboundPreAdvanceFrames);
        Assert.False(profile.ModelsDealSafetyDoneForkOrReset);
        Assert.False(profile.SafeForNetworkUse);
        Assert.Contains("Outbound-only", profile.Boundary, StringComparison.Ordinal);
        Assert.Contains("does not prove inbound", profile.Boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeNoResetOutboundProfileMatchesPreferredGoldenVector()
    {
        var profile = MiPlayPostAuthSafetyDataCipherProfile.CreateNativeNoResetOutboundProfile();
        var frame = BuildOfficialSetPlaySourceFrame(profile);
        var preferred = MiPlayPostAuthSafetyDataStateBoundaryEvidence
            .CreateDeterministicCandidateVectors()
            .Single(vector => vector.Label == profile.ExpectedSendOnlyVectorLabel);

        Assert.Equal(preferred.CommandFrameHex, Convert.ToHexString(frame));
        Assert.Equal(preferred.CommandFrameSha256, Sha256Hex(frame));
    }

    [Fact]
    public void ObservedInboundPromotedProfileMatchesOldProbeNegativeControlVector()
    {
        var nativeProfile = MiPlayPostAuthSafetyDataCipherProfile.CreateNativeNoResetOutboundProfile();
        var oldProbeProfile = MiPlayPostAuthSafetyDataCipherProfile.CreateObservedInboundPromotedOutboundProfile();
        var nativeFrame = BuildOfficialSetPlaySourceFrame(nativeProfile);
        var oldProbeFrame = BuildOfficialSetPlaySourceFrame(oldProbeProfile);
        var negativeControl = MiPlayPostAuthSafetyDataStateBoundaryEvidence
            .CreateDeterministicCandidateVectors()
            .Single(vector => vector.Label == oldProbeProfile.ExpectedSendOnlyVectorLabel);

        Assert.Equal("observed-inbound-promoted-outbound-type1", oldProbeProfile.Label);
        Assert.False(oldProbeProfile.SafeForNetworkUse);
        Assert.Equal(negativeControl.CommandFrameHex, Convert.ToHexString(oldProbeFrame));
        Assert.NotEqual(Sha256Hex(nativeFrame), Sha256Hex(oldProbeFrame));
    }

    [Fact]
    public void OutboundCipherProfileRequiresBothLocalSafetyAuthFramesBeforePostAuthCommand()
    {
        var profile = MiPlayPostAuthSafetyDataCipherProfile.CreateNativeNoResetOutboundProfile();
        var authKey = MiPlayPostAuthSafetyDataStateBoundaryEvidence.DeterministicVectorSyntheticAuthKey;
        var onlyLocalChallenge = new[]
        {
            Encoding.ASCII.GetBytes(MiPlayPostAuthSafetyDataStateBoundaryEvidence.DeterministicPreAuth1402Plaintext),
        };

        var error = Assert.Throws<ArgumentException>(() =>
            MiPlayPostAuthSafetyDataCipherProfile.CreateOutboundCommandCipher(authKey, profile, onlyLocalChallenge));

        Assert.Contains("requires exactly 2 outbound SafetyAuth", error.Message, StringComparison.Ordinal);
    }

    private static byte[] BuildOfficialSetPlaySourceFrame(MiPlayPostAuthSafetyDataOutboundCipherProfile profile)
    {
        var authKey = MiPlayPostAuthSafetyDataStateBoundaryEvidence.DeterministicVectorSyntheticAuthKey;
        var preAuthPlaintexts = new[]
        {
            Encoding.ASCII.GetBytes(MiPlayPostAuthSafetyDataStateBoundaryEvidence.DeterministicPreAuth1402Plaintext),
            Encoding.ASCII.GetBytes(MiPlayPostAuthSafetyDataStateBoundaryEvidence.DeterministicPreAuth1403Plaintext),
        };
        var cipher = MiPlayPostAuthSafetyDataCipherProfile.CreateOutboundCommandCipher(
            authKey,
            profile,
            preAuthPlaintexts);

        return MiPlaySetPlaySourceOneFrameProbe.ToSafetyDataCommandFrame(
            MiPlayPostAuthSafetyDataStateBoundaryEvidence.DeterministicVectorSequence,
            cipher);
    }

    private static string Sha256Hex(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}