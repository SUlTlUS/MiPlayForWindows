namespace DLNACast.Core.Models;

public enum StreamProfile
{
    PcmWave,
    Mp3Cbr320
}

public enum CastSessionState
{
    Idle,
    Discovering,
    Ready,
    Preparing,
    Connecting,
    Streaming,
    Recovering,
    Stopping,
    Error
}

public sealed record CastDiagnostics(
    CastSessionState State,
    StreamProfile? Profile = null,
    int BufferedMilliseconds = 0,
    long Overruns = 0,
    long Underruns = 0,
    string Message = "",
    string? LastError = null);

public sealed record TransportStatus(string State, string Status);

