using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DLNACast.Core.MiPlay;

public sealed record MiPlayOpenDeviceRequest(IPAddress SenderAddress, int MediaPort, int MirrorMode = 1)
{
    public string ToPayloadText()
    {
        ArgumentNullException.ThrowIfNull(SenderAddress);
        if (SenderAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new NotSupportedException("The observed MiPlay openDevice request only supports IPv4 sender addresses.");
        }
        if (MediaPort is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
        {
            throw new ArgumentOutOfRangeException(nameof(MediaPort));
        }
        if (MirrorMode < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MirrorMode));
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"wfd://{SenderAddress}:{MediaPort}?mirrorMode={MirrorMode}");
    }

    public byte[] ToPayloadBytes() => Encoding.UTF8.GetBytes(ToPayloadText());

    public byte[] ToCommandFrame(ushort sequence) => MiPlayCommandFrameCodec.Encode(
        MiPlayProtocolConstants.OpenDeviceCommand,
        sequence,
        ToPayloadBytes());
}
