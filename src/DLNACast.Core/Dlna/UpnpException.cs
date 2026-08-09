namespace DLNACast.Core.Dlna;

public sealed class UpnpException(int? errorCode, string message, Exception? innerException = null) : Exception(errorCode is null ? message : $"UPnP {errorCode}: {message}", innerException)
{
    public int? ErrorCode { get; } = errorCode;
}

