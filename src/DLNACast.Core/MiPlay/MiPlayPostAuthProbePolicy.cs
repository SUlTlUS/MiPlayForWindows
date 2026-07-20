namespace DLNACast.Core.MiPlay;

public sealed record MiPlayPostAuthStagedDeviceInfoDecision(bool CanSend, string Reason);

public enum MiPlayPostAuthConnectionMode
{
    Unknown = 0,
    LegacyTcp8899 = 1,
    LyraContinuityChannel = 2,
}

public sealed record MiPlayPostAuthGetDeviceInfoPrerequisites(
    bool MutualSafetyAuthVerified,
    bool CommandSessionListenerRegisteredBeforeSafetyDone,
    bool DealSafetyDoneListenerEventDelivered,
    bool JavaOnSuccessDispatched,
    bool SourceIdentityAvailable,
    bool DeviceContextAvailable,
    MiPlayPostAuthConnectionMode ConnectionMode,
    ushort NextCommandSequence,
    bool ReadOnlyProbeBoundary);

/// <summary>
/// Offline policy checks for constrained post-auth MiPlay probes. It only decides
/// whether a previously observed and decrypted command satisfies the static
/// native gate for the next diagnostic step; it never sends frames.
/// </summary>
public static class MiPlayPostAuthProbePolicy
{
    public const int MinimumDeviceInfoAcknowledgementPayloadLength = 40;

    public static MiPlayPostAuthStagedDeviceInfoDecision EvaluateGetDeviceInfoReadiness(
        MiPlayPostAuthGetDeviceInfoPrerequisites prerequisites)
    {
        if (!prerequisites.MutualSafetyAuthVerified)
        {
            return new MiPlayPostAuthStagedDeviceInfoDecision(false, "Mutual SafetyAuth has not been verified.");
        }

        if (!prerequisites.CommandSessionListenerRegisteredBeforeSafetyDone)
        {
            return new MiPlayPostAuthStagedDeviceInfoDecision(
                false,
                "The command-session listener registration that receives DealSafetyDone is not established.");
        }

        if (!prerequisites.DealSafetyDoneListenerEventDelivered)
        {
            return new MiPlayPostAuthStagedDeviceInfoDecision(
                false,
                "DealSafetyDone has not been delivered to the command-session listener.");
        }

        if (!prerequisites.JavaOnSuccessDispatched)
        {
            return new MiPlayPostAuthStagedDeviceInfoDecision(
                false,
                "The Java onSuccess callback that schedules getDeviceInfo has not been dispatched.");
        }

        if (!prerequisites.SourceIdentityAvailable)
        {
            return new MiPlayPostAuthStagedDeviceInfoDecision(
                false,
                "The source identity context is missing.");
        }

        if (!prerequisites.DeviceContextAvailable)
        {
            return new MiPlayPostAuthStagedDeviceInfoDecision(
                false,
                "The target device context is missing.");
        }

        if (prerequisites.ConnectionMode != MiPlayPostAuthConnectionMode.LegacyTcp8899)
        {
            return new MiPlayPostAuthStagedDeviceInfoDecision(
                false,
                "The current connection mode is not the legacy TCP 8899 command-session path.");
        }

        if (prerequisites.NextCommandSequence == 0)
        {
            return new MiPlayPostAuthStagedDeviceInfoDecision(false, "The next command sequence is not initialized.");
        }

        if (!prerequisites.ReadOnlyProbeBoundary)
        {
            return new MiPlayPostAuthStagedDeviceInfoDecision(
                false,
                "The proposed getDeviceInfo probe is not constrained to a read-only boundary.");
        }

        return new MiPlayPostAuthStagedDeviceInfoDecision(
            true,
            "The static post-auth listener, identity, connection, and read-only gates are satisfied for getDeviceInfo.");
    }

    public static MiPlayPostAuthStagedDeviceInfoDecision EvaluateStagedLocalDeviceInfoGate(
        bool awaitingGetDeviceInfoAcknowledgement,
        bool hasLocalDeviceInfoPayloads,
        bool alreadySentLocalDeviceInfo,
        ushort observedCommand,
        ushort observedSequence,
        ushort expectedGetDeviceInfoSequence,
        int decryptedPayloadLength)
    {
        if (decryptedPayloadLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(decryptedPayloadLength));
        }

        if (!awaitingGetDeviceInfoAcknowledgement)
        {
            return new MiPlayPostAuthStagedDeviceInfoDecision(false, "No staged getDeviceInfo acknowledgement is pending.");
        }

        if (!hasLocalDeviceInfoPayloads)
        {
            return new MiPlayPostAuthStagedDeviceInfoDecision(false, "No staged local device info payloads are available.");
        }

        if (alreadySentLocalDeviceInfo)
        {
            return new MiPlayPostAuthStagedDeviceInfoDecision(false, "The staged local device info frames were already sent.");
        }

        if (observedCommand != MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand)
        {
            return new MiPlayPostAuthStagedDeviceInfoDecision(false, "The observed command is not getDeviceInfo acknowledgement.");
        }

        if (observedSequence != expectedGetDeviceInfoSequence)
        {
            return new MiPlayPostAuthStagedDeviceInfoDecision(false, "The getDeviceInfo acknowledgement sequence does not match the pending request.");
        }

        if (decryptedPayloadLength < MinimumDeviceInfoAcknowledgementPayloadLength)
        {
            return new MiPlayPostAuthStagedDeviceInfoDecision(
                false,
                $"The decrypted getDeviceInfo acknowledgement payload is shorter than {MinimumDeviceInfoAcknowledgementPayloadLength} bytes.");
        }

        return new MiPlayPostAuthStagedDeviceInfoDecision(true, "The decrypted getDeviceInfo acknowledgement matches the staged local device info gate.");
    }
}
