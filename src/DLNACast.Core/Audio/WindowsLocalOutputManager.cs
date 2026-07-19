using DLNACast.Core.Abstractions;
using DLNACast.Core.Models;
using NAudio.CoreAudioApi;

namespace DLNACast.Core.Audio;

public sealed class WindowsLocalOutputManager : ILocalOutputManager
{
    public ValueTask<IAsyncDisposable> MuteForCastAsync(
        CaptureSelection selection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var enumerator = new MMDeviceEnumerator();
        using var device = selection switch
        {
            CaptureSelection.SystemMix systemMix => enumerator.GetDevice(systemMix.EndpointId),
            CaptureSelection.Process => enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia),
            _ => throw new ArgumentOutOfRangeException(nameof(selection))
        };

        var endpointId = device.ID;
        var wasMuted = device.AudioEndpointVolume.Mute;
        if (!wasMuted)
        {
            device.AudioEndpointVolume.Mute = true;
        }

        return ValueTask.FromResult<IAsyncDisposable>(new EndpointMuteLease(endpointId, wasMuted));
    }

    private sealed class EndpointMuteLease(string endpointId, bool wasMuted) : IAsyncDisposable
    {
        private int _restored;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _restored, 1) != 0)
            {
                return ValueTask.CompletedTask;
            }

            try
            {
                using var enumerator = new MMDeviceEnumerator();
                using var device = enumerator.GetDevice(endpointId);
                device.AudioEndpointVolume.Mute = wasMuted;
            }
            catch
            {
                // The endpoint may have been unplugged or removed while casting.
                // Cleanup must remain safe during app shutdown and device changes.
            }

            return ValueTask.CompletedTask;
        }
    }
}
