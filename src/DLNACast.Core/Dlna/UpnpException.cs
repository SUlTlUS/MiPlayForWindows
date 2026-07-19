namespace DLNACast.Core.Dlna;

public sealed class UpnpException : Exception
{
    public UpnpException(int? errorCode, string message, Exception? innerException = null)
        : base(errorCode is null ? message : $"UPnP {errorCode}: {message}", innerException)
    {
        ErrorCode = errorCode;
    }

    public int? ErrorCode { get; }
}

