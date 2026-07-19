namespace DLNACast.Core.Models;

public abstract record CaptureSelection
{
    private CaptureSelection() { }

    public sealed record SystemMix(string EndpointId, string DisplayName) : CaptureSelection;

    public sealed record Process(int ProcessId, string DisplayName, bool IncludeChildren = true) : CaptureSelection;
}

