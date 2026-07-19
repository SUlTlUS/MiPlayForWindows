namespace DLNACast.Core.Models;

public sealed record AudioSourceItem(string Id, string DisplayName, int? ProcessId = null)
{
    public override string ToString() => DisplayName;
}

