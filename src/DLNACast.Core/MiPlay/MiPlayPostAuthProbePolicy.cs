namespace DLNACast.Core.MiPlay;

public sealed record MiPlayPostAuthStagedDeviceInfoDecision(bool CanSend, string Reason);

/// <summary>
/// Offline policy checks for constrained post-auth MiPlay probes. It only decides
/// whether a previously observed and decrypted command satisfies the static
/// native gate for the next diagnostic step; it never sends frames.
/// </summary>
public static class MiPlayPostAuthProbePolicy
{
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

        if (decryptedPayloadLength == 0)
        {
            return new MiPlayPostAuthStagedDeviceInfoDecision(false, "The decrypted getDeviceInfo acknowledgement payload is empty.");
        }

        return new MiPlayPostAuthStagedDeviceInfoDecision(true, "The decrypted getDeviceInfo acknowledgement matches the staged local device info gate.");
    }
}
