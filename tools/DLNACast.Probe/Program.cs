using System.Net;
using System.Net.Sockets;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using DLNACast.Core.Audio;
using DLNACast.Core.Dlna;
using DLNACast.Core.MiPlay;
using DLNACast.Core.Platform;

Console.OutputEncoding = System.Text.Encoding.UTF8;

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
var safetyProbeArgument = nativeSafetyMutualAuthLocalDeviceInfoArgument ?? nativeSafetyMutualAuthDeviceInfoArgument ?? nativeSafetyMutualAuthHeartbeatArgument ?? nativeSafetyMutualAuthObserveArgument ?? nativeSafetyMutualAuthArgument ?? nativeSafetyAuthArgument ?? nativeSafetyDecryptArgument ?? nativeSafetyArgument ?? safetyOfferArgument ?? args.FirstOrDefault(argument =>
    argument.StartsWith("--miplay-safety-probe=", StringComparison.OrdinalIgnoreCase));
if (safetyProbeArgument is not null)
{
    var addressText = safetyProbeArgument[(safetyProbeArgument.IndexOf('=') + 1)..];
    if (!IPAddress.TryParse(addressText, out var deviceAddress) || deviceAddress.AddressFamily != AddressFamily.InterNetwork)
    {
        throw new ArgumentException("Use --miplay-safety-probe=<device IPv4 address>, --miplay-safety-offer=<device IPv4 address>, --miplay-native-safety-probe=<device IPv4 address>, --miplay-native-safety-decrypt-probe=<device IPv4 address>, --miplay-native-safety-auth-probe=<device IPv4 address>, --miplay-native-safety-mutual-auth-probe=<device IPv4 address>, --miplay-native-safety-mutual-auth-observe-probe=<device IPv4 address>, --miplay-native-safety-mutual-auth-heartbeat-probe=<device IPv4 address>, --miplay-native-safety-mutual-auth-device-info-probe=<device IPv4 address>, or --miplay-native-safety-mutual-auth-local-device-info-probe=<device IPv4 address>; for example --miplay-safety-probe=192.168.10.4.");
    }

    if (nativeSafetyMutualAuthLocalDeviceInfoArgument is not null &&
        (nativeSafetyMutualAuthDeviceInfoArgument is not null ||
         nativeSafetyMutualAuthHeartbeatArgument is not null ||
         nativeSafetyMutualAuthObserveArgument is not null))
    {
        throw new ArgumentException("Use only one post-auth MiPlay probe option at a time.");
    }

    (byte[] SourceNamePayload, byte[] LocalDeviceInfoPayload, string SourceNameJson, string LocalDeviceInfoJson)? postAuthLocalDeviceInfoPayloads = nativeSafetyMutualAuthLocalDeviceInfoArgument is null
        ? null
        : CreatePostAuthLocalDeviceInfoPayloads(args);

    await ProbeMiPlayLegacySafetyAsync(
        deviceAddress,
        sendSafetyInfoOffer: safetyOfferArgument is not null || nativeSafetyArgument is not null || nativeSafetyDecryptArgument is not null || nativeSafetyAuthArgument is not null || nativeSafetyMutualAuthArgument is not null || nativeSafetyMutualAuthObserveArgument is not null || nativeSafetyMutualAuthHeartbeatArgument is not null || nativeSafetyMutualAuthDeviceInfoArgument is not null || nativeSafetyMutualAuthLocalDeviceInfoArgument is not null,
        sendNativeBootstrap: nativeSafetyArgument is not null || nativeSafetyDecryptArgument is not null || nativeSafetyAuthArgument is not null || nativeSafetyMutualAuthArgument is not null || nativeSafetyMutualAuthObserveArgument is not null || nativeSafetyMutualAuthHeartbeatArgument is not null || nativeSafetyMutualAuthDeviceInfoArgument is not null || nativeSafetyMutualAuthLocalDeviceInfoArgument is not null,
        decryptSafetyAuth: nativeSafetyDecryptArgument is not null || nativeSafetyAuthArgument is not null || nativeSafetyMutualAuthArgument is not null || nativeSafetyMutualAuthObserveArgument is not null || nativeSafetyMutualAuthHeartbeatArgument is not null || nativeSafetyMutualAuthDeviceInfoArgument is not null || nativeSafetyMutualAuthLocalDeviceInfoArgument is not null,
        sendSafetyAuthAcknowledgement: nativeSafetyAuthArgument is not null || nativeSafetyMutualAuthArgument is not null || nativeSafetyMutualAuthObserveArgument is not null || nativeSafetyMutualAuthHeartbeatArgument is not null || nativeSafetyMutualAuthDeviceInfoArgument is not null || nativeSafetyMutualAuthLocalDeviceInfoArgument is not null,
        sendLocalSafetyAuthChallenge: nativeSafetyMutualAuthArgument is not null || nativeSafetyMutualAuthObserveArgument is not null || nativeSafetyMutualAuthHeartbeatArgument is not null || nativeSafetyMutualAuthDeviceInfoArgument is not null || nativeSafetyMutualAuthLocalDeviceInfoArgument is not null,
        observeAfterMutualSafetyAuth: nativeSafetyMutualAuthObserveArgument is not null || nativeSafetyMutualAuthHeartbeatArgument is not null || nativeSafetyMutualAuthDeviceInfoArgument is not null || nativeSafetyMutualAuthLocalDeviceInfoArgument is not null,
        sendPostAuthGetDeviceInfo: nativeSafetyMutualAuthDeviceInfoArgument is not null,
        sendPostAuthHeartbeat: nativeSafetyMutualAuthHeartbeatArgument is not null,
        postAuthLocalDeviceInfoPayloads: postAuthLocalDeviceInfoPayloads);
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
    bool sendPostAuthHeartbeat,
    (byte[] SourceNamePayload, byte[] LocalDeviceInfoPayload, string SourceNameJson, string LocalDeviceInfoJson)? postAuthLocalDeviceInfoPayloads)
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
    MiPlaySafetyHashAlgorithm? safetyAuthAlgorithm = null;
    MiPlaySafetyAuthChallenge? localSafetyAuthChallenge = null;
    (string Label, string AuthKey, MiPlaySafetyDataSessionCipher Cipher)? selectedSafetyAuthCandidate = null;
    List<(string Label, string AuthKey, MiPlaySafetyDataSessionCipher Cipher)>? safetyAesCandidates = null;
    async Task<bool> CompleteMutualSafetyAuthAsync()
    {
        if (!sendLocalSafetyAuthChallenge ||
            !sentSafetyAuthAcknowledgement ||
            !verifiedPeerSafetyAuthAcknowledgement)
        {
            return false;
        }

        if (postAuthLocalDeviceInfoPayloads is { } localDeviceInfoPayloads)
        {
            if (selectedSafetyAuthCandidate is not { } postAuthCandidate)
            {
                Console.WriteLine("Refused post-auth local device info sequence: no verified SafetyData session candidate is available.");
                return true;
            }

            var getDeviceInfoSequence = sendNativeBootstrap ? (ushort)4 : (ushort)3;
            var sourceNameSequence = checked((ushort)(getDeviceInfoSequence + 1));
            var localDeviceInfoSequence = checked((ushort)(getDeviceInfoSequence + 2));
            var getDeviceInfoPayload = postAuthCandidate.Cipher.EncryptVersion1(ReadOnlySpan<byte>.Empty);
            var sourceNamePayload = postAuthCandidate.Cipher.EncryptVersion1(localDeviceInfoPayloads.SourceNamePayload);
            var localDeviceInfoPayload = postAuthCandidate.Cipher.EncryptVersion1(localDeviceInfoPayloads.LocalDeviceInfoPayload);
            var getDeviceInfoFrame = MiPlayCommandFrameCodec.Encode(
                MiPlayProtocolConstants.GetDeviceInfoCommand,
                getDeviceInfoSequence,
                getDeviceInfoPayload);
            var sourceNameFrame = MiPlayCommandFrameCodec.Encode(
                MiPlayProtocolConstants.SetLocalDeviceInfoCommand,
                sourceNameSequence,
                sourceNamePayload);
            var localDeviceInfoFrame = MiPlayCommandFrameCodec.Encode(
                MiPlayProtocolConstants.SetLocalDeviceInfoCommand,
                localDeviceInfoSequence,
                localDeviceInfoPayload);

            using var postAuthSendTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await stream.WriteAsync(getDeviceInfoFrame, postAuthSendTimeout.Token);
            await stream.FlushAsync(postAuthSendTimeout.Token);
            await stream.WriteAsync(sourceNameFrame, postAuthSendTimeout.Token);
            await stream.FlushAsync(postAuthSendTimeout.Token);
            await stream.WriteAsync(localDeviceInfoFrame, postAuthSendTimeout.Token);
            await stream.FlushAsync(postAuthSendTimeout.Token);

            sentPostAuthCommand = MiPlayProtocolConstants.SetLocalDeviceInfoCommand;
            sentPostAuthBoundaryDescription = "post-auth 0x001e getDeviceInfo plus two 0x0058 setLocalDeviceInfo frames";
            completedMutualSafetyAuth = true;
            Console.WriteLine($"Mutual SafetyAuth completed: local 0x1402 was acknowledged by peer 0x1403, and peer 0x1402 was acknowledged by local 0x1403. Sent post-auth sequence candidate={postAuthCandidate.Label}: command=0x{MiPlayProtocolConstants.GetDeviceInfoCommand:X4}, sequence=0x{getDeviceInfoSequence:X4}, encryptedPayloadLength={getDeviceInfoPayload.Length}, plaintextLength=0; command=0x{MiPlayProtocolConstants.SetLocalDeviceInfoCommand:X4}, sequence=0x{sourceNameSequence:X4}, encryptedPayloadLength={sourceNamePayload.Length}, plaintextJson={JsonSerializer.Serialize(localDeviceInfoPayloads.SourceNameJson)}; command=0x{MiPlayProtocolConstants.SetLocalDeviceInfoCommand:X4}, sequence=0x{localDeviceInfoSequence:X4}, encryptedPayloadLength={localDeviceInfoPayload.Length}, plaintextJson={JsonSerializer.Serialize(localDeviceInfoPayloads.LocalDeviceInfoJson)}. The probe will now only observe for 5 seconds; no additional getDeviceInfo, setLocalDeviceInfo, heartbeat, media, RTSP, audio, playback, openDevice, or other control data will be sent.");
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
            var postAuthPayload = postAuthCandidate.Cipher.EncryptVersion1(ReadOnlySpan<byte>.Empty);
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
            Console.WriteLine($"Mutual SafetyAuth completed: local 0x1402 was acknowledged by peer 0x1403, and peer 0x1402 was acknowledged by local 0x1403. Sent one post-auth {postAuthName} command=0x{postAuthCommand:X4}, sequence=0x{postAuthSequence:X4}, encryptedPayloadLength={postAuthPayload.Length}, candidate={postAuthCandidate.Label}. The probe will now only observe for 5 seconds; no additional heartbeat, getDeviceInfo, setLocalDeviceInfo, media, RTSP, audio, playback, openDevice, or other control data will be sent.");
            return false;
        }

        if (observeAfterMutualSafetyAuth)
        {
            completedMutualSafetyAuth = true;
            Console.WriteLine("Mutual SafetyAuth completed: local 0x1402 was acknowledged by peer 0x1403, and peer 0x1402 was acknowledged by local 0x1403. Entering read-only post-auth observation for 5 seconds; no post-auth heartbeat, getDeviceInfo, setLocalDeviceInfo, media, RTSP, audio, playback, openDevice, or other control data will be sent.");
            return false;
        }

        Console.WriteLine("Mutual SafetyAuth completed: local 0x1402 was acknowledged by peer 0x1403, and peer 0x1402 was acknowledged by local 0x1403. Stopping before media or playback.");
        return true;
    }

    try
    {
        while (true)
        {
            using var readTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
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
            }

            if (completedMutualSafetyAuth)
            {
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

                    if (sentPostAuthCommand == MiPlayProtocolConstants.HeartbeatCommand &&
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
                    Console.WriteLine("Post-auth observe mode: frame logged, but SafetyData decrypt did not succeed; no response or control data will be sent.");
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
                        safetyAesCandidates = new List<(string Label, string AuthKey, MiPlaySafetyDataSessionCipher Cipher)>
                        {
                            selectedSafetyAuthCandidate.Value
                        };
                        localSafetyAuthChallenge = MiPlaySafetyAuthCodec.CreateChallenge(GetUnixTimestampMicroseconds());
                        var localSafetyAuthPlaintext = MiPlaySafetyEnvelopeCodec.Encode(
                            isAcknowledgement: false,
                            MiPlayProtocolConstants.SafetyValueType,
                            localSafetyAuthChallenge.ToJsonPayload());
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
            ? $"after mutual SafetyAuth completion and {canceledPostAuthBoundary}; no further data was sent"
            : completedMutualSafetyAuth
            ? "after mutual SafetyAuth completion; no post-auth heartbeat, getDeviceInfo, setLocalDeviceInfo, media, RTSP, audio, playback, openDevice, or other control data was sent"
            : sentSafetyAuthAcknowledgement
            ? "after sending one 0x1403 acknowledgement; no further data was sent"
            : "without sending 0x1403, media, or playback data";
        Console.WriteLine($"Observation ended after 5 seconds with {observedFrames} follow-up frame(s), {safetyAuthBoundary}.");
    }
    catch (EndOfStreamException)
    {
        var safetyAuthBoundary = completedMutualSafetyAuth && sentPostAuthBoundaryDescription is { } closedPostAuthBoundary
            ? $"after mutual SafetyAuth completion and {closedPostAuthBoundary}; no further data was sent"
            : completedMutualSafetyAuth
            ? "after mutual SafetyAuth completion; no post-auth data was sent"
            : sentSafetyAuthAcknowledgement
            ? "after one 0x1403 acknowledgement"
            : "without sending 0x1403";
        Console.WriteLine($"The device closed the connection after {observedFrames} follow-up frame(s), {safetyAuthBoundary}. The probe sends no further data.");
    }
    catch (IOException exception) when (exception.InnerException is SocketException socketException)
    {
        var safetyAuthBoundary = completedMutualSafetyAuth && sentPostAuthBoundaryDescription is { } abortedPostAuthBoundary
            ? $"after mutual SafetyAuth completion and {abortedPostAuthBoundary}; no further data was sent"
            : completedMutualSafetyAuth
            ? "after mutual SafetyAuth completion; no post-auth data was sent"
            : sentSafetyAuthAcknowledgement
            ? "after one 0x1403 acknowledgement"
            : "without sending 0x1403";
        Console.WriteLine($"The TCP connection was aborted while reading after {observedFrames} follow-up frame(s), socketError={socketException.SocketErrorCode}, nativeError={socketException.NativeErrorCode}, message={JsonSerializer.Serialize(exception.Message)}, {safetyAuthBoundary}. The probe sends no further data.");
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

static string? GetOptionValue(IEnumerable<string> args, string prefix)
{
    var argument = args.FirstOrDefault(value =>
        value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    return argument is null
        ? null
        : argument[prefix.Length..];
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
