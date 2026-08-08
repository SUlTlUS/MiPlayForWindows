namespace DLNACast.Core.MiPlay;

public sealed record MiPlayLegacySourceStatusCommandEvidence(
    string Name,
    ushort RequestCommand,
    ushort NormalAcknowledgementCommand,
    int DispatcherCompareAddress,
    int LogStringLoadAddress,
    int ResponseCommandLoadAddress,
    bool RequestObservedOnRealSource,
    bool NormalAcknowledgementObservedOnRealReceiver,
    string ModeTwoBehavior);

public sealed record MiPlayLegacySourceStatusQuerySnapshot(
    string ReceiverBinary,
    string ReceiverBinarySha256Hex,
    string ReceiverFirmwareVersion,
    string RealSourceCaptureArtifact,
    IReadOnlyList<MiPlayLegacySourceStatusCommandEvidence> Commands,
    IReadOnlyList<ushort> FirstReceiverAOrder,
    IReadOnlyList<ushort> FirstReceiverBOrder,
    bool RelativeOrderIsStable,
    bool SafeForNetworkUse,
    string Boundary);

/// <summary>
/// Cross-evidence mapping of the read-only status queries sent immediately
/// after the legacy source bootstrap. Static addresses are from LX06 1.88.51;
/// live ordering and responses are from the 1.94.13/1.78.61 receivers and are
/// not represented as proof that every firmware uses identical internals.
/// </summary>
public static class MiPlayLegacySourceStatusQueryEvidence
{
    public const string MediaInfoNotificationPayloadSha256Hex =
        "871BA314F60CC56027F578F5871D1B910525DCF5FB9524CD74CDFEE63CC15014";

    public static MiPlayLegacySourceStatusQuerySnapshot CreateCurrentSnapshot() =>
        new(
            "artifacts/firmware/mico_lx06_1.88.51/rootfs-extracted/usr/bin/mpas",
            "9336BA754E864DEE015CDEE688BC45631570133C8E64EF46EBEDD6800D805C43",
            "LX06 1.88.51",
            MiPlayRealLegacySourceFreshSessionEvidence.ArtifactPath,
            [
                new(
                    "Cmd_GetVolume",
                    MiPlayProtocolConstants.GetVolumeCommand,
                    MiPlayProtocolConstants.GetVolumeAcknowledgementCommand,
                    0x65c2c,
                    0x65c54,
                    0x65cd4,
                    RequestObservedOnRealSource: true,
                    NormalAcknowledgementObservedOnRealReceiver: true,
                    "Both receivers returned a five-byte zero-tag/u32-be 0x000f: values 25 and 24."),
                new(
                    "Cmd_GetPosition",
                    MiPlayProtocolConstants.GetPositionCommand,
                    MiPlayProtocolConstants.GetPositionAcknowledgementCommand,
                    0x679e4,
                    0x67a0c,
                    0x67ac0,
                    RequestObservedOnRealSource: false,
                    NormalAcknowledgementObservedOnRealReceiver: false,
                    "Mapped statically but absent from this no-playback capture."),
                new(
                    "Cmd_GetMediaInfo",
                    MiPlayProtocolConstants.GetMediaInfoCommand,
                    MiPlayProtocolConstants.GetMediaInfoAcknowledgementCommand,
                    0x65d6c,
                    0x65da8,
                    0x65e58,
                    RequestObservedOnRealSource: true,
                    NormalAcknowledgementObservedOnRealReceiver: false,
                    "In mirror mode 2 the 1.88.51 branch at 0x6aa34 sends 0x0022 (mov r1,#34 at 0x6aa10); both live receivers likewise emitted a same-hash 158-byte 0x0022 after each query instead of 0x0015."),
                new(
                    "Cmd_GetState",
                    MiPlayProtocolConstants.GetStateCommand,
                    MiPlayProtocolConstants.GetStateAcknowledgementCommand,
                    0x66534,
                    0x6655c,
                    0x665d0,
                    RequestObservedOnRealSource: true,
                    NormalAcknowledgementObservedOnRealReceiver: true,
                    "Both receivers returned the same five-byte zero-tag/u32-be state value 0 in 0x001d."),
            ],
            FirstReceiverAOrder:
            [
                MiPlayProtocolConstants.GetVolumeCommand,
                MiPlayProtocolConstants.GetStateCommand,
                MiPlayProtocolConstants.GetMediaInfoCommand,
            ],
            FirstReceiverBOrder:
            [
                MiPlayProtocolConstants.GetVolumeCommand,
                MiPlayProtocolConstants.GetMediaInfoCommand,
                MiPlayProtocolConstants.GetStateCommand,
            ],
            RelativeOrderIsStable: false,
            SafeForNetworkUse: false,
            "The three empty queries are named and byte-shaped. Volume/state have same-sequence scalar acknowledgements; mirror-mode-2 media info is an asynchronous, receiver-sequenced 0x0022 observation and is not a startup acknowledgement gate. Keep active use separately authorized and bounded.");
}
