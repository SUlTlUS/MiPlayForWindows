using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayLegacySystemAudioSessionRunnerTests
{
    [Fact]
    public void UsesTheOfficialProStatusQueryOrder()
    {
        Assert.Equal(
            MiPlayLegacyStatusQueryOrder.VolumeStateMediaInfo,
            MiPlayLegacySystemAudioSessionRunner.ValidatedStatusQueryOrder);
        Assert.Equal(
            [
                "write 1: 0x0036 seq=0 sourceVersion=1.0.1123012\\0 + 0x0029 seq=receiver-challenge lowercase HMAC-SHA1(full challenge)",
                "write 2: 0x001e seq=1 empty GetDeviceInfo",
                "write 3: 0x0058 seq=2 sourceName-only JSON",
                "write 4: 0x0058 seq=3 {\"isSameAccount\":0}",
                "write 5: 0x0034 seq=4 empty GetMirrorMode",
                "write 6: 0x000e seq=5 empty GetVolume",
                "write 7: 0x001c seq=6 empty GetState",
                "write 8: 0x0014 seq=7 empty GetMediaInfo",
            ],
            MiPlayLegacyAudioSourceBootstrapProbeGuard.CreateDryRunLedger(
                MiPlayLegacySystemAudioSessionRunner.ValidatedStatusQueryOrder));
    }

    [Fact]
    public async Task LabelsStartupPhaseTimeoutsWithoutHidingTheCause()
    {
        var pending = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
            MiPlayLegacySystemAudioSessionRunner.WaitForStartupPhaseAsync(
                pending.Task,
                TimeSpan.FromMilliseconds(20),
                "The reverse audio callback timed out.",
                CancellationToken.None));

        Assert.Equal("The reverse audio callback timed out.", exception.Message);
        Assert.IsType<TimeoutException>(exception.InnerException);
    }

    [Fact]
    public async Task StartupPhaseWaitPreservesCallerCancellation()
    {
        var pending = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            MiPlayLegacySystemAudioSessionRunner.WaitForStartupPhaseAsync(
                pending.Task,
                TimeSpan.FromSeconds(1),
                "This must not replace cancellation.",
                cancellation.Token));
    }

    [Fact]
    public async Task StartupPhaseWaitReturnsCompletedResult()
    {
        var result = await MiPlayLegacySystemAudioSessionRunner.WaitForStartupPhaseAsync(
            Task.FromResult(42),
            TimeSpan.FromSeconds(1),
            "unused",
            CancellationToken.None);

        Assert.Equal(42, result);
    }

    [Fact]
    public void FormatsBootstrapProgressAsAStableDiagnosticLedger()
    {
        var progress = new MiPlayLegacyAudioSourceProgress(
            MiPlayLegacyAudioSourcePhase.AwaitingAccountAndMirrorAcknowledgements,
            DeviceInfoAcknowledged: true,
            SourceNameAcknowledged: true,
            AccountAcknowledged: true,
            MirrorModeAcknowledged: false,
            StatusQueriesPrepared: true,
            VolumeAcknowledged: true,
            StateAcknowledged: false,
            MediaInfoObserved: false);

        Assert.Equal(
            "phase=AwaitingAccountAndMirrorAcknowledgements; deviceInfo=1, sourceName=1, " +
            "account=1, mirror=0, queries=1, volume=1, state=0, mediaInfo=0.",
            MiPlayLegacySystemAudioSessionRunner.FormatBootstrapProgress(progress));
    }

    [Fact]
    public void TreatsPrematureBackgroundCancellationAsFatal()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            MiPlayLegacySystemAudioSessionRunner.ThrowIfBackgroundFailed(
                Task.FromCanceled(canceled.Token)));

        Assert.Contains("stopped before", exception.Message);
    }

    [Fact]
    public void PropagatesTheUnderlyingBackgroundFailure()
    {
        var failure = new IOException("RTSP failed");

        var exception = Assert.Throws<IOException>(() =>
            MiPlayLegacySystemAudioSessionRunner.ThrowIfBackgroundFailed(
                Task.FromException(failure)));

        Assert.Same(failure, exception);
    }

    [Fact]
    public void RuntimeEvidenceFingerprintsSetMediaInfoWithoutRawPayload()
    {
        var session = new MiPlayLegacyPostOpenPlaybackSession(
            MiPlaySetMediaInfoPayloadCodec.CreateWindowsSystemAudio(
                600_000,
                MiPlayLegacySystemAudioSessionRunner.ValidatedSourceName));
        var frame = Assert.Single(Assert.Single(session.Start().OutboundWrites).Frames);

        var evidence = MiPlayRuntimeWireEvidence.DescribeSetMediaInfo(frame);

        Assert.Equal(
            "setMediaInfo command=0x0012, sequence=0x000F, payloadLength=179, " +
            "payloadSha256=83A6859C90535005160C904B8D23126ACB6C586A652429615D166E61A052BB0E, " +
            "frameSha256=3A00F9CEEED944A877B7E71B089F932AA1139A0378410A4A27C75AB8E8264498, " +
            "durationMs=600000, status=0, deviceState=2, sourceName=MI PAD 4/Plus",
            evidence);
        Assert.DoesNotContain("System Audio", evidence);
    }

    [Fact]
    public void RuntimeEvidenceRecordsFirstMediaAndReceiverReadinessState()
    {
        var mediaEvidence = MiPlayRuntimeWireEvidence.DescribeFirstMediaBatch(
            [0x01, 0x02, 0x03],
            [0x10, 0x20],
            1);
        Assert.Equal(
            "firstMedia accessUnitLength=3, " +
            "accessUnitSha256=039058C6F2C0CB492C533B0A4D14EF77CC0F78ABCCCED5287D84A1A2011CFB81, " +
            "rtpFrameCount=1, wireLength=2, " +
            "wireSha256=3C274A8322731C85C4D7F7D35A8B13CBAB3A57A14170CCE898055F9744E66124",
            mediaEvidence);

        var session = new MiPlayLegacyPostOpenPlaybackSession(
            MiPlaySetMediaInfoPayloadCodec.CreateWindowsSystemAudio(20_011, "Windows"));
        session.Start();
        var inbound = MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.NotifyCommand,
            0x1234,
            [14, .. "first-audiopcm"u8.ToArray(), MiPlayNotifyPayloadCodec.ByteValueType, 1]);
        var transition = session.ProcessInbound(inbound);

        Assert.Equal(
            "postOpenInbound command=0x0022, sequence=0x1234, payloadLength=17, " +
            "payloadSha256=8D39D2609B9F81AD339AE8862ADD144CE362B8AE2CCAACE2A015FB1DDEFF719C, " +
            "label=first-audiopcm, integerValue=1, firstAudioPcm=1, receiverState=unset, " +
            "unsupportedNotifications=0",
            MiPlayRuntimeWireEvidence.DescribePostOpenInbound(inbound, transition, session));
    }

    [Fact]
    public async Task ReceiverReadinessTimeoutStartsOnlyAfterFirstMediaWrite()
    {
        var firstMediaSent = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var readerEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingRead = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var wait = MiPlayLegacySystemAudioSessionRunner.WaitForReceiverReadinessWindowAsync(
            firstMediaSent.Task,
            token =>
            {
                readerEntered.TrySetResult();
                return pendingRead.Task.WaitAsync(token);
            },
            TimeSpan.FromMilliseconds(30),
            CancellationToken.None);

        await Task.Delay(60);
        Assert.False(wait.IsCompleted);
        Assert.False(readerEntered.Task.IsCompleted);

        firstMediaSent.TrySetResult();
        await readerEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var exception = await Assert.ThrowsAsync<TimeoutException>(() => wait);
        Assert.Contains("first media batch", exception.Message);
    }

}
