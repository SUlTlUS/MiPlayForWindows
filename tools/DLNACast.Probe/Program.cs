using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DLNACast.Core.Audio;
using DLNACast.Core.Dlna;
using DLNACast.Core.MiPlay;
using DLNACast.Core.Platform;
using NAudio.Wave;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var scanStraceArgument = args.FirstOrDefault(argument =>
    argument.StartsWith("--miplay-scan-strace=", StringComparison.OrdinalIgnoreCase));
if (scanStraceArgument is not null)
{
    PrintMiPlayStraceSummary(scanStraceArgument[(scanStraceArgument.IndexOf('=') + 1)..]);
    return;
}

var scanPcapArgument = args.FirstOrDefault(argument =>
    argument.StartsWith("--miplay-scan-pcap=", StringComparison.OrdinalIgnoreCase));
if (scanPcapArgument is not null)
{
    PrintMiPlayPcapSummary(scanPcapArgument[(scanPcapArgument.IndexOf('=') + 1)..]);
    return;
}

if (args.Contains("--miplay-legacy-audio-source-bootstrap-dry-run", StringComparer.OrdinalIgnoreCase))
{
    PrintLegacyAudioSourceBootstrapDryRun(ParseLegacyStatusQueryOrder(args));
    return;
}

if (args.Contains("--miplay-legacy-silence-playback-dry-run", StringComparer.OrdinalIgnoreCase))
{
    PrintLegacySilencePlaybackDryRun(ParseLegacyStatusQueryOrder(args));
    return;
}

if (args.Contains("--miplay-legacy-tone-playback-dry-run", StringComparer.OrdinalIgnoreCase))
{
    PrintLegacyTonePlaybackDryRun(ParseLegacyStatusQueryOrder(args));
    return;
}

if (args.Contains("--miplay-legacy-system-audio-playback-dry-run", StringComparer.OrdinalIgnoreCase))
{
    PrintLegacySystemAudioPlaybackDryRun(ParseLegacyStatusQueryOrder(args));
    return;
}

var aacEncoderSmokeArgument = args.FirstOrDefault(argument =>
    argument.StartsWith("--miplay-aac-encoder-smoke=", StringComparison.OrdinalIgnoreCase));
if (aacEncoderSmokeArgument is not null)
{
    var ffmpegPath = aacEncoderSmokeArgument[(aacEncoderSmokeArgument.IndexOf('=') + 1)..];
    using var encoderTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    await RunMiPlayAacEncoderSmokeAsync(ffmpegPath, encoderTimeout.Token);
    return;
}

var systemLoopbackAacSmokeArgument = args.FirstOrDefault(argument =>
    argument.StartsWith("--miplay-system-loopback-aac-smoke=", StringComparison.OrdinalIgnoreCase));
if (systemLoopbackAacSmokeArgument is not null)
{
    var ffmpegPath = systemLoopbackAacSmokeArgument[
        (systemLoopbackAacSmokeArgument.IndexOf('=') + 1)..];
    using var smokeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    await RunMiPlaySystemLoopbackAacSmokeAsync(ffmpegPath, smokeTimeout.Token);
    return;
}

var systemLoopbackAacAnalysisArgument = args.FirstOrDefault(argument =>
    argument.StartsWith("--miplay-system-loopback-aac-analysis=", StringComparison.OrdinalIgnoreCase));
if (systemLoopbackAacAnalysisArgument is not null)
{
    var ffmpegPath = systemLoopbackAacAnalysisArgument[
        (systemLoopbackAacAnalysisArgument.IndexOf('=') + 1)..];
    using var analysisTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    await RunMiPlaySystemLoopbackAacAnalysisAsync(ffmpegPath, analysisTimeout.Token);
    return;
}

var legacyTonePlaybackArgument = args.FirstOrDefault(argument =>
    argument.StartsWith("--miplay-legacy-tone-playback=", StringComparison.OrdinalIgnoreCase));
if (legacyTonePlaybackArgument is not null)
{
    var explicitlyAuthorized = args.Contains(
        "--miplay-confirm-legacy-tone-playback",
        StringComparer.OrdinalIgnoreCase);
    if (!explicitlyAuthorized)
    {
        throw new ArgumentException(
            "--miplay-legacy-tone-playback requires --miplay-confirm-legacy-tone-playback. " +
            "It performs one legacy source session and sends at most 96 generated low-amplitude 440 Hz AAC frames.");
    }

    var addressText = legacyTonePlaybackArgument[
        (legacyTonePlaybackArgument.IndexOf('=') + 1)..];
    if (!IPAddress.TryParse(addressText, out var targetAddress) ||
        targetAddress.AddressFamily != AddressFamily.InterNetwork)
    {
        throw new ArgumentException(
            "Use --miplay-legacy-tone-playback=<one speaker IPv4 address>.");
    }

    var ffmpegArgument = args.FirstOrDefault(argument =>
        argument.StartsWith("--miplay-ffmpeg=", StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException(
            "The tone validation requires --miplay-ffmpeg=<absolute ffmpeg.exe path>.");
    var ffmpegPath = ffmpegArgument[(ffmpegArgument.IndexOf('=') + 1)..];

    using var playbackTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    await RunLegacyPlaybackAsync(
        targetAddress,
        explicitlyAuthorized,
        ParseLegacyStatusQueryOrder(args),
        MiPlayLegacyProbeMediaMode.Tone,
        injectLocalTestTone: false,
        systemAudioDurationSeconds: 0,
        ffmpegPath,
        playbackTimeout.Token);
    return;
}

var legacySystemAudioPlaybackArgument = args.FirstOrDefault(argument =>
    argument.StartsWith("--miplay-legacy-system-audio-playback=", StringComparison.OrdinalIgnoreCase));
if (legacySystemAudioPlaybackArgument is not null)
{
    var explicitlyAuthorized = args.Contains(
        "--miplay-confirm-legacy-system-audio-playback",
        StringComparer.OrdinalIgnoreCase);
    if (!explicitlyAuthorized)
    {
        throw new ArgumentException(
            "--miplay-legacy-system-audio-playback requires --miplay-confirm-legacy-system-audio-playback. " +
            "It performs one legacy source session and sends at most 240 AAC access units from the default Windows output loopback.");
    }

    var addressText = legacySystemAudioPlaybackArgument[
        (legacySystemAudioPlaybackArgument.IndexOf('=') + 1)..];
    if (!IPAddress.TryParse(addressText, out var targetAddress) ||
        targetAddress.AddressFamily != AddressFamily.InterNetwork)
    {
        throw new ArgumentException(
            "Use --miplay-legacy-system-audio-playback=<one speaker IPv4 address>.");
    }

    var ffmpegArgument = args.FirstOrDefault(argument =>
        argument.StartsWith("--miplay-ffmpeg=", StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException(
            "The system-audio validation requires --miplay-ffmpeg=<absolute ffmpeg.exe path>.");
    var ffmpegPath = ffmpegArgument[(ffmpegArgument.IndexOf('=') + 1)..];

    using var playbackTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(50));
    await RunLegacyPlaybackAsync(
        targetAddress,
        explicitlyAuthorized,
        ParseLegacyStatusQueryOrder(args),
        MiPlayLegacyProbeMediaMode.SystemLoopback,
        injectLocalTestTone: args.Contains(
            "--miplay-inject-local-test-tone",
            StringComparer.OrdinalIgnoreCase),
        systemAudioDurationSeconds: ParseSystemAudioDurationSeconds(args),
        ffmpegPath,
        playbackTimeout.Token);
    return;
}

var legacySilencePlaybackArgument = args.FirstOrDefault(argument =>
    argument.StartsWith("--miplay-legacy-silence-playback=", StringComparison.OrdinalIgnoreCase));
if (legacySilencePlaybackArgument is not null)
{
    var explicitlyAuthorized = args.Contains(
        "--miplay-confirm-legacy-silence-playback",
        StringComparer.OrdinalIgnoreCase);
    if (!explicitlyAuthorized)
    {
        throw new ArgumentException(
            "--miplay-legacy-silence-playback requires --miplay-confirm-legacy-silence-playback. " +
            "It performs the captured legacy bootstrap and playback continuation, reverse RTSP/timer replies, " +
            "and at most 48 generated silent AAC frames. It never sends AddMirror or user audio.");
    }

    var addressText = legacySilencePlaybackArgument[
        (legacySilencePlaybackArgument.IndexOf('=') + 1)..];
    if (!IPAddress.TryParse(addressText, out var targetAddress) ||
        targetAddress.AddressFamily != AddressFamily.InterNetwork)
    {
        throw new ArgumentException(
            "Use --miplay-legacy-silence-playback=<one speaker IPv4 address>, for example " +
            "--miplay-legacy-silence-playback=192.168.10.4.");
    }

    using var playbackTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    await RunLegacyPlaybackAsync(
        targetAddress,
        explicitlyAuthorized,
        ParseLegacyStatusQueryOrder(args),
        MiPlayLegacyProbeMediaMode.Silence,
        injectLocalTestTone: false,
        systemAudioDurationSeconds: 0,
        ffmpegPath: null,
        playbackTimeout.Token);
    return;
}

var legacyAudioSourceBootstrapArgument = args.FirstOrDefault(argument =>
    argument.StartsWith("--miplay-legacy-audio-source-bootstrap=", StringComparison.OrdinalIgnoreCase));
if (legacyAudioSourceBootstrapArgument is not null)
{
    var explicitlyAuthorized = args.Contains(
        "--miplay-confirm-legacy-audio-source-bootstrap",
        StringComparer.OrdinalIgnoreCase);
    if (!explicitlyAuthorized)
    {
        throw new ArgumentException(
            "--miplay-legacy-audio-source-bootstrap requires --miplay-confirm-legacy-audio-source-bootstrap. " +
            "It may send only the recovered legacy-clear identity/status bootstrap and always stops before " +
            "0x0040, Open, AddMirror, RTSP, media, playback, or audio.");
    }

    var addressText = legacyAudioSourceBootstrapArgument[
        (legacyAudioSourceBootstrapArgument.IndexOf('=') + 1)..];
    if (!IPAddress.TryParse(addressText, out var targetAddress) ||
        targetAddress.AddressFamily != AddressFamily.InterNetwork)
    {
        throw new ArgumentException(
            "Use --miplay-legacy-audio-source-bootstrap=<one speaker IPv4 address>, for example " +
            "--miplay-legacy-audio-source-bootstrap=192.168.10.4.");
    }

    var statusQueryOrder = ParseLegacyStatusQueryOrder(args);
    using var bootstrapTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    await RunLegacyAudioSourceBootstrapAsync(
        targetAddress,
        explicitlyAuthorized,
        statusQueryOrder,
        bootstrapTimeout.Token);
    return;
}

if (args.Contains("--miplay-official-post-auth-sequence-dry-run", StringComparer.OrdinalIgnoreCase))
{
    PrintOfficialPostAuthSequenceDryRun();
    return;
}

if (args.Contains("--miplay-fresh-legacy-device-info-dry-run", StringComparer.OrdinalIgnoreCase))
{
    PrintFreshLegacyDeviceInfoDryRun();
    return;
}

if (args.Contains("--miplay-fresh-legacy-post-device-info-observation-dry-run", StringComparer.OrdinalIgnoreCase))
{
    PrintFreshLegacyPostDeviceInfoObservationDryRun();
    return;
}

var freshLegacyDeviceInfoReceiverArgument = args.FirstOrDefault(argument =>
    argument.StartsWith("--miplay-fresh-legacy-device-info-receiver=", StringComparison.OrdinalIgnoreCase));
var observeFreshLegacyPostDeviceInfoGetMirrorMode = args.Contains(
    "--miplay-observe-post-device-info-get-mirror-mode",
    StringComparer.OrdinalIgnoreCase);
if (observeFreshLegacyPostDeviceInfoGetMirrorMode && freshLegacyDeviceInfoReceiverArgument is null)
{
    throw new ArgumentException(
        "--miplay-observe-post-device-info-get-mirror-mode is valid only with " +
        "--miplay-fresh-legacy-device-info-receiver and its explicit confirmation option.");
}

if (freshLegacyDeviceInfoReceiverArgument is not null)
{
    if (!args.Contains("--miplay-confirm-fresh-legacy-device-info-receiver", StringComparer.OrdinalIgnoreCase))
    {
        throw new ArgumentException(
            "--miplay-fresh-legacy-device-info-receiver requires " +
            "--miplay-confirm-fresh-legacy-device-info-receiver. This mode advertises a distinct test receiver, " +
            "sends one legacy 0x0028 and, only after verified 0x0029 plus one empty clear 0x001e, " +
            "one same-sequence clear 0x001f. It sends no 0x0037, 0x0059, 0x0035, 0x001b, Open, AddMirror, RTSP, media, playback, or audio frame.");
    }

    var addressText = freshLegacyDeviceInfoReceiverArgument[
        (freshLegacyDeviceInfoReceiverArgument.IndexOf('=') + 1)..];
    if (!IPAddress.TryParse(addressText, out var localAddress) ||
        localAddress.AddressFamily != AddressFamily.InterNetwork)
    {
        throw new ArgumentException(
            "Use --miplay-fresh-legacy-device-info-receiver=<local LAN IPv4 address>, " +
            "for example --miplay-fresh-legacy-device-info-receiver=192.168.10.9.");
    }

    var captureSecondsText = GetOptionValue(args, "--miplay-capture-seconds=");
    var captureSeconds = 120;
    if (captureSecondsText is not null &&
        (!int.TryParse(captureSecondsText, out captureSeconds) ||
         captureSeconds is < 10 or > 600))
    {
        throw new ArgumentException("--miplay-capture-seconds must be an integer from 10 to 600 for this bounded receiver mode.");
    }

    var profile = MiPlayPassiveSenderCaptureProfile.CreateDefault(localAddress);
    using var captureTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(captureSeconds));
    void cancelHandler(object? _, ConsoleCancelEventArgs eventArgs)
    {
        eventArgs.Cancel = true;
        captureTimeout.Cancel();
    }
    Console.CancelKeyPress += cancelHandler;
    try
    {
        await RunFreshLegacyDeviceInfoReceiverAsync(
            profile,
            TimeSpan.FromSeconds(captureSeconds),
            explicitUserAuthorization: true,
            observeFreshLegacyPostDeviceInfoGetMirrorMode,
            captureTimeout.Token);
    }
    finally
    {
        Console.CancelKeyPress -= cancelHandler;
    }

    return;
}

var mutualAuthSenderCaptureArgument = args.FirstOrDefault(argument =>
    argument.StartsWith("--miplay-mutual-auth-sender-capture=", StringComparison.OrdinalIgnoreCase));
if (mutualAuthSenderCaptureArgument is not null)
{
    if (!args.Contains("--miplay-confirm-mutual-auth-sender-capture", StringComparer.OrdinalIgnoreCase))
    {
        throw new ArgumentException(
            "--miplay-mutual-auth-sender-capture requires --miplay-confirm-mutual-auth-sender-capture. " +
            "This mode advertises a distinct test receiver, sends only legacy 0x0028 plus SafetyInfo/SafetyAuth " +
            "0x1401/0x1402/0x1403, decrypts one phone-originated post-auth frame, and sends no business acknowledgement.");
    }

    var addressText = mutualAuthSenderCaptureArgument[(mutualAuthSenderCaptureArgument.IndexOf('=') + 1)..];
    if (!IPAddress.TryParse(addressText, out var localAddress) ||
        localAddress.AddressFamily != AddressFamily.InterNetwork)
    {
        throw new ArgumentException(
            "Use --miplay-mutual-auth-sender-capture=<local LAN IPv4 address>, " +
            "for example --miplay-mutual-auth-sender-capture=192.168.10.9.");
    }

    var captureSecondsText = GetOptionValue(args, "--miplay-capture-seconds=");
    var captureSeconds = 180;
    if (captureSecondsText is not null &&
        (!int.TryParse(captureSecondsText, out captureSeconds) ||
         captureSeconds is < 10 or > 1800))
    {
        throw new ArgumentException("--miplay-capture-seconds must be an integer from 10 to 1800.");
    }

    var profile = MiPlayPassiveSenderCaptureProfile.CreateDefault(localAddress);
    using var captureTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(captureSeconds));
    void cancelHandler(object? _, ConsoleCancelEventArgs eventArgs)
    {
        eventArgs.Cancel = true;
        captureTimeout.Cancel();
    }
    Console.CancelKeyPress += cancelHandler;
    try
    {
        await RunMutualAuthMiPlaySenderCaptureAsync(
            profile,
            TimeSpan.FromSeconds(captureSeconds),
            captureTimeout.Token);
    }
    finally
    {
        Console.CancelKeyPress -= cancelHandler;
    }

    return;
}

var passiveSenderCaptureArgument = args.FirstOrDefault(argument =>
    argument.StartsWith("--miplay-passive-sender-capture=", StringComparison.OrdinalIgnoreCase));
if (passiveSenderCaptureArgument is not null)
{
    if (!args.Contains("--miplay-confirm-passive-sender-capture", StringComparer.OrdinalIgnoreCase))
    {
        throw new ArgumentException(
            "--miplay-passive-sender-capture requires --miplay-confirm-passive-sender-capture. " +
            "This mode advertises a distinct test receiver, sends one verified pre-auth 0x0028 challenge, " +
            "and only records frames voluntarily sent by the selected phone.");
    }

    var addressText = passiveSenderCaptureArgument[(passiveSenderCaptureArgument.IndexOf('=') + 1)..];
    if (!IPAddress.TryParse(addressText, out var localAddress) ||
        localAddress.AddressFamily != AddressFamily.InterNetwork)
    {
        throw new ArgumentException(
            "Use --miplay-passive-sender-capture=<local LAN IPv4 address>, " +
            "for example --miplay-passive-sender-capture=192.168.10.9.");
    }

    var captureSecondsText = GetOptionValue(args, "--miplay-capture-seconds=");
    var captureSeconds = 180;
    if (captureSecondsText is not null &&
        (!int.TryParse(captureSecondsText, out captureSeconds) ||
         captureSeconds is < 10 or > 1800))
    {
        throw new ArgumentException("--miplay-capture-seconds must be an integer from 10 to 1800.");
    }

    var profile = MiPlayPassiveSenderCaptureProfile.CreateDefault(localAddress);
    using var captureTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(captureSeconds));
    void cancelHandler(object? _, ConsoleCancelEventArgs eventArgs)
    {
        eventArgs.Cancel = true;
        captureTimeout.Cancel();
    }
    Console.CancelKeyPress += cancelHandler;
    try
    {
        await RunPassiveMiPlaySenderCaptureAsync(
            profile,
            TimeSpan.FromSeconds(captureSeconds),
            captureTimeout.Token);
    }
    finally
    {
        Console.CancelKeyPress -= cancelHandler;
    }

    return;
}

var mdnsArgument = args.FirstOrDefault(argument =>
    argument.StartsWith("--miplay-mdns=", StringComparison.OrdinalIgnoreCase));
if (mdnsArgument is not null)
{
    var addressText = mdnsArgument[(mdnsArgument.IndexOf('=') + 1)..];
    if (!IPAddress.TryParse(addressText, out var localAddress))
    {
        throw new ArgumentException("Use --miplay-mdns=<local IPv4 address>, for example --miplay-mdns=192.168.10.9.");
    }

    using var mdnsTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var mdnsDiscovery = new MiPlayMdnsDiscovery();
    var miConnectTask = mdnsDiscovery.SearchAsync(
        localAddress, MiPlayMdnsQuery.ServiceName, TimeSpan.FromSeconds(3), mdnsTimeout.Token);
    var lyraTask = mdnsDiscovery.SearchAsync(
        localAddress, MiPlayMdnsQuery.LyraServiceName, TimeSpan.FromSeconds(3), mdnsTimeout.Token);
    await Task.WhenAll(miConnectTask, lyraTask);

    PrintMdnsDevices(MiPlayMdnsQuery.ServiceName, await miConnectTask);
    PrintMdnsDevices(MiPlayMdnsQuery.LyraServiceName, await lyraTask);

    return;
}

var nativeLegacyClearGetDeviceInfoAfterReadyNotifyArgument = args.FirstOrDefault(argument =>
    argument.StartsWith("--miplay-native-legacy-clear-get-device-info-after-ready-notify-probe=", StringComparison.OrdinalIgnoreCase));
var nativeLegacyClearSetPlaySourceAfterReadyNotifyAckArgument = args.FirstOrDefault(argument =>
    argument.StartsWith("--miplay-native-legacy-clear-set-play-source-after-ready-notify-ack-probe=", StringComparison.OrdinalIgnoreCase));
var nativeLegacyClearSetPlaySourceAckArgument = args.FirstOrDefault(argument =>
    argument.StartsWith("--miplay-native-legacy-clear-set-play-source-ack-probe=", StringComparison.OrdinalIgnoreCase));
var nativeSafetyMutualAuthSetPlaySourceAckArgument = args.FirstOrDefault(argument =>
    argument.StartsWith("--miplay-native-safety-mutual-auth-set-play-source-ack-probe=", StringComparison.OrdinalIgnoreCase));
var nativeSafetyMutualAuthOfficialPostAuthSequenceArgument = args.FirstOrDefault(argument =>
    argument.StartsWith("--miplay-native-safety-mutual-auth-official-post-auth-sequence-probe=", StringComparison.OrdinalIgnoreCase));
var confirmOfficialPostAuthSequence = args.Contains(
    "--miplay-confirm-official-post-auth-sequence",
    StringComparer.OrdinalIgnoreCase);
var nativeSafetyMutualAuthSetPlaySourceOneFrameArgument = args.FirstOrDefault(argument =>
    argument.StartsWith("--miplay-native-safety-mutual-auth-official-json-set-play-source-one-frame-probe=", StringComparison.OrdinalIgnoreCase));
var confirmOfficialJsonSetPlaySourceOneFrame = args.Contains(
    "--miplay-confirm-official-json-set-play-source-one-frame",
    StringComparer.OrdinalIgnoreCase);
var nativeSafetyMutualAuthReadOnlyGetDeviceInfoArgument = args.FirstOrDefault(argument =>
    argument.StartsWith("--miplay-native-safety-mutual-auth-readonly-get-device-info-probe=", StringComparison.OrdinalIgnoreCase));
var confirmReadOnlyGetDeviceInfoOneFrame = args.Contains(
    "--miplay-confirm-readonly-get-device-info-one-frame",
    StringComparer.OrdinalIgnoreCase);
var nativeSafetyMutualAuthAddMirrorArgument = args.FirstOrDefault(argument =>
    argument.StartsWith("--miplay-native-safety-mutual-auth-add-mirror-probe=", StringComparison.OrdinalIgnoreCase));
var nativeSafetyMutualAuthOpenRtspStubArgument = args.FirstOrDefault(argument =>
    argument.StartsWith("--miplay-native-safety-mutual-auth-open-rtsp-stub-probe=", StringComparison.OrdinalIgnoreCase));
var nativeSafetyMutualAuthLocalDeviceInfoArgument = args.FirstOrDefault(argument =>
    argument.StartsWith("--miplay-native-safety-mutual-auth-local-device-info-probe=", StringComparison.OrdinalIgnoreCase));
var nativeSafetyMutualAuthDeviceInfoArgument = args.FirstOrDefault(argument =>
    argument.StartsWith("--miplay-native-safety-mutual-auth-device-info-probe=", StringComparison.OrdinalIgnoreCase));
var nativeSafetyMutualAuthHeartbeatArgument = args.FirstOrDefault(argument =>
    argument.StartsWith("--miplay-native-safety-mutual-auth-heartbeat-probe=", StringComparison.OrdinalIgnoreCase));
var nativeSafetyMutualAuthObserveArgument = args.FirstOrDefault(argument =>
    argument.StartsWith("--miplay-native-safety-mutual-auth-observe-probe=", StringComparison.OrdinalIgnoreCase));
var nativeSafetyMutualAuthArgument = args.FirstOrDefault(argument =>
    argument.StartsWith("--miplay-native-safety-mutual-auth-probe=", StringComparison.OrdinalIgnoreCase));
var nativeSafetyAuthArgument = args.FirstOrDefault(argument =>
    argument.StartsWith("--miplay-native-safety-auth-probe=", StringComparison.OrdinalIgnoreCase));
var nativeSafetyDecryptArgument = args.FirstOrDefault(argument =>
    argument.StartsWith("--miplay-native-safety-decrypt-probe=", StringComparison.OrdinalIgnoreCase));
var nativeSafetyArgument = args.FirstOrDefault(argument =>
    argument.StartsWith("--miplay-native-safety-probe=", StringComparison.OrdinalIgnoreCase));
var safetyOfferArgument = args.FirstOrDefault(argument =>
    argument.StartsWith("--miplay-safety-offer=", StringComparison.OrdinalIgnoreCase));
var emitPostAuthOutboundProfileDryRun = args.Contains(
    "--miplay-post-auth-outbound-profile-dry-run",
    StringComparer.OrdinalIgnoreCase);
var postAuthObserveSecondsText = GetOptionValue(args, "--miplay-post-auth-observe-seconds=");
var postAuthObserveSeconds = 5;
if (postAuthObserveSecondsText is not null &&
    (!int.TryParse(postAuthObserveSecondsText, out postAuthObserveSeconds) ||
     postAuthObserveSeconds is < 1 or > 60))
{
    throw new ArgumentException("--miplay-post-auth-observe-seconds must be an integer from 1 to 60.");
}
var postAuthObserveTimeout = TimeSpan.FromSeconds(postAuthObserveSeconds);
var postAuthSendDelayMsText = GetOptionValue(args, "--miplay-post-auth-send-delay-ms=");
var postAuthSendDelayMs = 0;
if (postAuthSendDelayMsText is not null &&
    (!int.TryParse(postAuthSendDelayMsText, out postAuthSendDelayMs) ||
     postAuthSendDelayMs is < 0 or > 5000))
{
    throw new ArgumentException("--miplay-post-auth-send-delay-ms must be an integer from 0 to 5000.");
}
var postAuthSendDelay = TimeSpan.FromMilliseconds(postAuthSendDelayMs);
var safetyProbeArgument = nativeLegacyClearGetDeviceInfoAfterReadyNotifyArgument ?? nativeLegacyClearSetPlaySourceAfterReadyNotifyAckArgument ?? nativeLegacyClearSetPlaySourceAckArgument ?? nativeSafetyMutualAuthOfficialPostAuthSequenceArgument ?? nativeSafetyMutualAuthSetPlaySourceOneFrameArgument ?? nativeSafetyMutualAuthReadOnlyGetDeviceInfoArgument ?? nativeSafetyMutualAuthSetPlaySourceAckArgument ?? nativeSafetyMutualAuthAddMirrorArgument ?? nativeSafetyMutualAuthOpenRtspStubArgument ?? nativeSafetyMutualAuthLocalDeviceInfoArgument ?? nativeSafetyMutualAuthDeviceInfoArgument ?? nativeSafetyMutualAuthHeartbeatArgument ?? nativeSafetyMutualAuthObserveArgument ?? nativeSafetyMutualAuthArgument ?? nativeSafetyAuthArgument ?? nativeSafetyDecryptArgument ?? nativeSafetyArgument ?? safetyOfferArgument ?? args.FirstOrDefault(argument =>
    argument.StartsWith("--miplay-safety-probe=", StringComparison.OrdinalIgnoreCase));
if (emitPostAuthOutboundProfileDryRun && safetyProbeArgument is null)
{
    throw new ArgumentException("--miplay-post-auth-outbound-profile-dry-run requires a MiPlay mutual-auth probe target and never sends a post-auth business frame by itself.");
}

if (safetyProbeArgument is not null)
{
    var addressText = safetyProbeArgument[(safetyProbeArgument.IndexOf('=') + 1)..];
    if (!IPAddress.TryParse(addressText, out var deviceAddress) || deviceAddress.AddressFamily != AddressFamily.InterNetwork)
    {
        throw new ArgumentException("Use --miplay-safety-probe=<device IPv4 address>, --miplay-safety-offer=<device IPv4 address>, --miplay-native-safety-probe=<device IPv4 address>, --miplay-native-safety-decrypt-probe=<device IPv4 address>, --miplay-native-safety-auth-probe=<device IPv4 address>, --miplay-native-safety-mutual-auth-probe=<device IPv4 address>, --miplay-native-legacy-clear-get-device-info-after-ready-notify-probe=<device IPv4 address>, --miplay-native-legacy-clear-set-play-source-ack-probe=<device IPv4 address>, --miplay-native-legacy-clear-set-play-source-after-ready-notify-ack-probe=<device IPv4 address>, --miplay-native-safety-mutual-auth-observe-probe=<device IPv4 address>, --miplay-native-safety-mutual-auth-heartbeat-probe=<device IPv4 address>, --miplay-native-safety-mutual-auth-device-info-probe=<device IPv4 address>, --miplay-native-safety-mutual-auth-readonly-get-device-info-probe=<device IPv4 address>, --miplay-native-safety-mutual-auth-local-device-info-probe=<device IPv4 address>, --miplay-native-safety-mutual-auth-open-rtsp-stub-probe=<device IPv4 address>, --miplay-native-safety-mutual-auth-add-mirror-probe=<device IPv4 address>, --miplay-native-safety-mutual-auth-set-play-source-ack-probe=<device IPv4 address>, --miplay-native-safety-mutual-auth-official-post-auth-sequence-probe=<device IPv4 address> plus --miplay-confirm-official-post-auth-sequence for the authorized recovered official sequence, --miplay-native-safety-mutual-auth-official-json-set-play-source-one-frame-probe=<device IPv4 address> plus --miplay-confirm-official-json-set-play-source-one-frame for the single authorized JSON 0x0040 frame, or --miplay-native-safety-mutual-auth-readonly-get-device-info-probe=<device IPv4 address> plus --miplay-confirm-readonly-get-device-info-one-frame for the single authorized read-only 0x001e frame; for example --miplay-safety-probe=192.168.10.4.");
    }

    var postAuthProbeOptionCount = new[]
    {
        nativeLegacyClearGetDeviceInfoAfterReadyNotifyArgument,
        nativeLegacyClearSetPlaySourceAfterReadyNotifyAckArgument,
        nativeLegacyClearSetPlaySourceAckArgument,
        nativeSafetyMutualAuthSetPlaySourceAckArgument,
        nativeSafetyMutualAuthOfficialPostAuthSequenceArgument,
        nativeSafetyMutualAuthSetPlaySourceOneFrameArgument,
        nativeSafetyMutualAuthReadOnlyGetDeviceInfoArgument,
        nativeSafetyMutualAuthAddMirrorArgument,
        nativeSafetyMutualAuthOpenRtspStubArgument,
        nativeSafetyMutualAuthLocalDeviceInfoArgument,
        nativeSafetyMutualAuthDeviceInfoArgument,
        nativeSafetyMutualAuthHeartbeatArgument,
        nativeSafetyMutualAuthObserveArgument,
    }.Count(argument => argument is not null);
    if (postAuthProbeOptionCount > 1)
    {
        throw new ArgumentException("Use only one post-auth MiPlay probe option at a time.");
    }

    var dryRunBusinessProbeOptionCount = new[]
    {
        nativeLegacyClearGetDeviceInfoAfterReadyNotifyArgument,
        nativeLegacyClearSetPlaySourceAfterReadyNotifyAckArgument,
        nativeLegacyClearSetPlaySourceAckArgument,
        nativeSafetyMutualAuthSetPlaySourceAckArgument,
        nativeSafetyMutualAuthOfficialPostAuthSequenceArgument,
        nativeSafetyMutualAuthSetPlaySourceOneFrameArgument,
        nativeSafetyMutualAuthReadOnlyGetDeviceInfoArgument,
        nativeSafetyMutualAuthAddMirrorArgument,
        nativeSafetyMutualAuthOpenRtspStubArgument,
        nativeSafetyMutualAuthLocalDeviceInfoArgument,
        nativeSafetyMutualAuthDeviceInfoArgument,
        nativeSafetyMutualAuthHeartbeatArgument,
    }.Count(argument => argument is not null);
    if (emitPostAuthOutboundProfileDryRun && dryRunBusinessProbeOptionCount > 0)
    {
        throw new ArgumentException("--miplay-post-auth-outbound-profile-dry-run is allowed only with mutual-auth or observe-only modes; it refuses heartbeat, getDeviceInfo, 0x0040, 0x0058, AddMirror, Cmd_Open, RTSP, media, playback, and audio probes.");
    }

    if (emitPostAuthOutboundProfileDryRun && nativeSafetyMutualAuthArgument is null && nativeSafetyMutualAuthObserveArgument is null)
    {
        throw new ArgumentException("--miplay-post-auth-outbound-profile-dry-run requires --miplay-native-safety-mutual-auth-probe or --miplay-native-safety-mutual-auth-observe-probe.");
    }

    var postAuthOpenRtspPort = ParsePortOption(args, "--miplay-open-rtsp-port=", MiPlayProtocolConstants.DefaultMediaPort);
    var postAuthOpenMirrorMode = ParseNonNegativeIntOption(args, "--miplay-open-mirror-mode=", 1);

    (byte[] SourceNamePayload, byte[] LocalDeviceInfoPayload, string SourceNameJson, string LocalDeviceInfoJson)? postAuthLocalDeviceInfoPayloads = nativeSafetyMutualAuthLocalDeviceInfoArgument is null
        ? null
        : CreatePostAuthLocalDeviceInfoPayloads(args);

    await ProbeMiPlayLegacySafetyAsync(
        deviceAddress,
        sendSafetyInfoOffer: safetyOfferArgument is not null || nativeSafetyArgument is not null || nativeSafetyDecryptArgument is not null || nativeSafetyAuthArgument is not null || nativeSafetyMutualAuthArgument is not null || nativeSafetyMutualAuthObserveArgument is not null || nativeSafetyMutualAuthHeartbeatArgument is not null || nativeSafetyMutualAuthReadOnlyGetDeviceInfoArgument is not null || nativeSafetyMutualAuthDeviceInfoArgument is not null || nativeSafetyMutualAuthLocalDeviceInfoArgument is not null || nativeSafetyMutualAuthOpenRtspStubArgument is not null || nativeSafetyMutualAuthOfficialPostAuthSequenceArgument is not null || nativeSafetyMutualAuthSetPlaySourceOneFrameArgument is not null || nativeSafetyMutualAuthSetPlaySourceAckArgument is not null || nativeSafetyMutualAuthAddMirrorArgument is not null,
        sendNativeBootstrap: nativeLegacyClearGetDeviceInfoAfterReadyNotifyArgument is not null || nativeLegacyClearSetPlaySourceAfterReadyNotifyAckArgument is not null || nativeLegacyClearSetPlaySourceAckArgument is not null || nativeSafetyArgument is not null || nativeSafetyDecryptArgument is not null || nativeSafetyAuthArgument is not null || nativeSafetyMutualAuthArgument is not null || nativeSafetyMutualAuthObserveArgument is not null || nativeSafetyMutualAuthHeartbeatArgument is not null || nativeSafetyMutualAuthReadOnlyGetDeviceInfoArgument is not null || nativeSafetyMutualAuthDeviceInfoArgument is not null || nativeSafetyMutualAuthLocalDeviceInfoArgument is not null || nativeSafetyMutualAuthOpenRtspStubArgument is not null || nativeSafetyMutualAuthOfficialPostAuthSequenceArgument is not null || nativeSafetyMutualAuthSetPlaySourceOneFrameArgument is not null || nativeSafetyMutualAuthSetPlaySourceAckArgument is not null || nativeSafetyMutualAuthAddMirrorArgument is not null,
        decryptSafetyAuth: nativeSafetyDecryptArgument is not null || nativeSafetyAuthArgument is not null || nativeSafetyMutualAuthArgument is not null || nativeSafetyMutualAuthObserveArgument is not null || nativeSafetyMutualAuthHeartbeatArgument is not null || nativeSafetyMutualAuthReadOnlyGetDeviceInfoArgument is not null || nativeSafetyMutualAuthDeviceInfoArgument is not null || nativeSafetyMutualAuthLocalDeviceInfoArgument is not null || nativeSafetyMutualAuthOpenRtspStubArgument is not null || nativeSafetyMutualAuthOfficialPostAuthSequenceArgument is not null || nativeSafetyMutualAuthSetPlaySourceOneFrameArgument is not null || nativeSafetyMutualAuthSetPlaySourceAckArgument is not null || nativeSafetyMutualAuthAddMirrorArgument is not null,
        sendSafetyAuthAcknowledgement: nativeSafetyAuthArgument is not null || nativeSafetyMutualAuthArgument is not null || nativeSafetyMutualAuthObserveArgument is not null || nativeSafetyMutualAuthHeartbeatArgument is not null || nativeSafetyMutualAuthReadOnlyGetDeviceInfoArgument is not null || nativeSafetyMutualAuthDeviceInfoArgument is not null || nativeSafetyMutualAuthLocalDeviceInfoArgument is not null || nativeSafetyMutualAuthOpenRtspStubArgument is not null || nativeSafetyMutualAuthOfficialPostAuthSequenceArgument is not null || nativeSafetyMutualAuthSetPlaySourceOneFrameArgument is not null || nativeSafetyMutualAuthSetPlaySourceAckArgument is not null || nativeSafetyMutualAuthAddMirrorArgument is not null,
        sendLocalSafetyAuthChallenge: nativeSafetyMutualAuthArgument is not null || nativeSafetyMutualAuthObserveArgument is not null || nativeSafetyMutualAuthHeartbeatArgument is not null || nativeSafetyMutualAuthReadOnlyGetDeviceInfoArgument is not null || nativeSafetyMutualAuthDeviceInfoArgument is not null || nativeSafetyMutualAuthLocalDeviceInfoArgument is not null || nativeSafetyMutualAuthOpenRtspStubArgument is not null || nativeSafetyMutualAuthOfficialPostAuthSequenceArgument is not null || nativeSafetyMutualAuthSetPlaySourceOneFrameArgument is not null || nativeSafetyMutualAuthSetPlaySourceAckArgument is not null || nativeSafetyMutualAuthAddMirrorArgument is not null,
        observeAfterMutualSafetyAuth: nativeSafetyMutualAuthObserveArgument is not null || nativeSafetyMutualAuthHeartbeatArgument is not null || nativeSafetyMutualAuthReadOnlyGetDeviceInfoArgument is not null || nativeSafetyMutualAuthDeviceInfoArgument is not null || nativeSafetyMutualAuthLocalDeviceInfoArgument is not null || nativeSafetyMutualAuthOpenRtspStubArgument is not null || nativeSafetyMutualAuthOfficialPostAuthSequenceArgument is not null || nativeSafetyMutualAuthSetPlaySourceOneFrameArgument is not null || nativeSafetyMutualAuthSetPlaySourceAckArgument is not null || nativeSafetyMutualAuthAddMirrorArgument is not null,
        sendPostAuthGetDeviceInfo: nativeSafetyMutualAuthDeviceInfoArgument is not null,
        sendPostAuthReadOnlyGetDeviceInfo: nativeSafetyMutualAuthReadOnlyGetDeviceInfoArgument is not null,
        sendPostAuthHeartbeat: nativeSafetyMutualAuthHeartbeatArgument is not null,
        sendPostAuthOpenRtspStub: nativeSafetyMutualAuthOpenRtspStubArgument is not null,
        sendPostAuthAddMirror: nativeSafetyMutualAuthAddMirrorArgument is not null,
        sendPostAuthSetPlaySourceAck: nativeSafetyMutualAuthSetPlaySourceAckArgument is not null,
        sendPostAuthOfficialSequence: nativeSafetyMutualAuthOfficialPostAuthSequenceArgument is not null,
        sendPostAuthSetPlaySourceOneFrame: nativeSafetyMutualAuthSetPlaySourceOneFrameArgument is not null,
        confirmOfficialPostAuthSequence: confirmOfficialPostAuthSequence,
        confirmOfficialJsonSetPlaySourceOneFrame: confirmOfficialJsonSetPlaySourceOneFrame,
        confirmReadOnlyGetDeviceInfoOneFrame: confirmReadOnlyGetDeviceInfoOneFrame,
        sendLegacyClearSetPlaySourceAck: nativeLegacyClearSetPlaySourceAckArgument is not null,
        sendLegacyClearSetPlaySourceAfterReadyNotifyAck: nativeLegacyClearSetPlaySourceAfterReadyNotifyAckArgument is not null,
        sendLegacyClearGetDeviceInfoAfterReadyNotify: nativeLegacyClearGetDeviceInfoAfterReadyNotifyArgument is not null,
        postAuthOpenRtspPort: postAuthOpenRtspPort,
        postAuthOpenMirrorMode: postAuthOpenMirrorMode,
        postAuthLocalDeviceInfoPayloads: postAuthLocalDeviceInfoPayloads,
        postAuthObserveTimeout: postAuthObserveTimeout,
        postAuthSendDelay: postAuthSendDelay,
        emitPostAuthOutboundProfileDryRun: emitPostAuthOutboundProfileDryRun);
    return;
}

static async Task ProbeMiPlayLegacySafetyAsync(
    IPAddress deviceAddress,
    bool sendSafetyInfoOffer,
    bool sendNativeBootstrap,
    bool decryptSafetyAuth,
    bool sendSafetyAuthAcknowledgement,
    bool sendLocalSafetyAuthChallenge,
    bool observeAfterMutualSafetyAuth,
    bool sendPostAuthGetDeviceInfo,
    bool sendPostAuthReadOnlyGetDeviceInfo,
    bool sendPostAuthHeartbeat,
    bool sendPostAuthOpenRtspStub,
    bool sendPostAuthAddMirror,
    bool sendPostAuthSetPlaySourceAck,
    bool sendPostAuthOfficialSequence,
    bool sendPostAuthSetPlaySourceOneFrame,
    bool confirmOfficialPostAuthSequence,
    bool confirmOfficialJsonSetPlaySourceOneFrame,
    bool confirmReadOnlyGetDeviceInfoOneFrame,
    bool sendLegacyClearSetPlaySourceAck,
    bool sendLegacyClearSetPlaySourceAfterReadyNotifyAck,
    bool sendLegacyClearGetDeviceInfoAfterReadyNotify,
    int postAuthOpenRtspPort,
    int postAuthOpenMirrorMode,
    (byte[] SourceNamePayload, byte[] LocalDeviceInfoPayload, string SourceNameJson, string LocalDeviceInfoJson)? postAuthLocalDeviceInfoPayloads,
    TimeSpan postAuthObserveTimeout,
    TimeSpan postAuthSendDelay,
    bool emitPostAuthOutboundProfileDryRun)
{
    using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    using var client = new TcpClient(AddressFamily.InterNetwork);
    await client.ConnectAsync(deviceAddress, MiPlayProtocolConstants.DefaultControlPort, connectTimeout.Token);
    await using var stream = client.GetStream();
    MiPlayTcpSessionInfo? connectedTcpSession = null;
    if (decryptSafetyAuth)
    {
        var localEndPoint = client.Client.LocalEndPoint as IPEndPoint
            ?? throw new InvalidOperationException("The connected TCP socket did not provide an IPv4 local endpoint.");
        var peerEndPoint = client.Client.RemoteEndPoint as IPEndPoint
            ?? throw new InvalidOperationException("The connected TCP socket did not provide an IPv4 peer endpoint.");
        connectedTcpSession = new MiPlayTcpSessionInfo(
            localEndPoint.Address,
            checked((ushort)localEndPoint.Port),
            peerEndPoint.Address,
            checked((ushort)peerEndPoint.Port));
        Console.WriteLine($"Captured connected IPv4 endpoints for the verified type-1 SafetyAuth derivation: local={localEndPoint.Address}:{localEndPoint.Port}, peer={peerEndPoint.Address}:{peerEndPoint.Port}; no key is printed.");
    }

    TcpListener? noMediaRtspListener = null;
    Task<string>? noMediaRtspFirstRequestTask = null;
    CancellationTokenSource? noMediaRtspCaptureTimeout = null;
    IPAddress? noMediaRtspSourceAddress = null;
    if (sendPostAuthOpenRtspStub)
    {
        if (connectedTcpSession is null)
        {
            throw new InvalidOperationException("The no-media Cmd_Open validation requires the verified native SafetyAuth endpoint derivation path.");
        }

        noMediaRtspSourceAddress = connectedTcpSession.LocalAddress;
        noMediaRtspListener = new TcpListener(noMediaRtspSourceAddress, postAuthOpenRtspPort);
        noMediaRtspListener.Start(1);
        var rtspCaptureSeconds = Math.Clamp((int)postAuthObserveTimeout.TotalSeconds + 10, 10, 90);
        noMediaRtspCaptureTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(rtspCaptureSeconds));
        noMediaRtspFirstRequestTask = CaptureFirstNoMediaRtspRequestAsync(
            noMediaRtspListener,
            noMediaRtspCaptureTimeout.Token);
        Console.WriteLine($"Started no-media RTSP/WFD first-request listener on {noMediaRtspSourceAddress}:{postAuthOpenRtspPort} before Cmd_Open. It will capture one RTSP request and send no RTSP response, media, RTP, playback, or audio data.");
    }

    using var sendTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    if (sendNativeBootstrap)
    {
        const ushort nativeVersionSequence = 1;
        var nativeVersionFrame = MiPlayNativeVersionCodec.EncodeSourceVersion(nativeVersionSequence);
        await stream.WriteAsync(nativeVersionFrame, sendTimeout.Token);
        await stream.FlushAsync(sendTimeout.Token);
        Console.WriteLine($"Sent verified native 0x{MiPlayProtocolConstants.NativeSourceVersionCommand:X4} version, sequence=0x{nativeVersionSequence:X4}, payload={MiPlayProtocolConstants.NativeSourceVersion18_0_0_3}.");
    }

    Console.WriteLine($"Connected to {deviceAddress}:{MiPlayProtocolConstants.DefaultControlPort}; waiting for one command frame.");

    using var initialFrameTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var initialFrameBytes = await ReadMiPlayFrameAsync(stream, initialFrameTimeout.Token);
    if (!MiPlayCommandFrameCodec.TryDecode(initialFrameBytes, out var initialFrame, out _) || initialFrame is null)
    {
        throw new InvalidDataException("The device did not send a complete legacy MiPlay command frame.");
    }

    Console.WriteLine($"Received command=0x{initialFrame.Command:X4}, sequence=0x{initialFrame.Sequence:X4}, payload={Convert.ToHexString(initialFrame.Payload)}.");
    if (!MiPlayLegacySafetyChallengeCodec.TryCreateAcknowledgement(
            initialFrameBytes,
            out var acknowledgement,
            out _) || acknowledgement is null)
    {
        Console.WriteLine("Refused to send: the first command is not the verified 0x0028 legacy challenge.");
        return;
    }

    var responseFrame = MiPlayLegacySafetyChallengeCodec.EncodeAcknowledgement(acknowledgement);
    await stream.WriteAsync(responseFrame, sendTimeout.Token);
    await stream.FlushAsync(sendTimeout.Token);
    Console.WriteLine($"Sent command=0x{MiPlayProtocolConstants.LegacySafetyAcknowledgementCommand:X4}, sequence=0x{acknowledgement.Sequence:X4}, payload={acknowledgement.Response}.");

    if (sendSafetyInfoOffer)
    {
        var nativeSafetyInfoSequence = sendNativeBootstrap ? (ushort)2 : (ushort)1;
        var offer = MiPlaySafetyInfoOffer.Native18_0_0_3;
        var safetyInfoFrame = MiPlaySafetyCommandCodec.Encode(
            MiPlayProtocolConstants.SafetyInfoCommand,
            nativeSafetyInfoSequence,
            offer.ToJsonPayload());
        await stream.WriteAsync(safetyInfoFrame, sendTimeout.Token);
        await stream.FlushAsync(sendTimeout.Token);
        Console.WriteLine($"Sent verified 0x1400 SafetyInfo offer, sequence=0x{nativeSafetyInfoSequence:X4}, payload={Encoding.UTF8.GetString(offer.ToJsonPayload())}.");
    }

    var observedFrames = 0;
    var sentSafetyAuthAcknowledgement = false;
    var sentLocalSafetyAuthChallenge = false;
    var verifiedPeerSafetyAuthAcknowledgement = false;
    var completedMutualSafetyAuth = false;
    ushort? sentPostAuthCommand = null;
    string? sentPostAuthBoundaryDescription = null;
    ushort? stagedPostAuthGetDeviceInfoSequence = null;
    bool awaitingPostAuthDeviceInfoAckBeforeLocalDeviceInfo = false;
    bool awaitingPostAuthReadOnlyGetDeviceInfoAck = false;
    ushort? postAuthReadOnlyGetDeviceInfoSequence = null;
    bool sentPostAuthLocalDeviceInfoFrames = false;
    IReadOnlyList<MiPlayOfficialPostAuthSequenceStep>? officialPostAuthSequenceSteps = null;
    MiPlaySafetyDataSessionCipher? officialPostAuthOutboundCipher = null;
    int officialPostAuthNextStepIndex = 0;
    bool awaitingOfficialPostAuthDeviceInfoAck = false;
    bool awaitingOfficialPostAuthMirrorModeAck = false;
    bool sentOfficialPostAuthSetPlaySource = false;
    bool observingLegacyClearSetPlaySourceAck = false;
    bool observingLegacyClearGetDeviceInfo = false;
    bool waitingForLegacyClearSetPlaySourceReadyNotify = sendLegacyClearSetPlaySourceAfterReadyNotifyAck;
    bool waitingForLegacyClearGetDeviceInfoReadyNotify = sendLegacyClearGetDeviceInfoAfterReadyNotify;
    MiPlaySafetyHashAlgorithm? safetyAuthAlgorithm = null;
    MiPlaySafetyAuthChallenge? localSafetyAuthChallenge = null;
    byte[]? localSafetyAuthPlaintextForPostAuthDryRun = null;
    byte[]? localSafetyAuthAcknowledgementPlaintextForPostAuthDryRun = null;
    bool printedPostAuthOutboundProfileDryRun = false;
    (string Label, string AuthKey, MiPlaySafetyDataSessionCipher Cipher)? selectedSafetyAuthCandidate = null;
    List<(string Label, string AuthKey, MiPlaySafetyDataSessionCipher Cipher)>? safetyAesCandidates = null;

    if (sendLegacyClearSetPlaySourceAck)
    {
        var legacyClearSequence = sendNativeBootstrap ? (ushort)2 : (ushort)1;
        var readiness = MiPlayLegacyClearSetPlaySourceAckProbePolicy.EvaluateAckReadiness(
            new MiPlayLegacyClearSetPlaySourceAckPrerequisites(
                LegacyChallengeAcknowledged: true,
                NativeVersionBootstrapSent: sendNativeBootstrap,
                MpasModernSafetyCommandConstantsAbsentObserved: true,
                MpasExternalSetPlaySourceDispatchObserved: true,
                MpasAcknowledgesBeforePayloadParse: true,
                NextCommandSequence: legacyClearSequence,
                EmptyPayloadOnly: true,
                NoModernSafetyInfoOrSafetyAuth: !sendSafetyInfoOffer && !decryptSafetyAuth && !sendSafetyAuthAcknowledgement && !sendLocalSafetyAuthChallenge,
                NoSafetyDataEncryption: true,
                NoMediaBoundary: true,
                ForbidCmdOpen: true,
                Forbid0058: true,
                ForbidAddMirror: true,
                ForbidRtsp: true,
                ForbidPlaybackOrAudio: true));
        if (!readiness.CanSend || readiness.Command is null || readiness.Sequence is null || readiness.PlaintextPayloadLength is null)
        {
            Console.WriteLine($"Refused legacy clear Cmd_SetPlaySource ACK validation: {readiness.Reason}");
            return;
        }

        var legacyClearFrame = MiPlaySetPlaySourceAckProbe.ToCommandFrame(legacyClearSequence);
        using var legacyClearSendTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await stream.WriteAsync(legacyClearFrame, legacyClearSendTimeout.Token);
        await stream.FlushAsync(legacyClearSendTimeout.Token);

        sentPostAuthCommand = MiPlayProtocolConstants.SetPlaySourceCommand;
        sentPostAuthBoundaryDescription = $"one legacy clear empty Cmd_SetPlaySource 0x{MiPlayProtocolConstants.SetPlaySourceCommand:X4} ACK-only probe";
        completedMutualSafetyAuth = true;
        observingLegacyClearSetPlaySourceAck = true;
        Console.WriteLine($"Legacy 0x{MiPlayProtocolConstants.LegacySafetyAcknowledgementCommand:X4} acknowledgement sent after verified 0x{MiPlayProtocolConstants.LegacySafetyChallengeCommand:X4}. Sent one clear-text Cmd_SetPlaySource ACK-only command=0x{MiPlayProtocolConstants.SetPlaySourceCommand:X4}, sequence=0x{legacyClearSequence:X4}, plaintextPayloadLength=0. Static LX06 1.88.51 mpas has no localized 0x1400..0x1403 constants and sends 0x{MiPlayProtocolConstants.SetPlaySourceAcknowledgementCommand:X4} before payload parsing, so the probe will only observe for a clear ACK; no 0x1400, 0x1402, 0x1403, SafetyData, JSON source identity, Cmd_Open, 0x0058, AddMirror, RTSP listener/response, media, RTP, audio, playback, retry, or other control data will be sent.");
    }

    if (waitingForLegacyClearSetPlaySourceReadyNotify)
    {
        Console.WriteLine($"Legacy clear Cmd_SetPlaySource ACK validation is armed but will wait for decoded notify label=state integerValue=3 before sending exactly one empty clear-text 0x{MiPlayProtocolConstants.SetPlaySourceCommand:X4}; no 0x1400, 0x1402, 0x1403, SafetyData, JSON source identity, Cmd_Open, 0x0058, AddMirror, RTSP, media, playback, or audio will be sent.");
    }

    if (waitingForLegacyClearGetDeviceInfoReadyNotify)
    {
        Console.WriteLine($"Legacy clear Cmd_GetDeviceInfo validation is armed but will wait for decoded notify label=state integerValue=3 before sending exactly one empty clear-text 0x{MiPlayProtocolConstants.GetDeviceInfoCommand:X4}; it will only observe for 0x{MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand:X4}; no 0x1400, 0x1402, 0x1403, SafetyData, Cmd_SetPlaySource, JSON source identity, Cmd_Open, 0x0058, AddMirror, RTSP, media, playback, or audio will be sent.");
    }

    async Task SendPostAuthLocalDeviceInfoFramesAsync(
        (byte[] SourceNamePayload, byte[] LocalDeviceInfoPayload, string SourceNameJson, string LocalDeviceInfoJson) localDeviceInfoPayloads,
        (string Label, string AuthKey, MiPlaySafetyDataSessionCipher Cipher) postAuthCandidate,
        ushort getDeviceInfoSequence)
    {
        if (sentPostAuthLocalDeviceInfoFrames)
        {
            Console.WriteLine("Refused duplicate post-auth local device info send: the staged 0x0058 frames were already sent.");
            return;
        }

        var sourceNameSequence = checked((ushort)(getDeviceInfoSequence + 1));
        var localDeviceInfoSequence = checked((ushort)(getDeviceInfoSequence + 2));
        var sourceNamePayload = postAuthCandidate.Cipher.EncryptVersion1(localDeviceInfoPayloads.SourceNamePayload);
        var localDeviceInfoPayload = postAuthCandidate.Cipher.EncryptVersion1(localDeviceInfoPayloads.LocalDeviceInfoPayload);
        var sourceNameFrame = MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.SetLocalDeviceInfoCommand,
            sourceNameSequence,
            sourceNamePayload);
        var localDeviceInfoFrame = MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.SetLocalDeviceInfoCommand,
            localDeviceInfoSequence,
            localDeviceInfoPayload);

        using var postAuthSendTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await stream.WriteAsync(sourceNameFrame, postAuthSendTimeout.Token);
        await stream.FlushAsync(postAuthSendTimeout.Token);
        await stream.WriteAsync(localDeviceInfoFrame, postAuthSendTimeout.Token);
        await stream.FlushAsync(postAuthSendTimeout.Token);

        sentPostAuthCommand = MiPlayProtocolConstants.SetLocalDeviceInfoCommand;
        sentPostAuthBoundaryDescription = "post-auth 0x001e getDeviceInfo acknowledged by 0x001f, then two 0x0058 setLocalDeviceInfo frames";
        awaitingPostAuthDeviceInfoAckBeforeLocalDeviceInfo = false;
        sentPostAuthLocalDeviceInfoFrames = true;
        Console.WriteLine($"Verified 0x{MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand:X4} getDeviceInfo acknowledgement, then sent staged local device info candidate={postAuthCandidate.Label}: command=0x{MiPlayProtocolConstants.SetLocalDeviceInfoCommand:X4}, sequence=0x{sourceNameSequence:X4}, encryptedPayloadLength={sourceNamePayload.Length}, plaintextJson={JsonSerializer.Serialize(localDeviceInfoPayloads.SourceNameJson)}; command=0x{MiPlayProtocolConstants.SetLocalDeviceInfoCommand:X4}, sequence=0x{localDeviceInfoSequence:X4}, encryptedPayloadLength={localDeviceInfoPayload.Length}, plaintextJson={JsonSerializer.Serialize(localDeviceInfoPayloads.LocalDeviceInfoJson)}. The probe will now only observe; no additional getDeviceInfo, setLocalDeviceInfo, heartbeat, media, RTSP, audio, playback, openDevice, or other control data will be sent.");
    }

    async Task SendOfficialPostAuthStepAsync(
        MiPlayOfficialPostAuthSequenceStep step,
        string safetyAuthCandidateLabel,
        string outboundProfileLabel)
    {
        if (officialPostAuthOutboundCipher is null)
        {
            throw new InvalidOperationException("The official post-auth outbound cipher has not been initialized.");
        }

        var encryptedPayload = officialPostAuthOutboundCipher.EncryptVersion1(step.PlaintextPayload);
        var frame = MiPlayCommandFrameCodec.Encode(step.Command, step.Sequence, encryptedPayload);
        using var postAuthSendTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await stream.WriteAsync(frame, postAuthSendTimeout.Token);
        await stream.FlushAsync(postAuthSendTimeout.Token);

        sentPostAuthCommand = step.Command;
        var plaintextPreview = FormatOfficialPostAuthPlaintextPreview(step);
        Console.WriteLine($"Official post-auth sequence sent step={step.Kind}, command=0x{step.Command:X4}, sequence=0x{step.Sequence:X4}, plaintextPayloadLength={step.PlaintextPayload.Length}, encryptedPayloadLength={encryptedPayload.Length}, plaintextPreview={JsonSerializer.Serialize(plaintextPreview)}, safetyAuthCandidate={safetyAuthCandidateLabel}, outboundProfile={outboundProfileLabel}. Boundary: {step.Boundary} No Open, AddMirror, RTSP, media, playback, audio, retry, or fallback will be sent.");
    }

    static string FormatOfficialPostAuthPlaintextPreview(MiPlayOfficialPostAuthSequenceStep step)
    {
        if (step.PlaintextPayload.Length == 0)
        {
            return string.Empty;
        }

        var plaintext = Encoding.UTF8.GetString(step.PlaintextPayload);
        return step.Kind == MiPlayOfficialPostAuthSequenceStepKind.SendSourceName
            ? plaintext.Replace(
                MiPlayRealPhonePostAuthPlaintextEvidence.RecoveredOfficialSourceBluetoothMacHash,
                "<redacted-md5>",
                StringComparison.Ordinal)
            : plaintext;
    }

    async Task SendOfficialPostAuthPrefixAsync(
        string safetyAuthCandidateLabel,
        string outboundProfileLabel)
    {
        if (officialPostAuthSequenceSteps is null)
        {
            throw new InvalidOperationException("The official post-auth sequence plan has not been initialized.");
        }

        while (officialPostAuthNextStepIndex < officialPostAuthSequenceSteps.Count)
        {
            var step = officialPostAuthSequenceSteps[officialPostAuthNextStepIndex];
            if (step.Kind == MiPlayOfficialPostAuthSequenceStepKind.SendGetMirrorMode ||
                step.Kind == MiPlayOfficialPostAuthSequenceStepKind.SendSetPlaySource)
            {
                break;
            }

            await SendOfficialPostAuthStepAsync(step, safetyAuthCandidateLabel, outboundProfileLabel);
            if (step.Kind == MiPlayOfficialPostAuthSequenceStepKind.SendGetDeviceInfo)
            {
                awaitingOfficialPostAuthDeviceInfoAck = true;
            }

            officialPostAuthNextStepIndex++;
        }
    }

    async Task SendNextOfficialPostAuthStepAsync(
        MiPlayOfficialPostAuthSequenceStepKind expectedKind,
        string safetyAuthCandidateLabel,
        string outboundProfileLabel)
    {
        if (officialPostAuthSequenceSteps is null ||
            officialPostAuthNextStepIndex >= officialPostAuthSequenceSteps.Count)
        {
            throw new InvalidOperationException("No official post-auth step is ready to send.");
        }

        var step = officialPostAuthSequenceSteps[officialPostAuthNextStepIndex];
        if (step.Kind != expectedKind)
        {
            throw new InvalidOperationException($"Expected official post-auth step {expectedKind}, but next step is {step.Kind}.");
        }

        await SendOfficialPostAuthStepAsync(step, safetyAuthCandidateLabel, outboundProfileLabel);
        if (step.Kind == MiPlayOfficialPostAuthSequenceStepKind.SendGetMirrorMode)
        {
            awaitingOfficialPostAuthMirrorModeAck = true;
        }
        else if (step.Kind == MiPlayOfficialPostAuthSequenceStepKind.SendSetPlaySource)
        {
            sentOfficialPostAuthSetPlaySource = true;
        }

        officialPostAuthNextStepIndex++;
    }

    void PrintPostAuthOutboundProfileDryRun(ushort postAuthSequence)
    {
        if (!emitPostAuthOutboundProfileDryRun || printedPostAuthOutboundProfileDryRun)
        {
            return;
        }

        if (selectedSafetyAuthCandidate is not { } postAuthCandidate ||
            localSafetyAuthPlaintextForPostAuthDryRun is null ||
            localSafetyAuthAcknowledgementPlaintextForPostAuthDryRun is null)
        {
            Console.WriteLine("Skipped post-auth outbound profile dry-run: mutual SafetyAuth plaintext state is incomplete. No post-auth business frame was sent.");
            return;
        }

        var comparison = MiPlayPostAuthSafetyDataOutboundDryRun.CompareOfficialSetPlaySourceProfiles(
            postAuthCandidate.AuthKey,
            localSafetyAuthPlaintextForPostAuthDryRun,
            localSafetyAuthAcknowledgementPlaintextForPostAuthDryRun,
            postAuthSequence);
        printedPostAuthOutboundProfileDryRun = true;
        Console.WriteLine($"Dry-run post-auth outbound profile comparison for official JSON Cmd_SetPlaySource command=0x{comparison.NativeNoReset.Command:X4}, sequence=0x{comparison.NativeNoReset.Sequence:X4}: nativeProfile={comparison.NativeNoReset.ProfileLabel}, nativeFrameLength={comparison.NativeNoReset.CommandFrameLength}, nativePayloadLength={comparison.NativeNoReset.SafetyDataPayloadLength}, nativeFrameSha256={comparison.NativeNoReset.CommandFrameSha256}; oldProbeNegativeControl={comparison.ObservedInboundPromotedNegativeControl.ProfileLabel}, oldProbeFrameSha256={comparison.ObservedInboundPromotedNegativeControl.CommandFrameSha256}, framesDiffer={comparison.FramesDiffer}. Dry-run only: no authKey, plaintext, post-auth business frame, 0x0040, 0x001e, 0x0058, Cmd_Open, AddMirror, RTSP, media, playback, or audio is sent by this diagnostic.");
    }

    async Task<bool> CompleteMutualSafetyAuthAsync()
    {
        if (!sendLocalSafetyAuthChallenge ||
            !sentSafetyAuthAcknowledgement ||
            !verifiedPeerSafetyAuthAcknowledgement)
        {
            return false;
        }

        if (sendPostAuthOfficialSequence)
        {
            if (selectedSafetyAuthCandidate is not { } postAuthCandidate)
            {
                Console.WriteLine("Refused official post-auth sequence validation: no verified SafetyData session candidate is available.");
                return true;
            }

            var firstPostAuthSequence = sendNativeBootstrap ? (ushort)4 : (ushort)3;
            var readiness = MiPlayOfficialPostAuthSequenceProbePlan.Evaluate(
                new MiPlayOfficialPostAuthSequencePrerequisites(
                    MutualSafetyAuthVerified: true,
                    NativeNoResetOutboundProfileAvailable: localSafetyAuthPlaintextForPostAuthDryRun is not null &&
                        localSafetyAuthAcknowledgementPlaintextForPostAuthDryRun is not null,
                    OfficialPlaintextRecoveredFromRootPcap: true,
                    FreshSessionCommandOrderCaptured: false,
                    SafetyDataIntegrityEndianAlignedWithNative: true,
                    LocalDeviceInfoPayloadsAvailable: true,
                    GetDeviceInfoAcknowledgementParserAvailable: true,
                    GetMirrorModePairLocalized: true,
                    StopOnUnexpectedFrameOrClose: true,
                    ForbidCmdOpen: true,
                    ForbidAddMirror: true,
                    ForbidRtsp: true,
                    ForbidMediaPlaybackOrAudio: true,
                    FreshUserAuthorizationPresent: confirmOfficialPostAuthSequence,
                    FirstCommandSequence: firstPostAuthSequence));
            if (!readiness.CanSendNow || !readiness.SafeForNetworkUse || readiness.Steps.Count == 0)
            {
                Console.WriteLine($"Refused official post-auth sequence validation: {readiness.Reason}");
                return true;
            }

            if (localSafetyAuthPlaintextForPostAuthDryRun is null ||
                localSafetyAuthAcknowledgementPlaintextForPostAuthDryRun is null)
            {
                Console.WriteLine("Refused official post-auth sequence validation: local outbound SafetyAuth plaintext state is incomplete, so the native no-reset post-auth outbound profile cannot be reconstructed safely.");
                return true;
            }

            var outboundProfile = MiPlayPostAuthSafetyDataCipherProfile.CreateNativeNoResetOutboundProfile();
            officialPostAuthOutboundCipher = MiPlayPostAuthSafetyDataCipherProfile.CreateOutboundCommandCipher(
                postAuthCandidate.AuthKey,
                outboundProfile,
                [
                    localSafetyAuthPlaintextForPostAuthDryRun,
                    localSafetyAuthAcknowledgementPlaintextForPostAuthDryRun,
                ]);
            officialPostAuthSequenceSteps = readiness.Steps;
            officialPostAuthNextStepIndex = 0;

            if (postAuthSendDelay > TimeSpan.Zero)
            {
                Console.WriteLine($"Waiting {postAuthSendDelay.TotalMilliseconds:0} ms after local/peer 0x1403 verification before sending the official recovered post-auth sequence prefix; no data is sent during this delay.");
                await Task.Delay(postAuthSendDelay);
            }

            await SendOfficialPostAuthPrefixAsync(postAuthCandidate.Label, outboundProfile.Label);
            completedMutualSafetyAuth = true;
            sentPostAuthBoundaryDescription = "official recovered post-auth sequence prefix; requires same-sequence 0x001f before 0x0034, then same-sequence 0x0035 before 0x0040; no Open/AddMirror/RTSP/media/playback/audio";
            Console.WriteLine($"Mutual SafetyAuth completed and official recovered post-auth sequence prefix was sent. The probe is now waiting for same-sequence command=0x{MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand:X4}; it will send 0x{MiPlayProtocolConstants.GetMirrorModeCommand:X4} only after parsed 0x{MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand:X4}, then send 0x{MiPlayProtocolConstants.SetPlaySourceCommand:X4} only after parsed 0x{MiPlayProtocolConstants.GetMirrorModeAcknowledgementCommand:X4}. No Open, AddMirror, RTSP, media, playback, audio, retry, or fallback will be sent.");
            return false;
        }

        if (sendPostAuthReadOnlyGetDeviceInfo)
        {
            if (selectedSafetyAuthCandidate is not { } postAuthCandidate)
            {
                Console.WriteLine("Refused read-only post-auth Cmd_GetDeviceInfo validation: no verified SafetyData session candidate is available.");
                return true;
            }

            var postAuthSequence = sendNativeBootstrap ? (ushort)4 : (ushort)3;
            var readiness = MiPlayPostAuthGetDeviceInfoProbePolicy.EvaluateReadiness(
                new MiPlayPostAuthGetDeviceInfoProbePrerequisites(
                    MutualSafetyAuthVerified: true,
                    SafetyDataSessionCandidateAvailable: true,
                    NativeNoResetOutboundProfileAvailable: localSafetyAuthPlaintextForPostAuthDryRun is not null &&
                        localSafetyAuthAcknowledgementPlaintextForPostAuthDryRun is not null,
                    OfficialGetDeviceInfoOrderLocalized: true,
                    CmdSourceGetDeviceInfoFrameShapeLocalized: true,
                    Source001fAckListenerLocalized: true,
                    ReceiverGetDeviceInfoAckSemanticsLocalized: true,
                    FreshUserAuthorizationPresent: confirmReadOnlyGetDeviceInfoOneFrame,
                    NextCommandSequence: postAuthSequence,
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
                    ForbidMediaPlaybackOrAudio: true));
            if (!readiness.CanSendNow || readiness.Command is null || readiness.ExpectedAcknowledgementCommand is null || readiness.Sequence is null || readiness.PlaintextPayloadLength is null || readiness.MinimumAcknowledgementPayloadLength is null)
            {
                Console.WriteLine($"Refused read-only post-auth Cmd_GetDeviceInfo validation: {readiness.Reason}");
                return true;
            }

            if (localSafetyAuthPlaintextForPostAuthDryRun is null ||
                localSafetyAuthAcknowledgementPlaintextForPostAuthDryRun is null)
            {
                Console.WriteLine("Refused read-only post-auth Cmd_GetDeviceInfo validation: local outbound SafetyAuth plaintext state is incomplete, so the native no-reset post-auth outbound profile cannot be reconstructed safely.");
                return true;
            }

            var outboundProfile = MiPlayPostAuthSafetyDataCipherProfile.CreateNativeNoResetOutboundProfile();
            var outboundCipher = MiPlayPostAuthSafetyDataCipherProfile.CreateOutboundCommandCipher(
                postAuthCandidate.AuthKey,
                outboundProfile,
                [
                    localSafetyAuthPlaintextForPostAuthDryRun,
                    localSafetyAuthAcknowledgementPlaintextForPostAuthDryRun,
                ]);
            var getDeviceInfoFrame = MiPlayPostAuthGetDeviceInfoProbe.ToSafetyDataCommandFrame(
                postAuthSequence,
                outboundCipher);
            var encryptedFramePayloadLength = MiPlayCommandFrameCodec.TryDecode(getDeviceInfoFrame, out var getDeviceInfoCommandFrame, out _) && getDeviceInfoCommandFrame is not null
                ? getDeviceInfoCommandFrame.Payload.Length
                : -1;

            if (postAuthSendDelay > TimeSpan.Zero)
            {
                Console.WriteLine($"Waiting {postAuthSendDelay.TotalMilliseconds:0} ms after local/peer 0x1403 verification before sending the read-only Cmd_GetDeviceInfo frame; no data is sent during this delay.");
                await Task.Delay(postAuthSendDelay);
            }

            using var postAuthSendTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await stream.WriteAsync(getDeviceInfoFrame, postAuthSendTimeout.Token);
            await stream.FlushAsync(postAuthSendTimeout.Token);

            sentPostAuthCommand = MiPlayProtocolConstants.GetDeviceInfoCommand;
            sentPostAuthBoundaryDescription = $"one read-only post-auth Cmd_GetDeviceInfo 0x{MiPlayProtocolConstants.GetDeviceInfoCommand:X4} probe";
            awaitingPostAuthReadOnlyGetDeviceInfoAck = true;
            postAuthReadOnlyGetDeviceInfoSequence = postAuthSequence;
            completedMutualSafetyAuth = true;
            Console.WriteLine($"Mutual SafetyAuth completed: local 0x1402 was acknowledged by peer 0x1403, and peer 0x1402 was acknowledged by local 0x1403. Sent exactly one SafetyData-wrapped read-only Cmd_GetDeviceInfo command=0x{MiPlayProtocolConstants.GetDeviceInfoCommand:X4}, sequence=0x{postAuthSequence:X4}, plaintextPayloadLength=0, encryptedPayloadLength={encryptedFramePayloadLength}, safetyAuthCandidate={postAuthCandidate.Label}, outboundProfile={outboundProfile.Label}. The probe will only observe for same-sequence command=0x{MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand:X4} with decrypted payload length >= {readiness.MinimumAcknowledgementPayloadLength}; no retry, fallback, Cmd_SetPlaySource 0x0040, 0x0058, Cmd_Open, AddMirror, RTSP listener/response, media, RTP, audio, playback, or other control data will be sent.");
            return false;
        }
        if (sendPostAuthSetPlaySourceOneFrame)
        {
            if (selectedSafetyAuthCandidate is not { } postAuthCandidate)
            {
                Console.WriteLine("Refused official JSON Cmd_SetPlaySource one-frame validation: no verified SafetyData session candidate is available.");
                return true;
            }

            var postAuthSequence = sendNativeBootstrap ? (ushort)4 : (ushort)3;
            var readiness = MiPlaySetPlaySourceOneFrameProbePlan.Evaluate(
                new MiPlaySetPlaySourceOneFramePrerequisites(
                    MutualSafetyAuthVerified: true,
                    SafetyDataSessionCandidateAvailable: true,
                    OfficialSenderPayloadBuilderLocalized: true,
                    NativeSetPlaySourceCommandId0040Confirmed: true,
                    NativeConnectCmdSession2OnlyCarriesLyraKeyMaterial: true,
                    PriorEmptyAckRoutesClosedWithoutAcknowledgement: true,
                    FreshUserAuthorizationPresent: confirmOfficialJsonSetPlaySourceOneFrame,
                    NextCommandSequence: postAuthSequence,
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
                    ForbidMediaPlaybackOrAudio: true));
            if (!readiness.CanSendNow || readiness.Command is null || readiness.Sequence is null || readiness.PayloadText is null || readiness.PlaintextPayloadLength is null)
            {
                Console.WriteLine($"Refused official JSON Cmd_SetPlaySource one-frame validation: {readiness.Reason}");
                return true;
            }

            if (localSafetyAuthPlaintextForPostAuthDryRun is null ||
                localSafetyAuthAcknowledgementPlaintextForPostAuthDryRun is null)
            {
                Console.WriteLine("Refused official JSON Cmd_SetPlaySource one-frame validation: local outbound SafetyAuth plaintext state is incomplete, so the native no-reset post-auth outbound profile cannot be reconstructed safely.");
                return true;
            }

            var outboundProfile = MiPlayPostAuthSafetyDataCipherProfile.CreateNativeNoResetOutboundProfile();
            var outboundCipher = MiPlayPostAuthSafetyDataCipherProfile.CreateOutboundCommandCipher(
                postAuthCandidate.AuthKey,
                outboundProfile,
                [
                    localSafetyAuthPlaintextForPostAuthDryRun,
                    localSafetyAuthAcknowledgementPlaintextForPostAuthDryRun,
                ]);
            var setPlaySourceFrame = MiPlaySetPlaySourceOneFrameProbe.ToSafetyDataCommandFrame(
                postAuthSequence,
                outboundCipher);
            var encryptedFramePayloadLength = MiPlayCommandFrameCodec.TryDecode(setPlaySourceFrame, out var setPlayFrame, out _) && setPlayFrame is not null
                ? setPlayFrame.Payload.Length
                : -1;

            if (postAuthSendDelay > TimeSpan.Zero)
            {
                Console.WriteLine($"Waiting {postAuthSendDelay.TotalMilliseconds:0} ms after local/peer 0x1403 verification before sending the official JSON Cmd_SetPlaySource one-frame probe; no data is sent during this delay.");
                await Task.Delay(postAuthSendDelay);
            }

            using var postAuthSendTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await stream.WriteAsync(setPlaySourceFrame, postAuthSendTimeout.Token);
            await stream.FlushAsync(postAuthSendTimeout.Token);

            sentPostAuthCommand = MiPlayProtocolConstants.SetPlaySourceCommand;
            sentPostAuthBoundaryDescription = $"one post-auth official minimal JSON Cmd_SetPlaySource 0x{MiPlayProtocolConstants.SetPlaySourceCommand:X4} one-frame probe payload {JsonSerializer.Serialize(readiness.PayloadText)}";
            completedMutualSafetyAuth = true;
            Console.WriteLine($"Mutual SafetyAuth completed: local 0x1402 was acknowledged by peer 0x1403, and peer 0x1402 was acknowledged by local 0x1403. Sent one SafetyData-wrapped official JSON Cmd_SetPlaySource command=0x{MiPlayProtocolConstants.SetPlaySourceCommand:X4}, sequence=0x{postAuthSequence:X4}, plaintextPayloadLength={readiness.PlaintextPayloadLength}, encryptedPayloadLength={encryptedFramePayloadLength}, plaintextPayload={JsonSerializer.Serialize(readiness.PayloadText)}, safetyAuthCandidate={postAuthCandidate.Label}, outboundProfile={outboundProfile.Label}. The probe will only observe for command=0x{MiPlayProtocolConstants.SetPlaySourceAcknowledgementCommand:X4}; no retry, fallback, Cmd_Open, 0x0058, AddMirror, RTSP listener/response, media, RTP, audio, playback, or other control data will be sent.");
            return false;
        }

        if (sendPostAuthSetPlaySourceAck)
        {
            if (selectedSafetyAuthCandidate is not { } postAuthCandidate)
            {
                Console.WriteLine("Refused post-auth Cmd_SetPlaySource ACK validation: no verified SafetyData session candidate is available.");
                return true;
            }

            var postAuthSequence = sendNativeBootstrap ? (ushort)4 : (ushort)3;
            var readiness = MiPlaySetPlaySourceAckProbePolicy.EvaluateAckReadiness(
                new MiPlaySetPlaySourceAckPrerequisites(
                    MutualSafetyAuthVerified: true,
                    SafetyDataSessionCandidateAvailable: true,
                    MpasExternalSetPlaySourceDispatchObserved: true,
                    MpasAcknowledgesBeforePayloadParse: true,
                    NextCommandSequence: postAuthSequence,
                    EmptyPayloadOnly: true,
                    NoMediaBoundary: true,
                    ForbidCmdOpen: true,
                    Forbid0058: true,
                    ForbidAddMirror: true,
                    ForbidRtsp: true,
                    ForbidPlaybackOrAudio: true));
            if (!readiness.CanSend || readiness.Command is null || readiness.Sequence is null || readiness.PlaintextPayloadLength is null)
            {
                Console.WriteLine($"Refused post-auth Cmd_SetPlaySource ACK validation: {readiness.Reason}");
                return true;
            }

            var setPlaySourceFrame = MiPlaySetPlaySourceAckProbe.ToSafetyDataCommandFrame(
                postAuthSequence,
                postAuthCandidate.Cipher);
            var encryptedFramePayloadLength = MiPlayCommandFrameCodec.TryDecode(setPlaySourceFrame, out var setPlayFrame, out _) && setPlayFrame is not null
                ? setPlayFrame.Payload.Length
                : -1;

            if (postAuthSendDelay > TimeSpan.Zero)
            {
                Console.WriteLine($"Waiting {postAuthSendDelay.TotalMilliseconds:0} ms after local/peer 0x1403 verification before sending the ACK-only Cmd_SetPlaySource frame; no data is sent during this delay.");
                await Task.Delay(postAuthSendDelay);
            }

            using var postAuthSendTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await stream.WriteAsync(setPlaySourceFrame, postAuthSendTimeout.Token);
            await stream.FlushAsync(postAuthSendTimeout.Token);

            sentPostAuthCommand = MiPlayProtocolConstants.SetPlaySourceCommand;
            sentPostAuthBoundaryDescription = $"one post-auth empty-plaintext Cmd_SetPlaySource 0x{MiPlayProtocolConstants.SetPlaySourceCommand:X4} ACK-only probe";
            completedMutualSafetyAuth = true;
            Console.WriteLine($"Mutual SafetyAuth completed: local 0x1402 was acknowledged by peer 0x1403, and peer 0x1402 was acknowledged by local 0x1403. Sent one SafetyData-wrapped Cmd_SetPlaySource ACK-only command=0x{MiPlayProtocolConstants.SetPlaySourceCommand:X4}, sequence=0x{postAuthSequence:X4}, plaintextPayloadLength=0, encryptedPayloadLength={encryptedFramePayloadLength}, candidate={postAuthCandidate.Label}. Static mpas evidence sends 0x{MiPlayProtocolConstants.SetPlaySourceAcknowledgementCommand:X4} before payload parsing, so the probe will only observe for that ACK; no JSON source identity, Cmd_Open, 0x0058, AddMirror, RTSP listener/response, media, RTP, audio, playback, retry, or other control data will be sent.");
            return false;
        }
        if (sendPostAuthAddMirror)
        {
            if (selectedSafetyAuthCandidate is not { } postAuthCandidate)
            {
                Console.WriteLine("Refused post-auth Cmd_AddMirror validation: no verified SafetyData session candidate is available.");
                return true;
            }

            if (connectedTcpSession is null)
            {
                Console.WriteLine("Refused post-auth Cmd_AddMirror validation: the verified native SafetyAuth endpoint material was not captured.");
                return true;
            }

            var postAuthSequence = sendNativeBootstrap ? (ushort)4 : (ushort)3;
            var readiness = MiPlayAddMirrorProbePolicy.EvaluateAddMirrorReadiness(
                new MiPlayAddMirrorPrerequisites(
                    MutualSafetyAuthVerified: true,
                    SafetyDataSessionCandidateAvailable: true,
                    SourceAddress: connectedTcpSession.LocalAddress,
                    SourcePort: MiPlayProtocolConstants.DefaultMediaPort,
                    NextCommandSequence: postAuthSequence,
                    NoMediaBoundary: true,
                    ForbidCmdOpen: true,
                    Forbid0058: true));
            if (!readiness.CanSend || readiness.PayloadText is null || readiness.Command is null || readiness.Sequence is null)
            {
                Console.WriteLine($"Refused post-auth Cmd_AddMirror validation: {readiness.Reason}");
                return true;
            }

            var addMirrorRequest = new MiPlayAddMirrorRequest(
                connectedTcpSession.LocalAddress,
                MiPlayProtocolConstants.DefaultMediaPort);
            var addMirrorPlaintext = addMirrorRequest.ToPayloadBytes();
            var addMirrorPayload = postAuthCandidate.Cipher.EncryptVersion1(addMirrorPlaintext);
            var addMirrorFrame = MiPlayCommandFrameCodec.Encode(
                MiPlayProtocolConstants.AddMirrorCommand,
                postAuthSequence,
                addMirrorPayload);

            using var postAuthSendTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await stream.WriteAsync(addMirrorFrame, postAuthSendTimeout.Token);
            await stream.FlushAsync(postAuthSendTimeout.Token);

            sentPostAuthCommand = MiPlayProtocolConstants.AddMirrorCommand;
            sentPostAuthBoundaryDescription = $"one post-auth Cmd_AddMirror 0x{MiPlayProtocolConstants.AddMirrorCommand:X4} with recovered local payload {readiness.PayloadText}";
            completedMutualSafetyAuth = true;
            Console.WriteLine($"Mutual SafetyAuth completed: local 0x1402 was acknowledged by peer 0x1403, and peer 0x1402 was acknowledged by local 0x1403. Sent one SafetyData-wrapped Cmd_AddMirror command=0x{MiPlayProtocolConstants.AddMirrorCommand:X4}, sequence=0x{postAuthSequence:X4}, encryptedPayloadLength={addMirrorPayload.Length}, plaintextPayload={JsonSerializer.Serialize(readiness.PayloadText)}, candidate={postAuthCandidate.Label}. The probe will only observe for command=0x{MiPlayProtocolConstants.AddMirrorAcknowledgementCommand:X4}; no Cmd_Open, 0x0058, RTSP listener/response, media, RTP, audio, playback, retry, or other control data will be sent.");
            return false;
        }

        if (sendPostAuthOpenRtspStub)
        {
            if (selectedSafetyAuthCandidate is not { } postAuthCandidate)
            {
                Console.WriteLine("Refused post-auth Cmd_Open RTSP callback validation: no verified SafetyData session candidate is available.");
                return true;
            }

            if (noMediaRtspSourceAddress is null || noMediaRtspListener is null || noMediaRtspFirstRequestTask is null)
            {
                Console.WriteLine("Refused post-auth Cmd_Open RTSP callback validation: the no-media RTSP/WFD listener is not active.");
                return true;
            }

            var postAuthSequence = sendNativeBootstrap ? (ushort)4 : (ushort)3;
            var readiness = MiPlayNoMediaRtspProbePolicy.EvaluateOpenReadiness(
                new MiPlayNoMediaRtspOpenPrerequisites(
                    MutualSafetyAuthVerified: true,
                    SafetyDataSessionCandidateAvailable: true,
                    RtspListenerStartedBeforeCmdOpen: true,
                    SourceAddress: noMediaRtspSourceAddress,
                    SourcePort: postAuthOpenRtspPort,
                    MirrorMode: postAuthOpenMirrorMode,
                    NextCommandSequence: postAuthSequence,
                    NoMediaBoundary: true,
                    Forbid0058: true));
            if (!readiness.CanSend || readiness.PayloadText is null || readiness.Command is null || readiness.Sequence is null)
            {
                Console.WriteLine($"Refused post-auth Cmd_Open RTSP callback validation: {readiness.Reason}");
                return true;
            }

            var openRequest = new MiPlayOpenDeviceRequest(
                noMediaRtspSourceAddress,
                postAuthOpenRtspPort,
                postAuthOpenMirrorMode);
            var openPlaintext = openRequest.ToPayloadBytes();
            var openPayload = postAuthCandidate.Cipher.EncryptVersion1(openPlaintext);
            var openFrame = MiPlayCommandFrameCodec.Encode(
                MiPlayProtocolConstants.OpenDeviceCommand,
                postAuthSequence,
                openPayload);

            using var postAuthSendTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await stream.WriteAsync(openFrame, postAuthSendTimeout.Token);
            await stream.FlushAsync(postAuthSendTimeout.Token);

            sentPostAuthCommand = MiPlayProtocolConstants.OpenDeviceCommand;
            sentPostAuthBoundaryDescription = $"one post-auth Cmd_Open 0x{MiPlayProtocolConstants.OpenDeviceCommand:X4} with no-media RTSP first-request listener at {noMediaRtspSourceAddress}:{postAuthOpenRtspPort}";
            completedMutualSafetyAuth = true;
            Console.WriteLine($"Mutual SafetyAuth completed: local 0x1402 was acknowledged by peer 0x1403, and peer 0x1402 was acknowledged by local 0x1403. Sent one SafetyData-wrapped Cmd_Open command=0x{MiPlayProtocolConstants.OpenDeviceCommand:X4}, sequence=0x{postAuthSequence:X4}, encryptedPayloadLength={openPayload.Length}, plaintextPayload={JsonSerializer.Serialize(readiness.PayloadText)}, candidate={postAuthCandidate.Label}. The probe will only observe for a receiver RTSP callback and control frames; no 0x0058, heartbeat, getDeviceInfo, media, RTP, RTSP response, audio, playback, openDevice retry, or other control data will be sent.");
            return false;
        }

        if (postAuthLocalDeviceInfoPayloads is { })
        {
            if (selectedSafetyAuthCandidate is not { } postAuthCandidate)
            {
                Console.WriteLine("Refused post-auth local device info sequence: no verified SafetyData session candidate is available.");
                return true;
            }

            var getDeviceInfoSequence = sendNativeBootstrap ? (ushort)4 : (ushort)3;
            var getDeviceInfoPayload = postAuthCandidate.Cipher.EncryptVersion1([]);
            var getDeviceInfoFrame = MiPlayCommandFrameCodec.Encode(
                MiPlayProtocolConstants.GetDeviceInfoCommand,
                getDeviceInfoSequence,
                getDeviceInfoPayload);

            using var postAuthSendTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await stream.WriteAsync(getDeviceInfoFrame, postAuthSendTimeout.Token);
            await stream.FlushAsync(postAuthSendTimeout.Token);

            sentPostAuthCommand = MiPlayProtocolConstants.GetDeviceInfoCommand;
            sentPostAuthBoundaryDescription = "post-auth 0x001e getDeviceInfo staged before local device info";
            stagedPostAuthGetDeviceInfoSequence = getDeviceInfoSequence;
            awaitingPostAuthDeviceInfoAckBeforeLocalDeviceInfo = true;
            completedMutualSafetyAuth = true;
            Console.WriteLine($"Mutual SafetyAuth completed: local 0x1402 was acknowledged by peer 0x1403, and peer 0x1402 was acknowledged by local 0x1403. Sent staged post-auth getDeviceInfo candidate={postAuthCandidate.Label}: command=0x{MiPlayProtocolConstants.GetDeviceInfoCommand:X4}, sequence=0x{getDeviceInfoSequence:X4}, encryptedPayloadLength={getDeviceInfoPayload.Length}, plaintextLength=0. The probe will send the two 0x{MiPlayProtocolConstants.SetLocalDeviceInfoCommand:X4} setLocalDeviceInfo frames only if it observes a decrypted 0x{MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand:X4} getDeviceInfo acknowledgement; no heartbeat, media, RTSP, audio, playback, openDevice, or other control data will be sent.");
            return false;
        }

        if (sendPostAuthGetDeviceInfo || sendPostAuthHeartbeat)
        {
            if (selectedSafetyAuthCandidate is not { } postAuthCandidate)
            {
                Console.WriteLine("Refused post-auth command: no verified SafetyData session candidate is available.");
                return true;
            }

            var postAuthCommand = sendPostAuthGetDeviceInfo
                ? MiPlayProtocolConstants.GetDeviceInfoCommand
                : MiPlayProtocolConstants.HeartbeatCommand;
            var postAuthName = sendPostAuthGetDeviceInfo ? "getDeviceInfo" : "heartbeat";
            var postAuthPayload = postAuthCandidate.Cipher.EncryptVersion1([]);
            var postAuthSequence = sendNativeBootstrap ? (ushort)4 : (ushort)3;
            var postAuthFrame = MiPlayCommandFrameCodec.Encode(
                postAuthCommand,
                postAuthSequence,
                postAuthPayload);
            using var postAuthSendTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await stream.WriteAsync(postAuthFrame, postAuthSendTimeout.Token);
            await stream.FlushAsync(postAuthSendTimeout.Token);
            sentPostAuthCommand = postAuthCommand;
            sentPostAuthBoundaryDescription = $"one post-auth 0x{postAuthCommand:X4} {postAuthName}";
            completedMutualSafetyAuth = true;
            Console.WriteLine($"Mutual SafetyAuth completed: local 0x1402 was acknowledged by peer 0x1403, and peer 0x1402 was acknowledged by local 0x1403. Sent one post-auth {postAuthName} command=0x{postAuthCommand:X4}, sequence=0x{postAuthSequence:X4}, encryptedPayloadLength={postAuthPayload.Length}, candidate={postAuthCandidate.Label}. The probe will now only observe for {postAuthObserveTimeout.TotalSeconds:0} seconds; no additional heartbeat, getDeviceInfo, setLocalDeviceInfo, media, RTSP, audio, playback, openDevice, or other control data will be sent.");
            return false;
        }

        if (observeAfterMutualSafetyAuth)
        {
            completedMutualSafetyAuth = true;
            PrintPostAuthOutboundProfileDryRun(sendNativeBootstrap ? (ushort)4 : (ushort)3);
            Console.WriteLine($"Mutual SafetyAuth completed: local 0x1402 was acknowledged by peer 0x1403, and peer 0x1402 was acknowledged by local 0x1403. Entering read-only post-auth observation for {postAuthObserveTimeout.TotalSeconds:0} seconds; no post-auth heartbeat, getDeviceInfo, setLocalDeviceInfo, media, RTSP, audio, playback, openDevice, or other control data will be sent.");
            return false;
        }

        PrintPostAuthOutboundProfileDryRun(sendNativeBootstrap ? (ushort)4 : (ushort)3);
        Console.WriteLine("Mutual SafetyAuth completed: local 0x1402 was acknowledged by peer 0x1403, and peer 0x1402 was acknowledged by local 0x1403. Stopping before media or playback.");
        return true;
    }

    try
    {
        while (true)
        {
            var readTimeoutDuration = completedMutualSafetyAuth ? postAuthObserveTimeout : TimeSpan.FromSeconds(5);
            using var readTimeout = new CancellationTokenSource(readTimeoutDuration);
            var followUpBytes = await ReadMiPlayFrameAsync(stream, readTimeout.Token);
            if (!MiPlayCommandFrameCodec.TryDecode(followUpBytes, out var followUp, out _) || followUp is null)
            {
                continue;
            }

            observedFrames++;
            Console.WriteLine($"Observed follow-up command=0x{followUp.Command:X4}, sequence=0x{followUp.Sequence:X4}, payload={Convert.ToHexString(followUp.Payload)}.");
            if (followUp.Command == MiPlayProtocolConstants.NotifyCommand &&
                MiPlayNotifyPayloadCodec.TryDecode(followUp.Payload, out var notifyPayload, out var notifyBytesConsumed) &&
                notifyPayload is not null &&
                notifyBytesConsumed == followUp.Payload.Length)
            {
                Console.WriteLine($"Decoded notify command=0x{MiPlayProtocolConstants.NotifyCommand:X4}, sequence=0x{followUp.Sequence:X4}, {DescribeNotifyPayload(notifyPayload)}. Native static evidence routes this to onRecvNotify without constructing a reply; the probe sends no notify acknowledgement.");

                if (waitingForLegacyClearSetPlaySourceReadyNotify &&
                    string.Equals(notifyPayload.Label, "state", StringComparison.Ordinal) &&
                    notifyPayload.IntegerValue == 3)
                {
                    var legacyClearSequence = sendNativeBootstrap ? (ushort)2 : (ushort)1;
                    var readiness = MiPlayLegacyClearSetPlaySourceAckProbePolicy.EvaluateAckReadiness(
                        new MiPlayLegacyClearSetPlaySourceAckPrerequisites(
                            LegacyChallengeAcknowledged: true,
                            NativeVersionBootstrapSent: sendNativeBootstrap,
                            MpasModernSafetyCommandConstantsAbsentObserved: true,
                            MpasExternalSetPlaySourceDispatchObserved: true,
                            MpasAcknowledgesBeforePayloadParse: true,
                            NextCommandSequence: legacyClearSequence,
                            EmptyPayloadOnly: true,
                            NoModernSafetyInfoOrSafetyAuth: !sendSafetyInfoOffer && !decryptSafetyAuth && !sendSafetyAuthAcknowledgement && !sendLocalSafetyAuthChallenge,
                            NoSafetyDataEncryption: true,
                            NoMediaBoundary: true,
                            ForbidCmdOpen: true,
                            Forbid0058: true,
                            ForbidAddMirror: true,
                            ForbidRtsp: true,
                            ForbidPlaybackOrAudio: true));
                    if (!readiness.CanSend || readiness.Command is null || readiness.Sequence is null || readiness.PlaintextPayloadLength is null)
                    {
                        Console.WriteLine($"Refused legacy clear after-ready-notify Cmd_SetPlaySource ACK validation: {readiness.Reason}");
                        return;
                    }

                    var legacyClearFrame = MiPlaySetPlaySourceAckProbe.ToCommandFrame(legacyClearSequence);
                    using var legacyClearSendTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await stream.WriteAsync(legacyClearFrame, legacyClearSendTimeout.Token);
                    await stream.FlushAsync(legacyClearSendTimeout.Token);

                    waitingForLegacyClearSetPlaySourceReadyNotify = false;
                    sentPostAuthCommand = MiPlayProtocolConstants.SetPlaySourceCommand;
                    sentPostAuthBoundaryDescription = $"one legacy clear empty Cmd_SetPlaySource 0x{MiPlayProtocolConstants.SetPlaySourceCommand:X4} ACK-only probe after decoded state=3 notify";
                    completedMutualSafetyAuth = true;
                    observingLegacyClearSetPlaySourceAck = true;
                    Console.WriteLine($"Observed decoded notify label=state integerValue=3, then sent one clear-text Cmd_SetPlaySource ACK-only command=0x{MiPlayProtocolConstants.SetPlaySourceCommand:X4}, sequence=0x{legacyClearSequence:X4}, plaintextPayloadLength=0. The probe will only observe for command=0x{MiPlayProtocolConstants.SetPlaySourceAcknowledgementCommand:X4}; no 0x1400, 0x1402, 0x1403, SafetyData, JSON source identity, Cmd_Open, 0x0058, AddMirror, RTSP listener/response, media, RTP, audio, playback, retry, or other control data will be sent.");
                    continue;
                }
                if (waitingForLegacyClearGetDeviceInfoReadyNotify &&
                    string.Equals(notifyPayload.Label, "state", StringComparison.Ordinal) &&
                    notifyPayload.IntegerValue == 3)
                {
                    var legacyClearSequence = sendNativeBootstrap ? (ushort)2 : (ushort)1;
                    var readiness = MiPlayLegacyClearGetDeviceInfoProbePolicy.EvaluateReadiness(
                        new MiPlayLegacyClearGetDeviceInfoPrerequisites(
                            LegacyChallengeAcknowledged: true,
                            NativeVersionBootstrapSent: sendNativeBootstrap,
                            MpasGetDeviceInfoDispatchObserved: true,
                            MpasGetDeviceInfoAcknowledgementObserved: true,
                            MpasGetDeviceInfoAsyncPreparePathObserved: true,
                            ReadyStateNotifyObservedBeforeSend: true,
                            NextCommandSequence: legacyClearSequence,
                            EmptyPayloadOnly: true,
                            NoModernSafetyInfoOrSafetyAuth: !sendSafetyInfoOffer && !decryptSafetyAuth && !sendSafetyAuthAcknowledgement && !sendLocalSafetyAuthChallenge,
                            NoSafetyDataEncryption: true,
                            NoSetPlaySource: true,
                            NoMediaBoundary: true,
                            ForbidCmdOpen: true,
                            Forbid0058: true,
                            ForbidAddMirror: true,
                            ForbidRtsp: true,
                            ForbidPlaybackOrAudio: true));
                    if (!readiness.CanSend || readiness.Command is null || readiness.ExpectedAcknowledgementCommand is null || readiness.Sequence is null || readiness.PlaintextPayloadLength is null)
                    {
                        Console.WriteLine($"Refused legacy clear after-ready-notify Cmd_GetDeviceInfo validation: {readiness.Reason}");
                        return;
                    }

                    var legacyClearFrame = MiPlayLegacyClearGetDeviceInfoProbe.ToCommandFrame(legacyClearSequence);
                    using var legacyClearSendTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await stream.WriteAsync(legacyClearFrame, legacyClearSendTimeout.Token);
                    await stream.FlushAsync(legacyClearSendTimeout.Token);

                    waitingForLegacyClearGetDeviceInfoReadyNotify = false;
                    sentPostAuthCommand = MiPlayProtocolConstants.GetDeviceInfoCommand;
                    sentPostAuthBoundaryDescription = $"one legacy clear empty Cmd_GetDeviceInfo 0x{MiPlayProtocolConstants.GetDeviceInfoCommand:X4} read-only probe after decoded state=3 notify";
                    completedMutualSafetyAuth = true;
                    observingLegacyClearGetDeviceInfo = true;
                    Console.WriteLine($"Observed decoded notify label=state integerValue=3, then sent one clear-text Cmd_GetDeviceInfo command=0x{MiPlayProtocolConstants.GetDeviceInfoCommand:X4}, sequence=0x{legacyClearSequence:X4}, plaintextPayloadLength=0. The probe will only observe for command=0x{MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand:X4}; no 0x1400, 0x1402, 0x1403, SafetyData, Cmd_SetPlaySource, JSON source identity, Cmd_Open, 0x0058, AddMirror, RTSP listener/response, media, RTP, audio, playback, retry, or other control data will be sent.");
                    continue;
                }
            }

            if (completedMutualSafetyAuth)
            {
                if (observingLegacyClearGetDeviceInfo)
                {
                    if (MiPlayNativeVersionCodec.TryDecodeAcknowledgement(
                            followUpBytes,
                            out var legacyVersionSequence,
                            out var legacyDeviceVersion))
                    {
                        Console.WriteLine($"Decoded native version acknowledgement during legacy clear getDeviceInfo observe: command=0x{MiPlayProtocolConstants.NativeSourceVersionAcknowledgementCommand:X4}, sequence=0x{legacyVersionSequence:X4}, payload={JsonSerializer.Serialize(legacyDeviceVersion)}. No response will be sent.");
                    }
                    else if (followUp.Command == MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand)
                    {
                        var payloadSha256 = Convert.ToHexString(SHA256.HashData(followUp.Payload));
                        var parsedDeviceInfo = MiPlayLegacyDeviceInfoPayloadCodec.TryDecode(
                                followUp.Payload,
                                out var deviceInfoPayload,
                                out var deviceInfoBytesConsumed) &&
                            deviceInfoPayload is not null &&
                            deviceInfoBytesConsumed == followUp.Payload.Length
                                ? MiPlayLegacyDeviceInfoPayloadCodec.DescribeRedacted(deviceInfoPayload)
                                : "<decode-failed>";

                        Console.WriteLine($"Verified legacy clear Cmd_GetDeviceInfo acknowledgement command=0x{MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand:X4}, sequence=0x{followUp.Sequence:X4}, payloadLength={followUp.Payload.Length}, payloadSha256={payloadSha256}, parsedDeviceInfo={JsonSerializer.Serialize(parsedDeviceInfo)}. The legacy clear getDeviceInfo probe sent an empty plaintext 0x001e and sends no 0x1400, 0x1402, 0x1403, SafetyData, Cmd_SetPlaySource, JSON source identity, Cmd_Open, 0x0058, AddMirror, RTSP, media, audio, playback, retry, or follow-up control data.");
                    }
                    else
                    {
                        Console.WriteLine($"Legacy clear getDeviceInfo observe mode: frame logged without response; waiting only for command=0x{MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand:X4} and sending no follow-up data.");
                    }

                    continue;
                }
                if (observingLegacyClearSetPlaySourceAck)
                {
                    if (MiPlayNativeVersionCodec.TryDecodeAcknowledgement(
                            followUpBytes,
                            out var legacyVersionSequence,
                            out var legacyDeviceVersion))
                    {
                        Console.WriteLine($"Decoded native version acknowledgement during legacy clear observe: command=0x{MiPlayProtocolConstants.NativeSourceVersionAcknowledgementCommand:X4}, sequence=0x{legacyVersionSequence:X4}, payload={JsonSerializer.Serialize(legacyDeviceVersion)}. No response will be sent.");
                    }
                    else if (followUp.Command == MiPlayProtocolConstants.SetPlaySourceAcknowledgementCommand)
                    {
                        var utf8Preview = followUp.Payload.Length == 0 ? string.Empty : Encoding.UTF8.GetString(followUp.Payload);
                        if (utf8Preview.Length > 256)
                        {
                            utf8Preview = utf8Preview[..256] + "...";
                        }

                        Console.WriteLine($"Verified legacy clear Cmd_SetPlaySource acknowledgement command=0x{MiPlayProtocolConstants.SetPlaySourceAcknowledgementCommand:X4}, sequence=0x{followUp.Sequence:X4}, rawPayloadLength={followUp.Payload.Length}, rawPayloadHex={Convert.ToHexString(followUp.Payload)}, rawPayloadUtf8Preview={JsonSerializer.Serialize(utf8Preview)}. The legacy clear ACK-only probe sent an empty plaintext 0x0040 and sends no 0x1400, 0x1402, 0x1403, SafetyData, JSON source identity, Cmd_Open, 0x0058, AddMirror, RTSP, media, audio, playback, retry, or follow-up control data.");
                    }
                    else
                    {
                        Console.WriteLine($"Legacy clear ACK-only observe mode: frame logged without response; waiting only for command=0x{MiPlayProtocolConstants.SetPlaySourceAcknowledgementCommand:X4} and sending no follow-up data.");
                    }

                    continue;
                }

                if (selectedSafetyAuthCandidate is { } postAuthCandidate &&
                    postAuthCandidate.Cipher.TryDecryptVersion1(followUp.Payload, out var postAuthDecoded) &&
                    postAuthDecoded is not null)
                {
                    var plaintext = postAuthDecoded.Plaintext;
                    var utf8Preview = plaintext.Length == 0 ? string.Empty : Encoding.UTF8.GetString(plaintext);
                    if (utf8Preview.Length > 256)
                    {
                        utf8Preview = utf8Preview[..256] + "...";
                    }

                    if (officialPostAuthSequenceSteps is not null)
                    {
                        var outboundProfileLabel = MiPlayPostAuthSafetyDataCipherProfile.CreateNativeNoResetOutboundProfile().Label;
                        if (awaitingOfficialPostAuthDeviceInfoAck)
                        {
                            if (followUp.Command == MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand)
                            {
                                Console.WriteLine($"Official post-auth sequence observed setLocalDeviceInfo ACK command=0x{followUp.Command:X4}, sequence=0x{followUp.Sequence:X4}, candidate={postAuthCandidate.Label}, decryptedPayloadLength={plaintext.Length}. Waiting for required 0x{MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand:X4}; no response will be sent.");
                                continue;
                            }

                            var expectedDeviceInfoStep = officialPostAuthSequenceSteps.First(step =>
                                step.Kind == MiPlayOfficialPostAuthSequenceStepKind.SendGetDeviceInfo);
                            if (followUp.Command != MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand ||
                                followUp.Sequence != expectedDeviceInfoStep.Sequence)
                            {
                                Console.WriteLine($"Official post-auth sequence stopped on unexpected decoded command=0x{followUp.Command:X4}, sequence=0x{followUp.Sequence:X4}; expected same-sequence command=0x{MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand:X4}, sequence=0x{expectedDeviceInfoStep.Sequence:X4}. No retry, fallback, 0x0040, Open, AddMirror, RTSP, media, playback, audio, or other control data will be sent.");
                                return;
                            }

                            var payloadSha256 = Convert.ToHexString(SHA256.HashData(plaintext));
                            var parsedDeviceInfoText = MiPlayLegacyDeviceInfoPayloadCodec.TryDecode(
                                    plaintext,
                                    out var parsedDeviceInfo,
                                    out _) && parsedDeviceInfo is not null
                                ? MiPlayLegacyDeviceInfoPayloadCodec.DescribeRedacted(parsedDeviceInfo)
                                : null;
                            if (parsedDeviceInfoText is null ||
                                plaintext.Length < MiPlayPostAuthProbePolicy.MinimumDeviceInfoAcknowledgementPayloadLength)
                            {
                                Console.WriteLine($"Official post-auth sequence stopped: 0x{MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand:X4} did not parse as a sufficient device-info payload. sequence=0x{followUp.Sequence:X4}, decryptedPayloadLength={plaintext.Length}, payloadSha256={payloadSha256}. No retry, fallback, 0x0040, Open, AddMirror, RTSP, media, playback, audio, or other control data will be sent.");
                                return;
                            }

                            awaitingOfficialPostAuthDeviceInfoAck = false;
                            Console.WriteLine($"Official post-auth sequence verified required 0x{MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand:X4}, sequence=0x{followUp.Sequence:X4}, payloadSha256={payloadSha256}, parsedDeviceInfo={JsonSerializer.Serialize(parsedDeviceInfoText)}. Now sending 0x{MiPlayProtocolConstants.GetMirrorModeCommand:X4}; no Open, AddMirror, RTSP, media, playback, or audio will be sent.");
                            await SendNextOfficialPostAuthStepAsync(
                                MiPlayOfficialPostAuthSequenceStepKind.SendGetMirrorMode,
                                postAuthCandidate.Label,
                                outboundProfileLabel);
                            continue;
                        }

                        if (awaitingOfficialPostAuthMirrorModeAck)
                        {
                            if (followUp.Command == MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand)
                            {
                                Console.WriteLine($"Official post-auth sequence observed delayed setLocalDeviceInfo ACK command=0x{followUp.Command:X4}, sequence=0x{followUp.Sequence:X4}, candidate={postAuthCandidate.Label}, decryptedPayloadLength={plaintext.Length}. Waiting for required 0x{MiPlayProtocolConstants.GetMirrorModeAcknowledgementCommand:X4}; no response will be sent.");
                                continue;
                            }

                            var expectedMirrorModeStep = officialPostAuthSequenceSteps.First(step =>
                                step.Kind == MiPlayOfficialPostAuthSequenceStepKind.SendGetMirrorMode);
                            if (followUp.Command != MiPlayProtocolConstants.GetMirrorModeAcknowledgementCommand ||
                                followUp.Sequence != expectedMirrorModeStep.Sequence)
                            {
                                Console.WriteLine($"Official post-auth sequence stopped on unexpected decoded command=0x{followUp.Command:X4}, sequence=0x{followUp.Sequence:X4}; expected same-sequence command=0x{MiPlayProtocolConstants.GetMirrorModeAcknowledgementCommand:X4}, sequence=0x{expectedMirrorModeStep.Sequence:X4}. No retry, fallback, 0x0040, Open, AddMirror, RTSP, media, playback, audio, or other control data will be sent.");
                                return;
                            }

                            if (plaintext.Length != 5 || plaintext[0] != 0)
                            {
                                Console.WriteLine($"Official post-auth sequence stopped: 0x{MiPlayProtocolConstants.GetMirrorModeAcknowledgementCommand:X4} payload shape is unexpected. sequence=0x{followUp.Sequence:X4}, decryptedPayloadLength={plaintext.Length}, plaintextHex={Convert.ToHexString(plaintext)}. No retry, fallback, 0x0040, Open, AddMirror, RTSP, media, playback, audio, or other control data will be sent.");
                                return;
                            }

                            var mirrorMode = BinaryPrimitives.ReadUInt32BigEndian(plaintext.AsSpan(1, sizeof(uint)));
                            if (mirrorMode != 2)
                            {
                                Console.WriteLine($"Official post-auth sequence stopped: 0x{MiPlayProtocolConstants.GetMirrorModeAcknowledgementCommand:X4} mirrorMode={mirrorMode}, expected 2 from rooted official capture. No retry, fallback, 0x0040, Open, AddMirror, RTSP, media, playback, audio, or other control data will be sent.");
                                return;
                            }

                            awaitingOfficialPostAuthMirrorModeAck = false;
                            Console.WriteLine($"Official post-auth sequence verified required 0x{MiPlayProtocolConstants.GetMirrorModeAcknowledgementCommand:X4}, sequence=0x{followUp.Sequence:X4}, mirrorMode={mirrorMode}. Now sending recovered official runtime 0x{MiPlayProtocolConstants.SetPlaySourceCommand:X4}; no Open, AddMirror, RTSP, media, playback, or audio will be sent.");
                            await SendNextOfficialPostAuthStepAsync(
                                MiPlayOfficialPostAuthSequenceStepKind.SendSetPlaySource,
                                postAuthCandidate.Label,
                                outboundProfileLabel);
                            continue;
                        }

                        if (sentOfficialPostAuthSetPlaySource)
                        {
                            if (followUp.Command == MiPlayProtocolConstants.SetPlaySourceAcknowledgementCommand)
                            {
                                Console.WriteLine($"Official post-auth sequence observed SetPlaySource ACK command=0x{followUp.Command:X4}, sequence=0x{followUp.Sequence:X4}, candidate={postAuthCandidate.Label}, decryptedPayloadLength={plaintext.Length}, plaintextHex={Convert.ToHexString(plaintext)}, plaintextUtf8Preview={JsonSerializer.Serialize(utf8Preview)}. Validation complete; no Open, AddMirror, RTSP, media, playback, audio, retry, or fallback will be sent.");
                                return;
                            }

                            Console.WriteLine($"Official post-auth sequence post-0x0040 observe: decoded command=0x{followUp.Command:X4}, sequence=0x{followUp.Sequence:X4}, candidate={postAuthCandidate.Label}, decryptedPayloadLength={plaintext.Length}, plaintextHex={Convert.ToHexString(plaintext)}, plaintextUtf8Preview={JsonSerializer.Serialize(utf8Preview)}. No response or follow-up control data will be sent.");
                            continue;
                        }
                    }

                    if (awaitingPostAuthReadOnlyGetDeviceInfoAck &&
                        followUp.Command != MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand)
                    {
                        Console.WriteLine($"Read-only post-auth Cmd_GetDeviceInfo validation stopped on unexpected decoded command=0x{followUp.Command:X4}, sequence=0x{followUp.Sequence:X4}; expected same-sequence command=0x{MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand:X4}. No retry, fallback, 0x0040, 0x0058, Cmd_Open, AddMirror, RTSP, media, playback, audio, or other control data will be sent.");
                        return;
                    }
                    if (followUp.Command == MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand)
                    {
                        Console.WriteLine($"Decoded native post-auth getDeviceInfo acknowledgement command=0x{MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand:X4}, sequence=0x{followUp.Sequence:X4}, candidate={postAuthCandidate.Label}, decryptedPayloadLength={plaintext.Length}, plaintextHex={Convert.ToHexString(plaintext)}, plaintextUtf8Preview={JsonSerializer.Serialize(utf8Preview)}. Native static evidence maps this command to onCmdSessionDeviceInfoAck(byte[]).");
                        if (awaitingPostAuthReadOnlyGetDeviceInfoAck &&
                            postAuthReadOnlyGetDeviceInfoSequence is { } expectedReadOnlyGetDeviceInfoSequence)
                        {
                            var payloadSha256 = Convert.ToHexString(SHA256.HashData(plaintext));
                            var sequenceMatches = followUp.Sequence == expectedReadOnlyGetDeviceInfoSequence;
                            var payloadLengthAccepted = plaintext.Length >= MiPlayPostAuthProbePolicy.MinimumDeviceInfoAcknowledgementPayloadLength;
                            var parsedDeviceInfoText = MiPlayLegacyDeviceInfoPayloadCodec.TryDecode(
                                    plaintext,
                                    out var parsedDeviceInfo,
                                    out _) && parsedDeviceInfo is not null
                                ? MiPlayLegacyDeviceInfoPayloadCodec.DescribeRedacted(parsedDeviceInfo)
                                : null;
                            awaitingPostAuthReadOnlyGetDeviceInfoAck = false;

                            if (sequenceMatches && payloadLengthAccepted && parsedDeviceInfoText is not null)
                            {
                                Console.WriteLine($"Verified read-only post-auth Cmd_GetDeviceInfo gate command=0x{MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand:X4}, sequence=0x{followUp.Sequence:X4}, requestSequence=0x{expectedReadOnlyGetDeviceInfoSequence:X4}, candidate={postAuthCandidate.Label}, decryptedPayloadLength={plaintext.Length}, payloadSha256={payloadSha256}, parsedDeviceInfo={JsonSerializer.Serialize(parsedDeviceInfoText)}. This one-frame validation sends no retry, fallback, 0x0040, 0x0058, Cmd_Open, AddMirror, RTSP, media, playback, audio, or follow-up control data.");
                            }
                            else
                            {
                                Console.WriteLine($"Read-only post-auth Cmd_GetDeviceInfo gate not satisfied: command=0x{followUp.Command:X4}, sequence=0x{followUp.Sequence:X4}, requestSequence=0x{expectedReadOnlyGetDeviceInfoSequence:X4}, sequenceMatches={sequenceMatches}, payloadLengthAccepted={payloadLengthAccepted}, parsedDeviceInfoAvailable={parsedDeviceInfoText is not null}, decryptedPayloadLength={plaintext.Length}, payloadSha256={payloadSha256}. No retry, fallback, 0x0040, 0x0058, Cmd_Open, AddMirror, RTSP, media, playback, audio, or follow-up control data will be sent.");
                            }

                            return;
                        }
                        if (awaitingPostAuthDeviceInfoAckBeforeLocalDeviceInfo &&
                            postAuthLocalDeviceInfoPayloads is { } stagedLocalDeviceInfoPayloads &&
                            stagedPostAuthGetDeviceInfoSequence is { } getDeviceInfoSequence)
                        {
                            var stagedDecision = MiPlayPostAuthProbePolicy.EvaluateStagedLocalDeviceInfoGate(
                                awaitingGetDeviceInfoAcknowledgement: awaitingPostAuthDeviceInfoAckBeforeLocalDeviceInfo,
                                hasLocalDeviceInfoPayloads: true,
                                alreadySentLocalDeviceInfo: sentPostAuthLocalDeviceInfoFrames,
                                observedCommand: followUp.Command,
                                observedSequence: followUp.Sequence,
                                expectedGetDeviceInfoSequence: getDeviceInfoSequence,
                                decryptedPayloadLength: plaintext.Length);
                            if (stagedDecision.CanSend)
                            {
                                await SendPostAuthLocalDeviceInfoFramesAsync(
                                    stagedLocalDeviceInfoPayloads,
                                    postAuthCandidate,
                                    getDeviceInfoSequence);
                            }
                            else
                            {
                                Console.WriteLine($"Refused staged local device info send: {stagedDecision.Reason} Observed command=0x{followUp.Command:X4}, sequence=0x{followUp.Sequence:X4}, pending 0x{MiPlayProtocolConstants.GetDeviceInfoCommand:X4} sequence=0x{getDeviceInfoSequence:X4}, decryptedPayloadLength={plaintext.Length}. No response or control data will be sent.");
                            }
                        }
                    }
                    else if (followUp.Command == MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand)
                    {
                        Console.WriteLine($"Verified native post-auth setLocalDeviceInfo acknowledgement command=0x{MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand:X4}, sequence=0x{followUp.Sequence:X4}, candidate={postAuthCandidate.Label}, decryptedPayloadLength={plaintext.Length}, plaintextHex={Convert.ToHexString(plaintext)}, plaintextUtf8Preview={JsonSerializer.Serialize(utf8Preview)}. Native static evidence maps this command to CMD_SESSION_INFO_SET_DEVICEINFO_ACK; the probe sends no response.");
                    }
                    else if (followUp.Command == MiPlayProtocolConstants.AddMirrorAcknowledgementCommand)
                    {
                        Console.WriteLine($"Verified post-auth Cmd_AddMirror acknowledgement command=0x{MiPlayProtocolConstants.AddMirrorAcknowledgementCommand:X4}, sequence=0x{followUp.Sequence:X4}, candidate={postAuthCandidate.Label}, decryptedPayloadLength={plaintext.Length}, plaintextHex={Convert.ToHexString(plaintext)}, plaintextUtf8Preview={JsonSerializer.Serialize(utf8Preview)}. The AddMirror-only probe sends no Cmd_Open, 0x0058, RTSP, media, audio, playback, retry, or follow-up control data.");
                    }
                    else if (followUp.Command == MiPlayProtocolConstants.SetPlaySourceAcknowledgementCommand)
                    {
                        var setPlaySourceBoundary = sentPostAuthBoundaryDescription ?? "post-auth Cmd_SetPlaySource probe";
                        Console.WriteLine($"Verified post-auth Cmd_SetPlaySource acknowledgement command=0x{MiPlayProtocolConstants.SetPlaySourceAcknowledgementCommand:X4}, sequence=0x{followUp.Sequence:X4}, candidate={postAuthCandidate.Label}, decryptedPayloadLength={plaintext.Length}, plaintextHex={Convert.ToHexString(plaintext)}, plaintextUtf8Preview={JsonSerializer.Serialize(utf8Preview)}. The {setPlaySourceBoundary} sends no follow-up JSON source identity, Cmd_Open, 0x0058, AddMirror, RTSP, media, audio, playback, retry, or control data.");
                    }
                    else if (sentPostAuthCommand == MiPlayProtocolConstants.HeartbeatCommand &&
                        followUp.Command == MiPlayProtocolConstants.HeartbeatAcknowledgementCommand &&
                        plaintext.Length == 0)
                    {
                        Console.WriteLine($"Verified post-auth command=0x{MiPlayProtocolConstants.HeartbeatAcknowledgementCommand:X4}, sequence=0x{followUp.Sequence:X4}, candidate={postAuthCandidate.Label}, decryptedPayloadLength=0. No response or control data will be sent.");
                    }
                    else
                    {
                        Console.WriteLine($"Decoded post-auth SafetyData command=0x{followUp.Command:X4}, sequence=0x{followUp.Sequence:X4}, candidate={postAuthCandidate.Label}, decryptedPayloadLength={plaintext.Length}, plaintextHex={Convert.ToHexString(plaintext)}, plaintextUtf8Preview={JsonSerializer.Serialize(utf8Preview)}. No response or control data will be sent.");
                    }
                }
                else
                {
                    var failure = MiPlaySafetyDataDiagnostics.DescribeVersion1DecodeFailure(followUp.Payload);
                    if (officialPostAuthSequenceSteps is not null &&
                        (awaitingOfficialPostAuthDeviceInfoAck || awaitingOfficialPostAuthMirrorModeAck))
                    {
                        Console.WriteLine($"Official post-auth sequence stopped because SafetyData decrypt did not succeed while waiting for a required ACK ({failure}); no retry, fallback, 0x0040, Open, AddMirror, RTSP, media, playback, audio, or other control data will be sent.");
                        return;
                    }

                    if (awaitingPostAuthReadOnlyGetDeviceInfoAck)
                    {
                        Console.WriteLine($"Read-only post-auth Cmd_GetDeviceInfo validation stopped because SafetyData decrypt did not succeed ({failure}); no retry, fallback, 0x0040, 0x0058, Cmd_Open, AddMirror, RTSP, media, playback, audio, or other control data will be sent.");
                        return;
                    }

                    Console.WriteLine($"Post-auth observe mode: frame logged, but SafetyData decrypt did not succeed ({failure}); no response or control data will be sent.");
                }

                continue;
            }

            if (followUp.Command == MiPlayProtocolConstants.SafetyInfoAcknowledgementCommand)
            {
                if (!MiPlaySafetyCommandCodec.TryDecode(followUpBytes, out var safetyCommand, out _) ||
                    safetyCommand is null ||
                    !MiPlaySafetyInfoCodec.TryDecodeAcknowledgement(safetyCommand.JsonPayload, out var safetyAcknowledgement) ||
                    safetyAcknowledgement is null)
                {
                    Console.WriteLine("Refused SafetyInfo progression: the 0x1401 acknowledgement is not a verified OPack/JSON selection.");
                    if (decryptSafetyAuth)
                    {
                        return;
                    }

                    continue;
                }

                var selection = safetyAcknowledgement.Selection;
                Console.WriteLine($"Decoded 0x1401: result={safetyAcknowledgement.Result}, authKey={selection.AuthKeyType}, authAlgorithm={selection.AuthAlgorithmType}, integrity={selection.IntegrityType}, aesKey={selection.AesKeyType}, aesIv={selection.AesIvType}.");
                if (decryptSafetyAuth)
                {
                    if (!MiPlaySafetyInfoCodec.TryDecodeSelection(safetyCommand.JsonPayload, out var acceptedSelection) ||
                        acceptedSelection is null ||
                        !IsObservedS12SafetySelection(acceptedSelection))
                    {
                        Console.WriteLine("Refused SafetyAuth decryption: 0x1401 did not contain the statically verified S12 selection.");
                        return;
                    }

                    if (connectedTcpSession is null)
                    {
                        throw new InvalidOperationException("The type-1 SafetyAuth endpoint material was not captured.");
                    }

                    safetyAuthAlgorithm = (MiPlaySafetyHashAlgorithm)acceptedSelection.AuthAlgorithmType.GetValueOrDefault();
                    safetyAesCandidates = BuildS12InboundSafetyAesCandidates(connectedTcpSession, acceptedSelection);
                    if (safetyAesCandidates.Count == 0)
                    {
                        Console.WriteLine("Refused SafetyAuth decryption: no AES material candidate could be derived from the verified S12 selection.");
                        return;
                    }

                    if (sendLocalSafetyAuthChallenge)
                    {
                        var verifiedCandidateIndex = safetyAesCandidates.FindIndex(candidate =>
                            string.Equals(candidate.Label, "peer-first:observed-s12-inbound-iv-type1", StringComparison.Ordinal));
                        if (verifiedCandidateIndex < 0)
                        {
                            Console.WriteLine("Refused mutual SafetyAuth: the verified S12 observed inbound IV candidate was not available.");
                            return;
                        }

                        selectedSafetyAuthCandidate = safetyAesCandidates[verifiedCandidateIndex];
                        safetyAesCandidates =
                        [
                            selectedSafetyAuthCandidate.Value
                        ];
                        localSafetyAuthChallenge = MiPlaySafetyAuthCodec.CreateChallenge(GetUnixTimestampMicroseconds());
                        var localSafetyAuthPlaintext = MiPlaySafetyEnvelopeCodec.Encode(
                            isAcknowledgement: false,
                            MiPlayProtocolConstants.SafetyValueType,
                            localSafetyAuthChallenge.ToJsonPayload());
                        localSafetyAuthPlaintextForPostAuthDryRun = [.. localSafetyAuthPlaintext];
                        var localSafetyAuthData = selectedSafetyAuthCandidate.Value.Cipher.EncryptVersion1(localSafetyAuthPlaintext);
                        var localSafetyAuthSequence = sendNativeBootstrap ? (ushort)3 : (ushort)2;
                        var localSafetyAuthFrame = MiPlayCommandFrameCodec.Encode(
                            MiPlayProtocolConstants.SafetyAuthCommand,
                            localSafetyAuthSequence,
                            localSafetyAuthData);
                        using var localSafetyAuthSendTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                        await stream.WriteAsync(localSafetyAuthFrame, localSafetyAuthSendTimeout.Token);
                        await stream.FlushAsync(localSafetyAuthSendTimeout.Token);
                        sentLocalSafetyAuthChallenge = true;
                        Console.WriteLine($"Sent command=0x{MiPlayProtocolConstants.SafetyAuthCommand:X4}, sequence=0x{localSafetyAuthSequence:X4}, encryptedPayloadLength={localSafetyAuthData.Length}, candidate={selectedSafetyAuthCandidate.Value.Label}, localAuthMsgLength={localSafetyAuthChallenge.AuthMessage.Length}. The probe will only answer one peer 0x1402 and verify one peer 0x1403; no media, RTSP, audio, playback, or other control data will be sent.");
                    }
                    else
                    {
                        var safetyAuthMode = sendSafetyAuthAcknowledgement
                            ? "If exactly one candidate decodes cmd/authMsg, the probe will send one encrypted 0x1403 acknowledgement and then only observe."
                            : "The probe will not send 0x1403.";
                        Console.WriteLine($"Accepted verified S12 SafetyInfo selection; ready to decrypt one 0x1402 challenge with candidates={string.Join(',', safetyAesCandidates.Select(candidate => candidate.Label))}. {safetyAuthMode}");
                    }
                }

                continue;
            }

            if (decryptSafetyAuth && followUp.Command == MiPlayProtocolConstants.SafetyAuthCommand)
            {
                if (sentSafetyAuthAcknowledgement)
                {
                    Console.WriteLine("Observed an additional 0x1402 after the authentication acknowledgement; the probe will not send another 0x1403.");
                    continue;
                }

                if (safetyAesCandidates is null || safetyAesCandidates.Count == 0)
                {
                    Console.WriteLine("Refused SafetyAuth decryption: no verified 0x1401 selection was observed first.");
                    return;
                }

                MiPlaySafetyDataDecodeResult? matchedSafetyData = null;
                MiPlaySafetyEnvelope? matchedEnvelope = null;
                MiPlaySafetyAuthChallenge? matchedChallenge = null;
                string? matchedCandidateLabel = null;
                (string Label, string AuthKey, MiPlaySafetyDataSessionCipher Cipher)? matchedCandidate = null;
                var decryptedCandidateLabels = new List<string>();
                var decodedButInvalidCandidateDescriptions = new List<string>();
                foreach (var candidate in safetyAesCandidates)
                {
                    if (!candidate.Cipher.TryDecryptVersion1(followUp.Payload, out var candidateSafetyData) ||
                        candidateSafetyData is null)
                    {
                        continue;
                    }

                    decryptedCandidateLabels.Add(candidate.Label);
                    if (!MiPlaySafetyEnvelopeCodec.TryDecode(
                            candidateSafetyData.Plaintext,
                            out var candidateEnvelope,
                            out var envelopeBytesConsumed) ||
                        candidateEnvelope is null ||
                        envelopeBytesConsumed != candidateSafetyData.Plaintext.Length ||
                        candidateEnvelope.IsAcknowledgement ||
                        !MiPlaySafetyAuthCodec.TryDecodeChallenge(candidateEnvelope.Payload, out var candidateChallenge) ||
                        candidateChallenge is null)
                    {
                        decodedButInvalidCandidateDescriptions.Add($"{candidate.Label}:{DescribeOpackPrefix(candidateSafetyData.Plaintext)}");
                        continue;
                    }

                    if (matchedSafetyData is not null)
                    {
                        Console.WriteLine($"Refused SafetyAuth progression: multiple AES candidates decoded a cmd/authMsg challenge ({matchedCandidateLabel},{candidate.Label}).");
                        return;
                    }

                    matchedSafetyData = candidateSafetyData;
                    matchedEnvelope = candidateEnvelope;
                    matchedChallenge = candidateChallenge;
                    matchedCandidateLabel = candidate.Label;
                    matchedCandidate = candidate;
                }

                if (matchedSafetyData is null || matchedEnvelope is null || matchedChallenge is null || matchedCandidateLabel is null || matchedCandidate is null)
                {
                    var decryptedLabels = decryptedCandidateLabels.Count == 0
                        ? "none"
                        : string.Join(',', decryptedCandidateLabels);
                    var decodedButInvalid = decodedButInvalidCandidateDescriptions.Count == 0
                        ? "none"
                        : string.Join(" | ", decodedButInvalidCandidateDescriptions);
                    Console.WriteLine($"Refused SafetyAuth progression: no AES candidate decoded a complete cmd/authMsg 0x1402 challenge; decryptedCandidates={decryptedLabels}; decodedButInvalid={decodedButInvalid}; tried={string.Join(',', safetyAesCandidates.Select(candidate => candidate.Label))}.");
                    return;
                }

                Console.WriteLine($"Decoded 0x1402 OPack with {matchedCandidateLabel}: tag={(matchedEnvelope.IsAcknowledgement ? "ack" : "cmd")}, valueType={matchedEnvelope.ValueType}, jsonLength={matchedEnvelope.Payload.Length}, fields={DescribeJsonObjectFields(matchedEnvelope.Payload)}.");
                if (!sendSafetyAuthAcknowledgement)
                {
                    Console.WriteLine($"Decoded verified 0x1402 challenge, authMsgLength={matchedChallenge.AuthMessage.Length}. Read-only probe completed without sending 0x1403, media, or playback data.");
                    return;
                }

                if (safetyAuthAlgorithm is null)
                {
                    Console.WriteLine("Refused SafetyAuth acknowledgement: no verified HMAC algorithm was captured from 0x1401.");
                    return;
                }

                var safetyAuthAcknowledgement = MiPlaySafetyAuthCodec.CreateAcknowledgement(
                    matchedChallenge.AuthMessage,
                    matchedCandidate.Value.AuthKey,
                    safetyAuthAlgorithm.Value);
                var safetyAuthAcknowledgementPlaintext = MiPlaySafetyEnvelopeCodec.Encode(
                    isAcknowledgement: true,
                    MiPlayProtocolConstants.SafetyValueType,
                    safetyAuthAcknowledgement.ToJsonPayload());
                localSafetyAuthAcknowledgementPlaintextForPostAuthDryRun = [.. safetyAuthAcknowledgementPlaintext];
                var safetyAuthAcknowledgementData = matchedCandidate.Value.Cipher.EncryptVersion1(
                    safetyAuthAcknowledgementPlaintext);
                var safetyAuthAcknowledgementFrame = MiPlayCommandFrameCodec.Encode(
                    MiPlayProtocolConstants.SafetyAuthAcknowledgementCommand,
                    followUp.Sequence,
                    safetyAuthAcknowledgementData);

                using var safetyAuthSendTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await stream.WriteAsync(safetyAuthAcknowledgementFrame, safetyAuthSendTimeout.Token);
                await stream.FlushAsync(safetyAuthSendTimeout.Token);
                sentSafetyAuthAcknowledgement = true;
                Console.WriteLine($"Sent command=0x{MiPlayProtocolConstants.SafetyAuthAcknowledgementCommand:X4}, sequence=0x{followUp.Sequence:X4}, encryptedPayloadLength={safetyAuthAcknowledgementData.Length}, candidate={matchedCandidateLabel}. The probe will now only observe for authentication frames; no media, RTSP, audio, playback, or other control data will be sent.");
                if (await CompleteMutualSafetyAuthAsync())
                {
                    return;
                }

                continue;
            }

            if (decryptSafetyAuth && sendLocalSafetyAuthChallenge && followUp.Command == MiPlayProtocolConstants.SafetyAuthAcknowledgementCommand)
            {
                if (!sentLocalSafetyAuthChallenge || localSafetyAuthChallenge is null || selectedSafetyAuthCandidate is null)
                {
                    Console.WriteLine("Refused peer SafetyAuth acknowledgement: no local 0x1402 challenge is pending.");
                    return;
                }

                if (safetyAuthAlgorithm is null)
                {
                    Console.WriteLine("Refused peer SafetyAuth acknowledgement: no verified HMAC algorithm was captured from 0x1401.");
                    return;
                }

                if (!selectedSafetyAuthCandidate.Value.Cipher.TryDecryptVersion1(followUp.Payload, out var peerAcknowledgementSafetyData) ||
                    peerAcknowledgementSafetyData is null ||
                    !MiPlaySafetyEnvelopeCodec.TryDecode(
                        peerAcknowledgementSafetyData.Plaintext,
                        out var peerAcknowledgementEnvelope,
                        out var peerAcknowledgementBytesConsumed) ||
                    peerAcknowledgementEnvelope is null ||
                    peerAcknowledgementBytesConsumed != peerAcknowledgementSafetyData.Plaintext.Length ||
                    !peerAcknowledgementEnvelope.IsAcknowledgement ||
                    !MiPlaySafetyAuthCodec.TryDecodeAcknowledgement(peerAcknowledgementEnvelope.Payload, out var peerAcknowledgement) ||
                    peerAcknowledgement is null)
                {
                    Console.WriteLine($"Refused peer SafetyAuth acknowledgement: encrypted 0x1403 did not decode as a complete ack/authMsgAck with candidate={selectedSafetyAuthCandidate.Value.Label}.");
                    return;
                }

                if (!MiPlaySafetyAuthCodec.VerifyAcknowledgement(
                        localSafetyAuthChallenge.AuthMessage,
                        selectedSafetyAuthCandidate.Value.AuthKey,
                        safetyAuthAlgorithm.Value,
                        peerAcknowledgement))
                {
                    Console.WriteLine($"Refused peer SafetyAuth acknowledgement: HMAC verification failed for candidate={selectedSafetyAuthCandidate.Value.Label}.");
                    return;
                }

                verifiedPeerSafetyAuthAcknowledgement = true;
                Console.WriteLine($"Verified peer command=0x{MiPlayProtocolConstants.SafetyAuthAcknowledgementCommand:X4}, sequence=0x{followUp.Sequence:X4}, candidate={selectedSafetyAuthCandidate.Value.Label}. This satisfies the native 0x1403 -> DealSafetyDone success precondition locally; no media, RTSP, audio, playback, or other control data will be sent.");
                if (await CompleteMutualSafetyAuthAsync())
                {
                    return;
                }

                continue;
            }

            if (MiPlayNativeVersionCodec.TryDecodeAcknowledgement(
                    followUpBytes,
                    out var versionSequence,
                    out var deviceVersion) &&
                deviceVersion is not null)
            {
                Console.WriteLine($"Decoded 0x0037, sequence=0x{versionSequence:X4}, device version={deviceVersion}.");
            }
        }
    }
    catch (OperationCanceledException)
    {
        var safetyAuthBoundary = completedMutualSafetyAuth && sentPostAuthBoundaryDescription is { } canceledPostAuthBoundary
            ? (observingLegacyClearSetPlaySourceAck || observingLegacyClearGetDeviceInfo)
                ? $"after legacy clear validation and {canceledPostAuthBoundary}; no further data was sent"
                : $"after mutual SafetyAuth completion and {canceledPostAuthBoundary}; no further data was sent"
            : completedMutualSafetyAuth
            ? (observingLegacyClearSetPlaySourceAck || observingLegacyClearGetDeviceInfo)
                ? "after legacy clear validation; no modern SafetyAuth, media, RTSP, audio, playback, openDevice, or other control data was sent"
                : "after mutual SafetyAuth completion; no post-auth heartbeat, getDeviceInfo, setLocalDeviceInfo, media, RTSP, audio, playback, openDevice, or other control data was sent"
            : sentSafetyAuthAcknowledgement
            ? "after sending one 0x1403 acknowledgement; no further data was sent"
            : "without sending 0x1403, media, or playback data";
        var observedSeconds = completedMutualSafetyAuth ? postAuthObserveTimeout.TotalSeconds : 5;
        Console.WriteLine($"Observation ended after {observedSeconds:0} seconds with {observedFrames} follow-up frame(s), {safetyAuthBoundary}.");
    }
    catch (EndOfStreamException)
    {
        var safetyAuthBoundary = completedMutualSafetyAuth && sentPostAuthBoundaryDescription is { } closedPostAuthBoundary
            ? (observingLegacyClearSetPlaySourceAck || observingLegacyClearGetDeviceInfo)
                ? $"after legacy clear validation and {closedPostAuthBoundary}; no further data was sent"
                : $"after mutual SafetyAuth completion and {closedPostAuthBoundary}; no further data was sent"
            : completedMutualSafetyAuth
            ? (observingLegacyClearSetPlaySourceAck || observingLegacyClearGetDeviceInfo)
                ? "after legacy clear validation; no modern SafetyAuth or post-auth data was sent"
                : "after mutual SafetyAuth completion; no post-auth data was sent"
            : sentSafetyAuthAcknowledgement
            ? "after one 0x1403 acknowledgement"
            : "without sending 0x1403";
        Console.WriteLine($"The device closed the connection after {observedFrames} follow-up frame(s), {safetyAuthBoundary}. The probe sends no further data.");
    }
    catch (IOException exception) when (exception.InnerException is SocketException socketException)
    {
        var safetyAuthBoundary = completedMutualSafetyAuth && sentPostAuthBoundaryDescription is { } abortedPostAuthBoundary
            ? (observingLegacyClearSetPlaySourceAck || observingLegacyClearGetDeviceInfo)
                ? $"after legacy clear validation and {abortedPostAuthBoundary}; no further data was sent"
                : $"after mutual SafetyAuth completion and {abortedPostAuthBoundary}; no further data was sent"
            : completedMutualSafetyAuth
            ? (observingLegacyClearSetPlaySourceAck || observingLegacyClearGetDeviceInfo)
                ? "after legacy clear validation; no modern SafetyAuth or post-auth data was sent"
                : "after mutual SafetyAuth completion; no post-auth data was sent"
            : sentSafetyAuthAcknowledgement
            ? "after one 0x1403 acknowledgement"
            : "without sending 0x1403";
        Console.WriteLine($"The TCP connection was aborted while reading after {observedFrames} follow-up frame(s), socketError={socketException.SocketErrorCode}, nativeError={socketException.NativeErrorCode}, message={JsonSerializer.Serialize(exception.Message)}, {safetyAuthBoundary}. The probe sends no further data.");
    }
    finally
    {
        if (noMediaRtspFirstRequestTask is not null)
        {
            try
            {
                var observation = noMediaRtspFirstRequestTask.IsCompleted
                    ? await noMediaRtspFirstRequestTask
                    : await noMediaRtspFirstRequestTask.WaitAsync(TimeSpan.FromMilliseconds(750));
                Console.WriteLine(observation);
            }
            catch (TimeoutException)
            {
                Console.WriteLine("No-media RTSP/WFD listener did not observe a receiver callback before the control observation ended. No RTSP response, media, RTP, playback, or audio data was sent.");
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("No-media RTSP/WFD listener timed out without a receiver callback. No RTSP response, media, RTP, playback, or audio data was sent.");
            }
            catch (IOException exception)
            {
                Console.WriteLine($"No-media RTSP/WFD listener ended with IO error before a complete first request: {JsonSerializer.Serialize(exception.Message)}. No RTSP response, media, RTP, playback, or audio data was sent.");
            }
            catch (SocketException exception)
            {
                Console.WriteLine($"No-media RTSP/WFD listener ended with socketError={exception.SocketErrorCode}, nativeError={exception.NativeErrorCode}. No RTSP response, media, RTP, playback, or audio data was sent.");
            }
            catch (ObjectDisposedException)
            {
                Console.WriteLine("No-media RTSP/WFD listener was disposed before a complete receiver callback was captured. No RTSP response, media, RTP, playback, or audio data was sent.");
            }
        }

        noMediaRtspCaptureTimeout?.Cancel();
        noMediaRtspListener?.Stop();
        noMediaRtspCaptureTimeout?.Dispose();
    }
}

static (byte[] SourceNamePayload, byte[] LocalDeviceInfoPayload, string SourceNameJson, string LocalDeviceInfoJson) CreatePostAuthLocalDeviceInfoPayloads(string[] args)
{
    var sourceName = GetOptionValue(args, "--miplay-local-source-name=") ?? "DLNACast Windows";
    if (string.IsNullOrWhiteSpace(sourceName))
    {
        throw new ArgumentException("--miplay-local-source-name must not be empty for the 0x0058 local device info probe.");
    }

    var bluetoothMac = GetOptionValue(args, "--miplay-local-bluetooth-mac=");
    var canAlonePlayCtrl = GetOptionValue(args, "--miplay-local-can-alone-play-ctrl=") ?? "0";
    var model = GetOptionValue(args, "--miplay-local-model=") ?? "Windows";
    var romVersion = GetOptionValue(args, "--miplay-local-rom-version=") ?? Environment.OSVersion.VersionString;
    var appVersionText = GetOptionValue(args, "--miplay-local-app-version=");
    var appVersion = 1;
    if (appVersionText is not null &&
        (!int.TryParse(appVersionText, out appVersion) || appVersion < 0))
    {
        throw new ArgumentException("--miplay-local-app-version must be a non-negative integer.");
    }

    var sourceNamePayload = MiPlayLocalDeviceInfoPayloadCodec.EncodeSourceName(
        sourceName,
        bluetoothMac,
        canAlonePlayCtrl);
    var localDeviceInfoPayload = MiPlayLocalDeviceInfoPayloadCodec.EncodeLocalDeviceInfo(
        model,
        romVersion,
        appVersion);
    return (
        sourceNamePayload,
        localDeviceInfoPayload,
        Encoding.UTF8.GetString(sourceNamePayload),
        Encoding.UTF8.GetString(localDeviceInfoPayload));
}

static int ParsePortOption(IEnumerable<string> args, string prefix, int defaultValue)
{
    var text = GetOptionValue(args, prefix);
    if (text is null)
    {
        return defaultValue;
    }

    if (!int.TryParse(text, out var value) || value is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
    {
        throw new ArgumentException($"{prefix.TrimEnd('=')} must be a TCP port from {IPEndPoint.MinPort} to {IPEndPoint.MaxPort}.");
    }

    return value;
}

static int ParseNonNegativeIntOption(IEnumerable<string> args, string prefix, int defaultValue)
{
    var text = GetOptionValue(args, prefix);
    if (text is null)
    {
        return defaultValue;
    }

    if (!int.TryParse(text, out var value) || value < 0)
    {
        throw new ArgumentException($"{prefix.TrimEnd('=')} must be a non-negative integer.");
    }

    return value;
}

static string? GetOptionValue(IEnumerable<string> args, string prefix)
{
    var argument = args.FirstOrDefault(value =>
        value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    return argument?[prefix.Length..];
}

static long GetUnixTimestampMicroseconds()
{
    var milliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    return checked(milliseconds * 1000);
}

static List<(string Label, string AuthKey, MiPlaySafetyDataSessionCipher Cipher)> BuildS12InboundSafetyAesCandidates(
    MiPlayTcpSessionInfo session,
    MiPlaySafetyInfoSelection selection)
{
    var candidates = new List<(string Label, string AuthKey, MiPlaySafetyDataSessionCipher Cipher)>();
    AddS12InboundSafetyAesCandidates(
        candidates,
        "peer-first",
        session.DeriveType1SafetyKey(),
        selection);
    AddS12InboundSafetyAesCandidates(
        candidates,
        "diagnostic-local-first",
        MiPlaySafetyKeyDerivation.DeriveType1(
            session.LocalAddress.ToString(),
            session.LocalPort,
            session.PeerAddress.ToString(),
            session.PeerPort),
        selection);
    return candidates;
}

static void AddS12InboundSafetyAesCandidates(
    List<(string Label, string AuthKey, MiPlaySafetyDataSessionCipher Cipher)> candidates,
    string endpointOrderLabel,
    string authKey,
    MiPlaySafetyInfoSelection selection)
{
    if (selection.AesKeyType is not { } aesKeyType ||
        selection.AesIvType is not { } aesIvType)
    {
        return;
    }

    byte[] aesKey;
    try
    {
        aesKey = Encoding.ASCII.GetBytes(MiPlaySafetyKeyDerivation.SelectDerivedAesMaterial(
            authKey,
            aesKeyType));
    }
    catch (ArgumentOutOfRangeException)
    {
        return;
    }

    try
    {
        var nativeAesIv = Encoding.ASCII.GetBytes(MiPlaySafetyKeyDerivation.SelectDerivedAesMaterial(
            authKey,
            aesIvType));
        candidates.Add(($"{endpointOrderLabel}:native-iv-type{aesIvType}", authKey, new MiPlaySafetyDataSessionCipher(aesKey, nativeAesIv)));
    }
    catch (ArgumentOutOfRangeException)
    {
    }

    if (aesKeyType == MiPlaySafetyKeyDerivation.FirstHalfMaterialType &&
        aesIvType == MiPlaySafetyKeyDerivation.SecondHalfMaterialType)
    {
        var observedInboundAesIv = Encoding.ASCII.GetBytes(MiPlaySafetyKeyDerivation.SelectObservedS12InboundSafetyIvMaterial(
            authKey,
            aesKeyType,
            aesIvType));
        candidates.Add(($"{endpointOrderLabel}:observed-s12-inbound-iv-type1", authKey, new MiPlaySafetyDataSessionCipher(aesKey, observedInboundAesIv)));
    }
}

static bool IsObservedS12SafetySelection(MiPlaySafetyInfoSelection selection) =>
    selection.AuthKeyType == 1 &&
    selection.AuthAlgorithmType == 4 &&
    selection.IntegrityType == 1 &&
    selection.AesKeyType == 1 &&
    selection.AesIvType == 2;

static string DescribeOpackPrefix(ReadOnlySpan<byte> data)
{
    if (data.IsEmpty)
    {
        return "plaintextLength=0";
    }

    var tagLength = data[0];
    if (tagLength == 0 || data.Length < 1 + tagLength + 1)
    {
        return $"plaintextLength={data.Length}, tagLength={tagLength}";
    }

    var tag = Encoding.ASCII.GetString(data.Slice(1, tagLength));
    var valueTypeOffset = 1 + tagLength;
    if (data[valueTypeOffset] != MiPlayProtocolConstants.SafetyValueType ||
        data.Length < valueTypeOffset + 1 + sizeof(uint))
    {
        return $"plaintextLength={data.Length}, tag={tag}, valueType={data[valueTypeOffset]}";
    }

    var payloadLength = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(valueTypeOffset + 1, sizeof(uint)));
    return $"plaintextLength={data.Length}, tag={tag}, valueType={data[valueTypeOffset]}, declaredPayloadLength={payloadLength}";
}

static string DescribeNotifyPayload(MiPlayNotifyPayload notify)
{
    if (notify.IntegerValue is { } integerValue)
    {
        return $"label={notify.Label}, valueType=0x{notify.ValueType:X2}, integerValue={integerValue}";
    }

    var fields = string.Join(',', notify.Fields.Select(DescribeNotifyField));
    return $"label={notify.Label}, valueType=0x{notify.ValueType:X2}, declaredPayloadLength={notify.DeclaredPayloadLength}, fields=[{fields}]";
}

static string DescribeNotifyField(MiPlayNotifyField field)
{
    if (field.StringValue is { } stringValue)
    {
        return $"{field.Name}:0x{field.ValueType:X2}={JsonSerializer.Serialize(stringValue)}";
    }

    if (field.IntegerValue is { } integerValue)
    {
        return $"{field.Name}:0x{field.ValueType:X2}={integerValue}";
    }

    return $"{field.Name}:0x{field.ValueType:X2}";
}

static string DescribeJsonObjectFields(ReadOnlySpan<byte> payload)
{
    try
    {
        using var document = JsonDocument.Parse(payload.ToArray());
        return document.RootElement.ValueKind == JsonValueKind.Object
            ? string.Join(',', document.RootElement.EnumerateObject().Select(property => property.Name))
            : document.RootElement.ValueKind.ToString();
    }
    catch (JsonException)
    {
        return "non-json";
    }
}

static async Task RunPassiveMiPlaySenderCaptureAsync(
    MiPlayPassiveSenderCaptureProfile profile,
    TimeSpan duration,
    CancellationToken cancellationToken)
{
    ArgumentNullException.ThrowIfNull(profile);

    var announcement = profile.BuildMdnsAnnouncement();
    var advertisedDevice = MiPlayMdnsMessageParser.Parse(announcement).Single();
    var challengeFrame = profile.BuildLegacyChallengeFrame();
    if (!MiPlayCommandFrameCodec.TryDecode(challengeFrame, out var challenge, out var challengeBytesConsumed) ||
        challenge is null ||
        challengeBytesConsumed != challengeFrame.Length ||
        !MiPlayPassiveSenderCaptureProfile.IsPermittedOutboundCommand(challenge.Command))
    {
        throw new InvalidOperationException(
            "The passive sender capture profile refused its own outbound challenge.");
    }

    using var udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
    {
        ExclusiveAddressUse = false,
    };
    udp.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
    udp.SetSocketOption(
        SocketOptionLevel.IP,
        SocketOptionName.AddMembership,
        new MulticastOption(IPAddress.Parse("224.0.0.251"), profile.Address));
    udp.SetSocketOption(
        SocketOptionLevel.IP,
        SocketOptionName.MulticastInterface,
        profile.Address.GetAddressBytes());
    udp.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
    udp.Bind(new IPEndPoint(IPAddress.Any, MiPlayPassiveSenderCaptureProfile.MdnsPort));

    var listener = new TcpListener(profile.Address, MiPlayProtocolConstants.DefaultControlPort);
    listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
    listener.Start(backlog: 1);

    using var run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    var mdnsTask = AdvertisePassiveMiPlayCaptureAsync(udp, announcement, run.Token);
    Console.WriteLine(
        $"Passive MiPlay sender capture ready for {duration.TotalSeconds:0}s: " +
        $"name={JsonSerializer.Serialize(advertisedDevice.FriendlyName)}, " +
        $"id={profile.DeviceId:D}, address={profile.Address}, " +
        $"mDNS={MiPlayPassiveSenderCaptureProfile.MdnsPort}/UDP, " +
        $"command={MiPlayProtocolConstants.DefaultControlPort}/TCP.");
    Console.WriteLine(
        "Outbound boundary: exactly one legacy 0x0028 pre-auth challenge after the first phone connection; " +
        "no 0x0037, 0x1401, 0x1402, business command, RTSP, media, playback, or audio frame will be sent.");

    try
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        client.NoDelay = true;
        var remoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        var localEndPoint = client.Client.LocalEndPoint?.ToString() ?? "unknown";
        Console.WriteLine($"Phone sender connected: remote={remoteEndPoint}, local={localEndPoint}.");

        await using var stream = client.GetStream();
        await stream.WriteAsync(challengeFrame, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        Console.WriteLine(
            $"Outbound command=0x{challenge.Command:X4}, sequence=0x{challenge.Sequence:X4}, " +
            $"payloadLength={challenge.Payload.Length}, challenge={JsonSerializer.Serialize(MiPlayPassiveSenderCaptureProfile.ChallengeText)}.");

        while (!cancellationToken.IsCancellationRequested)
        {
            byte[] frameBytes;
            try
            {
                frameBytes = await ReadMiPlayFrameAsync(stream, cancellationToken);
            }
            catch (EndOfStreamException)
            {
                Console.WriteLine("Phone sender closed the TCP command connection.");
                break;
            }

            if (!MiPlayCommandFrameCodec.TryDecode(
                    frameBytes,
                    out var frame,
                    out var frameBytesConsumed) ||
                frame is null ||
                frameBytesConsumed != frameBytes.Length)
            {
                throw new InvalidDataException(
                    "A captured phone frame failed strict MiPlay command decoding.");
            }

            LogPassiveMiPlaySenderFrame(frameBytes, frame);
        }
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        Console.WriteLine("Passive MiPlay sender capture window ended.");
    }
    finally
    {
        run.Cancel();
        listener.Stop();
        try
        {
            await mdnsTask;
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested)
        {
        }
    }
}

static void PrintFreshLegacyDeviceInfoDryRun()
{
    var decision = MiPlayFreshLegacyReceiverBootstrapPlanner.EvaluateCurrentEvidence();
    var plan = decision.Plan;
    Console.WriteLine(
        $"Fresh legacy device-info dry-run: canBuild={decision.CanBuildDeterministicGetDeviceInfoAcknowledgement}, " +
        $"canSendNow={decision.CanSendNow}, safeForNetworkUse={plan.SafeForNetworkUse}, " +
        $"requestSequence=0x{plan.GetDeviceInfoRequestSequence:X4}, " +
        $"responseCommand=0x{MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand:X4}, " +
        $"payloadLength={plan.DeviceInfoPayload.Length}, frameLength={plan.GetDeviceInfoAcknowledgementFrame.Length}, " +
        $"fieldCount={plan.DeviceInfoProfile.ToOrderedFields().Count}, " +
        $"frameSha256={plan.GetDeviceInfoAcknowledgementFrameSha256}.");
    Console.WriteLine(
        "Dry-run only: no mDNS, TCP, 0x0028, 0x001f, 0x0037, 0x0059, 0x001b, Open, AddMirror, RTSP, media, playback, or audio frame was sent.");
}

static void PrintFreshLegacyPostDeviceInfoObservationDryRun()
{
    var decision = MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.Evaluate(
        MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.CreateCurrentSnapshot());
    var plan = decision.Plan;
    Console.WriteLine(
        $"Fresh legacy post-device-info observation dry-run: canPredict={decision.CanPredictNextQueuedCommand}, " +
        $"queuedWithout0059={decision.GetMirrorModeWasQueuedWithoutWaitingFor0059}, " +
        $"fifoOrder={decision.PredictedCommandOrderIsFifoPreserved}, " +
        $"canUseNetwork={decision.CanUseNetwork}, observedSetLocalSequence=0x{plan.ObservedSetLocalDeviceInfoSequence:X4}, " +
        $"predictedCommand=0x{MiPlayProtocolConstants.GetMirrorModeCommand:X4}, " +
        $"predictedSequence=0x{plan.PredictedGetMirrorModeSequence:X4}, " +
        $"payloadLength=0, frameLength={plan.PredictedGetMirrorModeFrame.Length}, " +
        $"frameSha256={plan.PredictedGetMirrorModeFrameSha256}.");
    Console.WriteLine(
        "Dry-run only: no mDNS, TCP, 0x0028, 0x001f, 0x0059, 0x0035, heartbeat ACK, Open, AddMirror, RTSP, media, playback, or audio frame was sent.");
}

static MiPlayLegacyStatusQueryOrder ParseLegacyStatusQueryOrder(string[] arguments)
{
    var value = GetOptionValue(arguments, "--miplay-legacy-status-order=");
    return value?.ToLowerInvariant() switch
    {
        null or "volume-media-state" => MiPlayLegacyStatusQueryOrder.VolumeMediaInfoState,
        "volume-state-media" => MiPlayLegacyStatusQueryOrder.VolumeStateMediaInfo,
        _ => throw new ArgumentException(
            "--miplay-legacy-status-order must be volume-media-state or volume-state-media."),
    };
}

static int ParseSystemAudioDurationSeconds(string[] arguments)
{
    var value = GetOptionValue(arguments, "--miplay-system-audio-duration-seconds=");
    if (value is null)
    {
        return 5;
    }
    if (!int.TryParse(value, out var seconds) || seconds is < 5 or > 30)
    {
        throw new ArgumentException(
            "--miplay-system-audio-duration-seconds must be an integer from 5 through 30.");
    }
    return seconds;
}

static void PrintLegacyAudioSourceBootstrapDryRun(MiPlayLegacyStatusQueryOrder statusQueryOrder)
{
    Console.WriteLine(
        $"Legacy audio-source bootstrap dry-run: statusQueryOrder={statusQueryOrder}, " +
        $"maximumWrites={MiPlayLegacyAudioSourceBootstrapProbeGuard.MaximumWrites}, " +
        $"maximumFrames={MiPlayLegacyAudioSourceBootstrapProbeGuard.MaximumFrames}.");
    foreach (var line in MiPlayLegacyAudioSourceBootstrapProbeGuard.CreateDryRunLedger(statusQueryOrder))
    {
        Console.WriteLine($"- {line}");
    }

    Console.WriteLine(
        "Dry-run only: no socket is opened and no LAN, 0x0040, Open, AddMirror, RTSP, media, playback, or audio frame is sent.");
}

static void PrintLegacySilencePlaybackDryRun(MiPlayLegacyStatusQueryOrder statusQueryOrder)
{
    Console.WriteLine(
        $"Legacy silence-playback dry-run: target=<one explicitly supplied IPv4>, statusQueryOrder={statusQueryOrder}.");
    foreach (var line in MiPlayLegacyAudioSourceBootstrapProbeGuard.CreateDryRunLedger(statusQueryOrder))
    {
        Console.WriteLine($"- bootstrap {line}");
    }
    Console.WriteLine("- continuation control sequences: 0x0058/8, 0x001e/9, 0x0058/10, 0x0034/11, 0x001a/12, 0x0040/13, Open/14");
    Console.WriteLine("- reverse infrastructure before Open: TCP/7274, UDP/36524, capacity for exactly three accepted TCP connections");
    Console.WriteLine("- RTSP: captured AAC-only 16-step handshake; timer replies only to exact 40-byte packets from the selected target");
    Console.WriteLine("- post-Open startup: playing SetMediaInfo/15 with mDeviceState=2, then wait for receiver first-audiopcm=1 and automatic state=2; no Pause, Resume, or startup heartbeat");
    Console.WriteLine("- media cap: 48 generated silent AAC access units, one RTP packet each, about 1.024 seconds, no captured or user audio");
    Console.WriteLine("- forbidden: Pause, Resume, AddMirror, 0x0041 wait, Open ACK wait, discovery, retry, fallback, alternate target, or second session");
    Console.WriteLine("Dry-run only: no socket is opened and no LAN packet is sent.");
}

static void PrintLegacyTonePlaybackDryRun(MiPlayLegacyStatusQueryOrder statusQueryOrder)
{
    Console.WriteLine(
        $"Legacy tone-playback dry-run: target=<one explicitly supplied IPv4>, statusQueryOrder={statusQueryOrder}.");
    foreach (var line in MiPlayLegacyAudioSourceBootstrapProbeGuard.CreateDryRunLedger(statusQueryOrder))
    {
        Console.WriteLine($"- bootstrap {line}");
    }
    Console.WriteLine("- continuation control sequences: 0x0058/8, 0x001e/9, 0x0058/10, 0x0034/11, 0x001a/12, 0x0040/13, Open/14");
    Console.WriteLine("- reverse infrastructure: TCP/7274, UDP/36524, exactly three reverse TCP accepts, captured AAC-only RTSP handshake");
    Console.WriteLine("- post-Open startup: playing SetMediaInfo/15 with mDeviceState=2, then wait for receiver first-audiopcm=1 and automatic state=2; no Pause, Resume, or startup heartbeat");
    Console.WriteLine("- local media pipeline: generated 44.1 kHz stereo signed-16 440 Hz PCM -> FFmpeg AAC-LC 48 kHz -> ADTS/TS/RTP");
    Console.WriteLine("- media cap: 96 AAC access units, about 2.048 seconds; amplitude 0.12; no system or captured user audio");
    Console.WriteLine("- forbidden: Pause, Resume, AddMirror, discovery, retry, fallback, alternate target, second session, or media beyond the cap");
    Console.WriteLine("Dry-run only: no FFmpeg process, socket, or LAN packet is created.");
}

static void PrintLegacySystemAudioPlaybackDryRun(MiPlayLegacyStatusQueryOrder statusQueryOrder)
{
    Console.WriteLine(
        $"Legacy system-audio playback dry-run: target=<one explicitly supplied IPv4>, statusQueryOrder={statusQueryOrder}.");
    foreach (var line in MiPlayLegacyAudioSourceBootstrapProbeGuard.CreateDryRunLedger(statusQueryOrder))
    {
        Console.WriteLine($"- bootstrap {line}");
    }
    Console.WriteLine("- continuation control sequences: 0x0058/8, 0x001e/9, 0x0058/10, 0x0034/11, 0x001a/12, 0x0040/13, Open/14");
    Console.WriteLine("- capture: current default Windows multimedia render endpoint through WASAPI loopback, 44.1 kHz stereo signed-16 PCM");
    Console.WriteLine("- optional --miplay-inject-local-test-tone: locally play a 440 Hz amplitude-0.04 signal through that same endpoint for deterministic capture");
    Console.WriteLine("- encoder/media: FFmpeg Media Foundation AAC-LC 48 kHz stereo 256 kbit/s -> ADTS/TS/RTP; duration is 5-30 seconds via --miplay-system-audio-duration-seconds=N");
    Console.WriteLine("- RTP: one AAC timestamp may use one or two packets; same timestamp across fragments, marker only on the final fragment, and fragments are coalesced into one TCP write");
    Console.WriteLine("- startup pacing: clean-phone measured initial burst through access unit 18, then nominal 1024/48000-second cadence");
    Console.WriteLine("- media clock: initial MPEG-TS PCR = captured RTSP TIME_OFFSET minus the observed 1,000,000 us playback delay; RTP/PTS remain zero-based");
    Console.WriteLine("- post-Open startup: Windows SetMediaInfo/15 with status=0 and mDeviceState=2, then wait for receiver first-audiopcm=1 and automatic state=2; no Pause, Resume, or startup heartbeat");
    Console.WriteLine("- steady-state control: empty 0x001a heartbeats from sequence 16 every 5 seconds, requiring same-sequence 0x001b");
    Console.WriteLine("- reverse infrastructure: TCP/7274, UDP/36524, exactly three reverse TCP accepts, captured AAC-only RTSP handshake");
    Console.WriteLine("- forbidden: Pause, Resume, AddMirror, discovery, retry, fallback, alternate target, second session, or media beyond the cap");
    Console.WriteLine("Dry-run only: no capture, FFmpeg process, socket, or LAN packet is created.");
}

static async Task RunMiPlayAacEncoderSmokeAsync(
    string ffmpegPath,
    CancellationToken cancellationToken)
{
    const int pcmFrameCount = 50;
    Console.WriteLine(
        $"Offline AAC encoder smoke: ffmpeg={ffmpegPath}, pcm={pcmFrameCount}x20ms " +
        "s16le/44100/stereo 440Hz tone, output=AAC-LC/48000/stereo/ADTS. No socket will be opened.");

    await using var encoder = MiPlayFfmpegAacEncoder.Start(ffmpegPath);
    var writeTask = Task.Run(async () =>
    {
        const int samplesPerPcmFrame =
            MiPlayFfmpegAacEncoder.InputSampleRate * PcmFrameBuffer.FrameMilliseconds / 1000;
        for (var index = 0; index < pcmFrameCount; index++)
        {
            var pcm = MiPlayPcmTestTone.CreateFrame(
                (long)index * samplesPerPcmFrame,
                samplesPerPcmFrame);
            await encoder.WritePcmAsync(pcm, cancellationToken);
        }
        await encoder.CompleteInputAsync(cancellationToken);
    }, cancellationToken);

    var packetizer = new MiPlayWfdAudioPacketizer();
    var accessUnitCount = 0;
    long adtsBytes = 0;
    long wireBytes = 0;
    while (await encoder.ReadAccessUnitAsync(cancellationToken) is { } accessUnit)
    {
        var packet = packetizer.Packetize(accessUnit);
        accessUnitCount++;
        adtsBytes += accessUnit.Length;
        wireBytes += packet.WireFrame.Length;
    }
    await writeTask;

    if (accessUnitCount == 0 || adtsBytes == 0 || wireBytes == 0)
    {
        throw new InvalidOperationException("FFmpeg produced no complete MiPlay-compatible ADTS access unit.");
    }
    Console.WriteLine(
        $"Offline AAC encoder smoke passed: accessUnits={accessUnitCount}, adtsBytes={adtsBytes}, " +
        $"packetizedWireBytes={wireBytes}. No network operation occurred.");
}

static async Task RunMiPlaySystemLoopbackAacSmokeAsync(
    string ffmpegPath,
    CancellationToken cancellationToken)
{
    const int pcmFrameCount = 50;
    const int samplesPerPcmFrame =
        MiPlayFfmpegAacEncoder.InputSampleRate * PcmFrameBuffer.FrameMilliseconds / 1000;
    var audioCatalog = new AudioSourceCatalog();
    var defaultOutput = audioCatalog.GetDefaultOutputDevice();
    Console.WriteLine(
        $"Offline system-loopback AAC smoke: endpoint={JsonSerializer.Serialize(defaultOutput.DisplayName)}, " +
        "local 440Hz amplitude=0.04 for about 1 second. No socket will be opened.");

    await using var buffer = new PcmFrameBuffer();
    await using var capture = audioCatalog.CreateCapture(
        new DLNACast.Core.Models.CaptureSelection.SystemMix(defaultOutput.Id, defaultOutput.DisplayName));
    using var localOutput = new WaveOutEvent { DesiredLatency = 50 };
    var localTone = new BufferedWaveProvider(new WaveFormat(
        MiPlayFfmpegAacEncoder.InputSampleRate,
        MiPlayFfmpegAacEncoder.InputBitsPerSample,
        MiPlayFfmpegAacEncoder.InputChannels))
    {
        ReadFully = true,
        BufferDuration = TimeSpan.FromSeconds(2),
    };
    for (var index = 0; index < 60; index++)
    {
        var frame = MiPlayPcmTestTone.CreateFrame(
            (long)index * samplesPerPcmFrame,
            samplesPerPcmFrame,
            amplitude: 0.04);
        localTone.AddSamples(frame, 0, frame.Length);
    }
    localOutput.Init(localTone);

    await capture.StartAsync(buffer, cancellationToken);
    localOutput.Play();
    await buffer.PrepareForPlaybackAsync(cancellationToken: cancellationToken);

    await using var encoder = MiPlayFfmpegAacEncoder.Start(ffmpegPath);
    var writeTask = Task.Run(async () =>
    {
        for (var index = 0; index < pcmFrameCount; index++)
        {
            var pcm = await buffer.ReadFrameOrSilenceAsync(cancellationToken);
            await encoder.WritePcmAsync(pcm, cancellationToken);
        }
        await encoder.CompleteInputAsync(cancellationToken);
    }, cancellationToken);

    var accessUnitCount = 0;
    long adtsBytes = 0;
    var packetizer = new MiPlayWfdAudioPacketizer();
    while (await encoder.ReadAccessUnitAsync(cancellationToken) is { } accessUnit)
    {
        _ = packetizer.Packetize(accessUnit);
        accessUnitCount++;
        adtsBytes += accessUnit.Length;
    }
    await writeTask;
    localOutput.Stop();
    await capture.StopAsync();

    if (accessUnitCount < 45 || adtsBytes < 5_000)
    {
        throw new InvalidOperationException(
            $"System loopback did not produce the expected non-silent AAC shape: accessUnits={accessUnitCount}, adtsBytes={adtsBytes}.");
    }
    Console.WriteLine(
        $"Offline system-loopback AAC smoke passed: accessUnits={accessUnitCount}, adtsBytes={adtsBytes}, " +
        $"captureOverruns={buffer.Overruns}, captureUnderruns={buffer.Underruns}. No network operation occurred.");
}

static async Task RunMiPlaySystemLoopbackAacAnalysisAsync(
    string ffmpegPath,
    CancellationToken cancellationToken)
{
    const int pcmFrameCount = 250;
    var audioCatalog = new AudioSourceCatalog();
    var defaultOutput = audioCatalog.GetDefaultOutputDevice();
    Console.WriteLine(
        $"Offline real-output AAC analysis: endpoint={JsonSerializer.Serialize(defaultOutput.DisplayName)}, " +
        $"captureFrames={pcmFrameCount}, durationMs={pcmFrameCount * PcmFrameBuffer.FrameMilliseconds}. No socket will be opened.");

    await using var buffer = new PcmFrameBuffer();
    await using var capture = audioCatalog.CreateCapture(
        new DLNACast.Core.Models.CaptureSelection.SystemMix(defaultOutput.Id, defaultOutput.DisplayName));
    await capture.StartAsync(buffer, cancellationToken);
    await buffer.PrepareForPlaybackAsync(cancellationToken: cancellationToken);
    var pcmFrames = new List<byte[]>(pcmFrameCount);
    for (var index = 0; index < pcmFrameCount; index++)
    {
        pcmFrames.Add(await buffer.ReadFrameOrSilenceAsync(cancellationToken));
    }
    await capture.StopAsync();
    Console.WriteLine(
        $"Offline PCM capture completed: frames={pcmFrames.Count}, overruns={buffer.Overruns}, underruns={buffer.Underruns}.");
    var captureMeter = new MiPlayPcm16SignalMeter();
    foreach (var pcm in pcmFrames)
    {
        captureMeter.Add(pcm);
    }
    LogPcmSignal("Offline system-loopback", captureMeter.Snapshot());

    var profiles = new (string Codec, int BitRate)[]
    {
        ("aac", 192_000),
        ("aac", 160_000),
        ("aac", 128_000),
        ("aac_mf", 256_000),
        ("aac_mf", 240_000),
        ("aac_mf", 224_000),
        ("aac_mf", 208_000),
        ("aac_mf", 192_000),
    };
    foreach (var (Codec, BitRate) in profiles)
    {
        try
        {
            var lengths = await EncodePcmForMiPlayAnalysisAsync(
                ffmpegPath,
                pcmFrames,
                Codec,
                BitRate,
                cancellationToken);
            var tableLimit = MiPlayMpegTsAudioMuxer.GetMaximumAdtsAccessUnitLength(includeProgramTables: true);
            var steadyLimit = MiPlayMpegTsAudioMuxer.GetMaximumAdtsAccessUnitLength(includeProgramTables: false);
            var tableOverflows = lengths
                .Select((length, index) => (length, index))
                .Where(item => (item.index == 0 || (item.index >= 10 && item.index % 5 == 0)) &&
                               item.length > tableLimit)
                .ToArray();
            var steadyOverflows = lengths
                .Select((length, index) => (length, index))
                .Where(item => item.index != 0 && (item.index < 10 || item.index % 5 != 0) &&
                               item.length > steadyLimit)
                .ToArray();
            Console.WriteLine(
                $"AAC profile codec={Codec}, bitrate={BitRate}, accessUnits={lengths.Count}, " +
                $"min={lengths.Min()}, max={lengths.Max()}, avg={lengths.Average():0.0}, " +
                $"tableOverflows={tableOverflows.Length}, steadyOverflows={steadyOverflows.Length}, " +
                $"firstTableOverflow={(tableOverflows.FirstOrDefault() is var first && first != default ? $"{first.index}:{first.length}" : "none")}.");
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"AAC profile codec={Codec}, bitrate={BitRate} unavailable: {exception.GetType().Name}: {exception.Message}");
        }
    }
    Console.WriteLine("Offline real-output AAC analysis completed. No network operation occurred.");
}

static async Task<IReadOnlyList<int>> EncodePcmForMiPlayAnalysisAsync(
    string ffmpegPath,
    IReadOnlyList<byte[]> pcmFrames,
    string codec,
    int bitRate,
    CancellationToken cancellationToken)
{
    await using var encoder = MiPlayFfmpegAacEncoder.Start(ffmpegPath, bitRate, codec);
    var writeTask = Task.Run(async () =>
    {
        foreach (var pcm in pcmFrames)
        {
            await encoder.WritePcmAsync(pcm, cancellationToken);
        }
        await encoder.CompleteInputAsync(cancellationToken);
    }, cancellationToken);

    var lengths = new List<int>();
    while (await encoder.ReadAccessUnitAsync(cancellationToken) is { } accessUnit)
    {
        lengths.Add(accessUnit.Length);
    }
    await writeTask;
    return lengths;
}

static async Task RunLegacyPlaybackAsync(
    IPAddress targetAddress,
    bool explicitlyAuthorized,
    MiPlayLegacyStatusQueryOrder statusQueryOrder,
    MiPlayLegacyProbeMediaMode mediaMode,
    bool injectLocalTestTone,
    int systemAudioDurationSeconds,
    string? ffmpegPath,
    CancellationToken cancellationToken)
{
    const int listenerPort = 7_274;
    const int timerPort = 36_524;
    var mediaFrameCap = mediaMode switch
    {
        MiPlayLegacyProbeMediaMode.Silence => 48,
        MiPlayLegacyProbeMediaMode.Tone => 96,
        MiPlayLegacyProbeMediaMode.SystemLoopback => checked((int)Math.Ceiling(
            systemAudioDurationSeconds * MiPlayProtocolConstants.SampleRate / 1024d)),
        _ => throw new ArgumentOutOfRangeException(nameof(mediaMode)),
    };
    var mediaLabel = mediaMode switch
    {
        MiPlayLegacyProbeMediaMode.Silence => "silence",
        MiPlayLegacyProbeMediaMode.Tone => "tone",
        MiPlayLegacyProbeMediaMode.SystemLoopback => "system-loopback",
        _ => throw new ArgumentOutOfRangeException(nameof(mediaMode)),
    };
    var usesEncoder = mediaMode != MiPlayLegacyProbeMediaMode.Silence;
    var mediaDurationMilliseconds =
        mediaFrameCap * 1024_000d / MiPlayProtocolConstants.SampleRate;
    if (injectLocalTestTone && mediaMode != MiPlayLegacyProbeMediaMode.SystemLoopback)
    {
        throw new ArgumentException(
            "A local test tone can only be injected into the system-loopback validation.",
            nameof(injectLocalTestTone));
    }
    var bootstrapGuard = new MiPlayLegacyAudioSourceBootstrapProbeGuard(
        targetAddress,
        explicitlyAuthorized,
        statusQueryOrder);
    var bootstrap = new MiPlayLegacyAudioSourceSession("MI PAD 4/Plus", statusQueryOrder);

    Console.WriteLine(
        $"Authorized one-target {mediaLabel} validation: target={targetAddress}:8899, " +
        $"bootstrapFrames<={MiPlayLegacyAudioSourceBootstrapProbeGuard.MaximumFrames}, " +
        $"preOpenFrames<=7, postOpenStartupFrames<=1, " +
        $"periodicHeartbeats<={Math.Floor(mediaDurationMilliseconds / MiPlayLegacyStreamingHeartbeatPlan.IntervalMilliseconds)}, " +
        $"reverseTcp={listenerPort}, timerUdp={timerPort}, mediaFrames<={mediaFrameCap}.");
    Console.WriteLine(
        "No Pause, Resume, AddMirror, 0x0041/Open ACK wait, discovery, retry, fallback, or alternate target is permitted. " +
        "Post-Open startup control is limited to one playing SetMediaInfo; subsequent control is ordinary five-second heartbeat only. " +
        $"The only media source is {mediaLabel}.");

    using var controlClient = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
    try
    {
        await controlClient.ConnectAsync(targetAddress, MiPlayProtocolConstants.DefaultControlPort, cancellationToken);
    }
    catch (SocketException exception)
    {
        Console.WriteLine(
            $"TCP connection failed before any protocol write: socketError={exception.SocketErrorCode}, outboundWrites=0, outboundFrames=0.");
        return;
    }

    await using var controlStream = controlClient.GetStream();
    Console.WriteLine("Control TCP connected; waiting for receiver-first 0x0028 with outbound count zero.");
    while (!bootstrap.Phase.Equals(MiPlayLegacyAudioSourcePhase.BasicBootstrapComplete))
    {
        var inboundBytes = await ReadMiPlayFrameAsync(controlStream, cancellationToken);
        LogLegacyBootstrapStatusFrame(inboundBytes);
        var transition = bootstrap.ProcessInboundFrame(inboundBytes);
        if (!transition.Accepted)
        {
            throw new InvalidOperationException($"Legacy bootstrap stopped: {transition.Boundary}");
        }

        foreach (var write in transition.OutboundWrites)
        {
            var decision = bootstrapGuard.AuthorizeNextWrite(write);
            if (!decision.CanSend)
            {
                throw new InvalidOperationException($"Bootstrap guard refused a write: {decision.Reason}");
            }

            await controlStream.WriteAsync(write.ToArray(), cancellationToken);
            await controlStream.FlushAsync(cancellationToken);
            foreach (var frame in decision.Frames)
            {
                Console.WriteLine(
                    $"Bootstrap sent command=0x{frame.Command:X4}, sequence=0x{frame.Sequence:X4}, " +
                    $"payloadLength={frame.PayloadLength}, frameSha256={frame.FrameSha256Hex}.");
            }
        }
    }

    if (controlClient.Client.LocalEndPoint is not IPEndPoint localControlEndPoint)
    {
        throw new InvalidOperationException("The connected control socket has no IPv4 local endpoint.");
    }
    var sourceAddress = localControlEndPoint.Address;
    var reverseListener = new TcpListener(sourceAddress, listenerPort);
    reverseListener.Start(backlog: 3);
    using var timer = new UdpClient(new IPEndPoint(sourceAddress, timerPort));
    using var run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    var timerTask = RunWfdTimerResponderAsync(timer, targetAddress, run.Token);
    Console.WriteLine(
        $"Bootstrap verified. Reverse endpoints bound before playback refresh: tcp={sourceAddress}:{listenerPort}, udp={sourceAddress}:{timerPort}.");

    var playback = new MiPlayLegacyPlaybackControlSession(
        bootstrap,
        "MI PAD 4/Plus",
        sourceAddress,
        listenerPort);
    var expectedControl = new Queue<(ushort Command, ushort Sequence)>(
        [
            (MiPlayProtocolConstants.SetLocalDeviceInfoCommand, 8),
            (MiPlayProtocolConstants.GetDeviceInfoCommand, 9),
            (MiPlayProtocolConstants.SetLocalDeviceInfoCommand, 10),
            (MiPlayProtocolConstants.GetMirrorModeCommand, 11),
            (MiPlayProtocolConstants.HeartbeatCommand, 12),
            (MiPlayProtocolConstants.SetPlaySourceCommand, 13),
            (MiPlayProtocolConstants.OpenDeviceCommand, 14),
        ]);
    var sentControlFrames = 0;
    long? preOpenHeartbeatSentAt = null;

    await SendPlaybackControlWritesAsync(
        controlStream,
        playback.Start().OutboundWrites,
        expectedControl,
        cancellationToken);
    sentControlFrames += 2;

    while (playback.Phase != MiPlayLegacyPlaybackControlPhase.AwaitingOpenPrerequisites)
    {
        var inboundBytes = await ReadMiPlayFrameAsync(controlStream, cancellationToken);
        if (MiPlayCommandFrameCodec.TryDecode(inboundBytes, out var inbound, out _) && inbound is not null)
        {
            Console.WriteLine(
                $"Playback inbound command=0x{inbound.Command:X4}, sequence=0x{inbound.Sequence:X4}, " +
                $"payloadLength={inbound.Payload.Length}; raw payload not logged.");
        }

        var transition = playback.ProcessInbound(inboundBytes);
        if (!transition.Accepted)
        {
            throw new InvalidOperationException($"Playback continuation stopped: {transition.Boundary}");
        }
        var sendsPreOpenHeartbeat = ContainsCommandSequence(
            transition.OutboundWrites,
            MiPlayProtocolConstants.HeartbeatCommand,
            MiPlayLegacyPlaybackControlSession.HeartbeatSequence);
        await SendPlaybackControlWritesAsync(
            controlStream,
            transition.OutboundWrites,
            expectedControl,
            cancellationToken);
        if (sendsPreOpenHeartbeat)
        {
            preOpenHeartbeatSentAt = Stopwatch.GetTimestamp();
        }
        sentControlFrames += transition.OutboundWrites.Sum(write => write.Frames.Count);
    }

    var audioCatalog = new AudioSourceCatalog();
    var defaultOutput = mediaMode == MiPlayLegacyProbeMediaMode.SystemLoopback
        ? audioCatalog.GetDefaultOutputDevice()
        : null;
    await using var systemAudioBuffer = defaultOutput is not null ? new PcmFrameBuffer() : null;
    await using var captureSource = defaultOutput is not null
        ? audioCatalog.CreateCapture(new DLNACast.Core.Models.CaptureSelection.SystemMix(
            defaultOutput.Id,
            defaultOutput.DisplayName))
        : null;
    using var injectedLocalOutput = injectLocalTestTone
        ? new WaveOutEvent { DesiredLatency = 50 }
        : null;
    BufferedWaveProvider? injectedTone = null;
    if (injectedLocalOutput is not null)
    {
        const int samplesPerPcmFrame =
            MiPlayFfmpegAacEncoder.InputSampleRate * PcmFrameBuffer.FrameMilliseconds / 1000;
        injectedTone = new BufferedWaveProvider(new WaveFormat(
            MiPlayFfmpegAacEncoder.InputSampleRate,
            MiPlayFfmpegAacEncoder.InputBitsPerSample,
            MiPlayFfmpegAacEncoder.InputChannels))
        {
            ReadFully = true,
            BufferDuration = TimeSpan.FromSeconds(7),
        };
        for (var index = 0; index < 300; index++)
        {
            var frame = MiPlayPcmTestTone.CreateFrame(
                (long)index * samplesPerPcmFrame,
                samplesPerPcmFrame,
                amplitude: 0.04);
            injectedTone.AddSamples(frame, 0, frame.Length);
        }
        injectedLocalOutput.Init(injectedTone);
    }
    if (captureSource is not null && systemAudioBuffer is not null)
    {
        await captureSource.StartAsync(systemAudioBuffer, cancellationToken);
        injectedLocalOutput?.Play();
        await systemAudioBuffer.PrepareForPlaybackAsync(cancellationToken: cancellationToken);
        Console.WriteLine(
            $"System loopback capture started: endpoint={JsonSerializer.Serialize(defaultOutput!.DisplayName)}, " +
            $"bufferedMs={systemAudioBuffer.BufferedMilliseconds}, injectedLocalTone={injectLocalTestTone}.");
    }

    var encoderBitRate = mediaMode == MiPlayLegacyProbeMediaMode.SystemLoopback
        ? MiPlayProtocolConstants.AacBitRate
        : MiPlayFfmpegAacEncoder.OutputBitRate;
    var encoderCodec = mediaMode == MiPlayLegacyProbeMediaMode.SystemLoopback
        ? "aac_mf"
        : "aac";
    await using MiPlayFfmpegAacEncoder? encoder = usesEncoder
        ? MiPlayFfmpegAacEncoder.Start(
            ffmpegPath ?? throw new ArgumentNullException(nameof(ffmpegPath)),
            encoderBitRate,
            encoderCodec)
        : null;
    using var encoderInputRun = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    var systemAudioMeter = mediaMode == MiPlayLegacyProbeMediaMode.SystemLoopback
        ? new MiPlayPcm16SignalMeter()
        : null;
    Task? encoderWriterTask = null;
    if (encoder is not null)
    {
        encoderWriterTask = Task.Run(async () =>
        {
            if (mediaMode == MiPlayLegacyProbeMediaMode.Tone)
            {
                const int pcmFrameCount = 105;
                const int samplesPerPcmFrame =
                    MiPlayFfmpegAacEncoder.InputSampleRate * PcmFrameBuffer.FrameMilliseconds / 1000;
                for (var frameIndex = 0; frameIndex < pcmFrameCount; frameIndex++)
                {
                    var pcm = MiPlayPcmTestTone.CreateFrame(
                        (long)frameIndex * samplesPerPcmFrame,
                        samplesPerPcmFrame);
                    await encoder.WritePcmAsync(pcm, encoderInputRun.Token);
                }
                await encoder.CompleteInputAsync(encoderInputRun.Token);
                return;
            }

            if (systemAudioBuffer is null)
            {
                throw new InvalidOperationException("The system-loopback encoder has no PCM frame buffer.");
            }
            while (!encoderInputRun.IsCancellationRequested)
            {
                var pcm = await systemAudioBuffer.ReadFrameOrSilenceAsync(encoderInputRun.Token);
                systemAudioMeter!.Add(pcm);
                await encoder.WritePcmAsync(pcm, encoderInputRun.Token);
            }
        }, encoderInputRun.Token);
        Console.WriteLine(
            $"Local FFmpeg {mediaLabel} pipeline started: pid={encoder.ProcessId}, " +
            $"input=s16le/44100/stereo, output={encoderCodec}/AAC-LC/48000/stereo/" +
            $"{encoderBitRate}bit/s/ADTS.");
    }

    var acceptControlTask = reverseListener.AcceptTcpClientAsync(cancellationToken).AsTask();
    var open = playback.PrepareOpen(new(
        TcpListenerBound: true,
        UdpTimerResponderBound: true,
        ReverseConnectionCapacity: 3,
        AacMpegTsPipelineReady: !usesEncoder || encoder is not null));
    if (!open.Accepted || !open.OpenPrepared)
    {
        throw new InvalidOperationException($"Open prerequisites were not accepted: {open.Boundary}");
    }
    await SendPlaybackControlWritesAsync(
        controlStream,
        open.OutboundWrites,
        expectedControl,
        cancellationToken);
    sentControlFrames += 1;
    if (sentControlFrames != 7 || expectedControl.Count != 0)
    {
        throw new InvalidOperationException("The playback-control seven-frame cap was not consumed exactly.");
    }
    if (preOpenHeartbeatSentAt is null)
    {
        throw new InvalidOperationException("The playback continuation did not record its pre-Open heartbeat anchor.");
    }

    using var rtspClient = await acceptControlTask;
    rtspClient.NoDelay = true;
    Console.WriteLine($"Accepted reverse RTSP control connection from {rtspClient.Client.RemoteEndPoint}.");
    var rtspBeforeTimeOffset = new TaskCompletionSource<ulong>(TaskCreationOptions.RunContinuationsAsynchronously);
    var postOpenContextSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var rtspReady = new TaskCompletionSource<ulong>(TaskCreationOptions.RunContinuationsAsynchronously);
    var rtspTask = RunWfdRtspControlAsync(
        rtspClient,
        sourceAddress,
        timerPort,
        rtspBeforeTimeOffset,
        postOpenContextSent.Task,
        rtspReady,
        run.Token);

    using var unusedClient = await reverseListener.AcceptTcpClientAsync(cancellationToken);
    using var audioClient = await reverseListener.AcceptTcpClientAsync(cancellationToken);
    audioClient.NoDelay = true;
    reverseListener.Stop();
    Console.WriteLine(
        $"Accepted reverse channel #2 from {unusedClient.Client.RemoteEndPoint} and audio channel #3 from {audioClient.Client.RemoteEndPoint}.");

    var pendingTimeOffsetMicroseconds = await rtspBeforeTimeOffset.Task.WaitAsync(cancellationToken);
    var postOpen = new MiPlayLegacyPostOpenPlaybackSession(
        MiPlaySetMediaInfoPayloadCodec.CreateWindowsSystemAudio(
            checked((int)Math.Ceiling(mediaDurationMilliseconds)),
            "MI PAD 4/Plus"));
    var postOpenStart = postOpen.Start();
    if (!postOpenStart.Accepted)
    {
        throw new InvalidOperationException($"Post-Open control did not start: {postOpenStart.Boundary}");
    }
    await SendPostOpenPlaybackWritesAsync(
        controlStream,
        postOpenStart.OutboundWrites,
        cancellationToken);
    postOpenContextSent.TrySetResult();
    var timeOffsetMicroseconds = await rtspReady.Task.WaitAsync(cancellationToken);
    if (timeOffsetMicroseconds != pendingTimeOffsetMicroseconds)
    {
        throw new InvalidOperationException("The RTSP TIME_OFFSET changed across the pre-TIME_OFFSET control gate.");
    }
    var heartbeatPlan = MiPlayLegacyStreamingHeartbeatPlan.Create(mediaDurationMilliseconds);
    var postOpenControlTask = RunLegacyPostOpenPlaybackControlAsync(
        controlStream,
        postOpen,
        heartbeatPlan,
        preOpenHeartbeatSentAt.Value,
        run.Token);
    await using var audioStream = audioClient.GetStream();
    var initialProgramClockReference90Khz =
        MiPlayWfdMediaClock.CreateInitialProgramClockReference90Khz(
            timeOffsetMicroseconds,
            MiPlayProtocolConstants.OtherNetworkPlaybackDelayMicroseconds);
    var packetizer = new MiPlayWfdAudioPacketizer(
        initialProgramClockReference90Khz: initialProgramClockReference90Khz);
    Console.WriteLine(
        $"Media clock anchored: timeOffsetUs={timeOffsetMicroseconds}, " +
        $"playbackDelayUs={MiPlayProtocolConstants.OtherNetworkPlaybackDelayMicroseconds}, " +
        $"initialPcr90Khz={initialProgramClockReference90Khz}.");
    var silence = usesEncoder ? null : MiPlayAacSilenceAccessUnit.Create();
    var mediaStartedAt = Stopwatch.GetTimestamp();
    long totalMediaBytes = 0;
    var totalRtpFrames = 0;
    ushort expectedRtpSequence = 0;
    for (var index = 0; index < mediaFrameCap; index++)
    {
        var accessUnit = encoder is not null
            ? await encoder.ReadAccessUnitAsync(cancellationToken) ??
              throw new EndOfStreamException("FFmpeg ended before the bounded tone access-unit cap.")
            : silence!;
        var packets = packetizer.PacketizeAccessUnit(accessUnit);
        if (packets.Count is < 1 or > 2)
        {
            throw new InvalidOperationException(
                "The AAC access unit exceeded the captured one-or-two-RTP-fragment boundary.");
        }
        foreach (var packet in packets)
        {
            if (packet.SequenceNumber != expectedRtpSequence || packet.WireFrame.Length > 1_500)
            {
                throw new InvalidOperationException(
                    $"The {mediaLabel} media frame violated its RTP sequence or size cap.");
            }
            expectedRtpSequence++;
        }

        var wireWrite = new byte[packets.Sum(packet => packet.WireFrame.Length)];
        var wireOffset = 0;
        foreach (var packet in packets)
        {
            packet.WireFrame.CopyTo(wireWrite, wireOffset);
            wireOffset += packet.WireFrame.Length;
        }
        await audioStream.WriteAsync(wireWrite, cancellationToken);
        await audioStream.FlushAsync(cancellationToken);
        totalMediaBytes += wireWrite.Length;
        totalRtpFrames += packets.Count;

        var nextAccessUnit = index + 1;
        var nextDueMilliseconds = MiPlayWfdStartupPacingPlan.GetDueAfterMilliseconds(nextAccessUnit);
        var nextSend = mediaStartedAt + checked((long)Math.Round(
            nextDueMilliseconds * Stopwatch.Frequency / 1_000d));
        var remaining = nextSend - Stopwatch.GetTimestamp();
        if (remaining > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(remaining / (double)Stopwatch.Frequency), cancellationToken);
        }
    }

    Console.WriteLine(
        $"{mediaLabel} media cap completed: accessUnits={mediaFrameCap}, " +
        $"rtpFrames={totalRtpFrames}, bytes={totalMediaBytes}, " +
        $"durationMs={mediaDurationMilliseconds:0.0}.");
    encoderInputRun.Cancel();
    if (encoderWriterTask is not null)
    {
        try
        {
            await encoderWriterTask;
        }
        catch (OperationCanceledException) when (encoderInputRun.IsCancellationRequested)
        {
        }
    }
    if (captureSource is not null)
    {
        injectedLocalOutput?.Stop();
        await captureSource.StopAsync();
        Console.WriteLine(
            $"System loopback capture stopped: overruns={systemAudioBuffer!.Overruns}, " +
            $"underruns={systemAudioBuffer.Underruns}.");
        LogPcmSignal("Live system-loopback", systemAudioMeter!.Snapshot());
    }
    await Task.Delay(250, cancellationToken);
    run.Cancel();
    try
    {
        await Task.WhenAll(rtspTask, timerTask, postOpenControlTask);
    }
    catch (OperationCanceledException) when (run.IsCancellationRequested)
    {
    }
    Console.WriteLine($"Bounded {mediaLabel} validation stopped; all owned reverse sockets are closing and no retry will run.");
}

static void LogLegacyBootstrapStatusFrame(ReadOnlySpan<byte> frameBytes)
{
    if (!MiPlayCommandFrameCodec.TryDecode(frameBytes, out var frame, out var consumed) ||
        frame is null || consumed != frameBytes.Length)
    {
        return;
    }

    if (frame.Command is MiPlayProtocolConstants.GetVolumeAcknowledgementCommand or
        MiPlayProtocolConstants.GetStateAcknowledgementCommand or
        MiPlayProtocolConstants.GetMirrorModeAcknowledgementCommand)
    {
        var label = frame.Command switch
        {
            MiPlayProtocolConstants.GetVolumeAcknowledgementCommand => "volume",
            MiPlayProtocolConstants.GetStateAcknowledgementCommand => "state",
            _ => "mirrorMode",
        };
        if (MiPlayLegacyStatusScalarCodec.TryDecode(frame.Payload, out var value))
        {
            Console.WriteLine(
                $"Bootstrap observed {label}={value}, command=0x{frame.Command:X4}, sequence=0x{frame.Sequence:X4}.");
        }
        return;
    }

    if (frame.Command == MiPlayProtocolConstants.NotifyCommand &&
        MiPlayNotifyPayloadCodec.TryDecode(frame.Payload, out var notify, out var notifyConsumed) &&
        notify is not null && notifyConsumed == frame.Payload.Length)
    {
        Console.WriteLine($"Bootstrap observed notify {DescribeNotifyPayload(notify)}.");
    }
}

static void LogPcmSignal(string label, MiPlayPcm16SignalSnapshot snapshot)
{
    var nonZeroPercent = snapshot.SampleCount == 0
        ? 0
        : snapshot.NonZeroSampleCount * 100d / snapshot.SampleCount;
    Console.WriteLine(
        $"{label} PCM signal: samples={snapshot.SampleCount}, nonZeroPercent={nonZeroPercent:0.000}, " +
        $"peak={snapshot.PeakNormalized:0.000000}, rms={snapshot.RmsNormalized:0.000000}, " +
        $"rmsDbfs={snapshot.RmsDecibelsFullScale:0.00}, containsAudibleSignal={snapshot.ContainsAudibleSignal}.");
}

static async Task SendPlaybackControlWritesAsync(
    NetworkStream stream,
    IReadOnlyList<MiPlayLegacyAudioSourceWrite> writes,
    Queue<(ushort Command, ushort Sequence)> expected,
    CancellationToken cancellationToken)
{
    foreach (var write in writes)
    {
        if (write.Frames.Count != 1 || expected.Count == 0 ||
            !MiPlayCommandFrameCodec.TryDecode(write.Frames[0], out var frame, out var consumed) ||
            frame is null || consumed != write.Frames[0].Length)
        {
            throw new InvalidOperationException("Playback control must contain exactly one strict command frame per write.");
        }

        var (Command, Sequence) = expected.Dequeue();
        if (frame.Command != Command || frame.Sequence != Sequence ||
            frame.Command == MiPlayProtocolConstants.AddMirrorCommand)
        {
            throw new InvalidOperationException(
                $"Playback control ledger mismatch: got 0x{frame.Command:X4}/{frame.Sequence}, expected 0x{Command:X4}/{Sequence}.");
        }

        await stream.WriteAsync(write.Frames[0], cancellationToken);
        await stream.FlushAsync(cancellationToken);
        Console.WriteLine(
            $"Playback sent command=0x{frame.Command:X4}, sequence=0x{frame.Sequence:X4}, " +
            $"payloadLength={frame.Payload.Length}, frameSha256={Convert.ToHexString(SHA256.HashData(write.Frames[0]))}.");
    }
}

static bool ContainsCommandSequence(
    IReadOnlyList<MiPlayLegacyAudioSourceWrite> writes,
    ushort command,
    ushort sequence)
{
    foreach (var frameBytes in writes.SelectMany(write => write.Frames))
    {
        if (MiPlayCommandFrameCodec.TryDecode(frameBytes, out var frame, out var consumed) &&
            frame is not null && consumed == frameBytes.Length &&
            frame.Command == command && frame.Sequence == sequence)
        {
            return true;
        }
    }
    return false;
}

static async Task RunLegacyStreamingHeartbeatsAsync(
    NetworkStream controlStream,
    IReadOnlyList<MiPlayLegacyStreamingHeartbeat> plan,
    long previousHeartbeatSentAt,
    CancellationToken cancellationToken)
{
    foreach (var heartbeat in plan)
    {
        var dueAt = MiPlayLegacyStreamingHeartbeatPlan.CalculateDueTimestamp(
            previousHeartbeatSentAt,
            heartbeat.DueAfterMilliseconds,
            Stopwatch.Frequency);
        var remaining = dueAt - Stopwatch.GetTimestamp();
        if (remaining > 0)
        {
            await Task.Delay(
                TimeSpan.FromSeconds(remaining / (double)Stopwatch.Frequency),
                cancellationToken);
        }

        await controlStream.WriteAsync(heartbeat.CommandFrame, cancellationToken);
        await controlStream.FlushAsync(cancellationToken);
        Console.WriteLine(
            $"Streaming heartbeat sent: command=0x001A, sequence=0x{heartbeat.Sequence:X4}, " +
            $"dueMs={heartbeat.DueAfterMilliseconds}.");

        using var acknowledgementTimeout =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        acknowledgementTimeout.CancelAfter(TimeSpan.FromSeconds(3));
        var inboundFrameCap = 8;
        var acknowledged = false;
        for (var index = 0; index < inboundFrameCap; index++)
        {
            var inboundBytes = await ReadMiPlayFrameAsync(
                controlStream,
                acknowledgementTimeout.Token);
            if (!MiPlayCommandFrameCodec.TryDecode(
                    inboundBytes,
                    out var inbound,
                    out var consumed) ||
                inbound is null || consumed != inboundBytes.Length)
            {
                throw new InvalidDataException("The heartbeat reader received a malformed command frame.");
            }

            if (inbound.Command == MiPlayProtocolConstants.HeartbeatAcknowledgementCommand &&
                inbound.Sequence == heartbeat.Sequence &&
                inbound.Payload.Length == 0)
            {
                acknowledged = true;
                Console.WriteLine(
                    $"Streaming heartbeat acknowledged: command=0x001B, sequence=0x{inbound.Sequence:X4}.");
                break;
            }
            if (inbound.Command == MiPlayProtocolConstants.NotifyCommand &&
                MiPlayNotifyPayloadCodec.TryDecode(
                    inbound.Payload,
                    out var notify,
                    out var notifyConsumed) &&
                notify is not null && notifyConsumed == inbound.Payload.Length)
            {
                Console.WriteLine($"Streaming heartbeat wait observed notify {DescribeNotifyPayload(notify)}.");
                continue;
            }
            Console.WriteLine(
                $"Streaming heartbeat wait observed command=0x{inbound.Command:X4}, " +
                $"sequence=0x{inbound.Sequence:X4}, payloadLength={inbound.Payload.Length}; raw payload not logged.");
        }
        if (!acknowledged)
        {
            throw new TimeoutException(
                $"No empty 0x001B acknowledgement was observed for heartbeat sequence 0x{heartbeat.Sequence:X4}.");
        }
    }
}

static async Task SendPostOpenPlaybackWritesAsync(
    NetworkStream controlStream,
    IReadOnlyList<MiPlayLegacyAudioSourceWrite> writes,
    CancellationToken cancellationToken)
{
    foreach (var write in writes)
    {
        if (write.Frames.Count != 1 ||
            !MiPlayCommandFrameCodec.TryDecode(write.Frames[0], out var frame, out var consumed) ||
            frame is null || consumed != write.Frames[0].Length ||
            !IsAllowedPostOpenPlaybackFrame(frame))
        {
            throw new InvalidOperationException(
                "Post-Open startup attempted a frame other than SetMediaInfo.");
        }

        await controlStream.WriteAsync(write.Frames[0], cancellationToken);
        await controlStream.FlushAsync(cancellationToken);
        Console.WriteLine(
            $"Post-Open sent command=0x{frame.Command:X4}, sequence=0x{frame.Sequence:X4}, " +
            $"payloadLength={frame.Payload.Length}, frameSha256={Convert.ToHexString(SHA256.HashData(write.Frames[0]))}.");
    }
}

static bool IsAllowedPostOpenPlaybackFrame(MiPlayCommandFrame frame) =>
    (frame.Command == MiPlayProtocolConstants.SetMediaInfoCommand &&
     frame.Sequence == MiPlayLegacyPostOpenPlaybackSession.SetMediaInfoSequence &&
     MiPlaySetMediaInfoPayloadCodec.TryDecode(frame.Payload, out var mediaInfo) &&
     mediaInfo is { Status: 0, DeviceState: 2 });

static async Task RunLegacyPostOpenPlaybackControlAsync(
    NetworkStream controlStream,
    MiPlayLegacyPostOpenPlaybackSession session,
    IReadOnlyList<MiPlayLegacyStreamingHeartbeat> steadyStateHeartbeats,
    long preOpenHeartbeatSentAt,
    CancellationToken cancellationToken)
{
    using (var playingTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
    {
        playingTimeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (session.Phase == MiPlayLegacyPostOpenPlaybackPhase.AwaitingReceiverPlaybackReadiness)
        {
            await ReadAndProcessPostOpenFrameAsync(controlStream, session, playingTimeout.Token);
        }
    }
    if (session.Phase != MiPlayLegacyPostOpenPlaybackPhase.Playing)
    {
        throw new InvalidOperationException(
            $"Receiver did not automatically report first-audiopcm=1 and state=2; phase={session.Phase}.");
    }

    Console.WriteLine(
        "Post-Open automatic playback transition verified: first-audiopcm=1 and receiver state=2 without Pause, Resume, or startup heartbeat.");
    await RunLegacyStreamingHeartbeatsAsync(
        controlStream,
        steadyStateHeartbeats,
        preOpenHeartbeatSentAt,
        cancellationToken);
}

static async Task ReadAndProcessPostOpenFrameAsync(
    NetworkStream controlStream,
    MiPlayLegacyPostOpenPlaybackSession session,
    CancellationToken cancellationToken)
{
    var inboundBytes = await ReadMiPlayFrameAsync(controlStream, cancellationToken);
    var transition = session.ProcessInbound(inboundBytes);
    if (!transition.Accepted)
    {
        throw new InvalidOperationException($"Post-Open control stopped: {transition.Boundary}");
    }

    if (transition.Notify is { } notify)
    {
        Console.WriteLine($"Post-Open observed notify {DescribeNotifyPayload(notify)}.");
        return;
    }
    if (MiPlayCommandFrameCodec.TryDecode(inboundBytes, out var frame, out _) && frame is not null)
    {
        Console.WriteLine(
            $"Post-Open observed command=0x{frame.Command:X4}, sequence=0x{frame.Sequence:X4}, " +
            $"payloadLength={frame.Payload.Length}; raw payload not logged.");
    }
}

static async Task RunWfdTimerResponderAsync(
    UdpClient timer,
    IPAddress selectedReceiver,
    CancellationToken cancellationToken)
{
    var replies = 0;
    while (!cancellationToken.IsCancellationRequested)
    {
        var received = await timer.ReceiveAsync(cancellationToken);
        if (!received.RemoteEndPoint.Address.Equals(selectedReceiver) ||
            received.Buffer.Length != MiPlayWfdTimerPacketCodec.PacketLength)
        {
            throw new InvalidOperationException(
                "The timer responder received an unexpected peer or non-40-byte packet and stopped.");
        }

        var receiveTime = GetMonotonicMicroseconds();
        var response = MiPlayWfdTimerPacketCodec.CreateResponse(
            received.Buffer,
            receiveTime,
            GetMonotonicMicroseconds());
        await timer.SendAsync(response, received.RemoteEndPoint, cancellationToken);
        replies++;
        if (replies == 1 || replies % 25 == 0)
        {
            Console.WriteLine($"Timer replies={replies}, lastPeer={received.RemoteEndPoint}, packetLength=40.");
        }
    }
}

static async Task RunWfdRtspControlAsync(
    TcpClient client,
    IPAddress sourceAddress,
    int timerPort,
    TaskCompletionSource<ulong> beforeTimeOffset,
    Task postOpenContextSent,
    TaskCompletionSource<ulong> ready,
    CancellationToken cancellationToken)
{
    try
    {
        await RunWfdRtspControlCoreAsync(
            client,
            sourceAddress,
            timerPort,
            beforeTimeOffset,
            postOpenContextSent,
            ready,
            cancellationToken);
    }
    catch (Exception exception)
    {
        beforeTimeOffset.TrySetException(exception);
        ready.TrySetException(exception);
        throw;
    }
}

static async Task RunWfdRtspControlCoreAsync(
    TcpClient client,
    IPAddress sourceAddress,
    int timerPort,
    TaskCompletionSource<ulong> beforeTimeOffset,
    Task postOpenContextSent,
    TaskCompletionSource<ulong> ready,
    CancellationToken cancellationToken)
{
    await using var stream = client.GetStream();
    var sessionId = RandomNumberGenerator.GetInt32(100_000_000, 1_000_000_000).ToString();
    var session = new MiPlayWfdSourceRtspSession(sourceAddress, timerPort, sessionId);
    await SendRtspMessagesAsync(stream, session.Start(DateTimeOffset.UtcNow).OutboundMessages, cancellationToken);

    var pending = new byte[64 * 1024];
    var pendingCount = 0;
    var readBuffer = new byte[8 * 1024];
    while (!cancellationToken.IsCancellationRequested)
    {
        var read = await stream.ReadAsync(readBuffer, cancellationToken);
        if (read == 0)
        {
            throw new EndOfStreamException("The receiver closed RTSP control before the bounded validation ended.");
        }
        if (pendingCount + read > pending.Length)
        {
            throw new InvalidOperationException("RTSP input exceeded the 64 KiB bounded buffer.");
        }
        readBuffer.AsSpan(0, read).CopyTo(pending.AsSpan(pendingCount));
        pendingCount += read;

        while (MiPlayRtspWireMessageCodec.TryDecode(
                   pending.AsSpan(0, pendingCount),
                   out _,
                   out var consumed))
        {
            var transition = session.ProcessInbound(
                pending.AsSpan(0, consumed),
                DateTimeOffset.UtcNow,
                GetMonotonicMicroseconds());
            if (!transition.Accepted)
            {
                ready.TrySetException(new InvalidOperationException(transition.Boundary));
                throw new InvalidOperationException($"RTSP state machine stopped: {transition.Boundary}");
            }
            pending.AsSpan(consumed, pendingCount - consumed).CopyTo(pending);
            pendingCount -= consumed;
            if (session.Phase == MiPlayWfdSourceRtspPhase.AwaitingTimeOffsetAcknowledgement &&
                transition.OutboundMessages.Count == 2 &&
                !beforeTimeOffset.Task.IsCompleted)
            {
                await SendRtspMessagesAsync(
                    stream,
                    [transition.OutboundMessages[0]],
                    cancellationToken);
                if (session.TimeOffsetMicroseconds is not ulong pendingTimeOffsetMicroseconds)
                {
                    throw new InvalidOperationException(
                        "RTSP accepted PLAY without recording the pending TIME_OFFSET clock.");
                }
                beforeTimeOffset.TrySetResult(pendingTimeOffsetMicroseconds);
                await postOpenContextSent.WaitAsync(cancellationToken);
                await SendRtspMessagesAsync(
                    stream,
                    [transition.OutboundMessages[1]],
                    cancellationToken);
            }
            else
            {
                await SendRtspMessagesAsync(stream, transition.OutboundMessages, cancellationToken);
            }
            if (transition.Ready)
            {
                if (session.TimeOffsetMicroseconds is not ulong timeOffsetMicroseconds)
                {
                    throw new InvalidOperationException("RTSP reached Ready without recording its TIME_OFFSET clock.");
                }
                ready.TrySetResult(timeOffsetMicroseconds);
            }
        }
    }
}

static async Task SendRtspMessagesAsync(
    NetworkStream stream,
    IReadOnlyList<byte[]> messages,
    CancellationToken cancellationToken)
{
    foreach (var messageBytes in messages)
    {
        if (!MiPlayRtspWireMessageCodec.TryDecode(messageBytes, out var message, out var consumed) ||
            message is null || consumed != messageBytes.Length)
        {
            throw new InvalidOperationException("The RTSP sender refused its own incomplete message.");
        }

        await stream.WriteAsync(messageBytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        Console.WriteLine(
            $"RTSP sent start={JsonSerializer.Serialize(message.StartLine)}, cseq={message.GetHeader("CSeq")}, " +
            $"length={messageBytes.Length}, sha256={Convert.ToHexString(SHA256.HashData(messageBytes))}.");
    }
}

static ulong GetMonotonicMicroseconds() =>
    MiPlayWfdMediaClock.ConvertStopwatchTicksToMicroseconds(
        Stopwatch.GetTimestamp(),
        Stopwatch.Frequency);

static async Task RunLegacyAudioSourceBootstrapAsync(
    IPAddress targetAddress,
    bool explicitlyAuthorized,
    MiPlayLegacyStatusQueryOrder statusQueryOrder,
    CancellationToken cancellationToken)
{
    var guard = new MiPlayLegacyAudioSourceBootstrapProbeGuard(
        targetAddress,
        explicitlyAuthorized,
        statusQueryOrder);
    var session = new MiPlayLegacyAudioSourceSession("MI PAD 4/Plus", statusQueryOrder);

    Console.WriteLine(
        $"Authorized legacy audio-source bootstrap target={targetAddress}:{MiPlayProtocolConstants.DefaultControlPort}, " +
        $"statusQueryOrder={statusQueryOrder}. The sender waits for the receiver's 0x0028 before writing anything.");
    foreach (var line in MiPlayLegacyAudioSourceBootstrapProbeGuard.CreateDryRunLedger(statusQueryOrder))
    {
        Console.WriteLine($"Planned {line}");
    }
    Console.WriteLine(
        "Hard boundary: no retry, fallback, heartbeat, 0x0040, Open, AddMirror, RTSP, media, playback, or audio will be sent; any unexpected frame stops the session.");

    using var client = new TcpClient(AddressFamily.InterNetwork);
    try
    {
        await client.ConnectAsync(targetAddress, MiPlayProtocolConstants.DefaultControlPort, cancellationToken);
    }
    catch (SocketException exception)
    {
        Console.WriteLine(
            $"TCP connection failed before any protocol write: socketError={exception.SocketErrorCode}, " +
            "outboundWrites=0, outboundFrames=0. No retry, fallback, discovery, or alternate target was attempted.");
        return;
    }

    await using var stream = client.GetStream();
    Console.WriteLine("TCP connected; outbound count remains zero while awaiting the receiver-first challenge.");

    while (!cancellationToken.IsCancellationRequested)
    {
        byte[] inboundBytes;
        try
        {
            inboundBytes = await ReadMiPlayFrameAsync(stream, cancellationToken);
        }
        catch (EndOfStreamException)
        {
            Console.WriteLine("Receiver closed the legacy bootstrap connection; no retry or fallback was attempted.");
            return;
        }

        if (!MiPlayCommandFrameCodec.TryDecode(inboundBytes, out var inbound, out var bytesConsumed) ||
            inbound is null ||
            bytesConsumed != inboundBytes.Length)
        {
            Console.WriteLine("Stopped before writing: inbound bytes failed strict one-frame decoding.");
            return;
        }

        Console.WriteLine(
            $"Inbound command=0x{inbound.Command:X4}, sequence=0x{inbound.Sequence:X4}, " +
            $"payloadLength={inbound.Payload.Length}, frameSha256={Convert.ToHexString(SHA256.HashData(inboundBytes))}. Raw payload is not logged.");

        var transition = session.ProcessInboundFrame(inboundBytes);
        if (!transition.Accepted)
        {
            Console.WriteLine($"Stopped without further write: {transition.Boundary}");
            return;
        }

        foreach (var write in transition.OutboundWrites)
        {
            var decision = guard.AuthorizeNextWrite(write);
            if (!decision.CanSend)
            {
                Console.WriteLine($"Safety guard refused the next write: {decision.Reason}");
                return;
            }

            var bytes = write.ToArray();
            await stream.WriteAsync(bytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            for (var frameIndex = 0; frameIndex < decision.Frames.Count; frameIndex++)
            {
                var frame = decision.Frames[frameIndex];
                Console.WriteLine(
                    $"Sent frame={decision.FramesAuthorized - decision.Frames.Count + frameIndex + 1}/{MiPlayLegacyAudioSourceBootstrapProbeGuard.MaximumFrames}, " +
                    $"command=0x{frame.Command:X4}, sequence=0x{frame.Sequence:X4}, payloadLength={frame.PayloadLength}, " +
                    $"frameSha256={frame.FrameSha256Hex}; write={decision.WritesAuthorized}/{MiPlayLegacyAudioSourceBootstrapProbeGuard.MaximumWrites}.");
            }

            if (decision.BoundaryReached)
            {
                Console.WriteLine("Outbound nine-frame boundary reached; only already-requested acknowledgements will be read.");
            }
        }

        if (transition.Completed)
        {
            Console.WriteLine(
                "Legacy source identity and status bootstrap verified by the receiver. Stopped before 0x0040, Open, AddMirror, RTSP, media, playback, or audio.");
            return;
        }
    }
}

static async Task RunFreshLegacyDeviceInfoReceiverAsync(
    MiPlayPassiveSenderCaptureProfile profile,
    TimeSpan duration,
    bool explicitUserAuthorization,
    bool observePostDeviceInfoGetMirrorMode,
    CancellationToken cancellationToken)
{
    ArgumentNullException.ThrowIfNull(profile);

    var offlineDecision = MiPlayFreshLegacyReceiverBootstrapPlanner.EvaluateCurrentEvidence();
    if (!offlineDecision.CanBuildDeterministicGetDeviceInfoAcknowledgement ||
        offlineDecision.Plan.SafeForNetworkUse)
    {
        throw new InvalidOperationException(
            "The fresh legacy device-info receiver requires a complete offline plan that remains network-disabled until runtime authorization.");
    }

    if (observePostDeviceInfoGetMirrorMode)
    {
        var observationEvidence = MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.Evaluate(
            MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.CreateCurrentSnapshot());
        if (!observationEvidence.CanPredictNextQueuedCommand ||
            observationEvidence.CanUseNetwork ||
            observationEvidence.Plan.SafeForNetworkUse)
        {
            throw new InvalidOperationException(
                "The extended observation requires complete offline post-device-info evidence that remains network-disabled until runtime authorization.");
        }
    }

    var announcement = profile.BuildMdnsAnnouncement();
    var advertisedDevice = MiPlayMdnsMessageParser.Parse(announcement).Single();
    var challengeFrame = profile.BuildLegacyChallengeFrame();
    if (!MiPlayCommandFrameCodec.TryDecode(challengeFrame, out var challenge, out var challengeBytesConsumed) ||
        challenge is null ||
        challengeBytesConsumed != challengeFrame.Length ||
        !MiPlayPassiveSenderCaptureProfile.IsPermittedOutboundCommand(challenge.Command))
    {
        throw new InvalidOperationException("The fresh legacy receiver refused its own one permitted 0x0028 challenge.");
    }

    using var udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
    {
        ExclusiveAddressUse = false,
    };
    udp.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
    udp.SetSocketOption(
        SocketOptionLevel.IP,
        SocketOptionName.AddMembership,
        new MulticastOption(IPAddress.Parse("224.0.0.251"), profile.Address));
    udp.SetSocketOption(
        SocketOptionLevel.IP,
        SocketOptionName.MulticastInterface,
        profile.Address.GetAddressBytes());
    udp.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
    udp.Bind(new IPEndPoint(IPAddress.Any, MiPlayPassiveSenderCaptureProfile.MdnsPort));

    var listener = new TcpListener(profile.Address, MiPlayProtocolConstants.DefaultControlPort);
    listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
    listener.Start(backlog: 1);

    using var run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    var mdnsTask = AdvertisePassiveMiPlayCaptureAsync(udp, announcement, run.Token);
    Console.WriteLine(
        $"Fresh legacy device-info receiver ready for {duration.TotalSeconds:0}s: " +
        $"name={JsonSerializer.Serialize(advertisedDevice.FriendlyName)}, " +
        $"id={profile.DeviceId:D}, address={profile.Address}, " +
        $"mDNS={MiPlayPassiveSenderCaptureProfile.MdnsPort}/UDP, " +
        $"command={MiPlayProtocolConstants.DefaultControlPort}/TCP.");
    Console.WriteLine(
        "Outbound boundary: exactly one legacy 0x0028, then at most one same-sequence clear 0x001f " +
        "only after verified 0x0029 plus one empty clear 0x001e. No 0x0037, 0x0059, 0x001b, " +
        "Open, AddMirror, RTSP, media, playback, audio, retry, or fallback will be sent.");
    if (observePostDeviceInfoGetMirrorMode)
    {
        Console.WriteLine(
            "Extended inbound-only boundary: after 0x001f, accept only exact 0x0058(seq=3,isSameAccount=0) " +
            "then exact empty 0x0034(seq=4); stop on success or any deviation without sending 0x0059, 0x0035, or another frame.");
    }

    try
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        client.NoDelay = true;
        if (client.Client.RemoteEndPoint is not IPEndPoint remoteEndPoint ||
            client.Client.LocalEndPoint is not IPEndPoint localEndPoint)
        {
            throw new InvalidOperationException("The fresh legacy receiver requires connected IP endpoints.");
        }

        Console.WriteLine($"Phone sender connected: remote={remoteEndPoint}, local={localEndPoint}.");
        await using var stream = client.GetStream();
        await stream.WriteAsync(challengeFrame, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        var outboundLegacyChallengeCount = 1;
        var outboundGetDeviceInfoAcknowledgementCount = 0;
        var noOtherOutboundFrames = true;
        Console.WriteLine(
            $"Outbound command=0x{challenge.Command:X4}, sequence=0x{challenge.Sequence:X4}, " +
            $"payloadLength={challenge.Payload.Length}, challenge={JsonSerializer.Serialize(MiPlayPassiveSenderCaptureProfile.ChallengeText)}.");

        var session = new MiPlayFreshLegacyReceiverBootstrapSession();
        var postDeviceInfoObservation = new MiPlayFreshLegacyPostDeviceInfoObservationSession();
        var responseSent = false;
        var postResponseObservationCount = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            byte[] frameBytes;
            try
            {
                frameBytes = await ReadMiPlayFrameAsync(stream, cancellationToken);
            }
            catch (EndOfStreamException)
            {
                Console.WriteLine("Phone sender closed the TCP command connection.");
                break;
            }

            if (!MiPlayCommandFrameCodec.TryDecode(
                    frameBytes,
                    out var frame,
                    out var frameBytesConsumed) ||
                frame is null ||
                frameBytesConsumed != frameBytes.Length)
            {
                throw new InvalidDataException("A fresh legacy receiver frame failed strict MiPlay command decoding.");
            }

            var frameSha256 = Convert.ToHexString(SHA256.HashData(frameBytes));
            Console.WriteLine(
                $"Inbound command=0x{frame.Command:X4}, sequence=0x{frame.Sequence:X4}, " +
                $"payloadLength={frame.Payload.Length}, frameSha256={frameSha256}. Raw bytes are not logged.");

            if (responseSent && observePostDeviceInfoGetMirrorMode)
            {
                var observation = postDeviceInfoObservation.ProcessInboundFrame(frameBytes);
                Console.WriteLine(
                    $"Post-device-info observation phase={observation.Phase}, accepted={observation.Accepted}, " +
                    $"initial0058Race={observation.ExactInitialSetLocalDeviceInfoRaceObserved}, " +
                    $"exact0058={observation.ExactSetLocalDeviceInfoObserved}, " +
                    $"exact0034={observation.ExactGetMirrorModeObserved}, allowsFollowUpSend={observation.AllowsFollowUpSend}.");
                if (!observation.Accepted || observation.Completed)
                {
                    Console.WriteLine(observation.Boundary);
                    break;
                }

                continue;
            }

            var result = session.ProcessInboundFrame(frameBytes);
            Console.WriteLine(
                $"Fresh legacy receiver phase={result.Phase}, accepted={result.Accepted}, " +
                $"legacyAck={result.LegacyAcknowledgementVerified}, emptyGetDeviceInfo={result.EmptyGetDeviceInfoObserved}.");
            if (!result.Accepted)
            {
                Console.WriteLine($"Stopped without a reply: {result.Boundary}");
                break;
            }

            if (result.ResponseCandidate is { } responseCandidate)
            {
                var policy = MiPlayFreshLegacyReceiverProbePolicy.Evaluate(
                    explicitUserAuthorization,
                    result,
                    outboundLegacyChallengeCount,
                    outboundGetDeviceInfoAcknowledgementCount,
                    noOtherOutboundFrames);
                if (!policy.CanSendNow || !policy.SafeForNetworkUse)
                {
                    Console.WriteLine($"Refused 0x001f candidate: {policy.Reason}");
                    break;
                }

                if (!MiPlayCommandFrameCodec.TryDecode(
                        responseCandidate,
                        out var response,
                        out var responseBytesConsumed) ||
                    response is null ||
                    responseBytesConsumed != responseCandidate.Length ||
                    response.Command != MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand ||
                    response.Sequence != session.PendingGetDeviceInfoSequence)
                {
                    throw new InvalidOperationException("The authorized candidate failed the final strict 0x001f accounting check.");
                }

                await stream.WriteAsync(responseCandidate, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                outboundGetDeviceInfoAcknowledgementCount++;
                responseSent = true;
                Console.WriteLine(
                    $"Outbound command=0x{response.Command:X4}, sequence=0x{response.Sequence:X4}, " +
                    $"payloadLength={response.Payload.Length}, frameSha256={Convert.ToHexString(SHA256.HashData(responseCandidate))}. " +
                    "This is the only device-info response; no other frame will be sent.");
                continue;
            }

            if (!responseSent)
            {
                continue;
            }

            postResponseObservationCount++;
            var advancedPastInitialSourceName =
                frame.Command == MiPlayProtocolConstants.SetLocalDeviceInfoCommand &&
                frame.Sequence > 2;
            if (advancedPastInitialSourceName)
            {
                Console.WriteLine(
                    "Observed a new post-0x001f setLocalDeviceInfo sequence, consistent with sender onDeviceInfo progression. " +
                    "Stopping without 0x0059 or any follow-up response.");
                break;
            }

            if (postResponseObservationCount >= 8)
            {
                Console.WriteLine(
                    "Post-0x001f observation limit reached. No 0x0037, 0x0059, 0x001b, business, RTSP, media, playback, or audio frame was sent.");
                break;
            }
        }
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        Console.WriteLine("Fresh legacy device-info receiver window ended.");
    }
    finally
    {
        run.Cancel();
        listener.Stop();
        try
        {
            await mdnsTask;
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested)
        {
        }
    }
}

static async Task RunMutualAuthMiPlaySenderCaptureAsync(
    MiPlayPassiveSenderCaptureProfile profile,
    TimeSpan duration,
    CancellationToken cancellationToken)
{
    ArgumentNullException.ThrowIfNull(profile);

    var announcement = profile.BuildMdnsAnnouncement();
    var advertisedDevice = MiPlayMdnsMessageParser.Parse(announcement).Single();
    var challengeFrame = profile.BuildLegacyChallengeFrame();
    if (!MiPlayCommandFrameCodec.TryDecode(challengeFrame, out var challenge, out var challengeBytesConsumed) ||
        challenge is null ||
        challengeBytesConsumed != challengeFrame.Length ||
        !MiPlayPassiveSenderMutualAuthCaptureSession.IsPermittedOutboundCommand(challenge.Command))
    {
        throw new InvalidOperationException(
            "The mutual-auth sender capture profile refused its own legacy challenge.");
    }

    using var udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
    {
        ExclusiveAddressUse = false,
    };
    udp.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
    udp.SetSocketOption(
        SocketOptionLevel.IP,
        SocketOptionName.AddMembership,
        new MulticastOption(IPAddress.Parse("224.0.0.251"), profile.Address));
    udp.SetSocketOption(
        SocketOptionLevel.IP,
        SocketOptionName.MulticastInterface,
        profile.Address.GetAddressBytes());
    udp.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
    udp.Bind(new IPEndPoint(IPAddress.Any, MiPlayPassiveSenderCaptureProfile.MdnsPort));

    var listener = new TcpListener(profile.Address, MiPlayProtocolConstants.DefaultControlPort);
    listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
    listener.Start(backlog: 1);

    using var run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    var mdnsTask = AdvertisePassiveMiPlayCaptureAsync(udp, announcement, run.Token);
    Console.WriteLine(
        $"Mutual-auth MiPlay sender capture ready for {duration.TotalSeconds:0}s: " +
        $"name={JsonSerializer.Serialize(advertisedDevice.FriendlyName)}, " +
        $"id={profile.DeviceId:D}, address={profile.Address}, " +
        $"mDNS={MiPlayPassiveSenderCaptureProfile.MdnsPort}/UDP, " +
        $"command={MiPlayProtocolConstants.DefaultControlPort}/TCP.");
    Console.WriteLine(
        "Outbound boundary: legacy 0x0028, one same-sequence 0x1401 selection (1,4,1,1,2), " +
        "one receiver 0x1402, and at most one 0x1403 response. After mutual SafetyAuth, " +
        "the capture decrypts exactly one phone-originated post-auth frame and sends no business ACK, " +
        "control, RTSP, media, playback, or audio data.");

    try
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        client.NoDelay = true;
        if (client.Client.RemoteEndPoint is not IPEndPoint remoteEndPoint ||
            client.Client.LocalEndPoint is not IPEndPoint localEndPoint ||
            remoteEndPoint.AddressFamily != AddressFamily.InterNetwork ||
            localEndPoint.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new InvalidOperationException("The mutual-auth sender capture requires connected IPv4 endpoints.");
        }

        Console.WriteLine($"Phone sender connected: remote={remoteEndPoint}, local={localEndPoint}.");
        var receiverSessionInfo = new MiPlayTcpSessionInfo(
            localEndPoint.Address,
            checked((ushort)localEndPoint.Port),
            remoteEndPoint.Address,
            checked((ushort)remoteEndPoint.Port));

        await using var stream = client.GetStream();
        await stream.WriteAsync(challengeFrame, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        Console.WriteLine(
            $"Outbound command=0x{challenge.Command:X4}, sequence=0x{challenge.Sequence:X4}, " +
            $"payloadLength={challenge.Payload.Length}, challenge={JsonSerializer.Serialize(MiPlayPassiveSenderCaptureProfile.ChallengeText)}.");

        MiPlayPassiveSenderMutualAuthCaptureSession? safetySession = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            byte[] frameBytes;
            try
            {
                frameBytes = await ReadMiPlayFrameAsync(stream, cancellationToken);
            }
            catch (EndOfStreamException)
            {
                Console.WriteLine("Phone sender closed the TCP command connection.");
                break;
            }

            if (!MiPlayCommandFrameCodec.TryDecode(
                    frameBytes,
                    out var frame,
                    out var frameBytesConsumed) ||
                frame is null ||
                frameBytesConsumed != frameBytes.Length)
            {
                throw new InvalidDataException(
                    "A captured phone frame failed strict MiPlay command decoding.");
            }

            LogMutualAuthMiPlaySenderFrame(frameBytes, frame);

            if (frame.Command == MiPlayProtocolConstants.SafetyInfoCommand)
            {
                if (safetySession is not null)
                {
                    Console.WriteLine("Stopped on duplicate SafetyInfo 0x1400; no response was sent.");
                    break;
                }

                if (!MiPlayPassiveSenderMutualAuthCaptureSession.TryCreate(
                        receiverSessionInfo,
                        frameBytes,
                        out safetySession,
                        out var safetyInfoAcknowledgementFrame,
                        out var error) ||
                    safetySession is null ||
                    safetyInfoAcknowledgementFrame is null)
                {
                    Console.WriteLine($"Stopped before SafetyAuth: {error} No response was sent.");
                    break;
                }

                var receiverChallengeFrame = safetySession.BuildLocalChallengeFrame(
                    GetUnixTimestampMicroseconds());
                foreach (var outboundFrame in new[] { safetyInfoAcknowledgementFrame, receiverChallengeFrame })
                {
                    if (!MiPlayCommandFrameCodec.TryDecode(
                            outboundFrame,
                            out var outbound,
                            out var outboundBytesConsumed) ||
                        outbound is null ||
                        outboundBytesConsumed != outboundFrame.Length ||
                        !MiPlayPassiveSenderMutualAuthCaptureSession.IsPermittedOutboundCommand(outbound.Command))
                    {
                        throw new InvalidOperationException(
                            "The mutual-auth sender capture refused a generated authentication frame.");
                    }

                    await stream.WriteAsync(outboundFrame, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                    Console.WriteLine(
                        $"Outbound authentication command=0x{outbound.Command:X4}, sequence=0x{outbound.Sequence:X4}, " +
                        $"payloadLength={outbound.Payload.Length}. No key, IV, challenge, or plaintext is logged.");
                }

                continue;
            }

            if (frame.Command is MiPlayProtocolConstants.SafetyAuthCommand or
                MiPlayProtocolConstants.SafetyAuthAcknowledgementCommand)
            {
                if (safetySession is null)
                {
                    Console.WriteLine("Stopped on SafetyAuth before a valid SafetyInfo offer; no response was sent.");
                    break;
                }

                var result = safetySession.ProcessInboundFrame(frameBytes);
                Console.WriteLine(
                    $"SafetyAuth capture phase={result.Phase}, accepted={result.Accepted}, " +
                    $"mutual={result.MutualSafetyAuthComplete}.");
                if (!result.Accepted)
                {
                    Console.WriteLine($"Stopped: {result.Boundary}");
                    break;
                }

                if (result.ResponseFrame is { } responseFrame)
                {
                    if (!MiPlayCommandFrameCodec.TryDecode(
                            responseFrame,
                            out var response,
                            out var responseBytesConsumed) ||
                        response is null ||
                        responseBytesConsumed != responseFrame.Length ||
                        !MiPlayPassiveSenderMutualAuthCaptureSession.IsPermittedOutboundCommand(response.Command))
                    {
                        throw new InvalidOperationException(
                            "The mutual-auth sender capture refused its generated 0x1403 response.");
                    }

                    await stream.WriteAsync(responseFrame, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                    Console.WriteLine(
                        $"Outbound authentication command=0x{response.Command:X4}, sequence=0x{response.Sequence:X4}, " +
                        $"payloadLength={response.Payload.Length}. No key, IV, HMAC, or plaintext is logged.");
                }

                if (result.MutualSafetyAuthComplete)
                {
                    Console.WriteLine(
                        "Mutual SafetyAuth is verified. Waiting only for the phone's first post-auth command; " +
                        "no heartbeat, getDeviceInfo, 0x0058, 0x0040, Open, AddMirror, RTSP, media, playback, audio, retry, or fallback will be sent.");
                }

                continue;
            }

            if (safetySession is null || !safetySession.MutualSafetyAuthComplete)
            {
                continue;
            }

            var capture = safetySession.ProcessInboundFrame(frameBytes);
            if (!capture.Accepted ||
                capture.CapturedCommand is not { } capturedCommand ||
                capture.CapturedSequence is not { } capturedSequence ||
                capture.CapturedPlaintext is not { } capturedPlaintext)
            {
                Console.WriteLine($"Stopped without a usable post-auth vector: {capture.Boundary}");
                break;
            }

            var frameSha256 = Convert.ToHexString(SHA256.HashData(frameBytes));
            var safetyDataSha256 = Convert.ToHexString(SHA256.HashData(frame.Payload));
            var plaintextSha256 = Convert.ToHexString(SHA256.HashData(capturedPlaintext));
            var jsonFields = capturedPlaintext.Length == 0
                ? "empty"
                : DescribeJsonObjectFields(capturedPlaintext);
            Console.WriteLine(
                $"Captured first fresh post-auth phone frame: command=0x{capturedCommand:X4}, " +
                $"sequence=0x{capturedSequence:X4}, SafetyDataPayloadLength={frame.Payload.Length}, " +
                $"plaintextLength={capturedPlaintext.Length}, jsonFields={JsonSerializer.Serialize(jsonFields)}, " +
                $"frameSha256={frameSha256}, SafetyDataSha256={safetyDataSha256}, plaintextSha256={plaintextSha256}.");
            Console.WriteLine(
                "Capture complete. No business acknowledgement or follow-up frame was sent; " +
                "no raw identity, key, IV, ciphertext, HMAC, or plaintext was logged.");
            break;
        }
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        Console.WriteLine("Mutual-auth MiPlay sender capture window ended.");
    }
    finally
    {
        run.Cancel();
        listener.Stop();
        try
        {
            await mdnsTask;
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested)
        {
        }
    }
}

static async Task AdvertisePassiveMiPlayCaptureAsync(
    Socket socket,
    byte[] announcement,
    CancellationToken cancellationToken)
{
    var multicastEndPoint = new IPEndPoint(
        IPAddress.Parse("224.0.0.251"),
        MiPlayPassiveSenderCaptureProfile.MdnsPort);
    var receiveBuffer = new byte[ushort.MaxValue];
    EndPoint anyRemote = new IPEndPoint(IPAddress.Any, 0);

    await socket.SendToAsync(
        announcement,
        SocketFlags.None,
        multicastEndPoint,
        cancellationToken);

    var receiveTask = socket.ReceiveFromAsync(
        receiveBuffer,
        SocketFlags.None,
        anyRemote,
        cancellationToken).AsTask();

    while (!cancellationToken.IsCancellationRequested)
    {
        var announcementDelay = Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        var completed = await Task.WhenAny(receiveTask, announcementDelay);
        if (completed == receiveTask)
        {
            var received = await receiveTask;
            if (IsMiPlayMdnsQuery(receiveBuffer.AsSpan(0, received.ReceivedBytes)))
            {
                await socket.SendToAsync(
                    announcement,
                    SocketFlags.None,
                    received.RemoteEndPoint,
                    cancellationToken);
            }

            receiveTask = socket.ReceiveFromAsync(
                receiveBuffer,
                SocketFlags.None,
                anyRemote,
                cancellationToken).AsTask();
            continue;
        }

        await announcementDelay;
        await socket.SendToAsync(
            announcement,
            SocketFlags.None,
            multicastEndPoint,
            cancellationToken);
    }
}

static bool IsMiPlayMdnsQuery(ReadOnlySpan<byte> datagram)
{
    if (datagram.Length < 12 ||
        (BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(2, 2)) & 0x8000) != 0)
    {
        return false;
    }

    return datagram.IndexOf("_mi-connect"u8) >= 0;
}

static void LogPassiveMiPlaySenderFrame(
    ReadOnlySpan<byte> frameBytes,
    MiPlayCommandFrame frame)
{
    var frameSha256 = Convert.ToHexString(SHA256.HashData(frameBytes));
    var frameBase64 = Convert.ToBase64String(frameBytes);
    Console.WriteLine(
        $"Inbound command=0x{frame.Command:X4}, sequence=0x{frame.Sequence:X4}, " +
        $"payloadLength={frame.Payload.Length}, frameSha256={frameSha256}, " +
        $"frameBase64={frameBase64}");

    if (frame.Command == MiPlayProtocolConstants.LegacySafetyAcknowledgementCommand)
    {
        var expected = MiPlayLegacySafetyChallengeCodec.CreateAcknowledgement(
            MiPlayPassiveSenderCaptureProfile.ChallengeSequence,
            Encoding.ASCII.GetBytes(MiPlayPassiveSenderCaptureProfile.ChallengeText));
        var expectedPayload = Encoding.ASCII.GetBytes(expected.Response);
        var acknowledgementMatches =
            frame.Sequence == expected.Sequence &&
            CryptographicOperations.FixedTimeEquals(frame.Payload, expectedPayload);
        Console.WriteLine(
            $"Legacy 0x0029 acknowledgement valid={acknowledgementMatches}, " +
            $"expectedSequence=0x{expected.Sequence:X4}.");
        return;
    }

    if (frame.Command == MiPlayProtocolConstants.NativeSourceVersionCommand)
    {
        var version = Encoding.ASCII.GetString(frame.Payload).TrimEnd('\0');
        Console.WriteLine($"Native phone source version={JsonSerializer.Serialize(version)}.");
        return;
    }

    if (frame.Command != MiPlayProtocolConstants.SafetyInfoCommand)
    {
        return;
    }

    if (!MiPlaySafetyCommandCodec.TryDecode(
            frameBytes,
            out var safetyCommand,
            out var safetyBytesConsumed) ||
        safetyCommand is null ||
        safetyBytesConsumed != frameBytes.Length)
    {
        Console.WriteLine("SafetyInfo 0x1400 envelope failed strict decoding.");
        return;
    }

    if (MiPlaySafetyInfoCodec.TryDecodeSelection(
            safetyCommand.JsonPayload,
            out var selection) &&
        selection is not null)
    {
        Console.WriteLine(
            "SafetyInfo 0x1400 selection: " +
            $"authKeyType={selection.AuthKeyType?.ToString() ?? "n/a"}, " +
            $"authAlgorithmType={selection.AuthAlgorithmType?.ToString() ?? "n/a"}, " +
            $"integrityType={selection.IntegrityType?.ToString() ?? "n/a"}, " +
            $"aesKeyType={selection.AesKeyType?.ToString() ?? "n/a"}, " +
            $"aesIvType={selection.AesIvType?.ToString() ?? "n/a"}.");
        return;
    }

    Console.WriteLine(
        $"SafetyInfo 0x1400 source offer JSON={JsonSerializer.Serialize(Encoding.UTF8.GetString(safetyCommand.JsonPayload))}, " +
        $"fields=[{DescribeJsonObjectFields(safetyCommand.JsonPayload)}].");
}

static void LogMutualAuthMiPlaySenderFrame(
    ReadOnlySpan<byte> frameBytes,
    MiPlayCommandFrame frame)
{
    var frameSha256 = Convert.ToHexString(SHA256.HashData(frameBytes));
    Console.WriteLine(
        $"Inbound command=0x{frame.Command:X4}, sequence=0x{frame.Sequence:X4}, " +
        $"payloadLength={frame.Payload.Length}, frameSha256={frameSha256}. Raw frame bytes are not logged.");

    if (frame.Command == MiPlayProtocolConstants.LegacySafetyAcknowledgementCommand)
    {
        var expected = MiPlayLegacySafetyChallengeCodec.CreateAcknowledgement(
            MiPlayPassiveSenderCaptureProfile.ChallengeSequence,
            Encoding.ASCII.GetBytes(MiPlayPassiveSenderCaptureProfile.ChallengeText));
        var expectedPayload = Encoding.ASCII.GetBytes(expected.Response);
        var acknowledgementMatches =
            frame.Sequence == expected.Sequence &&
            CryptographicOperations.FixedTimeEquals(frame.Payload, expectedPayload);
        Console.WriteLine(
            $"Legacy 0x0029 acknowledgement valid={acknowledgementMatches}, " +
            $"expectedSequence=0x{expected.Sequence:X4}.");
        return;
    }

    if (frame.Command == MiPlayProtocolConstants.NativeSourceVersionCommand)
    {
        var version = Encoding.ASCII.GetString(frame.Payload).TrimEnd('\0');
        Console.WriteLine($"Native phone source version={JsonSerializer.Serialize(version)}.");
        return;
    }

    if (frame.Command != MiPlayProtocolConstants.SafetyInfoCommand ||
        !MiPlaySafetyCommandCodec.TryDecode(
            frameBytes,
            out var safetyCommand,
            out var safetyBytesConsumed) ||
        safetyCommand is null ||
        safetyBytesConsumed != frameBytes.Length ||
        !MiPlaySafetyInfoCodec.TryDecodeOffer(safetyCommand.JsonPayload, out var offer) ||
        offer is null)
    {
        return;
    }

    Console.WriteLine(
        "SafetyInfo 0x1400 offer: " +
        $"authKeyTypes={offer.AuthKeyTypes}, " +
        $"authAlgorithmTypes={offer.AuthAlgorithmTypes}, " +
        $"integrityTypes={offer.IntegrityTypes}, " +
        $"aesKeyTypes={offer.AesKeyTypes}, " +
        $"aesIvTypes={offer.AesIvTypes}.");
}

static async Task<string> CaptureFirstNoMediaRtspRequestAsync(
    TcpListener listener,
    CancellationToken cancellationToken)
{
    using var client = await listener.AcceptTcpClientAsync(cancellationToken);
    var remoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
    await using var stream = client.GetStream();
    using var buffer = new MemoryStream();
    var scratch = new byte[4096];
    while (buffer.Length <= MiPlayRtspRequestCodec.MaximumHeaderLength + MiPlayRtspRequestCodec.MaximumBodyLength)
    {
        var read = await stream.ReadAsync(scratch, cancellationToken);
        if (read == 0)
        {
            throw new EndOfStreamException("The receiver closed the RTSP callback before sending a complete request.");
        }

        buffer.Write(scratch, 0, read);
        var bytes = buffer.ToArray();
        if (!MiPlayRtspRequestCodec.TryDecode(bytes, out var request, out var consumed) || request is null)
        {
            continue;
        }

        var decision = MiPlayNoMediaRtspProbePolicy.EvaluateFirstRtspRequest(request);
        var headers = string.Join(", ", request.Headers.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).Select(pair => $"{pair.Key}={pair.Value}"));
        return $"No-media RTSP/WFD listener captured first receiver callback from {remoteEndPoint}: method={request.Method}, target={request.RequestTarget}, version={request.Version}, consumedBytes={consumed}, bodyLength={request.Body.Length}, headers=[{headers}], useful={decision.IsUsefulEvidence}, decision={JsonSerializer.Serialize(decision.Reason)}. The listener sent no RTSP response, media, RTP, playback, or audio data.";
    }

    throw new InvalidDataException("The receiver RTSP callback exceeded the no-media capture safety limit before a complete request was decoded.");
}

static async Task<byte[]> ReadMiPlayFrameAsync(NetworkStream stream, CancellationToken cancellationToken)
{
    var header = new byte[MiPlayProtocolConstants.CommandHeaderLength];
    await stream.ReadExactlyAsync(header, cancellationToken);

    if (header[0] != MiPlayProtocolConstants.CommandFrameMagic)
    {
        throw new InvalidDataException($"Unexpected command-frame magic 0x{header[0]:X2}.");
    }

    var payloadLength = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(5, 4));
    if (payloadLength > MiPlayCommandFrameCodec.MaximumPayloadLength)
    {
        throw new InvalidDataException($"Command payload length {payloadLength} exceeds the safety limit.");
    }

    var frame = new byte[header.Length + (int)payloadLength];
    header.CopyTo(frame, 0);
    await stream.ReadExactlyAsync(frame.AsMemory(header.Length, (int)payloadLength), cancellationToken);
    return frame;
}

static void PrintMdnsDevices(string serviceName, IReadOnlyList<MiPlayMdnsDevice> devices)
{
    Console.WriteLine($"{serviceName} devices: {devices.Count}");
    foreach (var device in devices)
    {
        var capabilities = device.Capabilities;
        var protocol = capabilities.SupportsMiPlayAudio ? "MiPlay Audio" : "unknown";
        var security = capabilities.SecurityMode?.ToString() ?? "unknown";
        var applications = string.Join(',', capabilities.ApplicationIds);
        var controlPort = capabilities.MiPlayAudioAppData?.ControlPort.ToString() ?? "n/a";
        var lyra = capabilities.MiPlayAudioAppData?.SupportsLyra.ToString() ?? "n/a";
        Console.WriteLine($"- {device.FriendlyName} | {device.Address} | coapPort={device.Port?.ToString() ?? "n/a"} | controlPort={controlPort} | protocol={protocol} | apps=[{applications}] | version={capabilities.VersionMajor}.{capabilities.VersionMinor} | security={security} | lyra={lyra} | micoDeviceId={device.MicoAppData?.DeviceId ?? "n/a"} | {device.InstanceName}");
        foreach (var record in device.TxtRecords.OrderBy(record => record.Key, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"  {record.Key}={record.Value}");
        }
    }
}

static void PrintOfficialPostAuthSequenceDryRun()
{
    var snapshot = MiPlayOfficialPostAuthSequenceDryRunEvidence.CreateCurrentSnapshot();
    var decision = MiPlayOfficialPostAuthSequenceDryRunEvidence.Evaluate(snapshot);
    var redactedFirstJson = MiPlayRealPhonePostAuthPlaintextEvidence.FirstSetLocalDeviceInfoJson.Replace(
        MiPlayRealPhonePostAuthPlaintextEvidence.RecoveredOfficialSourceBluetoothMacHash,
        "<redacted-md5>",
        StringComparison.Ordinal);

    Console.WriteLine(
        "Official post-auth sequence dry-run: " +
        $"firstSequence=0x{snapshot.FirstCommandSequence:X4}, " +
        $"usesRecoveredIdentity={snapshot.UsesRecoveredOfficialSourceIdentity}, " +
        $"firstPlaintextLength={snapshot.RecoveredOfficialFirstPlaintextLength}, " +
        $"firstSafetyDataPayloadLength={snapshot.RecoveredOfficialFirstSafetyDataPayloadLength}, " +
        $"previousDefaultWindowsSafetyDataPayloadLength={snapshot.PreviousDefaultWindowsFirstSafetyDataPayloadLength}, " +
        $"matchesRecoveredPhonePcapLength={snapshot.FirstFrameMatchesRecoveredPhonePcapLength}, " +
        $"safeForNetworkUse={snapshot.SafeForNetworkUse}.");
    Console.WriteLine($"Recovered first 0x0058 plaintext={JsonSerializer.Serialize(redactedFirstJson)}.");

    foreach (var step in snapshot.Steps)
    {
        Console.WriteLine(
            $"- step={step.Kind}, command=0x{step.Command:X4}, sequence=0x{step.Sequence:X4}, " +
            $"plaintextLength={step.PlaintextPayloadLength}, safetyDataPayloadLength={step.SafetyDataPayloadLength}, " +
            $"commandFrameLength={step.CommandFrameLength}, acknowledgementGate={step.AcknowledgementGate}");
    }

    Console.WriteLine($"Decision: preparedRecoveredOfficialFirstFrame={decision.PreparedRecoveredOfficialFirstFrame}, authorizesNetworkSend={decision.AuthorizesNetworkSend}, reason={JsonSerializer.Serialize(decision.Reason)}.");
    Console.WriteLine("Dry-run only: no socket is opened and no S12/LAN, 0x0058, 0x001e, 0x0034, 0x0040, Open, AddMirror, RTSP, media, playback, or audio frame is sent.");
}

static void PrintMiPlayPcapSummary(string path)
{
    if (string.IsNullOrWhiteSpace(path))
    {
        throw new ArgumentException("--miplay-scan-pcap requires a classic tcpdump pcap path.");
    }

    var fullPath = Path.GetFullPath(path);
    var result = MiPlayTcpdumpPcapDecoder.Decode(File.ReadAllBytes(fullPath));
    Console.WriteLine(
        $"Offline MiPlay pcap summary: path={JsonSerializer.Serialize(fullPath)}, " +
        $"tcpPayloads={result.TcpPayloads.Count}, commandFrames={result.CommandFrames.Count}, issues={result.Issues.Count}. No network operation is performed.");

    foreach (var captured in result.CommandFrames)
    {
        Console.WriteLine(
            $"packet={captured.PacketIndex}, direction={captured.SourceEndpoint}->{captured.DestinationEndpoint}, " +
            $"command=0x{captured.Frame.Command:X4}, sequence=0x{captured.Frame.Sequence:X4}, " +
            $"payloadLength={captured.Frame.PayloadLength}, payloadSha256={captured.Frame.PayloadSha256Hex}.");
    }

    foreach (var issue in result.Issues)
    {
        Console.WriteLine($"issue packet={issue.PacketIndex}: {issue.Reason}");
    }
}

static void PrintMiPlayStraceSummary(string path)
{
    if (string.IsNullOrWhiteSpace(path))
    {
        throw new ArgumentException("--miplay-scan-strace requires a strace text path.");
    }

    var fullPath = Path.GetFullPath(path);
    var result = MiPlayStraceNetworkCaptureDecoder.Decode(File.ReadAllText(fullPath));
    Console.WriteLine(
        $"Offline MiPlay strace summary: path={JsonSerializer.Serialize(fullPath)}, " +
        $"tcpChunks={result.Chunks.Count}, commandFrames={result.Frames.Count}, issues={result.Issues.Count}, " +
        $"containsRawPayloads={result.ContainsRawPayloads}. No network operation is performed.");

    foreach (var captured in result.Frames)
    {
        Console.WriteLine(
            $"line={captured.FirstLineNumber}, direction={captured.Direction}, endpoint={captured.Endpoint}, " +
            $"command=0x{captured.Command:X4}, sequence=0x{captured.Sequence:X4}, " +
            $"payloadLength={captured.PayloadLength}, payloadSha256={captured.PayloadSha256Hex}, " +
            $"frameSha256={captured.FrameSha256Hex}.");
    }

    foreach (var issue in result.Issues)
    {
        Console.WriteLine($"issue line={issue.LineNumber}: {issue.Reason}");
    }
}

var network = new NetworkProfileService().GetStatus();
Console.WriteLine($"Network: {(network.IsPrivate ? "Private" : "Blocked")} - {network.Summary}");

var audioCatalog = new AudioSourceCatalog();
Console.WriteLine($"Output endpoints: {audioCatalog.GetOutputDevices().Count}");
Console.WriteLine($"Active audio processes: {audioCatalog.GetCandidateProcesses().Count}");

await using var discovery = new RendererDiscoveryService();
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
var devices = await discovery.SearchAsync(timeout.Token);
Console.WriteLine($"Renderers: {devices.Count}");
using var controller = new RendererController();
foreach (var device in devices)
{
    var volume = await controller.GetVolumeAsync(device, timeout.Token);
    Console.WriteLine($"- {device.FriendlyName} | {device.ModelName} | {device.Address} | udn={device.Udn} | sink={device.SinkProtocolInfo} | volume={volume?.ToString() ?? "n/a"}");
}

if (args.Contains("--capture-smoke", StringComparer.OrdinalIgnoreCase))
{
    var output = audioCatalog.GetOutputDevices().First();
    await using var buffer = new PcmFrameBuffer();
    await using var capture = audioCatalog.CreateCapture(new DLNACast.Core.Models.CaptureSelection.SystemMix(output.Id, output.DisplayName));
    using var captureTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    await capture.StartAsync(buffer, captureTimeout.Token);
    await Task.Delay(1_000, captureTimeout.Token);
    await capture.StopAsync();
    Console.WriteLine($"System loopback: started, buffered={buffer.BufferedMilliseconds}ms, overruns={buffer.Overruns}");
}

if (args.Contains("--process-smoke", StringComparer.OrdinalIgnoreCase))
{
    var process = audioCatalog.GetCandidateProcesses().FirstOrDefault()
                  ?? throw new InvalidOperationException("No active audio process is available for process-loopback smoke test.");
    await using var buffer = new PcmFrameBuffer();
    await using var capture = audioCatalog.CreateCapture(new DLNACast.Core.Models.CaptureSelection.Process(
        process.ProcessId!.Value, process.DisplayName, true));
    using var captureTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
    await capture.StartAsync(buffer, captureTimeout.Token);
    await Task.Delay(1_000, captureTimeout.Token);
    await capture.StopAsync();
    Console.WriteLine($"Process loopback: {process.DisplayName}, started, buffered={buffer.BufferedMilliseconds}ms, overruns={buffer.Overruns}");
}
