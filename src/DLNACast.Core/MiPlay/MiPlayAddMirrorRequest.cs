using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DLNACast.Core.MiPlay;

public sealed record MiPlayAddMirrorRequest(IPAddress SourceAddress, int SourcePort = MiPlayProtocolConstants.DefaultMediaPort)
{
    public string ToPayloadText()
    {
        ArgumentNullException.ThrowIfNull(SourceAddress);
        if (SourceAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new NotSupportedException("The observed LX06 mpas local Cmd_AddMirror payload only supports IPv4 source addresses.");
        }

        if (SourcePort is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
        {
            throw new ArgumentOutOfRangeException(nameof(SourcePort));
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{SourceAddress}:{SourcePort}&from:{SourceAddress}&islocal:1");
    }

    public byte[] ToPayloadBytes() => Encoding.UTF8.GetBytes(ToPayloadText());

    public byte[] ToCommandFrame(ushort sequence) => MiPlayCommandFrameCodec.Encode(
        MiPlayProtocolConstants.AddMirrorCommand,
        sequence,
        ToPayloadBytes());

    public byte[] ToSafetyDataCommandFrame(ushort sequence, MiPlaySafetyDataSessionCipher cipher)
    {
        ArgumentNullException.ThrowIfNull(cipher);

        return MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.AddMirrorCommand,
            sequence,
            cipher.EncryptVersion1(ToPayloadBytes()));
    }
}