using DLNACast.Core.Audio;
using NAudio.CoreAudioApi;

namespace DLNACast.Tests;

public sealed class WindowsAudioPolicyTests
{
    [Fact]
    public void CanReadCurrentProcessPersistedRenderEndpoint()
    {
        var exception = Record.Exception(() =>
            WindowsAudioPolicy.GetPersistedDefaultEndpoint(Environment.ProcessId, Role.Multimedia));

        Assert.Null(exception);
    }

    [Fact]
    public void CanRoundTripCurrentProcessPersistedRenderEndpointWithoutChangingIt()
    {
        var previous = WindowsAudioPolicy.GetPersistedDefaultEndpoint(
            Environment.ProcessId,
            Role.Multimedia);
        using var enumerator = new MMDeviceEnumerator();
        using var currentDefault = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

        try
        {
            WindowsAudioPolicy.SetPersistedDefaultEndpoint(
                Environment.ProcessId,
                Role.Multimedia,
                currentDefault.ID);

            Assert.Contains(
                currentDefault.ID,
                WindowsAudioPolicy.GetPersistedDefaultEndpoint(Environment.ProcessId, Role.Multimedia),
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            WindowsAudioPolicy.SetPersistedDefaultEndpoint(
                Environment.ProcessId,
                Role.Multimedia,
                previous);
        }
    }

    [Fact]
    public void CanReapplyCurrentSystemDefaultEndpointWithoutChangingIt()
    {
        using var enumerator = new MMDeviceEnumerator();
        using var current = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

        WindowsAudioPolicy.SetDefaultEndpoint(current.ID, Role.Multimedia);

        using var after = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        Assert.Equal(current.ID, after.ID);
    }
}
