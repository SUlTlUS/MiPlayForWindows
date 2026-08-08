using System.Text;

namespace DLNACast.Core.MiPlay;

public sealed record MiPlayPostAuthSafetyDataOutboundCipherProfile(
    string Label,
    string ExpectedSendOnlyVectorLabel,
    string AesKeyMaterial,
    string InitialEncryptIvMaterial,
    string InitialDecryptIvMaterial,
    int RequiredOutboundPreAdvanceFrames,
    bool ModelsDealSafetyDoneForkOrReset,
    bool SafeForNetworkUse,
    string Boundary);

/// <summary>
/// Builds offline SafetyData cipher profiles for post-auth outbound command
/// bytes. These profiles deliberately do not claim inbound response readiness.
/// </summary>
public static class MiPlayPostAuthSafetyDataCipherProfile
{
    public const string NativeNoResetOutboundProfileLabel = "native-no-reset-outbound-type2";
    public const string ObservedInboundPromotedOutboundProfileLabel = "observed-inbound-promoted-outbound-type1";
    public const int NoResetOutboundSafetyAuthPreAdvanceFrameCount = 2;

    public static MiPlayPostAuthSafetyDataOutboundCipherProfile CreateNativeNoResetOutboundProfile() =>
        new(
            Label: NativeNoResetOutboundProfileLabel,
            ExpectedSendOnlyVectorLabel: MiPlayPostAuthSafetyDataStateBoundaryEvidence.DealSafetyDoneNoResetPreferredVectorLabel,
            AesKeyMaterial: MiPlayPostAuthSafetyDataStateBoundaryEvidence.NativeAesKeyMaterialForType1,
            InitialEncryptIvMaterial: MiPlayPostAuthSafetyDataStateBoundaryEvidence.NativeIvMaterialForType2,
            InitialDecryptIvMaterial: MiPlayPostAuthSafetyDataStateBoundaryEvidence.ObservedS12InboundIvMaterial,
            RequiredOutboundPreAdvanceFrames: NoResetOutboundSafetyAuthPreAdvanceFrameCount,
            ModelsDealSafetyDoneForkOrReset: false,
            SafeForNetworkUse: false,
            Boundary: "Outbound-only no-reset profile: pre-advance local 0x1402 and local 0x1403 encryptions, then encrypt the first post-auth command with native aesIv type 2. It does not prove inbound decrypt state or authorize a network probe.");

    public static MiPlayPostAuthSafetyDataOutboundCipherProfile CreateObservedInboundPromotedOutboundProfile() =>
        new(
            Label: ObservedInboundPromotedOutboundProfileLabel,
            ExpectedSendOnlyVectorLabel: "observed-s12-inbound-symmetric-type1",
            AesKeyMaterial: MiPlayPostAuthSafetyDataStateBoundaryEvidence.NativeAesKeyMaterialForType1,
            InitialEncryptIvMaterial: MiPlayPostAuthSafetyDataStateBoundaryEvidence.ObservedS12InboundIvMaterial,
            InitialDecryptIvMaterial: MiPlayPostAuthSafetyDataStateBoundaryEvidence.ObservedS12InboundIvMaterial,
            RequiredOutboundPreAdvanceFrames: NoResetOutboundSafetyAuthPreAdvanceFrameCount,
            ModelsDealSafetyDoneForkOrReset: false,
            SafeForNetworkUse: false,
            Boundary: "Negative-control profile matching the old Probe behaviour: promotes the observed S12 inbound IV workaround to outbound post-auth encryption. It is retained only for byte comparison.");

    public static MiPlaySafetyDataSessionCipher CreateOutboundCommandCipher(
        string authKey,
        MiPlayPostAuthSafetyDataOutboundCipherProfile profile,
        IReadOnlyList<byte[]> outboundSafetyAuthPlaintexts)
    {
        ArgumentException.ThrowIfNullOrEmpty(authKey);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(outboundSafetyAuthPlaintexts);

        if (outboundSafetyAuthPlaintexts.Count != profile.RequiredOutboundPreAdvanceFrames)
        {
            throw new ArgumentException(
                $"Profile {profile.Label} requires exactly {profile.RequiredOutboundPreAdvanceFrames} outbound SafetyAuth plaintext frames before the first post-auth command.",
                nameof(outboundSafetyAuthPlaintexts));
        }

        var aesKey = SelectDeterministicOrRuntimeMaterial(authKey, profile.AesKeyMaterial);
        var encryptIv = SelectDeterministicOrRuntimeMaterial(authKey, profile.InitialEncryptIvMaterial);
        var decryptIv = SelectDeterministicOrRuntimeMaterial(authKey, profile.InitialDecryptIvMaterial);
        var cipher = new MiPlaySafetyDataSessionCipher(aesKey, encryptIv, decryptIv);

        foreach (var plaintext in outboundSafetyAuthPlaintexts)
        {
            ArgumentNullException.ThrowIfNull(plaintext);
            cipher.EncryptVersion1(plaintext);
        }

        return cipher;
    }

    private static byte[] SelectDeterministicOrRuntimeMaterial(string authKey, string material)
    {
        var materialType = material == MiPlayPostAuthSafetyDataStateBoundaryEvidence.NativeIvMaterialForType2
            ? MiPlaySafetyKeyDerivation.SecondHalfMaterialType
            : MiPlaySafetyKeyDerivation.FirstHalfMaterialType;
        return Encoding.ASCII.GetBytes(MiPlaySafetyKeyDerivation.SelectDerivedAesMaterial(authKey, materialType));
    }
}