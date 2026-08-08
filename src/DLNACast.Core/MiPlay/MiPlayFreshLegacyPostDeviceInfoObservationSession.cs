using System.Security.Cryptography;

namespace DLNACast.Core.MiPlay;

public sealed record MiPlayFreshLegacyPostDeviceInfoObservationResult(
    bool Accepted,
    bool Completed,
    string Phase,
    ushort ObservedCommand,
    ushort ObservedSequence,
    bool ExactInitialSetLocalDeviceInfoRaceObserved,
    bool ExactSetLocalDeviceInfoObserved,
    bool ExactGetMirrorModeObserved,
    bool AllowsFollowUpSend,
    string Boundary);

/// <summary>
/// Pure, strict post-0x001f observation state. It accepts only the two exact
/// source frames predicted by the recovered official path and never creates a
/// response candidate or permits a follow-up send.
/// </summary>
public sealed class MiPlayFreshLegacyPostDeviceInfoObservationSession
{
    private bool exactInitialSetLocalDeviceInfoRaceObserved;
    private bool exactSetLocalDeviceInfoObserved;
    private bool completed;

    public bool ExactInitialSetLocalDeviceInfoRaceObserved => exactInitialSetLocalDeviceInfoRaceObserved;
    public bool ExactSetLocalDeviceInfoObserved => exactSetLocalDeviceInfoObserved;
    public bool Completed => completed;

    public MiPlayFreshLegacyPostDeviceInfoObservationResult ProcessInboundFrame(
        ReadOnlySpan<byte> frameBytes)
    {
        if (!MiPlayCommandFrameCodec.TryDecode(frameBytes, out var frame, out var bytesConsumed) ||
            frame is null ||
            bytesConsumed != frameBytes.Length)
        {
            return Reject(0, 0, "The post-0x001f bytes are not one complete MiPlay command frame.");
        }

        if (completed)
        {
            return Reject(frame.Command, frame.Sequence, "The exact 0x0034 observation already completed; no additional frame is accepted.");
        }

        if (!exactSetLocalDeviceInfoObserved)
        {
            var expectedSetLocalDeviceInfo =
                MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.ReconstructAdvancedSetLocalDeviceInfoFrame();
            if (CryptographicOperations.FixedTimeEquals(frameBytes, expectedSetLocalDeviceInfo))
            {
                exactSetLocalDeviceInfoObserved = true;
                return new MiPlayFreshLegacyPostDeviceInfoObservationResult(
                    Accepted: true,
                    Completed: false,
                    Phase: "exact-setLocalDeviceInfo-observed",
                    frame.Command,
                    frame.Sequence,
                    exactInitialSetLocalDeviceInfoRaceObserved,
                    ExactSetLocalDeviceInfoObserved: true,
                    ExactGetMirrorModeObserved: false,
                    AllowsFollowUpSend: false,
                    "Observed the exact 0x0058 sequence 0x0003 frame; continue reading without sending 0x0059 or any other response.");
            }

            var expectedInitialSetLocalDeviceInfo =
                MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.ReconstructInitialSetLocalDeviceInfoFrame();
            if (!exactInitialSetLocalDeviceInfoRaceObserved &&
                CryptographicOperations.FixedTimeEquals(frameBytes, expectedInitialSetLocalDeviceInfo))
            {
                exactInitialSetLocalDeviceInfoRaceObserved = true;
                return new MiPlayFreshLegacyPostDeviceInfoObservationResult(
                    Accepted: true,
                    Completed: false,
                    Phase: "exact-initial-setLocalDeviceInfo-race-observed",
                    frame.Command,
                    frame.Sequence,
                    ExactInitialSetLocalDeviceInfoRaceObserved: true,
                    ExactSetLocalDeviceInfoObserved: false,
                    ExactGetMirrorModeObserved: false,
                    AllowsFollowUpSend: false,
                    "Observed the exact initial 0x0058 sequence 0x0002 sourceName frame after 0x001f; this proven race is read-only, so continue waiting for sequence 0x0003 without sending 0x0059.");
            }

            return Reject(
                frame.Command,
                frame.Sequence,
                exactInitialSetLocalDeviceInfoRaceObserved
                    ? "Expected the byte-exact 0x0058 sequence 0x0003 isSameAccount=0 frame after the one allowed initial sequence 0x0002 race frame."
                    : "Expected either the byte-exact initial 0x0058 sequence 0x0002 race frame or the byte-exact advanced 0x0058 sequence 0x0003 isSameAccount=0 frame after the one permitted 0x001f.");
        }

        var plan = MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.CreateOfflinePlan();
        if (!CryptographicOperations.FixedTimeEquals(frameBytes, plan.PredictedGetMirrorModeFrame))
        {
            return Reject(
                frame.Command,
                frame.Sequence,
                "Expected the byte-exact empty 0x0034 sequence 0x0004 frame after the exact 0x0058; stop without a reply.");
        }

        completed = true;
        return new MiPlayFreshLegacyPostDeviceInfoObservationResult(
            Accepted: true,
            Completed: true,
            Phase: "exact-getMirrorMode-observed",
            frame.Command,
            frame.Sequence,
            exactInitialSetLocalDeviceInfoRaceObserved,
            ExactSetLocalDeviceInfoObserved: true,
            ExactGetMirrorModeObserved: true,
            AllowsFollowUpSend: false,
            "Observed the predicted empty 0x0034 sequence 0x0004. Stop without 0x0035 or any other response.");
    }

    private MiPlayFreshLegacyPostDeviceInfoObservationResult Reject(
        ushort command,
        ushort sequence,
        string boundary) =>
        new(
            Accepted: false,
            Completed: completed,
            Phase: "stopped",
            command,
            sequence,
            exactInitialSetLocalDeviceInfoRaceObserved,
            exactSetLocalDeviceInfoObserved,
            ExactGetMirrorModeObserved: completed,
            AllowsFollowUpSend: false,
            boundary);
}
