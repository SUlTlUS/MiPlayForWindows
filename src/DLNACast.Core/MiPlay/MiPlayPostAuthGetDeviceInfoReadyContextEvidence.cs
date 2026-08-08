namespace DLNACast.Core.MiPlay;

public sealed record MiPlayPostAuthGetDeviceInfoReadyContextSnapshot(
    bool OfficialPhoneOrderGetsDeviceInfoBeforeSetPlaySource,
    bool CmdSourceGetDeviceInfoUsesEmpty001ePayload,
    bool CmdSourceSendCmdPayloadSafetyDataWrapsOriginalCommand,
    bool Source001fAckListenerLocalized,
    bool Receiver18851GetDeviceInfoHandlerPreservesSequence,
    bool ReceiverDeviceInfoPayloadCodecAvailable,
    bool Current19413LegacyClear001fObserved,
    bool Current19413PostAuthSafetyData001fObserved,
    bool CurrentProbeReproducesListenerOnSuccessReadyContext,
    bool CandidateIsReadOnlyGetDeviceInfoOnly,
    bool CandidateForbids0058OpenAddMirrorRtspMediaPlaybackAudio,
    bool NoNetworkOperationPerformed);

public sealed record MiPlayPostAuthGetDeviceInfoReadOnlyPlan(
    ushort Command,
    ushort ExpectedAcknowledgement,
    ushort FirstCandidateSequence,
    int PlaintextPayloadLength,
    int MinimumAcknowledgementPayloadLength,
    bool RequiresSafetyDataWrapper,
    bool RequiresSameSequenceAcknowledgement,
    bool SafeForNetworkUse,
    string Boundary);

public sealed record MiPlayPostAuthGetDeviceInfoReadyContextDecision(
    bool CanSendLiveReadOnlyProbe,
    bool CanWriteOfflineReadOnlyPlan,
    bool CanAdvanceToLocalDeviceInfoGate,
    MiPlayPostAuthGetDeviceInfoReadOnlyPlan? Plan,
    string Reason,
    string NextOfflineTarget);

/// <summary>
/// Offline-only evidence for the post-auth getDeviceInfo ready-context gate.
/// It joins the phone-side official command order with the native source frame
/// shape and receiver acknowledgement semantics, but deliberately keeps the
/// generated plan unsafe for network use until a fresh authorization and
/// pre-send review occur.
/// </summary>
public static class MiPlayPostAuthGetDeviceInfoReadyContextEvidence
{
    public const string OfficialOrder =
        "Mi13P source path reaches CmdSessionControl.getDeviceInfo() from cmdSessionSuccess before later StatsUtils.setPlaySource events";

    public const string SourceFrameShape =
        "CmdSource::getDeviceInfo sends original command 0x001e with empty plaintext through sendCmdPayload; when SafetyDataDeal is installed, sendCmdPayload wraps the original outer command";

    public const string SourceAckObservation =
        "CmdSource::onRecvCmd routes 0x001f to the device-info ACK listener at vtable +0x28 and must match the pending command sequence";

    public const string ReceiverSemantics =
        "LX06 1.88.51 mpas maps 0x001e to same-sequence 0x001f and does not inspect request payload bytes; 0x001f carries receiver context only";

    public const string CurrentRuntimeBoundary =
        "LX06 1.94.13 legacy clear 0x001e produced a parsed 0x001f, but no post-auth SafetyData-wrapped 0x001e/0x001f success has been observed";

    public const string MissingReadyContext =
        "the Probe has not reproduced the official listener/onSuccess ready context around cmdSessionSuccess before getDeviceInfo";

    public static MiPlayPostAuthGetDeviceInfoReadyContextSnapshot CreateCurrentSnapshot() =>
        new(
            OfficialPhoneOrderGetsDeviceInfoBeforeSetPlaySource: true,
            CmdSourceGetDeviceInfoUsesEmpty001ePayload: true,
            CmdSourceSendCmdPayloadSafetyDataWrapsOriginalCommand: true,
            Source001fAckListenerLocalized: true,
            Receiver18851GetDeviceInfoHandlerPreservesSequence: true,
            ReceiverDeviceInfoPayloadCodecAvailable: true,
            Current19413LegacyClear001fObserved: true,
            Current19413PostAuthSafetyData001fObserved: false,
            CurrentProbeReproducesListenerOnSuccessReadyContext: false,
            CandidateIsReadOnlyGetDeviceInfoOnly: true,
            CandidateForbids0058OpenAddMirrorRtspMediaPlaybackAudio: true,
            NoNetworkOperationPerformed: true);

    public static MiPlayPostAuthGetDeviceInfoReadyContextDecision Evaluate(
        MiPlayPostAuthGetDeviceInfoReadyContextSnapshot snapshot)
    {
        if (!snapshot.NoNetworkOperationPerformed)
        {
            return new MiPlayPostAuthGetDeviceInfoReadyContextDecision(
                false,
                false,
                false,
                null,
                "This evidence must remain offline-only; a network operation would invalidate the static ready-context boundary.",
                "restore an offline-only evidence boundary");
        }

        if (!snapshot.OfficialPhoneOrderGetsDeviceInfoBeforeSetPlaySource)
        {
            return new MiPlayPostAuthGetDeviceInfoReadyContextDecision(
                false,
                false,
                false,
                null,
                "The official phone command order has not proven getDeviceInfo before SetPlaySource.",
                "finish the phone-firmware cmdSessionSuccess/getDeviceInfo trace");
        }

        if (!snapshot.CmdSourceGetDeviceInfoUsesEmpty001ePayload ||
            !snapshot.CmdSourceSendCmdPayloadSafetyDataWrapsOriginalCommand ||
            !snapshot.Source001fAckListenerLocalized)
        {
            return new MiPlayPostAuthGetDeviceInfoReadyContextDecision(
                false,
                false,
                false,
                null,
                "The source-side post-auth 0x001e frame or 0x001f observation path is incomplete.",
                "recover CmdSource::getDeviceInfo/sendCmdPayload/onRecvCmd evidence before planning any read-only validation");
        }

        if (!snapshot.Receiver18851GetDeviceInfoHandlerPreservesSequence ||
            !snapshot.ReceiverDeviceInfoPayloadCodecAvailable ||
            !snapshot.Current19413LegacyClear001fObserved)
        {
            return new MiPlayPostAuthGetDeviceInfoReadyContextDecision(
                false,
                false,
                false,
                null,
                "The receiver-side 0x001e/0x001f semantics are not sufficiently localized.",
                "complete receiver getDeviceInfo acknowledgement parsing before designing the post-auth gate");
        }

        var plan = CreateOfflineReadOnlyPlan();
        if (!snapshot.CandidateIsReadOnlyGetDeviceInfoOnly ||
            !snapshot.CandidateForbids0058OpenAddMirrorRtspMediaPlaybackAudio)
        {
            return new MiPlayPostAuthGetDeviceInfoReadyContextDecision(
                false,
                false,
                false,
                plan,
                "The candidate expands beyond a single read-only getDeviceInfo frame.",
                "restore the no-0x0058/no-open/no-media boundary");
        }

        if (!snapshot.CurrentProbeReproducesListenerOnSuccessReadyContext)
        {
            return new MiPlayPostAuthGetDeviceInfoReadyContextDecision(
                false,
                true,
                false,
                plan,
                $"The next useful target is a read-only getDeviceInfo ready-context plan, not another 0x0040. {OfficialOrder}; {SourceFrameShape}; {SourceAckObservation}. Because {MissingReadyContext}, the plan remains SafeForNetworkUse=false and requires fresh explicit authorization before any S12 send.",
                "localize or emulate the listener/onSuccess ready context, then pre-review one SafetyData-wrapped 0x001e candidate that only observes for same-sequence 0x001f");
        }

        if (!snapshot.Current19413PostAuthSafetyData001fObserved)
        {
            return new MiPlayPostAuthGetDeviceInfoReadyContextDecision(
                false,
                true,
                false,
                plan,
                $"A bounded read-only plan can be written, but current LX06 1.94.13 has not returned a decryptable post-auth SafetyData 0x001f. {CurrentRuntimeBoundary}.",
                "perform only offline byte-level plan review until a fresh live-readonly authorization is given");
        }

        return new MiPlayPostAuthGetDeviceInfoReadyContextDecision(
            false,
            false,
            true,
            plan,
            "A same-sequence decryptable post-auth 0x001f has been observed and parsed; only then may the separate local-device-info gate be evaluated. This still does not authorize 0x0058/Open/AddMirror/media without a separate plan.",
            "evaluate MiPlayPostAuthProbePolicy.EvaluateStagedLocalDeviceInfoGate against the parsed 0x001f");
    }

    public static MiPlayPostAuthGetDeviceInfoReadOnlyPlan CreateOfflineReadOnlyPlan() =>
        new(
            Command: MiPlayProtocolConstants.GetDeviceInfoCommand,
            ExpectedAcknowledgement: MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand,
            FirstCandidateSequence: 0x0004,
            PlaintextPayloadLength: MiPlayLegacyTcp8899PostAuthFramingEvidence.CmdSourceGetDeviceInfoPayloadLength,
            MinimumAcknowledgementPayloadLength: MiPlayPostAuthProbePolicy.MinimumDeviceInfoAcknowledgementPayloadLength,
            RequiresSafetyDataWrapper: true,
            RequiresSameSequenceAcknowledgement: true,
            SafeForNetworkUse: false,
            Boundary: "exactly one SafetyData-wrapped 0x001e with empty plaintext; observe only for same-sequence 0x001f; no 0x0040, 0x0058, Open, AddMirror, RTSP, media, playback, audio, retry, or fallback");
}
