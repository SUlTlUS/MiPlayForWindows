using System.Text;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayPostAuthSafetyDataStateBoundaryEvidenceTests
{
    [Fact]
    public void SnapshotCapturesOfflineOnlyPostAuthSafetyDataBoundary()
    {
        var snapshot = MiPlayPostAuthSafetyDataStateBoundaryEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.SafetyInfoSelectionObserved);
        Assert.True(snapshot.NativeAesKeyType1Observed);
        Assert.True(snapshot.NativeAesIvType2Observed);
        Assert.True(snapshot.NativeGenAesIvType2UsesSecondHalf);
        Assert.True(snapshot.ObservedS12InboundIvType1RequiredForPeerChallenge);
        Assert.True(snapshot.MutualSafetyAuthVerifiedWithObservedInboundIv);
        Assert.True(snapshot.ProbeSelectedObservedInboundCandidate);
        Assert.True(snapshot.ProbeUsesSelectedCandidateForOutboundSafetyAuth);
        Assert.True(snapshot.ProbeUsesSameCipherForPostAuthCommands);
        Assert.True(snapshot.SafetyDataV1ContainerMatchesNative);
        Assert.True(snapshot.OuterCommandFrameMatchesSendCmdPayload);
        Assert.True(snapshot.SeparateDirectionalCbcContextsObserved);
        Assert.True(snapshot.CoreCanModelAsymmetricInitialDirectionalIvs);
        Assert.False(snapshot.ProbePostAuthCipherForkOrResetModeled);
        Assert.True(snapshot.OfficialJsonSetPlaySourceClosedWithoutAck);
        Assert.True(snapshot.LegacyClearGetDeviceInfoRouteSucceeded);
        Assert.True(snapshot.NoNewNetworkOperationPerformed);
        Assert.True(snapshot.ForbidRepeatingLivePostAuthCommands);
    }

    [Fact]
    public void CandidateMatrixSeparatesNativeObservedAndAsymmetricIvHypotheses()
    {
        var candidates = MiPlayPostAuthSafetyDataStateBoundaryEvidence.CreateOfflineCandidateMatrix();

        Assert.Contains(candidates, candidate =>
            candidate.Label == "native-selection-symmetric-type2" &&
            candidate.EncryptIvMaterial == MiPlayPostAuthSafetyDataStateBoundaryEvidence.NativeIvMaterialForType2 &&
            candidate.DecryptIvMaterial == MiPlayPostAuthSafetyDataStateBoundaryEvidence.NativeIvMaterialForType2 &&
            candidate.GroundedByNativeSafetyInfoSelection &&
            !candidate.GroundedByObservedS12InboundDecrypt);
        Assert.Contains(candidates, candidate =>
            candidate.Label == "observed-s12-inbound-symmetric-type1" &&
            candidate.EncryptIvMaterial == MiPlayPostAuthSafetyDataStateBoundaryEvidence.ObservedS12InboundIvMaterial &&
            candidate.DecryptIvMaterial == MiPlayPostAuthSafetyDataStateBoundaryEvidence.ObservedS12InboundIvMaterial &&
            !candidate.GroundedByNativeSafetyInfoSelection &&
            candidate.GroundedByObservedS12InboundDecrypt);
        Assert.Contains(candidates, candidate =>
            candidate.Label == "asymmetric-native-outbound-observed-inbound" &&
            candidate.EncryptIvMaterial == MiPlayPostAuthSafetyDataStateBoundaryEvidence.NativeIvMaterialForType2 &&
            candidate.DecryptIvMaterial == MiPlayPostAuthSafetyDataStateBoundaryEvidence.ObservedS12InboundIvMaterial &&
            candidate.GroundedByNativeSafetyInfoSelection &&
            candidate.GroundedByObservedS12InboundDecrypt);
        Assert.Contains(candidates, candidate =>
            candidate.Label == "post-auth-fork-native-selection" &&
            candidate.ModelsPostAuthForkOrReset);
        Assert.All(candidates, candidate => Assert.False(candidate.SafeForNetworkUse));
    }

    [Fact]
    public void DeterministicCandidateVectorsUseOfficialSetPlaySourceFrameShape()
    {
        var vectors = MiPlayPostAuthSafetyDataStateBoundaryEvidence.CreateDeterministicCandidateVectors();

        Assert.Equal(4, vectors.Count);
        Assert.All(vectors, vector =>
        {
            Assert.Equal(MiPlayProtocolConstants.SetPlaySourceCommand, vector.Command);
            Assert.Equal((ushort)0x0004, vector.Sequence);
            Assert.Equal(61, vector.PlaintextLength);
            Assert.Equal(73, vector.SafetyDataPayloadLength);
            Assert.Equal(82, vector.CommandFrameLength);
            Assert.False(vector.SafeForNetworkUse);

            var commandFrameBytes = Convert.FromHexString(vector.CommandFrameHex);
            Assert.True(MiPlayCommandFrameCodec.TryDecode(commandFrameBytes, out var frame, out var bytesConsumed));
            Assert.NotNull(frame);
            Assert.Equal(commandFrameBytes.Length, bytesConsumed);
            Assert.Equal(vector.Command, frame.Command);
            Assert.Equal(vector.Sequence, frame.Sequence);
            Assert.Equal(vector.SafetyDataPayloadLength, frame.Payload.Length);
            Assert.Equal(vector.SafetyDataPayloadHex, Convert.ToHexString(frame.Payload));

            Assert.True(MiPlaySafetyDataHeaderCodec.TryDecodeVersion1(frame.Payload, out var header));
            Assert.NotNull(header);
            Assert.Equal(9, header.HeaderLength);
            Assert.Equal((byte)3, header.PaddingLength);
            Assert.Equal(64, header.PayloadLength);
        });
    }

    [Fact]
    public void DeterministicCandidateVectorsSeparateOutboundAndInboundEvidence()
    {
        var vectors = MiPlayPostAuthSafetyDataStateBoundaryEvidence.CreateDeterministicCandidateVectors();
        var nativeNoReset = vectors.Single(vector => vector.Label == "native-selection-symmetric-type2");
        var observedNoReset = vectors.Single(vector => vector.Label == "observed-s12-inbound-symmetric-type1");
        var asymmetricNoReset = vectors.Single(vector => vector.Label == "asymmetric-native-outbound-observed-inbound");
        var nativeFork = vectors.Single(vector => vector.Label == "post-auth-fork-native-selection");

        Assert.Equal(2, nativeNoReset.OutboundSafetyAuthFramesPreAdvanced);
        Assert.Equal(2, observedNoReset.OutboundSafetyAuthFramesPreAdvanced);
        Assert.Equal(2, asymmetricNoReset.OutboundSafetyAuthFramesPreAdvanced);
        Assert.Equal(0, nativeFork.OutboundSafetyAuthFramesPreAdvanced);
        Assert.False(nativeNoReset.ModelsPostAuthForkOrReset);
        Assert.True(nativeFork.ModelsPostAuthForkOrReset);

        Assert.Equal(nativeNoReset.CommandFrameSha256, asymmetricNoReset.CommandFrameSha256);
        Assert.Equal(nativeNoReset.CommandFrameHex, asymmetricNoReset.CommandFrameHex);
        Assert.NotEqual(nativeNoReset.CommandFrameSha256, observedNoReset.CommandFrameSha256);
        Assert.NotEqual(nativeNoReset.CommandFrameSha256, nativeFork.CommandFrameSha256);
    }
    [Fact]
    public void DeterministicCandidateVectorsMatchGoldenCommandFrameBytes()
    {
        var vectors = MiPlayPostAuthSafetyDataStateBoundaryEvidence.CreateDeterministicCandidateVectors();
        var expectedCommandFrameHex = new Dictionary<string, string>
        {
            ["native-selection-symmetric-type2"] =
                "240040000400000049000701E0035B61A6250DC444292D7302DD88E634E6A16A1A0FCC2D00D8498BCBEAC9FD693EEE5F67C29734859ECADB834567CB6BF6DE969F0AAA5A64C03D1C1312AA91DEFAAC1F1DB3",
            ["observed-s12-inbound-symmetric-type1"] =
                "240040000400000049000701E0039614755B749AF5929C766C99B0F2D5DB7C6C1318788ABB9C36714BA71A5414D6153F6400E00E3BD9BF643A65D341480B5822E51D1539F3285AC77E945963ABADF2102845",
            ["asymmetric-native-outbound-observed-inbound"] =
                "240040000400000049000701E0035B61A6250DC444292D7302DD88E634E6A16A1A0FCC2D00D8498BCBEAC9FD693EEE5F67C29734859ECADB834567CB6BF6DE969F0AAA5A64C03D1C1312AA91DEFAAC1F1DB3",
            ["post-auth-fork-native-selection"] =
                "240040000400000049000701E00302544A7694CF9C841ABBB692FB514AF7C37281C26A0F00905D27FC47F75986A30FBD657A05494307E1A971E7E650BC3DC50898F5AFD62874D1ACEE74098EFA1F3C80967A",
        };
        var expectedCommandFrameSha256 = new Dictionary<string, string>
        {
            ["native-selection-symmetric-type2"] = "5c1d648c8cbd65c99b92bd96ef3e666aa648256b2d4ef82350513f6ae2eef21e",
            ["observed-s12-inbound-symmetric-type1"] = "bd9f769e4a1f866ec3c467e34f5a88edb28ff890053ac896584863ad5ca57d6e",
            ["asymmetric-native-outbound-observed-inbound"] = "5c1d648c8cbd65c99b92bd96ef3e666aa648256b2d4ef82350513f6ae2eef21e",
            ["post-auth-fork-native-selection"] = "ee39934bafff4d66a729b38f7d034c00938f92d6a5f4cd31cb3db970b33a5b26",
        };

        foreach (var vector in vectors)
        {
            Assert.Equal(expectedCommandFrameHex[vector.Label], vector.CommandFrameHex);
            Assert.Equal(expectedCommandFrameSha256[vector.Label], vector.CommandFrameSha256);
        }
    }
    [Fact]
    public void DealSafetyDoneContinuitySnapshotCapturesOldSourceStaticTrace()
    {
        var snapshot = MiPlayPostAuthSafetyDataStateBoundaryEvidence.CreateDealSafetyDoneCipherContinuitySnapshot();

        Assert.True(snapshot.DealSafetyInfoAckInstallsSafetyDataDeal);
        Assert.True(snapshot.SafetyDataDealPointerStoredBeforeSendSafetyAuth);
        Assert.True(snapshot.LocalSafetyAuthSentThroughSendCmdPayloadAfterInstall);
        Assert.True(snapshot.DealSafetyDoneObservedAfterSuccessfulSafetyAuthAck);
        Assert.True(snapshot.DealSafetyDoneSetsSafetyDoneFlag);
        Assert.True(snapshot.DealSafetyDoneNotifiesListener);
        Assert.True(snapshot.DealSafetyDoneSchedulesTimers);
        Assert.False(snapshot.DealSafetyDoneReinstallsSafetyDataDealObserved);
        Assert.False(snapshot.DealSafetyDoneClearsSafetyDataDealPointerObserved);
        Assert.True(snapshot.PostAuthHeartbeatUsesSendCmdPayload);
        Assert.True(snapshot.NoNewNetworkOperationPerformed);
        Assert.True(snapshot.ForbidNetworkProbeFromThisEvidence);
    }

    [Fact]
    public void DealSafetyDoneContinuityPrefersNativeNoResetSendOnlyVectorOffline()
    {
        var decision = MiPlayPostAuthSafetyDataStateBoundaryEvidence.EvaluateDealSafetyDoneCipherContinuity(
            MiPlayPostAuthSafetyDataStateBoundaryEvidence.CreateDealSafetyDoneCipherContinuitySnapshot());
        var vectors = MiPlayPostAuthSafetyDataStateBoundaryEvidence.CreateDeterministicCandidateVectors();
        var preferred = vectors.Single(vector => vector.Label == decision.PreferredSendOnlyVectorLabel);
        var equivalent = vectors.Single(vector => vector.Label == decision.EquivalentSendOnlyVectorLabel);
        var rejectedFork = vectors.Single(vector => vector.Label == decision.RejectedForkVectorLabel);

        Assert.True(decision.NativeSourceSupportsNoResetPostAuthOutboundState);
        Assert.False(decision.NativeSourceSupportsPostAuthCipherForkReset);
        Assert.Equal("native-selection-symmetric-type2", decision.PreferredSendOnlyVectorLabel);
        Assert.Equal("asymmetric-native-outbound-observed-inbound", decision.EquivalentSendOnlyVectorLabel);
        Assert.Equal("post-auth-fork-native-selection", decision.RejectedForkVectorLabel);
        Assert.Contains("No DealSafetyDone SafetyDataDeal pointer clear or reinstall", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("send-only bytes cannot distinguish", decision.Reason, StringComparison.Ordinal);
        Assert.Equal(preferred.CommandFrameSha256, equivalent.CommandFrameSha256);
        Assert.NotEqual(preferred.CommandFrameSha256, rejectedFork.CommandFrameSha256);
    }

    [Fact]
    public void ObservedDealSafetyDoneReinstallWouldFlipDecisionToForkResetCandidate()
    {
        var snapshot = MiPlayPostAuthSafetyDataStateBoundaryEvidence.CreateDealSafetyDoneCipherContinuitySnapshot() with
        {
            DealSafetyDoneReinstallsSafetyDataDealObserved = true,
        };

        var decision = MiPlayPostAuthSafetyDataStateBoundaryEvidence.EvaluateDealSafetyDoneCipherContinuity(snapshot);

        Assert.False(decision.NativeSourceSupportsNoResetPostAuthOutboundState);
        Assert.True(decision.NativeSourceSupportsPostAuthCipherForkReset);
        Assert.Equal("post-auth-fork-native-selection", decision.PreferredSendOnlyVectorLabel);
        Assert.Contains("pointer reset/reinstall", decision.Reason, StringComparison.Ordinal);
    }
    [Fact]
    public void CurrentStateHasProvableGapAndBlocksAnotherBusinessProbe()
    {
        var decision = MiPlayPostAuthSafetyDataStateBoundaryEvidence.Evaluate(
            MiPlayPostAuthSafetyDataStateBoundaryEvidence.CreateCurrentSnapshot());

        Assert.False(decision.CanReuseCurrentProbeCipherForNewPostAuthBusinessProbe);
        Assert.True(decision.HasProvableImplementationGap);
        Assert.Contains("inbound-only S12 IV workaround", decision.FirstProvableDifference, StringComparison.Ordinal);
        Assert.Contains("mutual SafetyAuth", decision.FirstProvableDifference, StringComparison.Ordinal);
        Assert.Contains("business commands", decision.FirstProvableDifference, StringComparison.Ordinal);
        Assert.Contains("deterministic candidate vectors", decision.NextOfflineTest, StringComparison.Ordinal);
    }

    [Fact]
    public void BoundaryExpansionSuppressesImplementationConclusion()
    {
        var snapshot = MiPlayPostAuthSafetyDataStateBoundaryEvidence.CreateCurrentSnapshot() with
        {
            NoNewNetworkOperationPerformed = false,
        };

        var decision = MiPlayPostAuthSafetyDataStateBoundaryEvidence.Evaluate(snapshot);

        Assert.False(decision.CanReuseCurrentProbeCipherForNewPostAuthBusinessProbe);
        Assert.False(decision.HasProvableImplementationGap);
        Assert.Contains("boundary was exceeded", decision.FirstProvableDifference, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionCipherCanModelDifferentInitialEncryptAndDecryptIvsOffline()
    {
        var key = Encoding.ASCII.GetBytes("0123456789abcdef");
        var nativeType2Iv = Encoding.ASCII.GetBytes("fedcba9876543210");
        var observedInboundIv = Encoding.ASCII.GetBytes("0011223344556677");
        var plaintext = "candidate-vec"u8.ToArray();

        var sender = new MiPlaySafetyDataSessionCipher(
            key,
            encryptAesIv: nativeType2Iv,
            decryptAesIv: observedInboundIv);
        var encrypted = sender.EncryptVersion1(plaintext);

        var matchingReceiver = new MiPlaySafetyDataSessionCipher(
            key,
            encryptAesIv: observedInboundIv,
            decryptAesIv: nativeType2Iv);
        var decoded = matchingReceiver.TryDecryptVersion1(encrypted, out var result);

        Assert.True(decoded);
        Assert.NotNull(result);
        Assert.Equal(plaintext, result.Plaintext);

        var wrongReceiver = new MiPlaySafetyDataSessionCipher(
            key,
            encryptAesIv: observedInboundIv,
            decryptAesIv: observedInboundIv);
        Assert.False(wrongReceiver.TryDecryptVersion1(encrypted, out _));
    }
}