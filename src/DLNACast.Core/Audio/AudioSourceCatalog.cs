using System.Diagnostics;
using DLNACast.Core.Abstractions;
using DLNACast.Core.Models;
using NAudio.CoreAudioApi;

namespace DLNACast.Core.Audio;

public sealed class AudioSourceCatalog : IAudioSourceCatalog
{
    public AudioSourceItem GetDefaultOutputDevice()
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        return new AudioSourceItem(device.ID, device.FriendlyName);
    }

    public IReadOnlyList<AudioSourceItem> GetOutputDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        return [.. enumerator
            .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Select(device => new AudioSourceItem(device.ID, device.FriendlyName))
            .OrderBy(device => device.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
    }

    public IReadOnlyList<AudioSourceItem> GetCandidateProcesses()
    {
        var activeProcessIds = GetActiveAudioProcessIds();
        var candidates = new List<AudioSourceItem>();

        foreach (var processId in activeProcessIds)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.Id == Environment.ProcessId || process.HasExited)
                {
                    continue;
                }

                var title = process.MainWindowTitle;
                var displayName = string.IsNullOrWhiteSpace(title)
                    ? process.ProcessName
                    : $"{title} ({process.ProcessName})";
                candidates.Add(new AudioSourceItem(process.Id.ToString(), displayName, process.Id));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // The process may exit or become inaccessible while audio sessions are enumerated.
            }
        }

        return [.. candidates
            .GroupBy(item => item.ProcessId)
            .Select(group => group.First())
            .OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
    }

    public IAudioCaptureSource CreateCapture(CaptureSelection selection) => selection switch
    {
        CaptureSelection.SystemMix systemMix => new SystemLoopbackCaptureSource(systemMix),
        CaptureSelection.Process process => new ProcessLoopbackCaptureSource(process),
        _ => throw new ArgumentOutOfRangeException(nameof(selection))
    };

    private static IReadOnlySet<int> GetActiveAudioProcessIds()
    {
        var result = new HashSet<int>();
        using var enumerator = new MMDeviceEnumerator();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            try
            {
                var sessions = device.AudioSessionManager.Sessions;
                for (var i = 0; i < sessions.Count; i++)
                {
                    var processId = (int)sessions[i].GetProcessID;
                    if (processId > 0)
                    {
                        result.Add(processId);
                    }
                }
            }
            catch
            {
                // A driver can invalidate its session collection while endpoints are changing.
            }
        }

        return result;
    }
}
