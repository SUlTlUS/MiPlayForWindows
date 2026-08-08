using DLNACast.Core.Abstractions;
using DLNACast.Core.Localization;
using DLNACast.Core.Models;
using NAudio.CoreAudioApi;

namespace DLNACast.Core.Audio;

public sealed class WindowsLocalOutputManager : ILocalOutputManager
{
    internal const string VirtualSpeakerName = "DLNA Cast Virtual Speaker";
    private static readonly Role[] Roles = [Role.Console, Role.Multimedia, Role.Communications];

    public ValueTask<ILocalOutputLease> RouteForCastAsync(
        CaptureSelection selection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var virtualSpeaker = FindVirtualSpeaker();

        return ValueTask.FromResult(selection switch
        {
            CaptureSelection.SystemMix => RouteSystemAudio(virtualSpeaker),
            CaptureSelection.Process process => RouteProcessAudio(process, virtualSpeaker),
            _ => throw new ArgumentOutOfRangeException(nameof(selection))
        });
    }

    private static VirtualSpeakerEndpoint FindVirtualSpeaker()
    {
        using var enumerator = new MMDeviceEnumerator();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (device)
            {
                if (device.FriendlyName.Contains(VirtualSpeakerName, StringComparison.OrdinalIgnoreCase))
                {
                    return new VirtualSpeakerEndpoint(device.ID, device.FriendlyName);
                }
            }
        }

        throw new InvalidOperationException(SystemLanguage.Select(
            "未找到 DLNA Cast 虚拟扬声器。请先安装项目内的虚拟音频驱动并重启 Windows。",
            "The DLNA Cast virtual speaker was not found. Install the bundled virtual audio driver and restart Windows first."));
    }

    private static ILocalOutputLease RouteSystemAudio(VirtualSpeakerEndpoint virtualSpeaker)
    {
        var previousEndpoints = new Dictionary<Role, string>();
        using (var enumerator = new MMDeviceEnumerator())
        {
            foreach (var role in Roles)
            {
                using var previous = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, role);
                previousEndpoints[role] = previous.ID;
            }
        }

        try
        {
            foreach (var role in Roles)
            {
                WindowsAudioPolicy.SetDefaultEndpoint(virtualSpeaker.Id, role);
            }
        }
        catch
        {
            RestoreSystemDefaults(previousEndpoints);
            throw;
        }

        return new SystemRouteLease(
            new CaptureSelection.SystemMix(virtualSpeaker.Id, virtualSpeaker.FriendlyName),
            previousEndpoints);
    }

    private static ILocalOutputLease RouteProcessAudio(
        CaptureSelection.Process process,
        VirtualSpeakerEndpoint virtualSpeaker)
    {
        var previousEndpoints = Roles.ToDictionary(
            role => role,
            role => WindowsAudioPolicy.GetPersistedDefaultEndpoint(process.ProcessId, role));

        try
        {
            foreach (var role in Roles)
            {
                WindowsAudioPolicy.SetPersistedDefaultEndpoint(process.ProcessId, role, virtualSpeaker.Id);
            }
        }
        catch
        {
            RestoreProcessDefaults(process.ProcessId, previousEndpoints);
            throw;
        }

        // Process loopback is endpoint-independent and remains the most reliable
        // capture path. Routing the process to the silent virtual speaker removes
        // local playback without changing what the process capture receives.
        return new ProcessRouteLease(process, previousEndpoints);
    }

    private static void RestoreSystemDefaults(IReadOnlyDictionary<Role, string> endpoints)
    {
        foreach (var (role, endpointId) in endpoints)
        {
            try { WindowsAudioPolicy.SetDefaultEndpoint(endpointId, role); }
            catch { }
        }
    }

    private static void RestoreProcessDefaults(
        int processId,
        IReadOnlyDictionary<Role, string?> endpoints)
    {
        foreach (var (role, endpointId) in endpoints)
        {
            try { WindowsAudioPolicy.SetPersistedDefaultEndpoint(processId, role, endpointId); }
            catch { }
        }
    }

    private sealed class SystemRouteLease(
        CaptureSelection captureSelection,
        IReadOnlyDictionary<Role, string> previousEndpoints) : ILocalOutputLease
    {
        private int _restored;
        public CaptureSelection CaptureSelection { get; } = captureSelection;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _restored, 1) == 0)
            {
                RestoreSystemDefaults(previousEndpoints);
            }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ProcessRouteLease(
        CaptureSelection captureSelection,
        IReadOnlyDictionary<Role, string?> previousEndpoints) : ILocalOutputLease
    {
        private int _restored;
        public CaptureSelection CaptureSelection { get; } = captureSelection;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _restored, 1) == 0 &&
                CaptureSelection is CaptureSelection.Process process)
            {
                RestoreProcessDefaults(process.ProcessId, previousEndpoints);
            }
            return ValueTask.CompletedTask;
        }
    }

    private sealed record VirtualSpeakerEndpoint(string Id, string FriendlyName);
}
