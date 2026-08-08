using System.Security.Cryptography;

namespace DLNACast.Core.MiPlay;

public sealed record MiPlayFreshLegacyReceiverDeviceInfoProfile(
    string AccountId,
    string AlonePlayCapacity,
    string BluetoothMac,
    string CanAlonePlayCtrl,
    string Channel,
    string DeviceId,
    string DeviceType,
    string GroupId,
    string GroupName,
    string HouseId,
    string IsMaster,
    string MiName,
    string MiotDid,
    string Model,
    string P2pSupport,
    string RomVersion,
    string RoomName,
    string RoomId,
    string SerialNumber,
    string Support)
{
    public IReadOnlyList<KeyValuePair<string, string>> ToOrderedFields() =>
        [
            KeyValuePair.Create("accountId", AccountId),
            KeyValuePair.Create("alonePlayCapacity", AlonePlayCapacity),
            KeyValuePair.Create("bluetoothMac", BluetoothMac),
            KeyValuePair.Create("canAlonePlayCtrl", CanAlonePlayCtrl),
            KeyValuePair.Create("channel", Channel),
            KeyValuePair.Create("deviceId", DeviceId),
            KeyValuePair.Create("deviceType", DeviceType),
            KeyValuePair.Create("groupId", GroupId),
            KeyValuePair.Create("groupName", GroupName),
            KeyValuePair.Create("house_Id", HouseId),
            KeyValuePair.Create("isMaster", IsMaster),
            KeyValuePair.Create("miName", MiName),
            KeyValuePair.Create("miotDid", MiotDid),
            KeyValuePair.Create("model", Model),
            KeyValuePair.Create("p2pSupport", P2pSupport),
            KeyValuePair.Create("romVersion", RomVersion),
            KeyValuePair.Create("roomName", RoomName),
            KeyValuePair.Create("room_Id", RoomId),
            KeyValuePair.Create("sn", SerialNumber),
            KeyValuePair.Create("support", Support),
        ];
}

public sealed record MiPlayFreshLegacyReceiverBootstrapPlan(
    MiPlayFreshLegacyReceiverDeviceInfoProfile DeviceInfoProfile,
    ushort GetDeviceInfoRequestSequence,
    byte[] DeviceInfoPayload,
    byte[] GetDeviceInfoAcknowledgementFrame,
    string GetDeviceInfoAcknowledgementFrameSha256,
    bool NativeVersionAcknowledgementRequiredBeforeSourceCommands,
    bool BuildsSetLocalDeviceInfoAcknowledgement,
    bool BuildsHeartbeatAcknowledgement,
    bool SafeForNetworkUse);

public sealed record MiPlayFreshLegacyReceiverBootstrapDecision(
    bool CanBuildDeterministicGetDeviceInfoAcknowledgement,
    bool SourceProgressesWithoutNativeVersionAcknowledgement,
    bool CanSendNow,
    string Reason,
    string RemainingBoundary,
    MiPlayFreshLegacyReceiverBootstrapPlan Plan);

/// <summary>
/// Pure offline plan for the first receiver response that the official legacy
/// sender actually waits on. It builds one same-sequence clear 0x001f candidate
/// and deliberately does not build 0x0037, 0x0059, 0x001b, or any business,
/// RTSP, media, playback, or audio frame.
/// </summary>
public static class MiPlayFreshLegacyReceiverBootstrapPlanner
{
    public const string SenderLogArtifact =
        "artifacts/phone_live/fresh-legacy-captures/fresh-legacy-20260807-014741.milink-logcat.txt";

    public const string SenderCausality =
        "CmdSource::onRecvCmd Cmd_Auth -> Java cmd_sessionsuccess/onSuccess -> getDeviceInfo + setLocalDeviceInfo(sourceName); 0x001f later triggers onDeviceInfo";

    public static MiPlayFreshLegacyReceiverDeviceInfoProfile CreateDefaultDeviceInfoProfile()
    {
        var capture = MiPlayPassiveSenderCaptureProfile.CreateDefault(
            System.Net.IPAddress.Parse("192.168.10.9"));

        return new MiPlayFreshLegacyReceiverDeviceInfoProfile(
            AccountId: string.Empty,
            AlonePlayCapacity: "0",
            BluetoothMac: CreateLocallyAdministeredBluetoothMac(capture.DeviceId),
            CanAlonePlayCtrl: "0",
            Channel: "center",
            DeviceId: capture.DeviceId.ToString("D"),
            DeviceType: "4",
            GroupId: string.Empty,
            GroupName: string.Empty,
            HouseId: string.Empty,
            IsMaster: "0",
            MiName: capture.FriendlyName,
            MiotDid: string.Empty,
            Model: "DLNACast.LegacyReceiver",
            P2pSupport: "0",
            RomVersion: "0.1.0",
            RoomName: string.Empty,
            RoomId: string.Empty,
            SerialNumber: string.Empty,
            Support: "audio");
    }

    public static MiPlayFreshLegacyReceiverBootstrapPlan CreateOfflinePlan(ushort getDeviceInfoRequestSequence)
    {
        var profile = CreateDefaultDeviceInfoProfile();
        var payload = MiPlayLegacyDeviceInfoPayloadCodec.Encode(profile.ToOrderedFields());
        var frame = MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand,
            getDeviceInfoRequestSequence,
            payload);

        return new MiPlayFreshLegacyReceiverBootstrapPlan(
            profile,
            getDeviceInfoRequestSequence,
            payload,
            frame,
            Convert.ToHexString(SHA256.HashData(frame)),
            NativeVersionAcknowledgementRequiredBeforeSourceCommands: false,
            BuildsSetLocalDeviceInfoAcknowledgement: false,
            BuildsHeartbeatAcknowledgement: false,
            SafeForNetworkUse: false);
    }

    public static MiPlayFreshLegacyReceiverBootstrapDecision EvaluateCurrentEvidence(
        ushort getDeviceInfoRequestSequence = 1)
    {
        var sourceCapture = MiPlayFreshLegacySenderCaptureEvidence.EvaluateCaptureBoundary(
            MiPlayFreshLegacySenderCaptureEvidence.CreateCurrentSnapshot());
        var receiverStatic = MiPlayLx06MpasReceiverEvidence.EvaluateGetDeviceInfoCommandAlignment(
            MiPlayLx06MpasReceiverEvidence.CreateCurrentSnapshot());
        var receiverLive = MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.EvaluateLiveResult(
            MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence.CreateCurrentSnapshot());
        var sourceLive = MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.EvaluateLiveResult(
            MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.CreateCurrentSnapshot());
        var plan = CreateOfflinePlan(getDeviceInfoRequestSequence);

        var canBuild =
            sourceCapture.ProvesFreshLegacyClearBranch &&
            sourceCapture.ProvesExactSetLocalDeviceInfoPayload &&
            receiverStatic.CanProceed &&
            receiverLive.CanProceed &&
            sourceLive.CanProceed &&
            MiPlayCommandFrameCodec.TryDecode(
                plan.GetDeviceInfoAcknowledgementFrame,
                out var acknowledgement,
                out var bytesConsumed) &&
            acknowledgement is not null &&
            bytesConsumed == plan.GetDeviceInfoAcknowledgementFrame.Length &&
            acknowledgement.Command == MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand &&
            acknowledgement.Sequence == getDeviceInfoRequestSequence &&
            MiPlayLegacyDeviceInfoPayloadCodec.TryDecode(
                acknowledgement.Payload,
                out var deviceInfo,
                out var payloadBytesConsumed) &&
            deviceInfo is not null &&
            payloadBytesConsumed == acknowledgement.Payload.Length;

        return new MiPlayFreshLegacyReceiverBootstrapDecision(
            canBuild,
            SourceProgressesWithoutNativeVersionAcknowledgement: true,
            CanSendNow: false,
            canBuild
                ? $"The fresh sender trace proves 0x0037 is not a prerequisite: receiving legacy 0x0028 caused Cmd_Auth, cmd_sessionsuccess/onSuccess, then clear 0x001e and 0x0058. LX06 static evidence and the prior S12 legacy-clear validation prove same-sequence 0x001f framing. The authorized distinct-receiver run then sent this exact deterministic 20-field 0x001f and the official source advanced from 0x0058 sequence 0x0002 to 0x0003. The bootstrap is wire-validated, while SafeForNetworkUse remains false outside a separately authorized run."
                : "The source capture, LX06 0x001e->0x001f mapping, receiver response, deterministic payload decode, or fresh-source progression evidence is incomplete.",
            "The first legacy receiver identity/bootstrap gate is now closed. Further work must remain offline until a separate, explicitly bounded command is justified; do not infer permission for 0x0059, 0x001b, Open, AddMirror, RTSP, media, playback, or audio.",
            plan);
    }

    private static string CreateLocallyAdministeredBluetoothMac(Guid deviceId)
    {
        var hash = SHA256.HashData(deviceId.ToByteArray());
        Span<byte> mac = stackalloc byte[6];
        hash.AsSpan(0, mac.Length).CopyTo(mac);
        mac[0] = (byte)((mac[0] & 0xfc) | 0x02);
        return string.Join(':', mac.ToArray().Select(value => value.ToString("X2")));
    }
}
