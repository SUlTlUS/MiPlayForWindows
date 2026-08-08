using System.Security.Cryptography;
using System.Text;

namespace DLNACast.Core.MiPlay;

public sealed record MiPlayPostAuthSafetyDataIvCandidate(
    string Label,
    string AesKeyMaterial,
    string EncryptIvMaterial,
    string DecryptIvMaterial,
    bool GroundedByNativeSafetyInfoSelection,
    bool GroundedByObservedS12InboundDecrypt,
    bool ModelsPostAuthForkOrReset,
    bool SafeForNetworkUse);
public sealed record MiPlayPostAuthSafetyDataCandidateVector(
    string Label,
    ushort Command,
    ushort Sequence,
    int PlaintextLength,
    int SafetyDataPayloadLength,
    int CommandFrameLength,
    int OutboundSafetyAuthFramesPreAdvanced,
    bool ModelsPostAuthForkOrReset,
    bool SafeForNetworkUse,
    string SafetyDataPayloadSha256,
    string CommandFrameSha256,
    string SafetyDataPayloadHex,
    string CommandFrameHex);

public sealed record MiPlayPostAuthSafetyDataStateBoundarySnapshot(
    bool SafetyInfoSelectionObserved,
    bool NativeAesKeyType1Observed,
    bool NativeAesIvType2Observed,
    bool NativeGenAesIvType2UsesSecondHalf,
    bool ObservedS12InboundIvType1RequiredForPeerChallenge,
    bool MutualSafetyAuthVerifiedWithObservedInboundIv,
    bool ProbeSelectedObservedInboundCandidate,
    bool ProbeUsesSelectedCandidateForOutboundSafetyAuth,
    bool ProbeUsesSameCipherForPostAuthCommands,
    bool SafetyDataV1ContainerMatchesNative,
    bool OuterCommandFrameMatchesSendCmdPayload,
    bool SeparateDirectionalCbcContextsObserved,
    bool CoreCanModelAsymmetricInitialDirectionalIvs,
    bool ProbePostAuthCipherForkOrResetModeled,
    bool OfficialJsonSetPlaySourceClosedWithoutAck,
    bool LegacyClearGetDeviceInfoRouteSucceeded,
    bool NoNewNetworkOperationPerformed,
    bool ForbidRepeatingLivePostAuthCommands);

public sealed record MiPlayPostAuthSafetyDataStateBoundaryDecision(
    bool CanReuseCurrentProbeCipherForNewPostAuthBusinessProbe,
    bool HasProvableImplementationGap,
    string FirstProvableDifference,
    string NextOfflineTest);

public sealed record MiPlayPostAuthDealSafetyDoneCipherContinuitySnapshot(
    bool DealSafetyInfoAckInstallsSafetyDataDeal,
    bool SafetyDataDealPointerStoredBeforeSendSafetyAuth,
    bool LocalSafetyAuthSentThroughSendCmdPayloadAfterInstall,
    bool DealSafetyDoneObservedAfterSuccessfulSafetyAuthAck,
    bool DealSafetyDoneSetsSafetyDoneFlag,
    bool DealSafetyDoneNotifiesListener,
    bool DealSafetyDoneSchedulesTimers,
    bool DealSafetyDoneReinstallsSafetyDataDealObserved,
    bool DealSafetyDoneClearsSafetyDataDealPointerObserved,
    bool PostAuthHeartbeatUsesSendCmdPayload,
    bool NoNewNetworkOperationPerformed,
    bool ForbidNetworkProbeFromThisEvidence);

public sealed record MiPlayPostAuthDealSafetyDoneCipherContinuityDecision(
    bool NativeSourceSupportsNoResetPostAuthOutboundState,
    bool NativeSourceSupportsPostAuthCipherForkReset,
    string PreferredSendOnlyVectorLabel,
    string EquivalentSendOnlyVectorLabel,
    string RejectedForkVectorLabel,
    string Reason);

/// <summary>
/// Offline-only boundary that separates native SafetyData structure from the
/// current Probe's post-auth cipher-state assumption. It does not send frames.
/// </summary>
public static class MiPlayPostAuthSafetyDataStateBoundaryEvidence
{
    public const string ObservedSafetyInfoSelection =
        "authKey=1, authAlgorithm=4, integrity=1, aesKey=1, aesIv=2";
    public const string NativeAesKeyMaterialForType1 = "authKey first half";
    public const string NativeIvMaterialForType2 = "authKey second half";
    public const string ObservedS12InboundIvMaterial = "authKey first half";
    public const string ProbeSelectedCandidate = "peer-first:observed-s12-inbound-iv-type1";
    public const string FirstProvableDifference =
        "Probe promotes an inbound-only S12 IV workaround to the outbound/post-auth command cipher state; this is verified for mutual SafetyAuth but unverified for business commands.";
    public const string CurrentImplementationGap =
        "The core cipher can now model asymmetric directional IVs, but the live Probe still selects one observed-inbound candidate and reuses that same cipher state for outbound SafetyAuth and post-auth business commands without a proven DealSafetyDone fork/reset.";
    public const string NextOfflineTest =
        "Generate deterministic candidate vectors for native type-2 outbound IV, observed type-1 inbound IV, asymmetric direction IVs, and no-reset versus post-auth fork/reset states; compare only offline until one candidate is grounded by APK or LX06 1.94 evidence.";
    public const string DeterministicVectorSyntheticAuthKey = "0123456789abcdeffedcba9876543210";
    public const string DeterministicPreAuth1402Plaintext = "synthetic-local-safetyauth-1402";
    public const string DeterministicPreAuth1403Plaintext = "synthetic-local-safetyauth-1403";
    public const ushort DeterministicVectorCommand = MiPlayProtocolConstants.SetPlaySourceCommand;
    public const ushort DeterministicVectorSequence = MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationEvidence.SetPlaySourceSequence;
    public const string DealSafetyDoneNoResetPreferredVectorLabel = "native-selection-symmetric-type2";
    public const string DealSafetyDoneNoResetEquivalentSendOnlyVectorLabel = "asymmetric-native-outbound-observed-inbound";
    public const string DealSafetyDoneForkResetVectorLabel = "post-auth-fork-native-selection";

    public static MiPlayPostAuthSafetyDataStateBoundarySnapshot CreateCurrentSnapshot() =>
        new(
            SafetyInfoSelectionObserved: true,
            NativeAesKeyType1Observed: true,
            NativeAesIvType2Observed: true,
            NativeGenAesIvType2UsesSecondHalf: true,
            ObservedS12InboundIvType1RequiredForPeerChallenge: true,
            MutualSafetyAuthVerifiedWithObservedInboundIv: true,
            ProbeSelectedObservedInboundCandidate: true,
            ProbeUsesSelectedCandidateForOutboundSafetyAuth: true,
            ProbeUsesSameCipherForPostAuthCommands: true,
            SafetyDataV1ContainerMatchesNative: true,
            OuterCommandFrameMatchesSendCmdPayload: true,
            SeparateDirectionalCbcContextsObserved: true,
            CoreCanModelAsymmetricInitialDirectionalIvs: true,
            ProbePostAuthCipherForkOrResetModeled: false,
            OfficialJsonSetPlaySourceClosedWithoutAck: true,
            LegacyClearGetDeviceInfoRouteSucceeded: true,
            NoNewNetworkOperationPerformed: true,
            ForbidRepeatingLivePostAuthCommands: true);

    public static IReadOnlyList<MiPlayPostAuthSafetyDataIvCandidate> CreateOfflineCandidateMatrix() =>
        [
            new(
                Label: "native-selection-symmetric-type2",
                AesKeyMaterial: NativeAesKeyMaterialForType1,
                EncryptIvMaterial: NativeIvMaterialForType2,
                DecryptIvMaterial: NativeIvMaterialForType2,
                GroundedByNativeSafetyInfoSelection: true,
                GroundedByObservedS12InboundDecrypt: false,
                ModelsPostAuthForkOrReset: false,
                SafeForNetworkUse: false),
            new(
                Label: "observed-s12-inbound-symmetric-type1",
                AesKeyMaterial: NativeAesKeyMaterialForType1,
                EncryptIvMaterial: ObservedS12InboundIvMaterial,
                DecryptIvMaterial: ObservedS12InboundIvMaterial,
                GroundedByNativeSafetyInfoSelection: false,
                GroundedByObservedS12InboundDecrypt: true,
                ModelsPostAuthForkOrReset: false,
                SafeForNetworkUse: false),
            new(
                Label: "asymmetric-native-outbound-observed-inbound",
                AesKeyMaterial: NativeAesKeyMaterialForType1,
                EncryptIvMaterial: NativeIvMaterialForType2,
                DecryptIvMaterial: ObservedS12InboundIvMaterial,
                GroundedByNativeSafetyInfoSelection: true,
                GroundedByObservedS12InboundDecrypt: true,
                ModelsPostAuthForkOrReset: false,
                SafeForNetworkUse: false),
            new(
                Label: "post-auth-fork-native-selection",
                AesKeyMaterial: NativeAesKeyMaterialForType1,
                EncryptIvMaterial: NativeIvMaterialForType2,
                DecryptIvMaterial: NativeIvMaterialForType2,
                GroundedByNativeSafetyInfoSelection: true,
                GroundedByObservedS12InboundDecrypt: false,
                ModelsPostAuthForkOrReset: true,
                SafeForNetworkUse: false),
        ];

    public static IReadOnlyList<MiPlayPostAuthSafetyDataCandidateVector> CreateDeterministicCandidateVectors()
    {
        var payload = MiPlaySetPlaySourceOneFrameProbe.BuildMinimalOfficialPayload();
        var vectors = new List<MiPlayPostAuthSafetyDataCandidateVector>();

        foreach (var candidate in CreateOfflineCandidateMatrix())
        {
            var aesKey = SelectDeterministicMaterial(candidate.AesKeyMaterial);
            var encryptIv = SelectDeterministicMaterial(candidate.EncryptIvMaterial);
            var decryptIv = SelectDeterministicMaterial(candidate.DecryptIvMaterial);
            var cipher = new MiPlaySafetyDataSessionCipher(aesKey, encryptIv, decryptIv);
            var preAdvancedFrames = 0;

            if (!candidate.ModelsPostAuthForkOrReset)
            {
                cipher.EncryptVersion1(Encoding.ASCII.GetBytes(DeterministicPreAuth1402Plaintext));
                cipher.EncryptVersion1(Encoding.ASCII.GetBytes(DeterministicPreAuth1403Plaintext));
                preAdvancedFrames = 2;
            }

            var safetyDataPayload = cipher.EncryptVersion1(payload);
            var commandFrame = MiPlayCommandFrameCodec.Encode(
                DeterministicVectorCommand,
                DeterministicVectorSequence,
                safetyDataPayload);

            vectors.Add(new MiPlayPostAuthSafetyDataCandidateVector(
                Label: candidate.Label,
                Command: DeterministicVectorCommand,
                Sequence: DeterministicVectorSequence,
                PlaintextLength: payload.Length,
                SafetyDataPayloadLength: safetyDataPayload.Length,
                CommandFrameLength: commandFrame.Length,
                OutboundSafetyAuthFramesPreAdvanced: preAdvancedFrames,
                ModelsPostAuthForkOrReset: candidate.ModelsPostAuthForkOrReset,
                SafeForNetworkUse: false,
                SafetyDataPayloadSha256: Sha256Hex(safetyDataPayload),
                CommandFrameSha256: Sha256Hex(commandFrame),
                SafetyDataPayloadHex: Convert.ToHexString(safetyDataPayload),
                CommandFrameHex: Convert.ToHexString(commandFrame)));
        }

        return vectors;
    }

    public static MiPlayPostAuthDealSafetyDoneCipherContinuitySnapshot CreateDealSafetyDoneCipherContinuitySnapshot() =>
        new(
            DealSafetyInfoAckInstallsSafetyDataDeal: true,
            SafetyDataDealPointerStoredBeforeSendSafetyAuth: true,
            LocalSafetyAuthSentThroughSendCmdPayloadAfterInstall: true,
            DealSafetyDoneObservedAfterSuccessfulSafetyAuthAck: true,
            DealSafetyDoneSetsSafetyDoneFlag: true,
            DealSafetyDoneNotifiesListener: true,
            DealSafetyDoneSchedulesTimers: true,
            DealSafetyDoneReinstallsSafetyDataDealObserved: false,
            DealSafetyDoneClearsSafetyDataDealPointerObserved: false,
            PostAuthHeartbeatUsesSendCmdPayload: true,
            NoNewNetworkOperationPerformed: true,
            ForbidNetworkProbeFromThisEvidence: true);

    public static MiPlayPostAuthDealSafetyDoneCipherContinuityDecision EvaluateDealSafetyDoneCipherContinuity(
        MiPlayPostAuthDealSafetyDoneCipherContinuitySnapshot snapshot)
    {
        if (!snapshot.NoNewNetworkOperationPerformed || !snapshot.ForbidNetworkProbeFromThisEvidence)
        {
            return new MiPlayPostAuthDealSafetyDoneCipherContinuityDecision(
                false,
                false,
                string.Empty,
                string.Empty,
                string.Empty,
                "The DealSafetyDone cipher-continuity evidence boundary was exceeded.");
        }

        if (!snapshot.DealSafetyInfoAckInstallsSafetyDataDeal ||
            !snapshot.SafetyDataDealPointerStoredBeforeSendSafetyAuth ||
            !snapshot.LocalSafetyAuthSentThroughSendCmdPayloadAfterInstall)
        {
            return new MiPlayPostAuthDealSafetyDoneCipherContinuityDecision(
                false,
                false,
                string.Empty,
                string.Empty,
                string.Empty,
                "The source-side SafetyDataDeal installation path before SafetyAuth is incomplete.");
        }

        if (snapshot.DealSafetyDoneReinstallsSafetyDataDealObserved ||
            snapshot.DealSafetyDoneClearsSafetyDataDealPointerObserved)
        {
            return new MiPlayPostAuthDealSafetyDoneCipherContinuityDecision(
                false,
                true,
                DealSafetyDoneForkResetVectorLabel,
                string.Empty,
                string.Empty,
                "The bounded static trace observed a DealSafetyDone SafetyDataDeal pointer reset/reinstall, so the fork/reset vector must be considered first.");
        }

        if (snapshot.DealSafetyDoneObservedAfterSuccessfulSafetyAuthAck &&
            snapshot.DealSafetyDoneSetsSafetyDoneFlag &&
            snapshot.DealSafetyDoneNotifiesListener &&
            snapshot.DealSafetyDoneSchedulesTimers &&
            snapshot.PostAuthHeartbeatUsesSendCmdPayload)
        {
            return new MiPlayPostAuthDealSafetyDoneCipherContinuityDecision(
                true,
                false,
                DealSafetyDoneNoResetPreferredVectorLabel,
                DealSafetyDoneNoResetEquivalentSendOnlyVectorLabel,
                DealSafetyDoneForkResetVectorLabel,
                "In the old source-side APK trace, dealSafetyInfoAck installs SafetyDataDeal before local SafetyAuth, DealSafetyDone only sets the done flag/listener/timers, and post-auth heartbeat still goes through sendCmdPayload. No DealSafetyDone SafetyDataDeal pointer clear or reinstall is observed, so the native no-reset outbound vector is the best offline source-path candidate; send-only bytes cannot distinguish the equivalent asymmetric inbound-IV hypothesis.");
        }

        return new MiPlayPostAuthDealSafetyDoneCipherContinuityDecision(
            false,
            false,
            string.Empty,
            string.Empty,
            string.Empty,
            "DealSafetyDone side effects are incomplete; keep both no-reset and fork/reset vectors as unresolved offline candidates.");
    }
    private static byte[] SelectDeterministicMaterial(string material) => material switch
    {
        NativeAesKeyMaterialForType1 or ObservedS12InboundIvMaterial =>
            Encoding.ASCII.GetBytes(DeterministicVectorSyntheticAuthKey[..16]),
        NativeIvMaterialForType2 => Encoding.ASCII.GetBytes(DeterministicVectorSyntheticAuthKey[16..]),
        _ => throw new ArgumentOutOfRangeException(nameof(material), material, "Unknown deterministic SafetyData material label."),
    };

    private static string Sha256Hex(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    public static MiPlayPostAuthSafetyDataStateBoundaryDecision Evaluate(
        MiPlayPostAuthSafetyDataStateBoundarySnapshot snapshot)
    {
        if (!snapshot.NoNewNetworkOperationPerformed || !snapshot.ForbidRepeatingLivePostAuthCommands)
        {
            return new MiPlayPostAuthSafetyDataStateBoundaryDecision(
                false,
                false,
                "The analysis boundary was exceeded, so no post-auth SafetyData implementation conclusion should be drawn.",
                "Restart from the last offline-only evidence state.");
        }

        if (!snapshot.SafetyDataV1ContainerMatchesNative || !snapshot.OuterCommandFrameMatchesSendCmdPayload)
        {
            return new MiPlayPostAuthSafetyDataStateBoundaryDecision(
                false,
                false,
                "The SafetyData container or outer command frame is still unproven; do not attribute failures to IV state yet.",
                "Complete native sendCmdPayload/SafetyDataDeal byte-level frame evidence first.");
        }

        if (!snapshot.SeparateDirectionalCbcContextsObserved)
        {
            return new MiPlayPostAuthSafetyDataStateBoundaryDecision(
                false,
                false,
                "Native directional CBC state has not been proven.",
                "Recheck SafetyDataDeal encrypt/decrypt context ownership offline.");
        }

        if (snapshot.ObservedS12InboundIvType1RequiredForPeerChallenge &&
            snapshot.ProbeSelectedObservedInboundCandidate &&
            snapshot.ProbeUsesSameCipherForPostAuthCommands &&
            !snapshot.ProbePostAuthCipherForkOrResetModeled)
        {
            return new MiPlayPostAuthSafetyDataStateBoundaryDecision(
                false,
                true,
                FirstProvableDifference,
                NextOfflineTest);
        }

        return new MiPlayPostAuthSafetyDataStateBoundaryDecision(
            true,
            false,
            "No current offline evidence separates the Probe cipher state from the native post-auth SafetyData state.",
            "Keep comparing byte-level vectors before any live probe is considered.");
    }
}